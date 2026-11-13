using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>Listy płac (009): zakładanie (R1, R3), statusy i nieaktualność (R2), blokady po zamknięciu (R25), role (R26).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PayrollRoutineTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task Create_numbers_regular_lists_and_rejects_duplicates_and_bad_input()
    {
        var company = await _kit.CompanyAsync();
        var payroll = await _kit.PayrollAsync(
            company,
            "employment",
            D(2026, 10, 15),
            D(2026, 11, 12)
        );
        var mandate = await _kit.PayrollAsync(company, "mandate", D(2026, 10, 1), D(2026, 10, 30));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PayrollAsync(company, "employment", D(2026, 10, 1), D(2026, 10, 30))
        );
        var invalid = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PayrollAsync(company, "bonus", D(2026, 10, 1), D(2026, 10, 30))
        );
        var early = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PayrollAsync(company, "specificWork", D(2026, 10, 1), D(2026, 9, 30))
        );
        var missingCorrected = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PayrollAsync(company, "correction", D(2026, 10, 1), D(2026, 10, 30))
        );

        Assert.Equal("""{"period": ["duplicate"]}""", duplicate.Hint);
        Assert.Equal("""{"kind": ["invalid"]}""", invalid.Hint);
        Assert.Equal("""{"paymentDate": ["beforeStart"]}""", early.Hint);
        Assert.Equal("""{"correctsPayrollId": ["required"]}""", missingCorrected.Hint);
        Assert.Equal(
            "2026/10/UP",
            await _kit.KadryAsync<string>("SELECT number FROM payroll_app.payroll_get($1)", payroll)
        );
        Assert.Equal(
            "2026/10/UZ",
            await _kit.KadryAsync<string>("SELECT number FROM payroll_app.payroll_get($1)", mandate)
        );
        Assert.Equal(
            D(2026, 10, 31),
            await _kit.KadryAsync<DateOnly>(
                "SELECT period_to FROM payroll_app.payroll_get($1)",
                payroll
            )
        );
        // Wypłata 12.11 — po 10. dniu następnego miesiąca (KP art. 85 § 2): ostrzeżenie, nie błąd.
        Assert.True(
            await _kit.KadryAsync<bool>(
                "SELECT payment_date_late FROM payroll_app.payroll_get($1)",
                payroll
            )
        );
        Assert.Equal(
            2L,
            await _kit.KadryAsync<long>(
                "SELECT count(*) FROM payroll_app.payroll_list($1, 2026)",
                company
            )
        );
    }

    [Fact]
    public async Task Status_flow_open_calculated_closed_with_delete_only_before_closing()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Status", D(1990, 1, 1), "90010112345");
        await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var payroll = await _kit.PayrollAsync(
            company,
            "employment",
            D(2026, 10, 1),
            D(2026, 10, 30)
        );

        var notCalculated = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.CloseAsync(payroll)
        );
        await _kit.CalculateAsync(payroll);
        await _kit.CalculateAsync(payroll);
        await _kit.CloseAsync(payroll);
        var recalculate = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.CalculateAsync(payroll)
        );
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>("CALL payroll_app.payroll_delete($1, NULL)", payroll)
        );
        var other = await _kit.PayrollAsync(company, "employment", D(2026, 11, 1), D(2026, 11, 30));
        await _kit.KadryAsync<object>("CALL payroll_app.payroll_delete($1, NULL)", other);
        var gone = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>("SELECT * FROM payroll_app.payroll_get($1)", other)
        );
        var staleVersion = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>("CALL payroll_app.payroll_calculate($1, 1)", payroll)
        );

        Assert.Equal("notCalculated", notCalculated.Hint);
        Assert.Equal("closed", recalculate.Hint);
        Assert.Equal("closed", delete.Hint);
        Assert.Equal("U0002", gone.SqlState);
        Assert.Equal("U0003", staleVersion.SqlState);
        Assert.Equal(
            "closed",
            await _kit.KadryAsync<string>("SELECT status FROM payroll_app.payroll_get($1)", payroll)
        );
        Assert.Equal(
            1L,
            await _kit.KadryAsync<long>("SELECT count(*) FROM payroll_app.entry_list($1)", payroll)
        );
    }

    [Fact]
    public async Task Excluded_contract_is_not_calculated_and_changes_make_a_calculated_list_stale()
    {
        var company = await _kit.CompanyAsync();
        var first = await _kit.EmployeeAsync(company, "Pierwszy", D(1990, 1, 1), "90010112345");
        var firstEmployment = await _kit.EmploymentAsync(
            first,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var second = await _kit.EmployeeAsync(company, "Drugi", D(1990, 1, 1), "90010112352");
        var secondEmployment = await _kit.EmploymentAsync(
            second,
            "employment",
            D(2026, 1, 1),
            D(2026, 9, 30),
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var payroll = await _kit.MonthAsync(company, 10, close: false);
        Assert.True(await _kit.HasEntryAsync(payroll, firstEmployment));
        Assert.False(await _kit.HasEntryAsync(payroll, secondEmployment));

        await _kit.KadryAsync<object>(
            "CALL payroll_app.payroll_members_set($1, NULL, $2, true)",
            payroll,
            new[] { firstEmployment }
        );
        var stale = await Assert.ThrowsAsync<PostgresException>(() => _kit.CloseAsync(payroll));
        await _kit.CalculateAsync(payroll);
        Assert.False(await _kit.HasEntryAsync(payroll, firstEmployment));
        var notMember = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>(
                "CALL payroll_app.payroll_members_set($1, NULL, $2, false)",
                payroll,
                new[] { secondEmployment }
            )
        );

        await _kit.KadryAsync<object>(
            "CALL payroll_app.payroll_members_set($1, NULL, $2, false)",
            payroll,
            new[] { firstEmployment }
        );
        await _kit.CalculateAsync(payroll);
        await _kit.KadryAsync<object>(
            "CALL time_absence_app.absence_create($1, 'UB', DATE '2026-10-05', DATE '2026-10-05', NULL, NULL, NULL)",
            firstEmployment
        );

        Assert.Equal("stale", stale.Hint);
        Assert.Equal("""{"employmentIds": ["notFound"]}""", notMember.Hint);
        Assert.True(
            await _kit.KadryAsync<bool>("SELECT is_stale FROM payroll_app.payroll_get($1)", payroll)
        );
        await _kit.CalculateAsync(payroll);
        Assert.Equal(
            -272.73m,
            (await _kit.EntryAsync(payroll, firstEmployment)).Amount("ZASAD-NB")
        );
        await _kit.CloseAsync(payroll);
    }

    [Fact]
    public async Task Closed_list_blocks_source_changes_and_deleting_used_data()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Blokada", D(1990, 1, 1), "90010112345");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.PpkAsync(employee, D(2026, 4, 10));
        var mandate = await _kit.EmploymentAsync(
            employee,
            "mandate",
            D(2026, 10, 1),
            D(2026, 10, 31),
            "hourly",
            40m,
            PayrollKit.MandateInsurances,
            "flat20"
        );
        await _kit.MandateHoursAsync(mandate, D(2026, 10, 1), D(2026, 10, 30), 10m);
        await _kit.MonthAsync(company, 10);
        await _kit.MonthAsync(company, 10, "mandate");

        var absence = await BlockedAsync(
            "CALL time_absence_app.absence_create($1, 'UB', DATE '2026-10-05', DATE '2026-10-05', NULL, NULL, NULL)",
            employment
        );
        var overtime = await BlockedAsync(
            "CALL time_absence_app.overtime_add($1, DATE '2026-10-06', 2, 50, NULL)",
            employment
        );
        var hours = await BlockedAsync(
            "CALL time_absence_app.mandate_hours_add($1, DATE '2026-10-31', DATE '2026-10-31', 1, NULL, NULL, NULL)",
            mandate
        );
        var declaration = await BlockedAsync(
            "CALL tax_declarations_app.declaration_create($1, 'increasedCosts', NULL, NULL, NULL, DATE '2026-10-05', DATE '2026-10-01', NULL, NULL)",
            employee
        );
        var deleteEmployment = await BlockedAsync(
            "CALL employment_app.employment_delete($1)",
            employment
        );
        var version = await _kit.AdminAsync<long>(
            "SELECT xmin::text::bigint FROM ppk.participations WHERE employee_id = $1",
            employee
        );
        var deletePpk = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>("CALL ppk_app.participation_delete($1, $2)", employee, version)
        );

        Assert.Equal("payrollClosed", absence.Hint);
        Assert.Equal("payrollClosed", overtime.Hint);
        Assert.Equal("payrollClosed", hours.Hint);
        Assert.Equal("payrollClosed", declaration.Hint);
        Assert.Equal("payrollClosed", deleteEmployment.Hint);
        Assert.Equal("payrollClosed", deletePpk.Hint);
        // Poza okresem zamkniętej listy zmiany są dozwolone.
        await _kit.KadryAsync<object>(
            "CALL time_absence_app.absence_create($1, 'UB', DATE '2026-11-05', DATE '2026-11-05', NULL, NULL, NULL)",
            employment
        );
    }

    [Fact]
    public async Task Moving_contract_dates_into_a_closed_period_is_rejected_unless_corrected()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Daty", D(1990, 1, 1), "90010112345");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var october = await _kit.MonthAsync(company, 10);
        var november = await _kit.MonthAsync(company, 11, close: false);

        var start = await Assert.ThrowsAsync<PostgresException>(async () =>
            await _kit.KadryAsync<object>(
                "CALL employment_app.employment_update($1, $2, $3, 'indefinite', DATE '2026-01-01', DATE '2026-10-05', NULL)",
                employment,
                await VersionAsync(employment),
                await _kit.AdminAsync<string>(
                    "SELECT number FROM employment.employments WHERE id = $1",
                    employment
                )
            )
        );
        var end = await Assert.ThrowsAsync<PostgresException>(async () =>
            await TerminateAsync(employment, D(2026, 10, 20))
        );
        Assert.Equal("payrollClosed", start.Hint);
        Assert.Equal("payrollClosed", end.Hint);
        Assert.False(await StaleAsync(november));

        await TerminateAsync(employment, D(2026, 11, 20));
        Assert.True(await StaleAsync(november));

        // Przy otwartej korekcie października koniec umowy może wejść w październik.
        await _kit.PayrollAsync(company, "correction", D(2026, 10, 1), D(2026, 11, 10), october);
        await TerminateAsync(employment, D(2026, 10, 20));
        Assert.Equal(
            D(2026, 10, 20),
            await _kit.AdminAsync<DateOnly>(
                "SELECT terminated_at FROM employment.employments WHERE id = $1",
                employment
            )
        );
    }

    private async Task<long> VersionAsync(Guid employment) =>
        await _kit.AdminAsync<long>(
            "SELECT xmin::text::bigint FROM employment.employments WHERE id = $1",
            employment
        );

    private async Task TerminateAsync(Guid employment, DateOnly terminatedAt) =>
        await _kit.KadryAsync<object>(
            "CALL employment_app.employment_terminate($1, $2, $3)",
            employment,
            await VersionAsync(employment),
            terminatedAt
        );

    private async Task<bool> StaleAsync(Guid payroll) =>
        await _kit.KadryAsync<bool>("SELECT is_stale FROM payroll_app.payroll_get($1)", payroll);

    [Fact]
    public async Task Deleting_a_contract_removes_it_from_a_calculated_list_and_makes_it_stale()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Pomyłka", D(1990, 1, 1), "90010112345");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var payroll = await _kit.MonthAsync(company, 10, close: false);

        await _kit.KadryAsync<object>("CALL employment_app.employment_delete($1)", employment);

        Assert.Equal(
            0L,
            await _kit.KadryAsync<long>("SELECT count(*) FROM payroll_app.entry_list($1)", payroll)
        );
        Assert.True(
            await _kit.KadryAsync<bool>("SELECT is_stale FROM payroll_app.payroll_get($1)", payroll)
        );
    }

    [Fact]
    public async Task Employment_list_needs_a_company_calendar()
    {
        var company = await TestDb.CompanyAsync(database);
        var payroll = await _kit.PayrollAsync(
            company,
            "employment",
            D(2026, 10, 1),
            D(2026, 10, 30)
        );

        var missing = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.CalculateAsync(payroll)
        );

        Assert.Equal("calendarMissing", missing.Hint);
    }

    [Fact]
    public async Task Entry_details_show_lines_with_trace_and_audit_the_read()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Ślad", D(1990, 1, 1), "90010112345");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var payroll = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(payroll, employment);

        var trace = await _kit.KadryAsync<string>(
            "SELECT trace::text FROM payroll_app.entry_lines($1) WHERE code = 'ZUS-E-P'",
            entry.Id
        );
        var lines = await _kit.KadryAsync<long>(
            "SELECT count(*) FROM payroll_app.entry_lines($1)",
            entry.Id
        );
        var audited = await _kit.AdminAsync<long>(
            "SELECT count(*) FROM platform.read_audit_entries WHERE resource = 'payslip' AND resource_id = $1",
            entry.Id.ToString()
        );

        Assert.Contains("zus.emerytalna.pracownik", trace);
        Assert.Contains("yearPensionBaseBefore", trace);
        Assert.True(lines > 10);
        // Dwa odczyty pozycji — dwa wpisy audytu.
        Assert.Equal(2L, audited);
        Assert.Equal(
            6000.00m,
            await _kit.KadryAsync<decimal>("SELECT gross FROM payroll_app.entry_get($1)", entry.Id)
        );
    }

    [Fact]
    public async Task Payroll_routines_are_for_kadry_and_administrator_only()
    {
        var company = await _kit.CompanyAsync();

        var list = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.AsAsync<object>(
                "pracownik",
                "SELECT count(*) FROM payroll_app.payroll_list($1, 2026)",
                company
            )
        );
        var create = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.AsAsync<object>(
                "pracownik",
                "CALL payroll_app.payroll_create($1, 'employment', DATE '2026-10-01', DATE '2026-10-30', NULL, NULL)",
                company
            )
        );
        await _kit.AsAsync<object>(
            "admin",
            "CALL payroll_app.payroll_create($1, 'employment', DATE '2026-10-01', DATE '2026-10-30', NULL, NULL)",
            company
        );

        Assert.Equal("U0004", list.SqlState);
        Assert.Equal("U0004", create.SqlState);
    }

    private async Task<PostgresException> BlockedAsync(string sql, Guid id) =>
        await Assert.ThrowsAsync<PostgresException>(() => _kit.KadryAsync<object>(sql, id));
}
