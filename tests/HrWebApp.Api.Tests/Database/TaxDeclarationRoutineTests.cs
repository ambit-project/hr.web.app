using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class TaxDeclarationRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Details_depend_on_kind()
    {
        var employee = await EmployeeAsync();

        var pit2WithoutShare = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "pit2", D(2024, 3, 20), D(2024, 4, 1))
        );
        var familyWithShare = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "familyRelief", D(2024, 3, 20), D(2024, 4, 1), share: "1/12")
        );
        var returnYears = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "returnRelief",
                D(2024, 3, 20),
                D(2024, 4, 1),
                yearFrom: 2025,
                yearTo: 2024
            )
        );
        var unknown = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "youthRelief", D(2024, 3, 20), D(2024, 4, 1))
        );

        Assert.Equal("""{"share": ["required"]}""", pit2WithoutShare.Hint);
        Assert.Equal("""{"share": ["invalid"]}""", familyWithShare.Hint);
        Assert.Equal("""{"yearTo": ["beforeFrom"]}""", returnYears.Hint);
        Assert.Equal("""{"kind": ["invalid"]}""", unknown.Hint);
    }

    [Fact]
    public async Task Valid_from_is_the_month_of_submission_or_the_next_one()
    {
        var employee = await EmployeeAsync();

        await CreateAsync(employee, "pit2", D(2024, 3, 20), D(2024, 3, 1), share: "1/12");
        await CreateAsync(employee, "increasedCosts", D(2024, 3, 20), D(2024, 4, 1));
        var tooLate = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "higherRate", D(2024, 3, 20), D(2024, 5, 1))
        );
        var notMonthStart = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "higherRate", D(2024, 3, 20), D(2024, 3, 15))
        );
        var tomorrow = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var future = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "higherRate",
                tomorrow,
                new DateOnly(tomorrow.Year, tomorrow.Month, 1)
            )
        );

        Assert.Equal("""{"validFrom": ["outsideAllowed"]}""", tooLate.Hint);
        Assert.Equal("""{"validFrom": ["invalid"]}""", notMonthStart.Hint);
        Assert.Equal("""{"submittedAt": ["future"]}""", future.Hint);
    }

    [Fact]
    public async Task No_advances_lasts_one_year_and_return_relief_ends_with_its_last_year()
    {
        var employee = await EmployeeAsync();

        var nextYear = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "noAdvances", D(2024, 12, 10), D(2025, 1, 1))
        );
        var noAdvances = await CreateAsync(employee, "noAdvances", D(2024, 12, 10), D(2024, 12, 1));
        var returnRelief = await CreateAsync(
            employee,
            "returnRelief",
            D(2024, 3, 20),
            D(2024, 4, 1),
            yearFrom: 2024,
            yearTo: 2027
        );
        var second = await EmployeeAsync();
        var pastLastYear = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                second,
                "returnRelief",
                D(2024, 3, 20),
                D(2024, 4, 1),
                yearFrom: 2024,
                yearTo: 2027,
                validTo: D(2028, 12, 31)
            )
        );
        var outsideYears = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                second,
                "returnRelief",
                D(2024, 3, 20),
                D(2024, 4, 1),
                yearFrom: 2025,
                yearTo: 2028
            )
        );

        Assert.Equal("""{"validFrom": ["outsideAllowed"]}""", nextYear.Hint);
        Assert.Equal(D(2024, 12, 31), await ValidToAsync(noAdvances));
        Assert.Equal(D(2027, 12, 31), await ValidToAsync(returnRelief));
        Assert.Equal("""{"validTo": ["invalid"]}""", pastLastYear.Hint);
        Assert.Equal("""{"validFrom": ["outsideAllowed"]}""", outsideYears.Hint);
    }

    [Fact]
    public async Task Same_kind_does_not_overlap_and_ending_allows_a_new_entry()
    {
        var employee = await EmployeeAsync();
        var first = await CreateAsync(
            employee,
            "pit2",
            D(2024, 3, 20),
            D(2024, 4, 1),
            share: "1/12"
        );

        var overlap = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "pit2", D(2024, 4, 2), D(2024, 5, 1), share: "1/24")
        );
        await CreateAsync(employee, "higherRate", D(2024, 3, 20), D(2024, 4, 1));
        await EndAsync(first, D(2024, 4, 20), D(2024, 5, 1));
        await CreateAsync(employee, "pit2", D(2024, 4, 20), D(2024, 5, 1), share: "1/24");
        var undo = await Assert.ThrowsAsync<PostgresException>(() => EndAsync(first, null, null));

        Assert.Equal("""{"validFrom": ["overlap"]}""", overlap.Hint);
        Assert.Equal(D(2024, 4, 30), await ValidToAsync(first));
        Assert.Equal("""{"stopFrom": ["overlap"]}""", undo.Hint);
    }

    [Fact]
    public async Task Ending_is_checked_can_be_undone_and_needs_current_row_version()
    {
        var employee = await EmployeeAsync();
        var id = await CreateAsync(employee, "pit2", D(2024, 3, 20), D(2024, 4, 1), share: "1/12");

        var outside = await Assert.ThrowsAsync<PostgresException>(() =>
            EndAsync(id, D(2024, 6, 10), D(2024, 9, 1))
        );
        var beforeFrom = await Assert.ThrowsAsync<PostgresException>(() =>
            EndAsync(id, D(2024, 3, 25), D(2024, 4, 1))
        );
        var beforeSubmission = await Assert.ThrowsAsync<PostgresException>(() =>
            EndAsync(id, D(2024, 3, 1), D(2024, 4, 1))
        );
        await EndAsync(id, D(2024, 6, 10), D(2024, 7, 1));
        Assert.Equal(D(2024, 6, 30), await ValidToAsync(id));
        await EndAsync(id, null, null);
        Assert.Null(await ValidToAsync(id));

        var stale = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync("CALL tax_declarations_app.declaration_delete($1, $2)", id, 1L)
        );
        await ExecAsync(
            "CALL tax_declarations_app.declaration_delete($1, $2)",
            id,
            await RowVersionAsync(employee, id)
        );

        Assert.Equal("""{"stopFrom": ["outsideAllowed"]}""", outside.Hint);
        Assert.Equal("""{"stopFrom": ["beforeFrom"]}""", beforeFrom.Hint);
        Assert.Contains("\"withdrawnAt\": [\"beforeSubmission\"]", beforeSubmission.Hint);
        Assert.Equal("U0003", stale.SqlState);
        Assert.Equal(
            4L,
            await AdminAsync<long>(
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'tax_declaration' AND entity_id = $1",
                id.ToString()
            )
        );
    }

    [Fact]
    public async Task Declarations_lapse_at_the_end_of_continuous_contracts_except_waivers()
    {
        var continuous = await EmployeeAsync();
        await ContractAsync(continuous, "employment", D(2024, 1, 1), D(2024, 6, 30));
        await ContractAsync(continuous, "mandate", D(2024, 7, 1), D(2024, 12, 31));
        var pitContinuous = await CreateAsync(
            continuous,
            "pit2",
            D(2024, 1, 10),
            D(2024, 2, 1),
            share: "1/12"
        );

        var withGap = await EmployeeAsync();
        await ContractAsync(withGap, "employment", D(2024, 1, 1), D(2024, 6, 30));
        await ContractAsync(withGap, "mandate", D(2024, 7, 2), D(2024, 12, 31));
        var pitGap = await CreateAsync(
            withGap,
            "pit2",
            D(2024, 1, 10),
            D(2024, 2, 1),
            share: "1/12"
        );
        var waiver = await CreateAsync(withGap, "youthReliefWaiver", D(2024, 1, 10), D(2024, 2, 1));

        var beforeFirst = await EmployeeAsync();
        await ContractAsync(beforeFirst, "employment", D(2024, 5, 1), D(2024, 9, 30));
        var pitBefore = await CreateAsync(
            beforeFirst,
            "pit2",
            D(2024, 3, 20),
            D(2024, 4, 1),
            share: "1/12"
        );

        Assert.Equal(D(2024, 12, 31), await LapsesOnAsync(continuous, pitContinuous));
        Assert.Equal(D(2024, 6, 30), await LapsesOnAsync(withGap, pitGap));
        Assert.Null(await LapsesOnAsync(withGap, waiver));
        Assert.Equal(D(2024, 9, 30), await LapsesOnAsync(beforeFirst, pitBefore));
        Assert.Equal("lapsed", await StatusAsync(withGap, pitGap));
        Assert.Equal("active", await StatusAsync(withGap, waiver));
        Assert.Equal(
            "youthReliefWaiver",
            await AdminAsync<string>(
                "SELECT string_agg(kind, ',') FROM tax_declarations.in_force($1, $2)",
                withGap,
                D(2024, 8, 1)
            )
        );
        Assert.Equal(
            "pit2",
            await AdminAsync<string>(
                "SELECT string_agg(kind, ',') FROM tax_declarations.in_force($1, $2)",
                continuous,
                D(2024, 8, 1)
            )
        );
    }

    [Fact]
    public async Task Pracownik_reads_only_own_declarations_and_cannot_write()
    {
        var own = await EmployeeAsync();
        var other = await EmployeeAsync();
        await CreateAsync(own, "pit2", D(2024, 3, 20), D(2024, 4, 1), share: "1/12");
        var user = await TestDb.CreateUserAsync(
            database,
            Unique.Login(),
            "haslo-testowe-123",
            "pracownik"
        );
        await TestDb.AdminScalarAsync<object>(
            database,
            """
            INSERT INTO platform.user_employee_links (user_id, employee_id, company_id)
            SELECT $1, e.id, e.company_id FROM employees.employees e WHERE e.id = $2
            """,
            new NpgsqlParameter<Guid> { TypedValue = user },
            new NpgsqlParameter<Guid> { TypedValue = own }
        );

        await using var connection = await TestDb.ApiAsync(database, user);
        var ownKinds = await RunAsync<string>(
            connection,
            "SELECT string_agg(kind, ',') FROM tax_declarations_app.declaration_list($1, false)",
            [own]
        );
        var foreign = await Assert.ThrowsAsync<PostgresException>(() =>
            RunAsync<string>(
                connection,
                "SELECT kind FROM tax_declarations_app.declaration_list($1, false)",
                [other]
            )
        );
        var write = await Assert.ThrowsAsync<PostgresException>(() =>
            RunAsync<object>(
                connection,
                "CALL tax_declarations_app.declaration_create($1, 'higherRate', NULL, NULL, NULL, $2, $3, NULL, NULL)",
                [own, D(2024, 3, 20), D(2024, 4, 1)]
            )
        );

        Assert.Equal("pit2", ownKinds);
        Assert.Equal("U0004", foreign.SqlState);
        Assert.Equal("U0004", write.SqlState);
    }

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private async Task<Guid> EmployeeAsync()
    {
        var company = await TestDb.CompanyAsync(database);
        return await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, '1', 'Anna', 'Testowa', DATE '1988-04-12') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
    }

    /// <summary>Umowa wstawiona wprost (bez warunków) — wystarcza do liczenia okresu stosunku prawnego.</summary>
    private async Task ContractAsync(Guid employee, string kind, DateOnly start, DateOnly end) =>
        await AdminAsync<object>(
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date, end_date)
            SELECT e.id, e.company_id, gen_random_uuid()::text, $2,
                   CASE WHEN $2 = 'employment' THEN 'fixed_term' END, $3, $3, $4
            FROM employees.employees e WHERE e.id = $1
            """,
            employee,
            kind,
            start,
            end
        );

    private async Task<Guid> CreateAsync(
        Guid employee,
        string kind,
        DateOnly submittedAt,
        DateOnly validFrom,
        string? share = null,
        int? yearFrom = null,
        int? yearTo = null,
        DateOnly? validTo = null
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    "CALL tax_declarations_app.declaration_create($1, $2, $3, $4, $5, $6, $7, $8, NULL)",
                    employee,
                    kind,
                    share,
                    yearFrom,
                    yearTo,
                    submittedAt,
                    validFrom,
                    validTo
                )
            )!;

    private async Task EndAsync(Guid id, DateOnly? withdrawnAt, DateOnly? stopFrom)
    {
        var employee = await AdminAsync<Guid>(
            "SELECT employee_id FROM tax_declarations.declarations WHERE id = $1",
            id
        );
        await ExecAsync(
            "CALL tax_declarations_app.declaration_end($1, $2, $3, $4)",
            id,
            await RowVersionAsync(employee, id),
            withdrawnAt,
            stopFrom
        );
    }

    private async Task<long> RowVersionAsync(Guid employee, Guid id) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM tax_declarations_app.declaration_list($1, false) WHERE id = $2",
            employee,
            id
        );

    private async Task<DateOnly?> ValidToAsync(Guid id) =>
        await AdminAsync<DateOnly?>(
            "SELECT valid_to FROM tax_declarations.declarations WHERE id = $1",
            id
        );

    private async Task<DateOnly?> LapsesOnAsync(Guid employee, Guid id) =>
        await ScalarAsync<DateOnly?>(
            "SELECT lapses_on FROM tax_declarations_app.declaration_list($1, false) WHERE id = $2",
            employee,
            id
        );

    private async Task<string?> StatusAsync(Guid employee, Guid id) =>
        await ScalarAsync<string>(
            "SELECT status FROM tax_declarations_app.declaration_list($1, false) WHERE id = $2",
            employee,
            id
        );

    private async Task ExecAsync(string sql, params object?[] args) =>
        await ScalarAsync<object>(sql, args);

    /// <summary>Wywołanie jako Kadry; null jako nieokreślony typ (rzutuje rutyna).</summary>
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
        return value switch
        {
            null or DBNull => default,
            DateTime date when typeof(T) == typeof(DateOnly?) => (T)
                (object)DateOnly.FromDateTime(date),
            _ => (T)value,
        };
    }
}
