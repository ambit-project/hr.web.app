using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class EmploymentRoutineTests(DatabaseFixture database)
{
    private static readonly string[] Mandatory =
    [
        "pension",
        "disability",
        "sickness",
        "accident",
        "health",
    ];

    [Fact]
    public async Task Empty_number_gets_next_number_and_duplicate_is_rejected()
    {
        var employee = await EmployeeAsync();
        var terms = await EmploymentTermsAsync();

        var first = await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2020, 1, 1),
            D(2020, 6, 30),
            terms
        );
        var second = await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2020, 7, 1),
            D(2020, 12, 31),
            terms
        );
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "employment",
                "fixed_term",
                D(2021, 1, 1),
                D(2021, 6, 30),
                terms,
                number: "1"
            )
        );

        Assert.Equal(
            "1",
            await AdminAsync<string>(
                "SELECT number FROM employment.employments WHERE id = $1",
                first
            )
        );
        Assert.Equal(
            "2",
            await AdminAsync<string>(
                "SELECT number FROM employment.employments WHERE id = $1",
                second
            )
        );
        Assert.Equal("""{"number": ["duplicate"]}""", duplicate.Hint);
    }

    [Fact]
    public async Task Dates_are_checked_by_contract_type()
    {
        var employee = await EmployeeAsync();
        var terms = await EmploymentTermsAsync();

        var indefiniteWithEnd = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "employment", "indefinite", D(2020, 1, 1), D(2020, 12, 31), terms)
        );
        var fixedWithoutEnd = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "employment", "fixed_term", D(2020, 1, 1), null, terms)
        );
        var signedAfterStart = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "employment",
                "indefinite",
                D(2020, 1, 1),
                null,
                terms,
                signedAt: D(2020, 1, 2)
            )
        );
        var mandateWithType = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "mandate",
                "fixed_term",
                D(2020, 1, 1),
                D(2020, 1, 31),
                MandateTerms()
            )
        );

        Assert.Equal("""{"endDate": ["invalid"]}""", indefiniteWithEnd.Hint);
        Assert.Equal("""{"endDate": ["required"]}""", fixedWithoutEnd.Hint);
        Assert.Equal("""{"signedAt": ["afterStart"]}""", signedAfterStart.Hint);
        Assert.Equal("""{"contractType": ["invalid"]}""", mandateWithType.Hint);
    }

    [Fact]
    public async Task Employment_contracts_of_one_employee_do_not_overlap()
    {
        var employee = await EmployeeAsync();
        var terms = await EmploymentTermsAsync();
        await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2020, 1, 1),
            D(2020, 12, 31),
            terms
        );

        var overlap = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "employment", "indefinite", D(2020, 12, 31), null, terms)
        );
        await CreateAsync(employee, "employment", "indefinite", D(2021, 1, 1), null, terms);

        Assert.Equal("""{"startDate": ["overlap"]}""", overlap.Hint);
    }

    [Fact]
    public async Task Mandate_or_work_with_own_employee_is_rejected_both_ways_but_allowed_after_termination()
    {
        var employee = await EmployeeAsync();
        var employment = await CreateAsync(
            employee,
            "employment",
            "indefinite",
            D(2020, 1, 1),
            null,
            await EmploymentTermsAsync()
        );

        var mandate = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "mandate", null, D(2020, 5, 1), D(2020, 5, 31), MandateTerms())
        );
        var work = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(employee, "specific_work", null, D(2020, 5, 1), D(2020, 5, 31), WorkTerms())
        );
        await TerminateAsync(employment, D(2020, 4, 30));
        await CreateAsync(employee, "mandate", null, D(2020, 5, 1), D(2020, 5, 31), MandateTerms());
        var undoTermination = await Assert.ThrowsAsync<PostgresException>(() =>
            TerminateAsync(employment, null)
        );

        Assert.All((PostgresException[])[mandate, work, undoTermination], error =>
        {
            Assert.Equal("U0003", error.SqlState);
            Assert.Equal("concurrentTitleUnsupported", error.Hint);
        });
    }

    [Fact]
    public async Task Terms_fields_depend_on_kind()
    {
        var employee = await EmployeeAsync();
        var employmentTerms = await EmploymentTermsAsync();

        var mandateWithPosition = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "mandate",
                null,
                D(2020, 1, 1),
                D(2020, 1, 31),
                MandateTerms() with
                {
                    PositionId = employmentTerms.PositionId,
                }
            )
        );
        var workWithCodes = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "specific_work",
                null,
                D(2020, 1, 1),
                D(2020, 1, 31),
                WorkTerms() with
                {
                    Title = "0411",
                }
            )
        );
        var missingInsurance = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "employment",
                "indefinite",
                D(2020, 1, 1),
                null,
                employmentTerms with
                {
                    Insurances = ["pension", "disability", "accident", "health"],
                }
            )
        );
        var sicknessWithoutPension = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "mandate",
                null,
                D(2020, 1, 1),
                D(2020, 1, 31),
                MandateTerms() with
                {
                    Insurances = ["sickness", "health"],
                }
            )
        );
        var unknownCode = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "mandate",
                null,
                D(2020, 1, 1),
                D(2020, 1, 31),
                MandateTerms() with
                {
                    Title = "9999",
                }
            )
        );
        await CreateAsync(
            employee,
            "mandate",
            null,
            D(2020, 1, 1),
            D(2020, 1, 31),
            MandateTerms() with
            {
                Insurances = ["health"],
            }
        );

        Assert.Equal("""{"positionId": ["invalid"]}""", mandateWithPosition.Hint);
        Assert.Equal("""{"zusTitleCode": ["invalid"]}""", workWithCodes.Hint);
        Assert.Equal("""{"insurances": ["missingMandatory"]}""", missingInsurance.Hint);
        Assert.Equal("""{"insurances": ["invalid"]}""", sicknessWithoutPension.Hint);
        Assert.Equal("""{"zusTitleCode": ["notFound"]}""", unknownCode.Hint);
    }

    [Fact]
    public async Task Minimum_wage_is_checked_with_fte_and_hourly_rate_when_parameter_exists()
    {
        var employee = await EmployeeAsync();
        var half = await EmploymentTermsAsync() with { FteNumerator = 1, FteDenominator = 2 };

        var belowHalf = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "employment",
                "fixed_term",
                D(2026, 2, 1),
                D(2026, 2, 28),
                half with
                {
                    BaseAmount = 2402.99m,
                }
            )
        );
        var belowHourly = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(
                employee,
                "mandate",
                null,
                D(2026, 3, 1),
                D(2026, 3, 31),
                MandateTerms() with
                {
                    BaseAmount = 31.39m,
                }
            )
        );
        await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2026, 2, 1),
            D(2026, 2, 28),
            half with
            {
                BaseAmount = 2403m,
            }
        );
        await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2018, 1, 1),
            D(2018, 1, 31),
            half with
            {
                BaseAmount = 100m,
            }
        );

        Assert.Equal("""{"baseAmount": ["belowMinimum"]}""", belowHalf.Hint);
        Assert.Equal("""{"baseAmount": ["belowMinimum"]}""", belowHourly.Hint);
    }

    [Fact]
    public async Task New_terms_close_previous_same_date_corrects_and_earlier_or_outside_is_rejected()
    {
        var employee = await EmployeeAsync();
        var terms = await EmploymentTermsAsync();
        var employment = await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2020, 1, 1),
            D(2020, 12, 31),
            terms
        );

        await AddTermsAsync(employment, D(2020, 6, 1), terms with { BaseAmount = 7000m });
        await AddTermsAsync(employment, D(2020, 6, 1), terms with { BaseAmount = 7100m });
        var earlier = await Assert.ThrowsAsync<PostgresException>(() =>
            AddTermsAsync(employment, D(2020, 5, 1), terms)
        );
        var outside = await Assert.ThrowsAsync<PostgresException>(() =>
            AddTermsAsync(employment, D(2021, 1, 1), terms)
        );

        Assert.Equal(
            "2020-01-01..2020-05-31=6000.00;2020-06-01..=7100.00",
            await AdminAsync<string>(
                """
                SELECT string_agg(valid_from || '..' || coalesce(valid_to::text, '') || '=' || base_amount, ';'
                                  ORDER BY valid_from)
                FROM employment.terms WHERE employment_id = $1
                """,
                employment
            )
        );
        Assert.Equal("""{"validFrom": ["beforeCurrent"]}""", earlier.Hint);
        Assert.Equal("""{"validFrom": ["outsidePeriod"]}""", outside.Hint);
    }

    [Fact]
    public async Task Last_terms_version_can_be_deleted_but_not_the_only_one()
    {
        var employee = await EmployeeAsync();
        var terms = await EmploymentTermsAsync();
        var employment = await CreateAsync(
            employee,
            "employment",
            "indefinite",
            D(2020, 1, 1),
            null,
            terms
        );
        await AddTermsAsync(employment, D(2020, 6, 1), terms with { BaseAmount = 7000m });

        await ExecAsync("CALL employment_app.terms_delete_last($1)", employment);
        var only = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync("CALL employment_app.terms_delete_last($1)", employment)
        );

        Assert.True(
            await AdminAsync<bool>(
                "SELECT valid_to IS NULL FROM employment.terms WHERE employment_id = $1",
                employment
            )
        );
        Assert.Equal("onlyVersion", only.Hint);
    }

    [Fact]
    public async Task Changing_start_moves_first_terms_version_and_cannot_pass_a_later_one()
    {
        var employee = await EmployeeAsync();
        var terms = await EmploymentTermsAsync();
        var employment = await CreateAsync(
            employee,
            "employment",
            "indefinite",
            D(2020, 1, 1),
            null,
            terms
        );
        await AddTermsAsync(employment, D(2020, 6, 1), terms with { BaseAmount = 7000m });

        await UpdateAsync(employment, D(2019, 12, 1));
        var pastSecond = await Assert.ThrowsAsync<PostgresException>(() =>
            UpdateAsync(employment, D(2020, 6, 1))
        );

        Assert.Equal(
            D(2019, 12, 1),
            await AdminAsync<DateOnly>(
                "SELECT min(valid_from) FROM employment.terms WHERE employment_id = $1",
                employment
            )
        );
        Assert.Equal("""{"startDate": ["termsOutsidePeriod"]}""", pastSecond.Hint);
    }

    [Fact]
    public async Task Retention_is_counted_from_end_of_year_since_2019_and_50_years_before()
    {
        Assert.Equal(
            D(2036, 12, 31),
            await AdminAsync<DateOnly>(
                "SELECT employment.retention_until('employment', DATE '2024-03-01', NULL, DATE '2026-03-15')"
            )
        );
        Assert.Equal(
            D(2068, 6, 30),
            await AdminAsync<DateOnly>(
                "SELECT employment.retention_until('mandate', DATE '2018-01-01', DATE '2018-06-30', NULL)"
            )
        );
        Assert.True(
            await AdminAsync<bool>(
                "SELECT employment.retention_until('employment', DATE '2024-03-01', NULL, NULL) = 'infinity'"
            )
        );
    }

    [Fact]
    public async Task Employee_with_contracts_in_retention_cannot_be_deleted_and_status_is_listed()
    {
        var employee = await EmployeeAsync();
        await CreateAsync(
            employee,
            "employment",
            "fixed_term",
            D(2020, 1, 1),
            D(2020, 12, 31),
            await EmploymentTermsAsync()
        );
        var company = await AdminAsync<Guid>(
            "SELECT company_id FROM employees.employees WHERE id = $1",
            employee
        );

        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync("CALL employees_app.employee_delete($1)", employee)
        );

        Assert.Equal("U0003", delete.SqlState);
        Assert.Equal("retention", delete.Hint);
        Assert.Equal(
            "former",
            await ScalarAsync<string>(
                "SELECT status FROM employees_app.employee_list($1, NULL, 10, 0, 'former')",
                company
            )
        );
        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                "SELECT count(*) FROM employees_app.employee_list($1, NULL, 10, 0, 'active')",
                company
            )
        );
        Assert.Equal(
            D(2030, 12, 31),
            await ScalarAsync<DateOnly>(
                "SELECT retention_until FROM employees_app.employee_get($1)",
                employee
            )
        );
    }

    [Fact]
    public async Task Reading_terms_is_audited_and_pracownik_has_no_access()
    {
        var employee = await EmployeeAsync();
        var employment = await CreateAsync(
            employee,
            "employment",
            "indefinite",
            D(2020, 1, 1),
            null,
            await EmploymentTermsAsync()
        );

        await ScalarAsync<object>("SELECT count(*) FROM employment_app.terms_list($1)", employment);
        await using var pracownik = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "pracownik")
        );
        await using var command = TestDb.Command(
            pracownik,
            "SELECT count(*) FROM employment_app.employment_list($1)",
            new NpgsqlParameter<Guid> { TypedValue = employee }
        );
        var forbidden = await Assert.ThrowsAsync<PostgresException>(command.ExecuteScalarAsync);

        Assert.Equal(
            1L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.read_audit_entries WHERE resource = 'employment_terms' AND resource_id = $1",
                new NpgsqlParameter<string> { TypedValue = employment.ToString() }
            )
        );
        Assert.Equal("U0004", forbidden.SqlState);
    }

    private sealed record Terms(
        Guid? PositionId,
        int? FteNumerator,
        int? FteDenominator,
        string PayBasis,
        decimal BaseAmount,
        string? Title,
        string? Pension,
        string? Disability,
        string[] Insurances,
        string CostKind
    );

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private async Task<Terms> EmploymentTermsAsync()
    {
        var position = await TestDb.AdminScalarAsync<Guid>(
            database,
            "INSERT INTO dictionaries.positions (name) VALUES ($1) RETURNING id",
            new NpgsqlParameter<string> { TypedValue = Unique.Name("Stanowisko") }
        );
        return new Terms(
            position,
            1,
            1,
            "monthly",
            6000m,
            "0110",
            "0",
            "0",
            [.. Mandatory, "fp", "fgsp"],
            "standard"
        );
    }

    private static Terms MandateTerms() =>
        new(null, null, null, "hourly", 40m, "0411", "0", "0", [.. Mandatory], "flat20");

    private static Terms WorkTerms() =>
        new(null, null, null, "lumpSum", 2000m, null, null, null, [], "author50");

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

    private async Task<Guid> CreateAsync(
        Guid employee,
        string kind,
        string? contractType,
        DateOnly start,
        DateOnly? end,
        Terms terms,
        string? number = null,
        DateOnly? signedAt = null
    ) =>
        (Guid)
            (
                await ScalarAsync<object>(
                    """
                    CALL employment_app.employment_create($1, $2, $3, $4, $5, $6, $7,
                        $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, NULL)
                    """,
                    [
                        employee,
                        number,
                        kind,
                        contractType,
                        signedAt ?? start,
                        start,
                        end,
                        .. TermsArgs(terms),
                    ]
                )
            )!;

    private async Task AddTermsAsync(Guid employment, DateOnly validFrom, Terms terms) =>
        await ScalarAsync<object>(
            "CALL employment_app.terms_add($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, NULL)",
            [employment, validFrom, .. TermsArgs(terms)]
        );

    private async Task TerminateAsync(Guid employment, DateOnly? terminatedAt) =>
        await ExecAsync(
            "CALL employment_app.employment_terminate($1, $2, $3)",
            employment,
            await RowVersionAsync(employment),
            terminatedAt
        );

    private async Task UpdateAsync(Guid employment, DateOnly start) =>
        await ExecAsync(
            "CALL employment_app.employment_update($1, $2, $3, 'indefinite', $4, $4, NULL)",
            employment,
            await RowVersionAsync(employment),
            await ScalarAsync<string>(
                "SELECT number FROM employment_app.employment_get($1)",
                employment
            ),
            start
        );

    private async Task<long> RowVersionAsync(Guid employment) =>
        await ScalarAsync<long>(
            "SELECT row_version FROM employment_app.employment_get($1)",
            employment
        );

    private static object?[] TermsArgs(Terms t) =>
        [
            t.PositionId,
            t.FteNumerator,
            t.FteDenominator,
            t.PayBasis,
            t.BaseAmount,
            t.Title,
            t.Pension,
            t.Disability,
            t.Insurances,
            t.CostKind,
        ];

    private async Task ExecAsync(string sql, params object?[] args) =>
        await ScalarAsync<object>(sql, args);

    /// <summary>Wywołanie jako Kadry; parametry z typem z wartości, a null jako nieokreślony typ (rzutuje rutyna).</summary>
    private async Task<T?> ScalarAsync<T>(string sql, params object?[] args)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        return await RunAsync<T>(connection, sql, args);
    }

    /// <summary>Odczyt tabel i funkcji wewnętrznych jako superużytkownik (rola API nie ma do nich praw).</summary>
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
