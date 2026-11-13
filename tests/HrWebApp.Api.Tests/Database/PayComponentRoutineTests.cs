using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class PayComponentRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Component_type_fields_depend_on_kind_and_system_types_are_read_only()
    {
        var code = Unique.Code();
        var addition = await TypeAsync(code, "addition", "fixed", flags: true);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            TypeAsync(code.ToLowerInvariant(), "addition", "fixed", flags: true)
        );
        var deductionWithFlags = await Assert.ThrowsAsync<PostgresException>(() =>
            TypeAsync(Unique.Code(), "deduction", "fixed", flags: true, category: "advance")
        );
        var deductionPercent = await Assert.ThrowsAsync<PostgresException>(() =>
            TypeAsync(Unique.Code(), "deduction", "percentOfBase", category: "advance")
        );
        var enforcementCategory = await Assert.ThrowsAsync<PostgresException>(() =>
            TypeAsync(Unique.Code(), "deduction", "fixed", category: "enforcement")
        );
        var kadry = await Assert.ThrowsAsync<PostgresException>(() =>
            TypeAsync(Unique.Code(), "addition", "fixed", flags: true, role: "kadry")
        );
        var system = await AdminAsync<Guid>(
            "SELECT id FROM pay_components.component_types WHERE code = 'ZASAD'"
        );
        var systemUpdate = await Assert.ThrowsAsync<PostgresException>(() =>
            UpdateTypeAsync(system, "fixed")
        );

        Assert.Equal(
            code,
            await AdminAsync<string>(
                "SELECT code FROM pay_components.component_types WHERE id = $1",
                addition
            )
        );
        Assert.Equal("""{"code": ["duplicate"]}""", duplicate.Hint);
        Assert.Equal("""{"inZusBase": ["invalid"]}""", deductionWithFlags.Hint);
        Assert.Equal("""{"calculation": ["invalid"]}""", deductionPercent.Hint);
        Assert.Equal("""{"deductionCategory": ["invalid"]}""", enforcementCategory.Hint);
        Assert.Equal("U0004", kadry.SqlState);
        Assert.Equal("systemComponent", systemUpdate.Hint);
    }

    [Fact]
    public async Task Calculation_cannot_change_once_the_type_is_assigned()
    {
        var bonus = await TypeAsync(Unique.Code(), "addition", "fixed", flags: true);
        var (_, employment) = await ContractAsync("employment");
        await AddAsync(employment, bonus, 500m, D(2025, 1, 1), D(2025, 1, 31));

        var inUse = await Assert.ThrowsAsync<PostgresException>(() =>
            UpdateTypeAsync(bonus, "percentOfBase")
        );
        await UpdateTypeAsync(bonus, "fixed", isActive: false);

        Assert.Equal("componentInUse", inUse.Hint);
    }

    [Fact]
    public async Task Assignment_stays_in_contract_does_not_overlap_and_penalty_needs_employment()
    {
        var bonus = await TypeAsync(Unique.Code(), "addition", "fixed", flags: true);
        var penalty = await TypeAsync(Unique.Code(), "deduction", "fixed", category: "penalty");
        var (_, employment) = await ContractAsync("employment");
        var (_, mandate) = await ContractAsync("mandate");

        await AddAsync(employment, bonus, 500m, D(2025, 1, 1), D(2025, 3, 31));
        var overlap = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, bonus, 300m, D(2025, 3, 1), null)
        );
        var outside = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, bonus, 300m, D(2023, 12, 1), D(2024, 1, 31))
        );
        var penaltyOnMandate = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(mandate, penalty, 100m, D(2025, 1, 1), D(2025, 1, 31))
        );
        await AddAsync(employment, penalty, 100m, D(2025, 1, 1), D(2025, 1, 31));

        Assert.Equal("""{"validFrom": ["overlap"]}""", overlap.Hint);
        Assert.Equal("""{"validFrom": ["outsidePeriod"]}""", outside.Hint);
        Assert.Equal("""{"componentTypeId": ["invalid"]}""", penaltyOnMandate.Hint);
    }

    [Fact]
    public async Task Voluntary_deduction_needs_a_signed_consent_covering_period_and_amount()
    {
        var card = await TypeAsync(Unique.Code(), "deduction", "fixed", category: "voluntaryOther");
        var (employee, employment) = await ContractAsync("employment");
        var unsigned = await ConsentAsync(
            employee,
            card,
            120m,
            D(2025, 1, 1),
            null,
            signedAt: null
        );
        var signed = await ConsentAsync(
            employee,
            card,
            100m,
            D(2025, 2, 1),
            D(2025, 6, 30),
            D(2025, 1, 20)
        );

        var none = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, card, 100m, D(2025, 2, 1), D(2025, 6, 30))
        );
        var notSigned = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, card, 100m, D(2025, 2, 1), D(2025, 6, 30), unsigned)
        );
        var tooEarly = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, card, 100m, D(2025, 1, 1), D(2025, 6, 30), signed)
        );
        var open = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, card, 100m, D(2025, 2, 1), null, signed)
        );
        var tooMuch = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(employment, card, 120m, D(2025, 2, 1), D(2025, 6, 30), signed)
        );
        await AddAsync(employment, card, 100m, D(2025, 2, 1), D(2025, 6, 30), signed);

        Assert.Equal("""{"consentId": ["consentMissing"]}""", none.Hint);
        Assert.Equal("""{"consentId": ["consentMissing"]}""", notSigned.Hint);
        Assert.Equal("""{"consentId": ["consentMissing"]}""", tooEarly.Hint);
        Assert.Equal("""{"consentId": ["consentMissing"]}""", open.Hint);
        Assert.Equal("""{"value": ["aboveConsent"]}""", tooMuch.Hint);
    }

    [Fact]
    public async Task Signed_consent_is_immutable_and_cannot_end_or_go_while_used()
    {
        var card = await TypeAsync(
            Unique.Code(),
            "deduction",
            "fixed",
            category: "voluntaryEmployer"
        );
        var (employee, employment) = await ContractAsync("employment");
        var consent = await ConsentAsync(employee, card, 150m, D(2025, 1, 1), null, signedAt: null);
        await ExecAsync(
            "CALL pay_components_app.consent_update($1, $2, 150, $3, NULL, $4)",
            consent,
            await ConsentVersionAsync(employee, consent),
            D(2025, 1, 1),
            D(2024, 12, 20)
        );
        var changeSigned = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecAsync(
                "CALL pay_components_app.consent_update($1, $2, 200, $3, NULL, $4)",
                consent,
                await ConsentVersionAsync(employee, consent),
                D(2025, 1, 1),
                D(2024, 12, 20)
            )
        );
        await AddAsync(employment, card, 150m, D(2025, 1, 1), D(2025, 6, 30), consent);

        var endInUse = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecAsync(
                "CALL pay_components_app.consent_end($1, $2, $3)",
                consent,
                await ConsentVersionAsync(employee, consent),
                D(2025, 3, 31)
            )
        );
        await ExecAsync(
            "CALL pay_components_app.consent_end($1, $2, $3)",
            consent,
            await ConsentVersionAsync(employee, consent),
            D(2025, 6, 30)
        );
        var deleteInUse = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecAsync(
                "CALL pay_components_app.consent_delete($1, $2)",
                consent,
                await ConsentVersionAsync(employee, consent)
            )
        );

        Assert.Equal("signed", changeSigned.Hint);
        Assert.Equal("consentInUse", endInUse.Hint);
        Assert.Equal("consentInUse", deleteInUse.Hint);
        Assert.Equal(
            "ended",
            await ScalarAsync<string>(
                "SELECT status FROM pay_components_app.consent_list($1) WHERE id = $2",
                employee,
                consent
            )
        );
    }

    [Fact]
    public async Task Garnishment_amounts_authority_duplicate_status_and_read_audit()
    {
        var (employee, _) = await ContractAsync("employment");

        var otherWithoutTotal = await Assert.ThrowsAsync<PostgresException>(() =>
            GarnishmentAsync(employee, "other", "court", "Km 1/25", monthly: 500m, total: null)
        );
        var creditorForOther = await Assert.ThrowsAsync<PostgresException>(() =>
            GarnishmentAsync(employee, "other", "creditor", "Km 2/25", monthly: null, total: 1000m)
        );
        var alimony = await GarnishmentAsync(
            employee,
            "alimony",
            "creditor",
            "III RC 10/25",
            monthly: 800m,
            total: null
        );
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            GarnishmentAsync(
                employee,
                "alimony",
                "creditor",
                "iii rc 10/25",
                monthly: 800m,
                total: null
            )
        );
        await ExecAsync(
            "CALL pay_components_app.garnishment_set_status($1, $2, 'paidOff', $3)",
            alimony,
            await GarnishmentVersionAsync(employee, alimony),
            D(2025, 6, 30)
        );

        Assert.Equal("""{"totalAmount": ["required"]}""", otherWithoutTotal.Hint);
        Assert.Equal("""{"authorityKind": ["invalid"]}""", creditorForOther.Hint);
        Assert.Equal("""{"caseNumber": ["duplicate"]}""", duplicate.Hint);
        Assert.Equal(
            "paidOff",
            await ScalarAsync<string>(
                "SELECT status FROM pay_components_app.garnishment_list($1) WHERE id = $2",
                employee,
                alimony
            )
        );
        Assert.True(
            await AdminAsync<long>(
                "SELECT count(*) FROM platform.read_audit_entries WHERE employee_id = $1 AND resource = 'garnishment'",
                employee
            ) > 0
        );
    }

    [Fact]
    public async Task Pracownik_sees_own_consents_only_and_no_garnishments()
    {
        var card = await TypeAsync(Unique.Code(), "deduction", "fixed", category: "voluntaryOther");
        var (own, _) = await ContractAsync("employment");
        var (other, _) = await ContractAsync("employment");
        await ConsentAsync(own, card, 50m, D(2025, 1, 1), null, D(2024, 12, 1));
        var user = await TestDb.CreateUserAsync(
            database,
            Unique.Login(),
            "haslo-testowe-123",
            "pracownik"
        );
        await AdminAsync<object>(
            """
            INSERT INTO platform.user_employee_links (user_id, employee_id, company_id)
            SELECT $1, e.id, e.company_id FROM employees.employees e WHERE e.id = $2
            """,
            user,
            own
        );

        await using var connection = await TestDb.ApiAsync(database, user);
        var ownCount = await RunAsync<long>(
            connection,
            "SELECT count(*) FROM pay_components_app.consent_list($1)",
            [own]
        );
        var foreign = await Assert.ThrowsAsync<PostgresException>(() =>
            RunAsync<long>(
                connection,
                "SELECT count(*) FROM pay_components_app.consent_list($1)",
                [other]
            )
        );
        var garnishments = await Assert.ThrowsAsync<PostgresException>(() =>
            RunAsync<long>(
                connection,
                "SELECT count(*) FROM pay_components_app.garnishment_list($1)",
                [own]
            )
        );

        Assert.Equal(1L, ownCount);
        Assert.Equal("U0004", foreign.SqlState);
        Assert.Equal("U0004", garnishments.SqlState);
    }

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private async Task<Guid> TypeAsync(
        string code,
        string kind,
        string calculation,
        bool flags = false,
        string? category = null,
        string role = "admin"
    )
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, role)
        );
        return (Guid)
            (
                await RunAsync<object>(
                    connection,
                    "CALL pay_components_app.component_type_create($1, 'Składnik testowy', $2, $3, $4, $4, $4, $4, $5, NULL)",
                    [code, kind, calculation, flags ? true : null, category]
                )
            )!;
    }

    private async Task UpdateTypeAsync(Guid id, string calculation, bool isActive = true)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "admin")
        );
        var version = await RunAsync<long>(
            connection,
            "SELECT row_version FROM pay_components_app.component_type_list(true) WHERE id = $1",
            [id]
        );
        await RunAsync<object>(
            connection,
            "CALL pay_components_app.component_type_update($1, $2, 'Składnik testowy', $3, true, true, true, true, NULL, $4)",
            [id, version, calculation, isActive]
        );
    }

    /// <summary>Kartoteka z jedną umową 2024–2025 wstawioną wprost (warunki nie są potrzebne do składników).</summary>
    private async Task<(Guid Employee, Guid Employment)> ContractAsync(string kind)
    {
        var company = await TestDb.CompanyAsync(database);
        var employee = await AdminAsync<Guid>(
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, '1', 'Jan', 'Kowalski', DATE '1985-01-01') RETURNING id
            """,
            company
        );
        var employment = await AdminAsync<Guid>(
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date, end_date)
            VALUES ($1, $2, gen_random_uuid()::text, $3, CASE WHEN $3 = 'employment' THEN 'fixed_term' END,
                    DATE '2024-01-01', DATE '2024-01-01', DATE '2025-12-31')
            RETURNING id
            """,
            employee,
            company,
            kind
        );
        return (employee, employment);
    }

    private async Task<Guid> AddAsync(
        Guid employment,
        Guid type,
        decimal value,
        DateOnly from,
        DateOnly? to,
        Guid? consent = null
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL pay_components_app.employment_component_add($1, $2, $3, $4, $5, $6, NULL)",
                    employment,
                    type,
                    value,
                    from,
                    to,
                    consent
                )
            )!;

    private async Task<Guid> ConsentAsync(
        Guid employee,
        Guid type,
        decimal amount,
        DateOnly from,
        DateOnly? to,
        DateOnly? signedAt
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL pay_components_app.consent_create($1, $2, $3, $4, $5, $6, NULL)",
                    employee,
                    type,
                    amount,
                    from,
                    to,
                    signedAt
                )
            )!;

    private async Task<Guid> GarnishmentAsync(
        Guid employee,
        string kind,
        string authorityKind,
        string caseNumber,
        decimal? monthly,
        decimal? total
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL pay_components_app.garnishment_create($1, $2, $3, 'Komornik Sądowy przy SR', $4, $5, $6, $7, NULL, NULL)",
                    employee,
                    kind,
                    authorityKind,
                    caseNumber,
                    D(2025, 1, 10),
                    monthly,
                    total
                )
            )!;

    private async Task<long> ConsentVersionAsync(Guid employee, Guid id) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM pay_components_app.consent_list($1) WHERE id = $2",
            employee,
            id
        );

    private async Task<long> GarnishmentVersionAsync(Guid employee, Guid id) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM pay_components_app.garnishment_list($1) WHERE id = $2",
            employee,
            id
        );

    private async Task ExecAsync(string sql, params object?[] args) =>
        await ScalarAsync<object>(sql, args);

    private async Task<T?> ScalarAsync<T>(string sql, params object?[] args)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
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
