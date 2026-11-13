using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class TimeAbsenceRoutineTests(DatabaseFixture database)
{
    private static readonly string[] Sickness = ["pension", "disability", "sickness", "health"];
    private static readonly string[] NoSickness = ["pension", "disability", "health"];

    [Fact]
    public async Task Generated_calendar_has_2026_norms_and_cannot_be_generated_twice()
    {
        var company = await TestDb.CompanyAsync(database);

        var saturdays = await ScalarAsync<int>(
            "CALL time_absence_app.calendar_generate($1, $2, NULL)",
            company,
            2026
        );
        var again = await Assert.ThrowsAsync<PostgresException>(() =>
            ScalarAsync<int>("CALL time_absence_app.calendar_generate($1, $2, NULL)", company, 2026)
        );
        var noHolidays = await Assert.ThrowsAsync<PostgresException>(() =>
            ScalarAsync<int>("CALL time_absence_app.calendar_generate($1, $2, NULL)", company, 2099)
        );

        Assert.Equal(2, saturdays);
        Assert.Equal("calendarExists", again.Hint);
        Assert.Equal("holidaysMissing", noHolidays.Hint);
        Assert.Equal((20, 160m, false), await MonthAsync(company, 11));
        Assert.Equal((21, 168m, true), await MonthAsync(company, 12));
        Assert.Equal(
            "holiday",
            await ScalarAsync<string>(
                "SELECT kind FROM time_absence_app.calendar_month($1, 2026, 12) WHERE date = DATE '2026-12-24'",
                company
            )
        );
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    public async Task Statutory_holidays_match_the_dictionary_from_the_act(int year)
    {
        // Słownik 2026–2027 z migracji (daty ruchome sprawdzone z kalendarzem) = wyliczenie z ustawy i Wielkanocy.
        var computed = await AdminAsync<string>(
            "SELECT string_agg(date || ' ' || name, '; ' ORDER BY date) FROM time_absence.statutory_holidays($1)",
            year
        );
        var dictionary = await AdminAsync<string>(
            "SELECT string_agg(date || ' ' || name, '; ' ORDER BY date) FROM time_absence.holidays WHERE extract(year FROM date) = $1 AND created_by IS NULL",
            year
        );

        Assert.Equal(dictionary, computed);
        Assert.Equal(14, computed!.Split("; ").Length);
    }

    [Fact]
    public async Task Fill_statutory_adds_only_missing_holidays_and_rejects_years_before_2025()
    {
        // Rok 2028 (Wielkanoc 16.04) w transakcji wycofanej na końcu — słownik współdzielony przez testy zostaje.
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "admin")
        );
        await using var transaction = await connection.BeginTransactionAsync();
        await RunAsync<object>(
            connection,
            "CALL time_absence_app.holiday_add(DATE '2028-01-01', 'Nowy Rok — wpis ręczny')",
            []
        );

        var added = await RunAsync<int>(
            connection,
            "CALL time_absence_app.holiday_fill_statutory(2028, NULL)",
            []
        );
        var again = await RunAsync<int>(
            connection,
            "CALL time_absence_app.holiday_fill_statutory(2028, NULL)",
            []
        );
        var movable = await RunAsync<string>(
            connection,
            """
            SELECT string_agg(to_char(date, 'MM-DD'), ',' ORDER BY date) FROM time_absence_app.holiday_list(2028)
            WHERE date NOT IN (SELECT make_date(2028, m, d) FROM (VALUES (1, 1), (1, 6), (5, 1), (5, 3), (8, 15),
                (11, 1), (11, 11), (12, 24), (12, 25), (12, 26)) AS f (m, d))
            """,
            []
        );
        var manual = await RunAsync<string>(
            connection,
            "SELECT name FROM time_absence_app.holiday_list(2028) WHERE date = DATE '2028-01-01'",
            []
        );
        var audited = await RunAsync<long>(
            connection,
            "SELECT count(*) FROM platform_app.audit_list('holiday', p_limit => 100) WHERE entity_id LIKE '2028-%'",
            []
        );
        await transaction.RollbackAsync();

        var before2025 = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<int>("admin", "CALL time_absence_app.holiday_fill_statutory(2024, NULL)")
        );
        var kadry = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<int>("kadry", "CALL time_absence_app.holiday_fill_statutory(2028, NULL)")
        );

        Assert.Equal(13, added);
        Assert.Equal(0, again);
        Assert.Equal("04-16,04-17,06-04,06-15", movable);
        Assert.Equal("Nowy Rok — wpis ręczny", manual);
        Assert.Equal(14L, audited);
        Assert.Equal("statutoryHolidaysUnsupported", before2025.Hint);
        Assert.Equal("U0004", kadry.SqlState);
    }

    [Fact]
    public async Task Designated_day_off_lowers_the_month_and_holidays_stay_holidays()
    {
        var company = await CalendarAsync();

        await SetDayAsync(company, D(2026, 12, 31), "designatedOff", null);
        var second = await Assert.ThrowsAsync<PostgresException>(() =>
            SetDayAsync(company, D(2026, 12, 30), "designatedOff", null)
        );
        var noSaturdayHoliday = await Assert.ThrowsAsync<PostgresException>(() =>
            SetDayAsync(company, D(2026, 11, 30), "designatedOff", null)
        );
        var holidayAsWorkday = await Assert.ThrowsAsync<PostgresException>(() =>
            SetDayAsync(company, D(2026, 12, 25), "workday", 8m)
        );
        var tooLong = await Assert.ThrowsAsync<PostgresException>(() =>
            SetDayAsync(company, D(2026, 12, 29), "workday", 13m)
        );
        await SetDayAsync(company, D(2026, 12, 29), "workday", 6m);

        Assert.Equal((20, 158m, false), await MonthAsync(company, 12));
        Assert.Equal("""{"kind": ["invalid"]}""", second.Hint);
        Assert.Equal("""{"kind": ["invalid"]}""", noSaturdayHoliday.Hint);
        Assert.Equal("""{"kind": ["invalid"]}""", holidayAsWorkday.Hint);
        Assert.Equal("""{"hours": ["invalid"]}""", tooLong.Hint);
        Assert.Equal(
            2L,
            await AdminAsync<long>(
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'calendar_day' AND entity_id LIKE $1",
                company + ":%"
            )
        );
    }

    [Fact]
    public async Task Norm_is_calendar_hours_in_contract_times_fte()
    {
        var company = await CalendarAsync();
        await SetDayAsync(company, D(2026, 12, 31), "designatedOff", null);
        var half = await ContractAsync(company, "employment", D(2026, 1, 1), null, 1, 2);
        var fromMid = await ContractAsync(company, "employment", D(2026, 11, 16), null);
        var mandate = await ContractAsync(company, "mandate", D(2026, 1, 1), D(2026, 12, 31));
        var noCalendar = await ContractAsync(
            await TestDb.CompanyAsync(database),
            "employment",
            D(2026, 1, 1),
            null
        );

        var notEmployment = await Assert.ThrowsAsync<PostgresException>(() =>
            NormAsync(mandate, 11)
        );
        var missing = await Assert.ThrowsAsync<PostgresException>(() => NormAsync(noCalendar, 11));

        Assert.Equal(80m, await NormAsync(half, 11));
        Assert.Equal(80m, await NormAsync(half, 12));
        // 16–30.11.2026: 11 dni roboczych.
        Assert.Equal(88m, await NormAsync(fromMid, 11));
        Assert.Equal("kindNotAllowed", notEmployment.Hint);
        Assert.Equal("calendarMissing", missing.Hint);
    }

    [Fact]
    public async Task Absence_stays_in_contract_does_not_overlap_and_counts_days_and_hours()
    {
        var company = await CalendarAsync();
        var employment = await ContractAsync(company, "employment", D(2026, 1, 1), null, 3, 4);

        var sickness = await AbsenceAsync(employment, "CH", D(2026, 11, 30), D(2026, 12, 2), "ZZ1");
        var overlap = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UB", D(2026, 12, 2), D(2026, 12, 3))
        );
        var outside = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UB", D(2025, 12, 31), D(2026, 1, 2))
        );
        var noCertificate = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "CH", D(2026, 3, 2), D(2026, 3, 3))
        );
        var unknownType = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "XX", D(2026, 3, 2), D(2026, 3, 3))
        );

        Assert.Equal("""{"dateFrom": ["overlap"]}""", overlap.Hint);
        Assert.Equal("""{"dateFrom": ["outsidePeriod"]}""", outside.Hint);
        Assert.Equal("""{"certificateNumber": ["required"]}""", noCertificate.Hint);
        Assert.Equal("""{"absenceTypeId": ["notFound"]}""", unknownType.Hint);
        Assert.Equal(
            3,
            await ScalarAsync<int>(
                "SELECT calendar_days FROM time_absence_app.absence_list($1, 2026) WHERE id = $2",
                employment,
                sickness
            )
        );
        // 30.11, 1.12, 2.12 — 3 dni robocze × 8 h × 3/4.
        Assert.Equal(
            18m,
            await ScalarAsync<decimal>(
                "SELECT work_hours FROM time_absence_app.absence_list($1, 2026) WHERE id = $2",
                employment,
                sickness
            )
        );
        // Funkcja wewnętrzna dla listy płac (009) — rola API jej nie widzi.
        Assert.Equal(
            18m,
            await AdminAsync<decimal>(
                "SELECT time_absence.working_hours($1, DATE '2026-11-30', DATE '2026-12-02')",
                employment
            )
        );
    }

    [Fact]
    public async Task Mandate_allows_only_sickness_with_sickness_insurance_and_specific_work_nothing()
    {
        var company = await TestDb.CompanyAsync(database);
        var insured = await ContractAsync(company, "mandate", D(2026, 1, 1), D(2026, 12, 31));
        var uninsured = await ContractAsync(
            company,
            "mandate",
            D(2026, 1, 1),
            D(2026, 12, 31),
            insurances: NoSickness
        );
        var work = await ContractAsync(company, "specific_work", D(2026, 1, 1), D(2026, 12, 31));

        await AbsenceAsync(insured, "CH", D(2026, 3, 2), D(2026, 3, 6), "ZZ2");
        var leave = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(insured, "UW", D(2026, 4, 1), D(2026, 4, 1))
        );
        var noInsurance = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(uninsured, "CH", D(2026, 3, 2), D(2026, 3, 6), "ZZ3")
        );
        var specificWork = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(work, "CH", D(2026, 3, 2), D(2026, 3, 6), "ZZ4")
        );

        Assert.Equal("""{"absenceTypeId": ["invalid"]}""", leave.Hint);
        Assert.Equal("""{"absenceTypeId": ["invalid"]}""", noInsurance.Hint);
        Assert.Equal("""{"absenceTypeId": ["invalid"]}""", specificWork.Hint);
    }

    [Fact]
    public async Task Yearly_and_per_event_limits_count_work_days()
    {
        var company = await CalendarAsync();
        var employment = await ContractAsync(company, "employment", D(2026, 1, 1), null);
        await LeaveSetAsync(employment, 2026, 26);

        // UZ: 4 dni w roku (KP art. 167²).
        await AbsenceAsync(employment, "UZ", D(2026, 2, 2), D(2026, 2, 4));
        var onDemand = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UZ", D(2026, 2, 9), D(2026, 2, 10))
        );
        await AbsenceAsync(employment, "UZ", D(2026, 2, 9), D(2026, 2, 9));
        // OD: 2 dni; weekend się nie liczy.
        await AbsenceAsync(employment, "OD", D(2026, 3, 6), D(2026, 3, 9));
        var care = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "OD", D(2026, 3, 16), D(2026, 3, 16))
        );
        // SW: 2 dni, UOP: 5 dni.
        var force = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "SW", D(2026, 4, 13), D(2026, 4, 15))
        );
        var carer = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UOP", D(2026, 5, 4), D(2026, 5, 11))
        );
        await AbsenceAsync(employment, "UOP", D(2026, 5, 4), D(2026, 5, 8));
        // UO: 2 dni na zdarzenie, bez limitu w roku.
        var occasional = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UO", D(2026, 6, 8), D(2026, 6, 10))
        );
        await AbsenceAsync(employment, "UO", D(2026, 6, 8), D(2026, 6, 9));
        await AbsenceAsync(employment, "UO", D(2026, 6, 15), D(2026, 6, 16));

        Assert.Equal("""{"dateTo": ["limitExceeded"], "remainingDays": ["1"]}""", onDemand.Hint);
        Assert.Equal("""{"dateTo": ["limitExceeded"], "remainingDays": ["0"]}""", care.Hint);
        Assert.Equal("""{"dateTo": ["limitExceeded"], "remainingDays": ["2"]}""", force.Hint);
        Assert.Equal("""{"dateTo": ["limitExceeded"], "remainingDays": ["5"]}""", carer.Hint);
        Assert.Equal("""{"dateTo": ["limitExceeded"], "remainingDays": ["2"]}""", occasional.Hint);
    }

    [Fact]
    public async Task Leave_cannot_exceed_balance_and_update_and_delete_need_row_version()
    {
        var company = await CalendarAsync();
        var employment = await ContractAsync(company, "employment", D(2026, 1, 1), null);
        var noCalendar = await ContractAsync(
            await TestDb.CompanyAsync(database),
            "employment",
            D(2026, 1, 1),
            null
        );

        var noEntitlement = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UW", D(2026, 7, 6), D(2026, 7, 6))
        );
        var calendarMissing = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(noCalendar, "UW", D(2026, 7, 6), D(2026, 7, 6))
        );
        // Limit poprawiony do 3 dni (24 h).
        await LeaveSetAsync(
            employment,
            2026,
            20,
            entitledHours: 24m,
            reason: "Wykorzystany u poprzedniego pracodawcy"
        );
        var leave = await AbsenceAsync(employment, "UW", D(2026, 7, 6), D(2026, 7, 7));
        var tooMuch = await Assert.ThrowsAsync<PostgresException>(() =>
            AbsenceAsync(employment, "UW", D(2026, 7, 13), D(2026, 7, 14))
        );
        var version = await AbsenceVersionAsync(employment, leave);
        var stale = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(
                "CALL time_absence_app.absence_update($1, $2, 'UW', $3, $4, NULL, NULL)",
                leave,
                version + 1,
                D(2026, 7, 6),
                D(2026, 7, 8)
            )
        );
        await ExecAsync(
            "CALL time_absence_app.absence_update($1, $2, 'UW', $3, $4, NULL, 'trzy dni')",
            leave,
            version,
            D(2026, 7, 6),
            D(2026, 7, 8)
        );
        var balance = await ScalarAsync<decimal>(
            "SELECT balance_hours FROM time_absence_app.leave_entitlement_get($1, 2026, NULL, NULL)",
            employment
        );
        await ExecAsync(
            "CALL time_absence_app.absence_delete($1, $2)",
            leave,
            await AbsenceVersionAsync(employment, leave)
        );

        Assert.Equal(
            """{"dateTo": ["leaveLimitExceeded"], "remainingHours": ["0"]}""",
            noEntitlement.Hint
        );
        Assert.Equal("calendarMissing", calendarMissing.Hint);
        Assert.Equal(
            """{"dateTo": ["leaveLimitExceeded"], "remainingHours": ["8.00"]}""",
            tooMuch.Hint
        );
        Assert.Equal("U0003", stale.SqlState);
        Assert.Null(stale.Hint);
        Assert.Equal(0m, balance);
        Assert.Equal(
            3L,
            await AdminAsync<long>(
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'absence' AND entity_id = $1",
                leave.ToString()
            )
        );
    }

    [Fact]
    public async Task Leave_proposal_follows_fte_months_and_first_job()
    {
        var company = await TestDb.CompanyAsync(database);
        var full = await ContractAsync(company, "employment", D(2025, 6, 1), null);
        var half = await ContractAsync(company, "employment", D(2025, 6, 1), null, 1, 2);
        var threeQuarters = await ContractAsync(company, "employment", D(2025, 6, 1), null, 3, 4);
        var fromMarch = await ContractAsync(company, "employment", D(2026, 3, 15), null);

        Assert.Equal(208m, await ProposalAsync(full, 26, false));
        Assert.Equal(104m, await ProposalAsync(half, 26, false));
        Assert.Equal(120m, await ProposalAsync(threeQuarters, 20, false));
        // 10 miesięcy (marzec w górę): 10/12 × 26 = 21,67 → 22 dni.
        Assert.Equal(176m, await ProposalAsync(fromMarch, 26, false));
        // Pierwsza praca: 9 pełnych miesięcy do końca roku × 1/12 × 20 = 15 dni.
        Assert.Equal(120m, await ProposalAsync(fromMarch, 20, true));
        // Poprzedni rok — 7 miesięcy (VI–XII): 7/12 × 20 = 11,67 → 12 dni.
        Assert.Equal(
            96m,
            await ScalarAsync<decimal>(
                "SELECT proposed_hours FROM time_absence_app.leave_entitlement_get($1, 2025, 20, false)",
                full
            )
        );
    }

    [Fact]
    public async Task Unused_leave_carries_over_and_override_needs_reason()
    {
        var company = await TestDb.CompanyAsync(database);
        var employment = await ContractAsync(company, "employment", D(2025, 6, 1), null);

        await LeaveSetAsync(employment, 2025, 20);
        var noReason = await Assert.ThrowsAsync<PostgresException>(() =>
            LeaveSetAsync(employment, 2026, 26, carriedOverHours: 10m, reason: null)
        );
        var wrongDays = await Assert.ThrowsAsync<PostgresException>(() =>
            LeaveSetAsync(employment, 2026, 25)
        );
        await LeaveSetAsync(employment, 2026, 26);
        var newWithoutVersion = await Assert.ThrowsAsync<PostgresException>(() =>
            LeaveSetAsync(employment, 2026, 20)
        );
        var carried = await ScalarAsync<decimal>(
            "SELECT carried_over_hours FROM time_absence_app.leave_entitlement_get($1, 2026, NULL, NULL)",
            employment
        );
        var balance = await ScalarAsync<decimal>(
            "SELECT balance_days FROM time_absence_app.leave_entitlement_get($1, 2026, NULL, NULL)",
            employment
        );

        Assert.Equal("""{"overrideReason": ["required"]}""", noReason.Hint);
        Assert.Equal("""{"baseDays": ["invalid"]}""", wrongDays.Hint);
        Assert.Equal("U0003", newWithoutVersion.SqlState);
        Assert.Equal(96m, carried);
        Assert.Equal(38m, balance);
    }

    [Fact]
    public async Task Overtime_is_for_employment_and_warns_above_150_hours()
    {
        var company = await TestDb.CompanyAsync(database);
        var employment = await ContractAsync(company, "employment", D(2026, 1, 1), null);
        var mandate = await ContractAsync(company, "mandate", D(2026, 1, 1), D(2026, 12, 31));

        for (var day = 1; day <= 10; day++)
        {
            await OvertimeAsync(employment, D(2026, 3, day), 15m, 50);
        }

        var first = await OvertimeAsync(employment, D(2026, 3, 11), 0.5m, 100);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            OvertimeAsync(employment, D(2026, 3, 11), 1m, 100)
        );
        var tooMany = await Assert.ThrowsAsync<PostgresException>(() =>
            OvertimeAsync(employment, D(2026, 3, 12), 16.5m, 50)
        );
        var badRate = await Assert.ThrowsAsync<PostgresException>(() =>
            OvertimeAsync(employment, D(2026, 3, 12), 2m, 75)
        );
        var onMandate = await Assert.ThrowsAsync<PostgresException>(() =>
            OvertimeAsync(mandate, D(2026, 3, 12), 2m, 50)
        );
        var total = await ScalarAsync<decimal>(
            "SELECT total_hours FROM time_absence_app.overtime_summary($1, 2026)",
            employment
        );
        var above = await ScalarAsync<bool>(
            "SELECT above_limit FROM time_absence_app.overtime_summary($1, 2026)",
            employment
        );
        await ExecAsync(
            "CALL time_absence_app.overtime_delete($1, $2)",
            first,
            await ScalarAsync<long>(
                "SELECT row_version FROM time_absence_app.overtime_list($1, 2026) WHERE id = $2",
                employment,
                first
            )
        );

        Assert.Equal("""{"date": ["duplicate"]}""", duplicate.Hint);
        Assert.Equal("""{"hours": ["invalid"]}""", tooMany.Hint);
        Assert.Equal("""{"rate": ["invalid"]}""", badRate.Hint);
        Assert.Equal("kindNotAllowed", onMandate.Hint);
        Assert.Equal(150.5m, total);
        Assert.True(above);
        Assert.False(
            await ScalarAsync<bool>(
                "SELECT above_limit FROM time_absence_app.overtime_summary($1, 2026)",
                employment
            )
        );
    }

    [Fact]
    public async Task Mandate_hours_do_not_overlap_and_confirmation_needs_date_and_form()
    {
        var company = await TestDb.CompanyAsync(database);
        var mandate = await ContractAsync(company, "mandate", D(2026, 1, 1), D(2026, 12, 31));
        var employment = await ContractAsync(company, "employment", D(2026, 1, 1), null);

        var march = await MandateHoursAsync(
            mandate,
            D(2026, 3, 1),
            D(2026, 3, 31),
            120m,
            null,
            null
        );
        var overlap = await Assert.ThrowsAsync<PostgresException>(() =>
            MandateHoursAsync(mandate, D(2026, 3, 15), D(2026, 4, 15), 10m, null, null)
        );
        var formOnly = await Assert.ThrowsAsync<PostgresException>(() =>
            MandateHoursAsync(mandate, D(2026, 4, 1), D(2026, 4, 30), 10m, null, "written")
        );
        var badForm = await Assert.ThrowsAsync<PostgresException>(() =>
            MandateHoursAsync(mandate, D(2026, 4, 1), D(2026, 4, 30), 10m, D(2026, 5, 2), "oral")
        );
        var onEmployment = await Assert.ThrowsAsync<PostgresException>(() =>
            MandateHoursAsync(employment, D(2026, 4, 1), D(2026, 4, 30), 10m, null, null)
        );
        await ExecAsync(
            "CALL time_absence_app.mandate_hours_update($1, $2, $3, $4, 121, $5, 'electronic')",
            march,
            await MandateVersionAsync(mandate, march),
            D(2026, 3, 1),
            D(2026, 3, 31),
            D(2026, 4, 2)
        );
        var confirmed = await ScalarAsync<bool>(
            "SELECT confirmed FROM time_absence_app.mandate_hours_list($1, 2026) WHERE id = $2",
            mandate,
            march
        );
        await ExecAsync(
            "CALL time_absence_app.mandate_hours_delete($1, $2)",
            march,
            await MandateVersionAsync(mandate, march)
        );

        Assert.Equal("""{"dateFrom": ["overlap"]}""", overlap.Hint);
        Assert.Equal("""{"confirmedAt": ["required"]}""", formOnly.Hint);
        Assert.Equal("""{"confirmationForm": ["invalid"]}""", badForm.Hint);
        Assert.Equal("kindNotAllowed", onEmployment.Hint);
        Assert.True(confirmed);
    }

    [Fact]
    public async Task Roles_admin_writes_holidays_and_pracownik_reads_only_own_record()
    {
        var date = new DateOnly(2101 + Random.Shared.Next(800), 1, 1).AddDays(
            Random.Shared.Next(365)
        );
        var company = await TestDb.CompanyAsync(database);
        var employment = await ContractAsync(company, "employment", D(2026, 1, 1), null);
        var employee = await AdminAsync<Guid>(
            "SELECT employee_id FROM employment.employments WHERE id = $1",
            employment
        );
        var own = await AdminAsync<Guid>(
            "SELECT l.employee_id FROM platform.user_employee_links l JOIN platform.users u ON u.id = l.user_id WHERE u.login = 'pracownik' LIMIT 1"
        );

        var kadryHoliday = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>("kadry", "CALL time_absence_app.holiday_add($1, 'Test')", date)
        );
        await AsAsync<object>("admin", "CALL time_absence_app.holiday_add($1, 'Test')", date);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>("admin", "CALL time_absence_app.holiday_add($1, 'Test')", date)
        );
        await AsAsync<object>("admin", "CALL time_absence_app.holiday_delete($1)", date);
        var pracownikList = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>(
                "pracownik",
                "SELECT count(*) FROM time_absence_app.absence_list($1, 2026)",
                employment
            )
        );
        var foreign = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>(
                "pracownik",
                "SELECT count(*) FROM time_absence_app.my_leave_balance($1, 2026)",
                employee
            )
        );
        var ownBalance = await AsAsync<decimal>(
            "pracownik",
            "SELECT balance_hours FROM time_absence_app.my_leave_balance($1, 2026)",
            own
        );
        var ownAbsences = await AsAsync<long>(
            "pracownik",
            "SELECT count(*) FROM time_absence_app.my_absence_list($1, 2026)",
            own
        );

        Assert.Equal("U0004", kadryHoliday.SqlState);
        Assert.Equal("""{"date": ["duplicate"]}""", duplicate.Hint);
        Assert.Equal("U0004", pracownikList.SqlState);
        Assert.Equal("U0004", foreign.SqlState);
        // Dane dev (R20): 26 dni = 208 h − urlop 3 dni (24 h).
        Assert.Equal(184m, ownBalance);
        Assert.Equal(2L, ownAbsences);
    }

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private async Task<Guid> CalendarAsync()
    {
        var company = await TestDb.CompanyAsync(database);
        await ScalarAsync<int>(
            "CALL time_absence_app.calendar_generate($1, $2, NULL)",
            company,
            2026
        );
        return company;
    }

    private async Task<(int WorkDays, decimal Hours, bool Missing)> MonthAsync(
        Guid company,
        int month
    )
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            """
            SELECT work_days, hours, missing_designated
            FROM time_absence_app.calendar_year($1, 2026) WHERE month = $2
            """,
            new NpgsqlParameter { Value = company },
            new NpgsqlParameter { Value = month }
        );
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetBoolean(2));
    }

    private async Task SetDayAsync(Guid company, DateOnly date, string kind, decimal? hours) =>
        await ExecAsync(
            "CALL time_absence_app.calendar_day_set($1, $2, $3, $4)",
            company,
            date,
            kind,
            hours
        );

    private async Task<decimal> NormAsync(Guid employment, int month) =>
        await ScalarAsync<decimal>(
            "SELECT norm_hours FROM time_absence_app.employment_norm($1, 2026, $2)",
            employment,
            month
        );

    private async Task<Guid> ContractAsync(
        Guid company,
        string kind,
        DateOnly start,
        DateOnly? end,
        int fteNumerator = 1,
        int fteDenominator = 1,
        string[]? insurances = null
    )
    {
        var employee = await AdminAsync<Guid>(
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, left(gen_random_uuid()::text, 20), 'Jan', 'Kowalski', DATE '1985-01-01') RETURNING id
            """,
            company
        );
        var employment = await AdminAsync<Guid>(
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date, end_date)
            VALUES ($1, $2, gen_random_uuid()::text, $3,
                    CASE WHEN $3 = 'employment' THEN CASE WHEN $5::date IS NULL THEN 'indefinite' ELSE 'fixed_term' END END,
                    $4, $4, $5)
            RETURNING id
            """,
            employee,
            company,
            kind,
            start,
            end
        );
        await AdminAsync<object>(
            """
            INSERT INTO employment.terms (employment_id, valid_from, fte_numerator, fte_denominator, pay_basis,
                                          base_amount, insurances, cost_kind)
            VALUES ($1, $2, CASE WHEN $5 = 'employment' THEN $3 END, CASE WHEN $5 = 'employment' THEN $4 END,
                    CASE WHEN $5 = 'employment' THEN 'monthly' ELSE 'hourly' END, 6000, $6, 'standard')
            """,
            employment,
            start,
            fteNumerator,
            fteDenominator,
            kind,
            insurances ?? Sickness
        );
        return employment;
    }

    private async Task<Guid> AbsenceAsync(
        Guid employment,
        string type,
        DateOnly from,
        DateOnly to,
        string? certificate = null
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL time_absence_app.absence_create($1, $2, $3, $4, $5, NULL, NULL)",
                    employment,
                    type,
                    from,
                    to,
                    certificate
                )
            )!;

    private async Task<long> AbsenceVersionAsync(Guid employment, Guid id) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM time_absence_app.absence_list($1, NULL) WHERE id = $2",
            employment,
            id
        );

    private async Task LeaveSetAsync(
        Guid employment,
        int year,
        int baseDays,
        decimal? entitledHours = null,
        decimal? carriedOverHours = null,
        string? reason = null
    ) =>
        await ExecAsync(
            "CALL time_absence_app.leave_entitlement_set($1, $2, $3, false, $4, $5, $6, NULL)",
            employment,
            year,
            baseDays,
            entitledHours,
            carriedOverHours,
            reason
        );

    private async Task<decimal> ProposalAsync(Guid employment, int baseDays, bool firstJob) =>
        await ScalarAsync<decimal>(
            "SELECT proposed_hours FROM time_absence_app.leave_entitlement_get($1, 2026, $2, $3)",
            employment,
            baseDays,
            firstJob
        );

    private async Task<Guid> OvertimeAsync(
        Guid employment,
        DateOnly date,
        decimal hours,
        int rate
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL time_absence_app.overtime_add($1, $2, $3, $4, NULL)",
                    employment,
                    date,
                    hours,
                    rate
                )
            )!;

    private async Task<Guid> MandateHoursAsync(
        Guid employment,
        DateOnly from,
        DateOnly to,
        decimal hours,
        DateOnly? confirmedAt,
        string? form
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL time_absence_app.mandate_hours_add($1, $2, $3, $4, $5, $6, NULL)",
                    employment,
                    from,
                    to,
                    hours,
                    confirmedAt,
                    form
                )
            )!;

    private async Task<long> MandateVersionAsync(Guid employment, Guid id) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM time_absence_app.mandate_hours_list($1, 2026) WHERE id = $2",
            employment,
            id
        );

    private async Task ExecAsync(string sql, params object?[] args) =>
        await ScalarAsync<object>(sql, args);

    private async Task<T?> ScalarAsync<T>(string sql, params object?[] args) =>
        await AsAsync<T>("kadry", sql, args);

    private async Task<T?> AsAsync<T>(string login, string sql, params object?[] args)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, login)
        );
        return await RunAsync<T>(connection, sql, args);
    }

    private async Task<T?> AdminAsync<T>(string sql, params object?[] args)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        return await RunAsync<T>(connection, sql, args);
    }

    private static async Task<T?> RunAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object?[] args
    )
    {
        await using var command = TestDb.Command(connection, sql);
        foreach (var arg in args)
        {
            command.Parameters.Add(
                arg is null
                    ? new NpgsqlParameter { Value = DBNull.Value }
                    : new NpgsqlParameter { Value = arg }
            );
        }

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}
