using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

/// <summary>Wypłata umowy na liście: kwoty, sumy pozycji po kodzie i ostrzeżenia.</summary>
public sealed record PayrollEntry(
    Guid Id,
    decimal Gross,
    decimal Net,
    decimal Payout,
    IReadOnlyDictionary<string, decimal> Amounts,
    IReadOnlyDictionary<string, decimal?> Bases,
    IReadOnlyList<string> Warnings
)
{
    public decimal Amount(string code) => Amounts.GetValueOrDefault(code);
}

/// <summary>
/// Aranżacja i operacje testów list płac (009): dane źródłowe wpisywane wprost (superużytkownik), listy przez rutyny
/// kontraktu jako Kadry.
/// </summary>
public sealed class PayrollKit(DatabaseFixture database)
{
    public static readonly string[] EmploymentInsurances =
    [
        "pension",
        "disability",
        "sickness",
        "accident",
        "health",
        "fp",
        "fgsp",
    ];

    public static readonly string[] MandateInsurances =
    [
        "pension",
        "disability",
        "sickness",
        "accident",
        "health",
    ];

    public static readonly string[] NoInsurances = [];

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public async Task<Guid> CompanyAsync()
    {
        var company = await TestDb.CompanyAsync(database);
        await KadryAsync<object>(
            "CALL time_absence_app.calendar_generate($1, 2026, NULL)",
            company
        );
        return company;
    }

    public async Task<Guid> EmployeeAsync(
        Guid company,
        string lastName,
        DateOnly birthDate,
        string? pesel = null
    ) =>
        await AdminAsync<Guid>(
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date, pesel_enc,
                                             pesel_hmac)
            VALUES ($1, left(gen_random_uuid()::text, 20), 'Test', $2, $3,
                    CASE WHEN $4::text IS NOT NULL THEN ext.pgp_sym_encrypt($4, $5) END,
                    CASE WHEN $4::text IS NOT NULL THEN ext.hmac($4, $5, 'sha256') END)
            RETURNING id
            """,
            company,
            lastName,
            birthDate,
            pesel,
            TestDb.DataKey
        );

    /// <summary>Umowa z pierwszą wersją warunków od początku umowy.</summary>
    public async Task<Guid> EmploymentAsync(
        Guid employee,
        string kind,
        DateOnly start,
        DateOnly? end,
        string payBasis,
        decimal amount,
        string[] insurances,
        string costKind = "standard",
        int fteNumerator = 1,
        int fteDenominator = 1
    )
    {
        var employment = await AdminAsync<Guid>(
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date, end_date)
            SELECT e.id, e.company_id, gen_random_uuid()::text, $2,
                   CASE WHEN $2 = 'employment' THEN CASE WHEN $4::date IS NULL THEN 'indefinite' ELSE 'fixed_term' END END,
                   $3, $3, $4
            FROM employees.employees e WHERE e.id = $1
            RETURNING id
            """,
            employee,
            kind,
            start,
            end
        );
        await AdminAsync<object>(
            """
            INSERT INTO employment.terms (employment_id, valid_from, fte_numerator, fte_denominator, pay_basis,
                                          base_amount, insurances, cost_kind)
            VALUES ($1, $2, CASE WHEN $3 = 'employment' THEN $4 END, CASE WHEN $3 = 'employment' THEN $5 END, $6, $7,
                    $8, $9)
            """,
            employment,
            start,
            kind,
            fteNumerator,
            fteDenominator,
            payBasis,
            amount,
            insurances,
            costKind
        );
        return employment;
    }

    /// <summary>Nowa wersja warunków od dnia z nową kwotą (pozostałe pola jak w ostatniej wersji).</summary>
    public async Task TermsAsync(Guid employment, DateOnly from, decimal amount) =>
        await AdminAsync<object>(
            """
            WITH last AS (
                UPDATE employment.terms t SET valid_to = $2::date - 1
                WHERE t.employment_id = $1 AND t.valid_to IS NULL
                RETURNING t.*
            )
            INSERT INTO employment.terms (employment_id, valid_from, fte_numerator, fte_denominator, pay_basis,
                                          base_amount, insurances, cost_kind)
            SELECT $1, $2, l.fte_numerator, l.fte_denominator, l.pay_basis, $3, l.insurances, l.cost_kind FROM last l
            """,
            employment,
            from,
            amount
        );

    public async Task DeclarationAsync(
        Guid employee,
        string kind,
        DateOnly from,
        string? share = null
    ) =>
        await AdminAsync<object>(
            """
            INSERT INTO tax_declarations.declarations (employee_id, kind, share, submitted_at, valid_from)
            VALUES ($1, $2, $3, $4, $4)
            """,
            employee,
            kind,
            share,
            from
        );

    public async Task Pit2Async(Guid employee) =>
        await DeclarationAsync(employee, "pit2", D(2026, 1, 1), "1/12");

    /// <summary>Uczestnictwo PPK ze stawkami pracownika w procentach (okres stawek wpisany wprost).</summary>
    public async Task PpkAsync(
        Guid employee,
        DateOnly enrolledAt,
        decimal basicRate = 2m,
        decimal additionalRate = 0m
    ) =>
        await AdminAsync<object>(
            """
            WITH p AS (
                INSERT INTO ppk.participations (employee_id, enrolled_at, initial_basic_rate, initial_additional_rate)
                VALUES ($1, $2, $3, $4) RETURNING id
            )
            INSERT INTO ppk.rate_periods (participation_id, valid_from, employee_basic_rate, employee_additional_rate)
            SELECT p.id, $2, $3, $4 FROM p
            """,
            employee,
            enrolledAt,
            basicRate,
            additionalRate
        );

    public async Task<Guid> AbsenceAsync(
        Guid employment,
        string type,
        DateOnly from,
        DateOnly to
    ) =>
        await AdminAsync<Guid>(
            """
            INSERT INTO time_absence.absences (employment_id, absence_type_code, date_from, date_to,
                                               certificate_number)
            VALUES ($1, $2, $3, $4, CASE WHEN $2 LIKE 'CH%' THEN 'ZLA1' END) RETURNING id
            """,
            employment,
            type,
            from,
            to
        );

    public async Task OvertimeAsync(Guid employment, DateOnly date, decimal hours, int rate) =>
        await AdminAsync<object>(
            "INSERT INTO time_absence.overtime_entries (employment_id, date, hours, rate) VALUES ($1, $2, $3, $4)",
            employment,
            date,
            hours,
            rate
        );

    public async Task MandateHoursAsync(
        Guid employment,
        DateOnly from,
        DateOnly to,
        decimal hours,
        bool confirmed = true
    ) =>
        await AdminAsync<object>(
            """
            INSERT INTO time_absence.mandate_hours (employment_id, date_from, date_to, hours, confirmed_at,
                                                    confirmation_form)
            VALUES ($1, $2, $3, $4, CASE WHEN $5 THEN $3 END, CASE WHEN $5 THEN 'written' END)
            """,
            employment,
            from,
            to,
            hours,
            confirmed
        );

    /// <summary>Składnik użytkownika (unikalny kod) — dodatek z cechami albo potrącenie z kategorią.</summary>
    public async Task<Guid> ComponentTypeAsync(
        string kind,
        string calculation,
        string? deductionCategory = null
    ) =>
        await AdminAsync<Guid>(
            """
            INSERT INTO pay_components.component_types (code, name, kind, calculation, in_zus_base, in_health_base,
                                                        taxable, in_sickness_base, deduction_category)
            VALUES ($1, 'Składnik testowy', $2, $3,
                    CASE WHEN $2 = 'addition' THEN true END, CASE WHEN $2 = 'addition' THEN true END,
                    CASE WHEN $2 = 'addition' THEN true END, CASE WHEN $2 = 'addition' THEN true END, $4)
            RETURNING id
            """,
            Unique.Code(),
            kind,
            calculation,
            deductionCategory
        );

    public async Task<Guid> ComponentAsync(
        Guid employment,
        Guid componentType,
        decimal value,
        DateOnly from,
        DateOnly? to = null,
        Guid? consent = null
    ) =>
        await AdminAsync<Guid>(
            """
            INSERT INTO pay_components.employment_components (employment_id, component_type_id, value, valid_from,
                                                              valid_to, consent_id)
            VALUES ($1, $2, $3, $4, $5, $6) RETURNING id
            """,
            employment,
            componentType,
            value,
            from,
            to,
            consent
        );

    public async Task<Guid> ConsentAsync(
        Guid employee,
        Guid componentType,
        decimal amount,
        DateOnly from
    ) =>
        await AdminAsync<Guid>(
            """
            INSERT INTO pay_components.consents (employee_id, component_type_id, monthly_amount, valid_from, signed_at)
            VALUES ($1, $2, $3, $4, $4) RETURNING id
            """,
            employee,
            componentType,
            amount,
            from
        );

    public async Task<Guid> GarnishmentAsync(
        Guid employee,
        string kind,
        DateOnly receivedAt,
        decimal? total,
        decimal? monthly = null
    ) =>
        await AdminAsync<Guid>(
            """
            INSERT INTO pay_components.garnishments (employee_id, kind, authority_kind, authority_name, case_number,
                                                     received_at, monthly_amount, total_amount)
            VALUES ($1, $2, 'court', 'Komornik Testowy', gen_random_uuid()::text, $3, $4, $5) RETURNING id
            """,
            employee,
            kind,
            receivedAt,
            monthly,
            total
        );

    public async Task<Guid> PayrollAsync(
        Guid company,
        string kind,
        DateOnly period,
        DateOnly paymentDate,
        Guid? corrects = null
    ) =>
        (Guid)
            (
                await KadryAsync<object>(
                    "CALL payroll_app.payroll_create($1, $2, $3, $4, $5, NULL)",
                    company,
                    kind,
                    period,
                    paymentDate,
                    corrects
                )
            )!;

    public async Task CalculateAsync(Guid payroll) =>
        await KadryAsync<object>("CALL payroll_app.payroll_calculate($1, NULL)", payroll);

    public async Task CloseAsync(Guid payroll) =>
        await KadryAsync<object>("CALL payroll_app.payroll_close($1, NULL)", payroll);

    /// <summary>Data przekazania wpłat PPK zamkniętej listy do instytucji finansowej (009 R19).</summary>
    public async Task PpkPaidAsync(Guid payroll, DateOnly? paidOn) =>
        await KadryAsync<object>(
            "CALL payroll_app.payroll_ppk_paid($1, NULL, $2)",
            payroll,
            paidOn
        );

    /// <summary>Lista zwykła za miesiąc wypłacona ostatniego dnia miesiąca: naliczona, a gdy close — zamknięta.</summary>
    public async Task<Guid> MonthAsync(
        Guid company,
        int month,
        string kind = "employment",
        bool close = true
    )
    {
        var period = D(2026, month, 1);
        var payroll = await PayrollAsync(company, kind, period, period.AddMonths(1).AddDays(-1));
        await CalculateAsync(payroll);
        if (close)
        {
            await CloseAsync(payroll);
        }

        return payroll;
    }

    public async Task<PayrollEntry> EntryAsync(Guid payroll, Guid employment)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        Guid id;
        decimal gross,
            net,
            payout;
        var warnings = new List<string>();
        await using (
            var command = TestDb.Command(
                connection,
                """
                SELECT e.id, e.gross, e.net, e.payout,
                       coalesce((SELECT array_agg(w ->> 'code') FROM jsonb_array_elements(e.warnings) w), '{}')
                FROM payroll.entries e WHERE e.payroll_id = $1 AND e.employment_id = $2
                """,
                new NpgsqlParameter { Value = payroll },
                new NpgsqlParameter { Value = employment }
            )
        )
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync(), "brak wypłaty umowy na liście");
            id = reader.GetGuid(0);
            gross = reader.GetDecimal(1);
            net = reader.GetDecimal(2);
            payout = reader.GetDecimal(3);
            warnings.AddRange(reader.GetFieldValue<string[]>(4));
        }

        var amounts = new Dictionary<string, decimal>();
        var bases = new Dictionary<string, decimal?>();
        await using (
            var command = TestDb.Command(
                connection,
                "SELECT code, sum(amount), sum(base) FROM payroll.lines WHERE entry_id = $1 GROUP BY code",
                new NpgsqlParameter { Value = id }
            )
        )
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                amounts[reader.GetString(0)] = reader.GetDecimal(1);
                bases[reader.GetString(0)] = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
            }
        }

        return new PayrollEntry(id, gross, net, payout, amounts, bases, warnings);
    }

    public async Task<bool> HasEntryAsync(Guid payroll, Guid employment) =>
        await AdminAsync<bool>(
            "SELECT EXISTS (SELECT FROM payroll.entries WHERE payroll_id = $1 AND employment_id = $2)",
            payroll,
            employment
        );

    public async Task<T?> KadryAsync<T>(string sql, params object?[] args) =>
        await AsAsync<T>("kadry", sql, args);

    public async Task<T?> AsAsync<T>(string login, string sql, params object?[] args)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, login)
        );
        return await RunAsync<T>(connection, sql, args);
    }

    public async Task<T?> AdminAsync<T>(string sql, params object?[] args)
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
