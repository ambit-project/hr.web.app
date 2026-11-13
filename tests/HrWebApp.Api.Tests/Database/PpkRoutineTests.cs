using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class PpkRoutineTests(DatabaseFixture database)
{
    private static readonly string[] Insured = ["pension", "disability", "accident", "health"];
    private static readonly string[] Student = [];

    [Fact]
    public async Task Employed_person_is_adult_employment_and_mandate_with_pension_and_disability()
    {
        var company = await TestDb.CompanyAsync(database);
        var adult = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 1, 1));
        var juvenile = await EmployeeAsync(company, D(2010, 5, 1), "employment", D(2026, 1, 1));
        var mandate = await EmployeeAsync(
            company,
            D(1990, 1, 1),
            "mandate",
            D(2026, 1, 1),
            D(2026, 12, 31)
        );
        var student = await EmployeeAsync(
            company,
            D(2004, 1, 1),
            "mandate",
            D(2026, 1, 1),
            D(2026, 12, 31),
            Student
        );
        var work = await EmployeeAsync(
            company,
            D(1990, 1, 1),
            "specific_work",
            D(2026, 1, 1),
            D(2026, 12, 31)
        );

        Assert.True(await EmployedAsync(adult, D(2026, 6, 1)));
        Assert.False(await EmployedAsync(adult, D(2025, 12, 31)));
        Assert.False(await EmployedAsync(juvenile, D(2026, 4, 30)));
        // Od 18. urodzin umowa o pracę się liczy.
        Assert.True(await EmployedAsync(juvenile, D(2028, 5, 1)));
        Assert.True(await EmployedAsync(mandate, D(2026, 6, 1)));
        Assert.False(await EmployedAsync(student, D(2026, 6, 1)));
        Assert.False(await EmployedAsync(work, D(2026, 6, 1)));
    }

    [Fact]
    public async Task Three_months_count_twelve_months_back_with_gap_and_two_titles()
    {
        var company = await TestDb.CompanyAsync(database);
        var simple = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 1, 15));
        var twoTitles = await EmployeeAsync(
            company,
            D(1990, 1, 1),
            "mandate",
            D(2025, 11, 1),
            D(2025, 11, 30)
        );
        await EmploymentAsync(twoTitles, company, "employment", D(2026, 1, 1), null);
        await EmploymentAsync(twoTitles, company, "specific_work", D(2025, 12, 1), D(2025, 12, 31));
        var fromMarch = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 3, 1));

        Assert.Equal(D(2026, 4, 14), await ReachedAsync(simple, D(2026, 4, 20)));
        // 30 dni zlecenia z XI 2025 + 60 dni umowy (1.01–1.03) = 90 dni okresu 2.12–1.03; dzieło się nie liczy.
        Assert.Equal(D(2026, 3, 1), await ReachedAsync(twoTitles, D(2026, 4, 20)));
        Assert.Equal(D(2026, 5, 31), await ReachedAsync(fromMarch, D(2026, 6, 1)));

        var waiting = await CandidateAsync(company, simple, D(2026, 3, 1));
        var toEnroll = await CandidateAsync(company, simple, D(2026, 5, 10));
        var overdue = await CandidateAsync(company, simple, D(2026, 5, 11));
        Assert.Equal(("waiting", D(2026, 4, 14), D(2026, 5, 10)), waiting);
        Assert.Equal(("toEnroll", D(2026, 4, 14), D(2026, 5, 10)), toEnroll);
        Assert.Equal(("overdue", D(2026, 4, 14), D(2026, 5, 10)), overdue);
    }

    [Fact]
    public async Task Age_decides_automatic_enrollment_request_or_none()
    {
        var company = await TestDb.CompanyAsync(database);
        var young = await EmployeeAsync(company, D(1996, 1, 1), "employment", D(2026, 1, 1));
        var senior = await EmployeeAsync(company, D(1966, 1, 1), "employment", D(2026, 1, 1));
        var old = await EmployeeAsync(company, D(1955, 1, 1), "employment", D(2026, 1, 1));
        var minor = await EmployeeAsync(
            company,
            D(2009, 1, 1),
            "mandate",
            D(2026, 1, 1),
            D(2026, 12, 31)
        );

        var list = await CandidatesAsync(company, D(2026, 4, 5));
        Assert.Equal("toEnroll", list[young]);
        Assert.Equal("requestOnly", list[senior]);
        Assert.False(list.ContainsKey(old));
        Assert.False(list.ContainsKey(minor));

        var requestMissing = await Assert.ThrowsAsync<PostgresException>(() =>
            EnrollAsync(senior, D(2026, 4, 10))
        );
        var tooOld = await Assert.ThrowsAsync<PostgresException>(() =>
            EnrollAsync(old, D(2026, 4, 10))
        );
        var tooEarly = await Assert.ThrowsAsync<PostgresException>(() =>
            EnrollAsync(young, D(2026, 1, 10))
        );
        var notEmployed = await Assert.ThrowsAsync<PostgresException>(() =>
            EnrollAsync(young, D(2025, 12, 1))
        );
        var badRate = await Assert.ThrowsAsync<PostgresException>(() =>
            EnrollAsync(young, D(2026, 4, 10), 0.4m)
        );
        Assert.Equal("""{"enrolledAt": ["requestMissing"]}""", requestMissing.Hint);
        Assert.Equal("""{"enrolledAt": ["tooOld"]}""", tooOld.Hint);
        Assert.Equal("""{"enrolledAt": ["tooEarly"]}""", tooEarly.Hint);
        Assert.Equal("""{"enrolledAt": ["notEmployed"]}""", notEmployed.Hint);
        Assert.Equal("""{"employeeBasicRate": ["invalid"]}""", badRate.Hint);

        await DeclareAsync(senior, "enrollmentRequest", D(2026, 4, 1));
        Assert.Equal("toEnroll", (await CandidatesAsync(company, D(2026, 4, 5)))[senior]);
        await EnrollAsync(senior, D(2026, 4, 10));
        await EnrollAsync(young, D(2026, 4, 10));
        var again = await Assert.ThrowsAsync<PostgresException>(() =>
            EnrollAsync(young, D(2026, 4, 11))
        );

        Assert.Equal("alreadyEnrolled", again.Hint);
        Assert.False((await CandidatesAsync(company, D(2026, 4, 20))).ContainsKey(young));
    }

    [Fact]
    public async Task Resignation_stops_contributions_from_its_month_and_restart_from_the_next()
    {
        var company = await TestDb.CompanyAsync(database);
        var employee = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 1, 1));
        await EnrollAsync(employee, D(2026, 4, 10));

        await DeclareAsync(employee, "resignation", D(2026, 6, 20));
        var twice = await Assert.ThrowsAsync<PostgresException>(() =>
            DeclareAsync(employee, "resignation", D(2026, 7, 1))
        );
        await DeclareAsync(employee, "restartRequest", D(2026, 9, 3));
        var notResigned = await Assert.ThrowsAsync<PostgresException>(() =>
            DeclareAsync(employee, "restartRequest", D(2026, 10, 5))
        );

        Assert.Equal("alreadyResigned", twice.Hint);
        Assert.Equal("notResigned", notResigned.Hint);
        Assert.Equal("notEnrolled", await StatusAsync(employee, D(2026, 4, 9)));
        Assert.Equal("active", await StatusAsync(employee, D(2026, 5, 31)));
        Assert.Equal("resigned", await StatusAsync(employee, D(2026, 6, 1)));
        Assert.Equal("resigned", await StatusAsync(employee, D(2026, 9, 30)));
        Assert.Equal("active", await StatusAsync(employee, D(2026, 10, 1)));
        Assert.Equal((2m, 0m, 1.5m, 0m), await RatesAsync(employee, D(2026, 5, 31)));
        Assert.Equal((0m, 0m, 0m, 0m), await RatesAsync(employee, D(2026, 6, 1)));
        Assert.Equal((2m, 0m, 1.5m, 0m), await RatesAsync(employee, D(2026, 10, 1)));
        Assert.Equal(
            [(D(2026, 10, 1), null), (D(2026, 4, 10), D(2026, 9, 30))],
            await PeriodsAsync(employee)
        );

        // Rezygnacja przed zapisem zdejmuje z listy „do zapisu” (R10).
        var other = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 1, 1));
        Assert.True((await CandidatesAsync(company, D(2026, 4, 20))).ContainsKey(other));
        await DeclareAsync(other, "resignation", D(2026, 3, 20));
        Assert.False((await CandidatesAsync(company, D(2026, 4, 20))).ContainsKey(other));
    }

    [Fact]
    public async Task Reduced_and_additional_rates_start_next_month_and_delete_rebuilds_periods()
    {
        var company = await TestDb.CompanyAsync(database);
        await SettingsAsync(company, D(2026, 6, 1), 0.5m);
        var employee = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 1, 1));
        var notParticipant = await EmployeeAsync(
            company,
            D(1990, 1, 1),
            "employment",
            D(2026, 1, 1)
        );
        await EnrollAsync(employee, D(2026, 4, 10));

        await DeclareAsync(employee, "reducedRate", D(2026, 5, 5), 1m);
        var additional = await DeclareAsync(employee, "additionalRate", D(2026, 5, 20), 1.5m);
        var tooLow = await Assert.ThrowsAsync<PostgresException>(() =>
            DeclareAsync(employee, "reducedRate", D(2026, 5, 21), 0.3m)
        );
        var rateOnResignation = await Assert.ThrowsAsync<PostgresException>(() =>
            DeclareAsync(employee, "resignation", D(2026, 5, 21), 1m)
        );
        var future = await Assert.ThrowsAsync<PostgresException>(() =>
            DeclareAsync(employee, "resignation", DateOnly.FromDateTime(DateTime.Today).AddDays(5))
        );
        var notEnrolled = await Assert.ThrowsAsync<PostgresException>(() =>
            DeclareAsync(notParticipant, "additionalRate", D(2026, 5, 20), 1m)
        );

        Assert.Equal("""{"rate": ["invalid"]}""", tooLow.Hint);
        Assert.Equal("""{"rate": ["invalid"]}""", rateOnResignation.Hint);
        Assert.Equal("""{"submittedAt": ["future"]}""", future.Hint);
        Assert.Equal("notEnrolled", notEnrolled.Hint);
        Assert.Equal((2m, 0m, 1.5m, 0m), await RatesAsync(employee, D(2026, 5, 31)));
        Assert.Equal((1m, 1.5m, 1.5m, 0.5m), await RatesAsync(employee, D(2026, 6, 1)));
        Assert.Equal(
            [(D(2026, 6, 1), null), (D(2026, 4, 10), D(2026, 5, 31))],
            await PeriodsAsync(employee)
        );

        var stale = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync("CALL ppk_app.declaration_delete($1, $2)", additional, 1L)
        );
        await ExecAsync(
            "CALL ppk_app.declaration_delete($1, $2)",
            additional,
            await DeclarationVersionAsync(employee, additional)
        );

        Assert.Equal("U0003", stale.SqlState);
        Assert.Equal((1m, 0m, 1.5m, 0.5m), await RatesAsync(employee, D(2026, 6, 1)));
        Assert.Equal(
            2L,
            await AdminAsync<long>(
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'ppk_declaration' AND entity_id = $1",
                additional.ToString()
            )
        );
    }

    [Fact]
    public async Task Mistaken_participation_is_deleted_with_rate_periods_and_declarations_stay()
    {
        var company = await TestDb.CompanyAsync(database);
        var employee = await EmployeeAsync(company, D(1990, 1, 1), "employment", D(2026, 1, 1));
        await EnrollAsync(employee, D(2026, 4, 10));
        await DeclareAsync(employee, "additionalRate", D(2026, 5, 20), 1m);
        var participation = await AdminAsync<Guid>(
            "SELECT id FROM ppk.participations WHERE employee_id = $1",
            employee
        );
        var version = await ScalarAsync<long>(
            "SELECT row_version FROM ppk_app.participation_get($1)",
            employee
        );

        var stale = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync("CALL ppk_app.participation_delete($1, $2)", employee, version + 1)
        );
        var pracownik = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>(
                "pracownik",
                "CALL ppk_app.participation_delete($1, $2)",
                employee,
                version
            )
        );
        await ExecAsync("CALL ppk_app.participation_delete($1, $2)", employee, version);
        var missing = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync("CALL ppk_app.participation_delete($1, $2)", employee, version)
        );

        Assert.Equal("U0003", stale.SqlState);
        Assert.Equal("U0004", pracownik.SqlState);
        Assert.Equal("U0002", missing.SqlState);
        Assert.Equal("notEnrolled", await StatusAsync(employee, D(2026, 6, 1)));
        Assert.Empty(await PeriodsAsync(employee));
        Assert.Equal(
            1L,
            await ScalarAsync<long>("SELECT count(*) FROM ppk_app.declaration_list($1)", employee)
        );
        Assert.Equal(
            1L,
            await AdminAsync<long>(
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'ppk_participation' AND entity_id = $1 AND action = 'delete'",
                participation.ToString()
            )
        );

        // Ponowny zapis z inną datą odbudowuje okresy z zachowanej deklaracji.
        await EnrollAsync(employee, D(2026, 4, 15));
        Assert.Equal(
            [(D(2026, 6, 1), null), (D(2026, 4, 15), D(2026, 5, 31))],
            await PeriodsAsync(employee)
        );
    }

    [Fact]
    public async Task Company_settings_periods_start_on_first_day_and_do_not_overlap()
    {
        var company = await TestDb.CompanyAsync(database);

        await SettingsAsync(company, D(2026, 1, 1), 0m);
        var midMonth = await Assert.ThrowsAsync<PostgresException>(() =>
            SettingsAsync(company, D(2026, 3, 15), 1m)
        );
        var before = await Assert.ThrowsAsync<PostgresException>(() =>
            SettingsAsync(company, D(2025, 12, 1), 1m)
        );
        var tooHigh = await Assert.ThrowsAsync<PostgresException>(() =>
            SettingsAsync(company, D(2026, 6, 1), 2.6m)
        );
        await SettingsAsync(company, D(2026, 6, 1), 2.5m);

        Assert.Equal("""{"validFrom": ["invalid"]}""", midMonth.Hint);
        Assert.Equal("""{"validFrom": ["beforeCurrent"]}""", before.Hint);
        Assert.Equal("""{"employerAdditionalRate": ["invalid"]}""", tooHigh.Hint);
        Assert.Equal(
            D(2026, 5, 31),
            await ScalarAsync<DateOnly>(
                "SELECT valid_to FROM ppk_app.company_settings_list($1) WHERE valid_from = DATE '2026-01-01'",
                company
            )
        );
        Assert.Equal(
            2.5m,
            await ScalarAsync<decimal>(
                "SELECT employer_additional_rate FROM ppk_app.company_settings_list($1) WHERE valid_to IS NULL",
                company
            )
        );
    }

    [Fact]
    public async Task Reenrollment_2027_splits_by_age_and_2023_cycle_restarts_from_april()
    {
        var company = await TestDb.CompanyAsync(database);
        var young = await EmployeeAsync(company, D(1990, 3, 1), "employment", D(2022, 1, 1));
        var turning55 = await EmployeeAsync(company, D(1972, 2, 10), "employment", D(2022, 1, 1));
        var over70 = await EmployeeAsync(company, D(1956, 6, 1), "employment", D(2022, 1, 1));
        var resignsAgain = await EmployeeAsync(company, D(1991, 1, 1), "employment", D(2022, 1, 1));
        var resignsAfterApply = await EmployeeAsync(
            company,
            D(1992, 1, 1),
            "employment",
            D(2022, 1, 1)
        );
        var active = await EmployeeAsync(company, D(1993, 1, 1), "employment", D(2022, 1, 1));
        await EnrollAsync(young, D(2022, 4, 10));
        foreach (
            var employee in new[] { young, turning55, over70, resignsAgain, resignsAfterApply }
        )
        {
            await DeclareAsync(employee, "resignation", D(2022, 5, 10));
        }

        var list2027 = await ReenrollmentAsync(company, 2027);
        var notCycle = await Assert.ThrowsAsync<PostgresException>(() =>
            ReenrollmentAsync(company, 2025)
        );
        Assert.Equal("restart", list2027[young]);
        // Urodzony 10.02.1972: 55 lat w dniu informacji 28.02.2027 — tylko na wniosek.
        Assert.Equal("requestOnly", list2027[turning55]);
        Assert.Equal("noRestart", list2027[over70]);
        Assert.False(list2027.ContainsKey(active));
        Assert.Equal("""{"year": ["notCycleYear"]}""", notCycle.Hint);

        var notInformed = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyAsync(company, 2023)
        );
        var lateInformation = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(
                "CALL ppk_app.reenrollment_mark_informed($1, 2023, DATE '2023-04-01')",
                company
            )
        );
        await ExecAsync(
            "CALL ppk_app.reenrollment_mark_informed($1, 2023, DATE '2023-02-20')",
            company
        );
        await DeclareAsync(resignsAgain, "resignation", D(2023, 3, 1));
        var restarted = await ApplyAsync(company, 2023);
        var again = await ApplyAsync(company, 2023);
        await DeclareAsync(resignsAfterApply, "resignation", D(2023, 3, 20));
        var afterApply = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(
                "CALL ppk_app.reenrollment_mark_informed($1, 2023, DATE '2023-02-21')",
                company
            )
        );

        Assert.Equal("notInformed", notInformed.Hint);
        Assert.Equal("""{"informedAt": ["invalid"]}""", lateInformation.Hint);
        Assert.Equal("applied", afterApply.Hint);
        // young, turning55 (51 lat w 2023) i resignsAfterApply; resignsAgain — rezygnacja po informacji.
        Assert.Equal(3, restarted);
        Assert.Equal(0, again);
        Assert.Equal("resigned", await StatusAsync(young, D(2023, 3, 31)));
        Assert.Equal("active", await StatusAsync(young, D(2023, 4, 1)));
        Assert.Equal((2m, 0m, 1.5m, 0m), await RatesAsync(young, D(2023, 4, 1)));
        Assert.Equal(D(2023, 4, 1), (await PeriodsAsync(young))[0].From);
        Assert.Equal("resigned", await StatusAsync(resignsAgain, D(2023, 4, 1)));
        // Rezygnacja po informacji usuwa już wykonane wznowienie.
        Assert.Equal("resigned", await StatusAsync(resignsAfterApply, D(2023, 4, 1)));
        Assert.Equal("notEnrolled", await StatusAsync(turning55, D(2023, 4, 1)));
        Assert.Equal("requestOnly", (await ReenrollmentAsync(company, 2023))[over70]);
        var list2023 = await ReenrollmentAsync(company, 2023, "resigned_after_information");
        Assert.Equal("true", list2023[resignsAgain]);
        Assert.Equal("true", list2023[resignsAfterApply]);
        Assert.Equal("false", list2023[young]);
    }

    [Fact]
    public async Task Pracownik_reads_own_ppk_only_and_dev_data_matches_r16()
    {
        var own = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT l.employee_id FROM platform.user_employee_links l JOIN platform.users u ON u.id = l.user_id WHERE u.login = 'pracownik' LIMIT 1"
        );
        var resigned = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT e.id FROM employees.employees e JOIN platform.companies c ON c.id = e.company_id WHERE c.short_name = 'Demo' AND e.number = '2'"
        );

        Assert.Equal(
            "active 2026-04-10 2.00 0.00 1.50 0.00",
            await AsAsync<string>(
                "pracownik",
                """
                SELECT status || ' ' || enrolled_at || ' ' || employee_basic_rate || ' ' || employee_additional_rate
                       || ' ' || employer_basic_rate || ' ' || employer_additional_rate
                FROM ppk_app.participation_get($1)
                """,
                own
            )
        );
        Assert.Equal(
            "resigned",
            await ScalarAsync<string>("SELECT status FROM ppk_app.participation_get($1)", resigned)
        );
        var foreign = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<string>(
                "pracownik",
                "SELECT status FROM ppk_app.participation_get($1)",
                resigned
            )
        );
        var write = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>(
                "pracownik",
                "CALL ppk_app.declaration_create($1, 'resignation', DATE '2026-11-02', NULL, NULL)",
                own
            )
        );
        var demo = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT id FROM platform.companies WHERE short_name = 'Demo'"
        );
        var settings = await Assert.ThrowsAsync<PostgresException>(() =>
            AsAsync<object>(
                "pracownik",
                "SELECT count(*) FROM ppk_app.company_settings_list($1)",
                demo
            )
        );

        Assert.Equal("U0004", foreign.SqlState);
        Assert.Equal("U0004", write.SqlState);
        Assert.Equal("U0004", settings.SqlState);
    }

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private async Task<Guid> EmployeeAsync(
        Guid company,
        DateOnly birthDate,
        string kind,
        DateOnly start,
        DateOnly? end = null,
        string[]? insurances = null
    )
    {
        var employee = await AdminAsync<Guid>(
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, left(gen_random_uuid()::text, 20), 'Jan', 'Kowalski', $2) RETURNING id
            """,
            company,
            birthDate
        );
        await EmploymentAsync(employee, company, kind, start, end, insurances);
        return employee;
    }

    private async Task EmploymentAsync(
        Guid employee,
        Guid company,
        string kind,
        DateOnly start,
        DateOnly? end,
        string[]? insurances = null
    )
    {
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
            VALUES ($1, $2, CASE WHEN $3 = 'employment' THEN 1 END, CASE WHEN $3 = 'employment' THEN 1 END,
                    CASE WHEN $3 = 'employment' THEN 'monthly' ELSE 'hourly' END, 6000, $4, 'standard')
            """,
            employment,
            start,
            kind,
            kind == "specific_work" ? Student : insurances ?? Insured
        );
    }

    private async Task<bool> EmployedAsync(Guid employee, DateOnly on) =>
        await AdminAsync<bool>("SELECT ppk.is_employed_person($1, $2)", employee, on);

    private async Task<DateOnly> ReachedAsync(Guid employee, DateOnly on) =>
        await AdminAsync<DateOnly>("SELECT ppk.three_months_reached_on($1, $2)", employee, on);

    private async Task<string> StatusAsync(Guid employee, DateOnly on) =>
        (await AdminAsync<string>("SELECT ppk.status_on($1, $2)", employee, on))!;

    private async Task<(decimal, decimal, decimal, decimal)> RatesAsync(Guid employee, DateOnly on)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = TestDb.Command(
            connection,
            "SELECT * FROM ppk.rates_on($1, $2)",
            new NpgsqlParameter { Value = employee },
            new NpgsqlParameter { Value = on }
        );
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3)
        );
    }

    private async Task<List<(DateOnly From, DateOnly? To)>> PeriodsAsync(Guid employee)
    {
        await using var connection = await KadryAsync();
        await using var command = TestDb.Command(
            connection,
            "SELECT valid_from, valid_to FROM ppk_app.rate_period_list($1)",
            new NpgsqlParameter { Value = employee }
        );
        await using var reader = await command.ExecuteReaderAsync();
        var periods = new List<(DateOnly, DateOnly?)>();
        while (await reader.ReadAsync())
        {
            periods.Add(
                (
                    reader.GetFieldValue<DateOnly>(0),
                    reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1)
                )
            );
        }

        return periods;
    }

    private async Task<(string, DateOnly, DateOnly)> CandidateAsync(
        Guid company,
        Guid employee,
        DateOnly on
    )
    {
        await using var connection = await KadryAsync();
        await using var command = TestDb.Command(
            connection,
            "SELECT status, reached_on, deadline FROM ppk_app.enrollment_candidates($1, $2) WHERE employee_id = $3",
            new NpgsqlParameter { Value = company },
            new NpgsqlParameter { Value = on },
            new NpgsqlParameter { Value = employee }
        );
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetString(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetFieldValue<DateOnly>(2)
        );
    }

    private async Task<Dictionary<Guid, string>> CandidatesAsync(Guid company, DateOnly on) =>
        await PairsAsync(
            "SELECT employee_id, status FROM ppk_app.enrollment_candidates($1, $2)",
            company,
            on
        );

    private async Task<Dictionary<Guid, string>> ReenrollmentAsync(
        Guid company,
        int year,
        string column = "group_kind"
    ) =>
        await PairsAsync(
            $"SELECT employee_id, {column}::text FROM ppk_app.reenrollment_list($1, $2)",
            company,
            year
        );

    private async Task<Dictionary<Guid, string>> PairsAsync(string sql, params object[] args)
    {
        await using var connection = await KadryAsync();
        await using var command = TestDb.Command(connection, sql);
        foreach (var arg in args)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = arg });
        }

        await using var reader = await command.ExecuteReaderAsync();
        var pairs = new Dictionary<Guid, string>();
        while (await reader.ReadAsync())
        {
            pairs[reader.GetGuid(0)] = reader.GetString(1);
        }

        return pairs;
    }

    private async Task<int> ApplyAsync(Guid company, int year) =>
        await ScalarAsync<int>("CALL ppk_app.reenrollment_apply($1, $2, NULL)", company, year);

    private async Task EnrollAsync(Guid employee, DateOnly enrolledAt, decimal basicRate = 2m) =>
        await ExecAsync(
            "CALL ppk_app.participation_create($1, $2, NULL, $3, 0, NULL)",
            employee,
            enrolledAt,
            basicRate
        );

    private async Task<Guid> DeclareAsync(
        Guid employee,
        string kind,
        DateOnly submittedAt,
        decimal? rate = null
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL ppk_app.declaration_create($1, $2, $3, $4, NULL)",
                    employee,
                    kind,
                    submittedAt,
                    rate
                )
            )!;

    private async Task<long> DeclarationVersionAsync(Guid employee, Guid id) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM ppk_app.declaration_list($1) WHERE id = $2",
            employee,
            id
        );

    private async Task SettingsAsync(Guid company, DateOnly validFrom, decimal rate) =>
        await ExecAsync(
            "CALL ppk_app.company_settings_set($1, $2, 'TFI Testowe', 'PPK/1', DATE '2025-12-01', $3, NULL)",
            company,
            validFrom,
            rate
        );

    private async Task<NpgsqlConnection> KadryAsync() =>
        await TestDb.ApiAsync(database, await TestDb.DevUserAsync(database, "kadry"));

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
