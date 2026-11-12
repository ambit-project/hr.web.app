using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class EmployeeRoutineTests(DatabaseFixture database)
{
    private static readonly DateOnly BirthDate = new(1988, 4, 12);

    [Theory]
    [InlineData("44051401359", true)]
    [InlineData("02070803628", true)]
    [InlineData("44051401358", false)]
    [InlineData("44133101350", false)]
    [InlineData("4405140135", false)]
    public async Task Pesel_checksum_and_date_are_checked(string pesel, bool valid)
    {
        Assert.Equal(
            valid,
            await TestDb.AdminScalarAsync<bool>(
                database,
                "SELECT employees.pesel_valid($1)",
                new NpgsqlParameter<string> { TypedValue = pesel }
            )
        );
    }

    [Fact]
    public async Task Pesel_from_2000s_gives_birth_date_with_month_plus_20()
    {
        Assert.Equal(
            new DateOnly(2002, 7, 8),
            await TestDb.AdminScalarAsync<DateOnly>(
                database,
                "SELECT employees.pesel_birth_date('02270803628')"
            )
        );
    }

    [Theory]
    [InlineData("PL61109010140000071219812874", true)]
    [InlineData("61 1090 1014 0000 0712 1981 2874", true)]
    [InlineData("PL61109010140000071219812875", false)]
    [InlineData("DE89370400440532013000", false)]
    public async Task Iban_is_normalized_and_checked(string iban, bool valid)
    {
        Assert.Equal(
            valid,
            await TestDb.AdminScalarAsync<bool>(
                database,
                "SELECT employees.iban_valid(employees.iban_normalize($1))",
                new NpgsqlParameter<string> { TypedValue = iban }
            )
        );
    }

    [Fact]
    public async Task Pesel_is_stored_encrypted_and_found_by_exact_search()
    {
        var company = await TestDb.CompanyAsync(database);
        var pesel = FakeIds.Pesel(BirthDate);

        var id = await CreateAsync(company, pesel);

        Assert.Equal(
            0L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM employees.employees WHERE id = $1 AND position($2::bytea IN pesel_enc) > 0",
                new NpgsqlParameter<Guid> { TypedValue = id },
                new NpgsqlParameter<string> { TypedValue = pesel }
            )
        );
        Assert.Equal([id], await SearchAsync(company, pesel));
        Assert.Empty(await SearchAsync(company, FakeIds.Pesel(BirthDate)));
    }

    [Fact]
    public async Task Pesel_is_unique_in_company_but_not_across_companies()
    {
        var alfa = await TestDb.CompanyAsync(database);
        var beta = await TestDb.CompanyAsync(database);
        var pesel = FakeIds.Pesel(BirthDate);
        await CreateAsync(alfa, pesel);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => CreateAsync(alfa, pesel));
        await CreateAsync(beta, pesel);

        Assert.Equal("""{"pesel": ["duplicate"]}""", duplicate.Hint);
    }

    [Fact]
    public async Task Birth_date_must_match_pesel_and_pesel_or_document_is_required()
    {
        var company = await TestDb.CompanyAsync(database);

        var mismatch = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(company, FakeIds.Pesel(BirthDate), birthDate: BirthDate.AddDays(1))
        );
        var missing = await Assert.ThrowsAsync<PostgresException>(() => CreateAsync(company, null));
        await CreateAsync(company, null, document: "paszport XX0000001");

        Assert.Equal("""{"birthDate": ["mismatch"]}""", mismatch.Hint);
        Assert.Equal("""{"pesel": ["required"]}""", missing.Hint);
    }

    [Fact]
    public async Task Empty_number_gets_next_number_in_company()
    {
        var company = await TestDb.CompanyAsync(database);

        var first = await CreateAsync(company, FakeIds.Pesel(BirthDate));
        var second = await CreateAsync(company, FakeIds.Pesel(BirthDate));

        Assert.Equal("1", await NumberAsync(first));
        Assert.Equal("2", await NumberAsync(second));
    }

    [Fact]
    public async Task Audit_of_change_has_no_pesel_value()
    {
        var company = await TestDb.CompanyAsync(database);
        var pesel = FakeIds.Pesel(BirthDate);

        var id = await CreateAsync(company, pesel);

        var after = await TestDb.AdminScalarAsync<string>(
            database,
            "SELECT after::text FROM platform.audit_entries WHERE entity = 'employee' AND entity_id = $1",
            new NpgsqlParameter<string> { TypedValue = id.ToString() }
        );
        Assert.DoesNotContain(pesel, after);
        Assert.Contains("\"hasPesel\": true", after);
    }

    [Fact]
    public async Task Reading_details_is_audited_for_kadry_but_not_for_the_employee()
    {
        var company = await TestDb.CompanyAsync(database);
        var id = await CreateAsync(company, FakeIds.Pesel(BirthDate));
        var employeeUser = await TestDb.CreateUserAsync(
            database,
            Unique.Login(),
            "haslo-testowe-123",
            "pracownik",
            "kadry"
        );
        await TestDb.AdminScalarAsync<object>(
            database,
            "INSERT INTO platform.user_employee_links (user_id, employee_id, company_id) VALUES ($1, $2, $3)",
            new NpgsqlParameter<Guid> { TypedValue = employeeUser },
            new NpgsqlParameter<Guid> { TypedValue = id },
            new NpgsqlParameter<Guid> { TypedValue = company }
        );

        await GetAsync(id, await TestDb.DevUserAsync(database, "kadry"));
        await GetAsync(id, employeeUser);

        Assert.Equal(
            1L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.read_audit_entries WHERE employee_id = $1",
                new NpgsqlParameter<Guid> { TypedValue = id }
            )
        );
    }

    [Fact]
    public async Task New_address_closes_previous_and_earlier_date_is_rejected()
    {
        var company = await TestDb.CompanyAsync(database);
        var id = await CreateAsync(company, FakeIds.Pesel(BirthDate));

        await AddAddressAsync(id, new DateOnly(2026, 12, 1));
        var earlier = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAddressAsync(id, new DateOnly(2026, 11, 30))
        );

        Assert.Equal("""{"validFrom": ["beforeCurrent"]}""", earlier.Hint);
        Assert.Equal(
            "2026-11-30|",
            await TestDb.AdminScalarAsync<string>(
                database,
                """
                SELECT string_agg(coalesce(valid_to::text, ''), '|' ORDER BY valid_from)
                FROM employees.addresses WHERE employee_id = $1 AND kind = 'residence'
                """,
                new NpgsqlParameter<Guid> { TypedValue = id }
            )
        );
    }

    [Fact]
    public async Task Pracownik_cannot_list_or_read_employees()
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "pracownik")
        );
        await using var command = TestDb.Command(
            connection,
            "SELECT count(*) FROM employees_app.employee_list(NULL, NULL, 10, 0)"
        );

        var error = await Assert.ThrowsAsync<PostgresException>(command.ExecuteScalarAsync);

        Assert.Equal("U0004", error.SqlState);
    }

    private async Task<Guid> CreateAsync(
        Guid company,
        string? pesel,
        DateOnly? birthDate = null,
        string? document = null
    )
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        var taxOffice = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT tax_office_id FROM platform.companies WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        await using var command = TestDb.Command(
            connection,
            """
            CALL employees_app.employee_create($1, NULL, 'Anna', NULL, 'Testowa', $2, $3, $4, $5, NULL,
                'ul. Lipowa 1', '00-001', 'Przykładowo', DATE '2020-01-01', NULL)
            """,
            new NpgsqlParameter<Guid> { TypedValue = company },
            new NpgsqlParameter<string?> { TypedValue = pesel },
            new NpgsqlParameter<string?> { TypedValue = document },
            new NpgsqlParameter<DateOnly> { TypedValue = birthDate ?? BirthDate },
            new NpgsqlParameter<Guid> { TypedValue = taxOffice }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task GetAsync(Guid id, Guid actor)
    {
        await using var connection = await TestDb.ApiAsync(database, actor);
        await using var command = TestDb.Command(
            connection,
            "SELECT pesel FROM employees_app.employee_get($1)",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );
        await command.ExecuteScalarAsync();
    }

    private async Task AddAddressAsync(Guid id, DateOnly from)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "CALL employees_app.address_add($1, 'residence', 'ul. Klonowa 5', '00-002', 'Przykładowo', $2, NULL)",
            new NpgsqlParameter<Guid> { TypedValue = id },
            new NpgsqlParameter<DateOnly> { TypedValue = from }
        );
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> NumberAsync(Guid id) =>
        await TestDb.AdminScalarAsync<string>(
            database,
            "SELECT number FROM employees.employees WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );

    private async Task<List<Guid>> SearchAsync(Guid company, string search)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "SELECT id FROM employees_app.employee_list($1, $2, 100, 0)",
            new NpgsqlParameter<Guid> { TypedValue = company },
            new NpgsqlParameter<string> { TypedValue = search }
        );
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
