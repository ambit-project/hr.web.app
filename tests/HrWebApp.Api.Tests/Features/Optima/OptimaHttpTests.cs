using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.Optima;

[Collection(DatabaseCollection.Name)]
public sealed class OptimaHttpTests(DatabaseFixture database)
{
    private static readonly string[] ReadKinds = ["ReadPayrollComponents", "ReadPayrolls"];

    private static readonly string[] AllKinds =
    [
        "ReadPayrollComponents",
        "ReadPayrolls",
        "AddEmployees",
        "ExportDeductions",
        "ExportAllowances",
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("optima_not-a-key")]
    [InlineData("optima_0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Agent_endpoints_refuse_missing_malformed_or_unknown_key(string? key)
    {
        using var client = database.Api.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Optima-Agent-Key", key);
        }

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/optima/agent/connection")).StatusCode
        );
    }

    [Fact]
    public async Task Revoked_key_is_refused_and_key_and_session_do_not_mix()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");
        var created = await admin.PostAsJsonAsync("/api/optima/agent-keys", new { name = "Biuro" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        Assert.Matches("^optima_[0-9a-f]{64}$", token);

        using var agent = OptimaTestData.AgentClient(database, token);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await agent.GetAsync("/api/optima/agent/connection")).StatusCode
        );
        // Klucz agenta nie otwiera endpointów użytkowników, a sesja nie otwiera endpointów agenta.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await agent.GetAsync("/api/companies")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await admin.GetAsync("/api/optima/agent/connection")).StatusCode
        );

        var keys = await admin.GetFromJsonAsync<JsonElement>("/api/optima/agent-keys");
        Assert.Contains(keys.EnumerateArray(), k =>
            k.GetProperty("keyPrefix").GetString() == token[..13]
        );

        var id = body.GetProperty("id").GetGuid();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/optima/agent-keys/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await agent.GetAsync("/api/optima/agent/connection")).StatusCode
        );
    }

    [Fact]
    public async Task Only_administrator_manages_agent_keys()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await kadry.PostAsJsonAsync("/api/optima/agent-keys", new { name = "x" })).StatusCode
        );
    }

    [Fact]
    public async Task Read_payroll_components_end_to_end_links_company_by_tax_number()
    {
        await OptimaTestData.ClearQueueAsync(database);
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var agent = OptimaTestData.AgentClient(
            database,
            await OptimaTestData.AgentKeyAsync(database)
        );
        var company = await TestDb.CompanyAsync(database);
        var nip = await TestDb.AdminScalarAsync<string>(
            database,
            "SELECT nip FROM platform.companies WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        var databaseName = "CDN_" + Unique.Code();

        Assert.Equal(HttpStatusCode.NoContent, (await ClaimAsync(agent, AllKinds)).StatusCode);
        var created = await kadry.PostAsJsonAsync(
            "/api/optima/operations",
            new { kind = "ReadPayrollComponents" }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var operationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var claim = await ClaimAsync(agent, ReadKinds);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var claimed = await claim.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(operationId, claimed.GetProperty("id").GetGuid());
        Assert.Equal("ReadPayrollComponents", claimed.GetProperty("kind").GetString());
        Assert.Equal(1, claimed.GetProperty("attempt").GetInt32());

        var result = new
        {
            attempt = 1,
            succeeded = true,
            errorCode = (string?)null,
            log = new[]
            {
                new
                {
                    at = DateTimeOffset.UtcNow,
                    code = "database_read",
                    databaseName,
                    count = 2,
                },
            },
            payrollComponents = new[]
            {
                new
                {
                    databaseName,
                    taxNumber = nip,
                    components = new object[]
                    {
                        new
                        {
                            optimaId = 338,
                            name = "Potrącenie za mieszkanie",
                            shortName = "pot.mieszkanie",
                            isDeduction = true,
                            sourceKind = 4,
                            standardId = 0,
                            defaultAmount = 0.00m,
                            isPkzp = false,
                            isSeizure = false,
                            isActive = true,
                            isWithdrawn = false,
                        },
                        new
                        {
                            optimaId = 12,
                            name = "Premia uznaniowa",
                            shortName = (string?)null,
                            isDeduction = false,
                            sourceKind = 1,
                            standardId = (int?)null,
                            defaultAmount = (decimal?)null,
                            isPkzp = false,
                            isSeizure = false,
                            isActive = false,
                            isWithdrawn = true,
                        },
                    },
                },
            },
        };
        var url = $"/api/optima/agent/operations/{operationId}/result";
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await agent.PostAsJsonAsync(url, result)).StatusCode
        );
        // Powtórzony wynik: ta sama odpowiedź, bez zmian.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await agent.PostAsJsonAsync(url, result)).StatusCode
        );

        var operation = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/optima/operations/{operationId}"
        );
        Assert.Equal("succeeded", operation.GetProperty("state").GetString());
        Assert.Equal(
            "database_read",
            operation.GetProperty("log")[0].GetProperty("code").GetString()
        );

        var databases = await kadry.GetFromJsonAsync<JsonElement>("/api/optima/databases");
        var linked = databases
            .EnumerateArray()
            .Single(d => d.GetProperty("databaseName").GetString() == databaseName);
        Assert.Equal(company, linked.GetProperty("companyId").GetGuid());
        Assert.True(linked.GetProperty("taxNumberMatches").GetBoolean());

        var payTypes = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/optima/companies/{company}/pay-types"
        );
        Assert.Equal(2, payTypes.GetArrayLength());
        Assert.Contains(payTypes.EnumerateArray(), p =>
            p.GetProperty("optimaId").GetInt32() == 12
            && p.GetProperty("isWithdrawn").GetBoolean()
            && !p.GetProperty("isActive").GetBoolean()
        );
    }

    [Fact]
    public async Task Add_employees_hands_out_decrypted_data_and_audits_the_read()
    {
        await OptimaTestData.ClearQueueAsync(database);
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var agent = OptimaTestData.AgentClient(
            database,
            await OptimaTestData.AgentKeyAsync(database)
        );
        var (company, databaseName, _) = await OptimaTestData.LinkedCompanyAsync(database);
        var employee = await OptimaTestData.EmployeeAsync(database, company);
        await OptimaTestData.EmployeeAsync(database, company, "Nowak", document: "X-1");

        var created = await kadry.PostAsJsonAsync(
            "/api/optima/operations",
            new { kind = "AddEmployees", companyId = company }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var operationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // Bez zapisu włączonego w agencie operacja zapisu nie jest wydawana.
        Assert.Equal(HttpStatusCode.NoContent, (await ClaimAsync(agent, ReadKinds)).StatusCode);
        // Przed wydaniem agentowi danych nie ma.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await agent.GetAsync($"/api/optima/agent/operations/{operationId}/employees")
            ).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(agent, AllKinds)).StatusCode);

        var work = await agent.GetFromJsonAsync<JsonElement>(
            $"/api/optima/agent/operations/{operationId}/employees"
        );
        Assert.Equal(databaseName, work.GetProperty("databaseName").GetString());
        var employees = work.GetProperty("employees").EnumerateArray().ToList();
        Assert.Equal(2, employees.Count);
        var kowalski = employees.Single(e => e.GetProperty("employeeId").GetGuid() == employee);
        Assert.Matches("^\\d{11}$", kowalski.GetProperty("pesel").GetString());
        Assert.Equal("IdentityCard", kowalski.GetProperty("documentKind").GetString());
        Assert.Equal("2026-01-01", kowalski.GetProperty("startsOn").GetString());
        Assert.Equal(JsonValueKind.Null, kowalski.GetProperty("endsOn").ValueKind);
        Assert.Equal(
            "Other",
            employees
                .Single(e => e.GetProperty("lastName").GetString() == "Nowak")
                .GetProperty("documentKind")
                .GetString()
        );

        Assert.Equal(
            1L,
            await TestDb.AdminScalarAsync<long>(
                database,
                """
                SELECT count(*) FROM platform.read_audit_entries
                WHERE employee_id = $1 AND resource = 'optima_export' AND resource_id = $2
                """,
                new NpgsqlParameter<Guid> { TypedValue = employee },
                new NpgsqlParameter<string> { TypedValue = operationId.ToString() }
            )
        );

        // Ten sam id, inny rodzaj → 404 (ostateczny dla agenta).
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await agent.GetAsync($"/api/optima/agent/operations/{operationId}/deductions")
            ).StatusCode
        );
    }

    [Fact]
    public async Task Export_allowances_sends_mapped_fixed_items_and_records_skipped()
    {
        await OptimaTestData.ClearQueueAsync(database);
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var agent = OptimaTestData.AgentClient(
            database,
            await OptimaTestData.AgentKeyAsync(database)
        );
        var (company, databaseName, _) = await OptimaTestData.LinkedCompanyAsync(database);
        var employee = await OptimaTestData.EmployeeAsync(database, company);
        var employment = await OptimaTestData.EmploymentOfAsync(database, employee);
        var mapped = await OptimaTestData.ComponentTypeAsync(database, "addition");
        var unmapped = await OptimaTestData.ComponentTypeAsync(database, "addition");
        var percent = await OptimaTestData.ComponentTypeAsync(
            database,
            "addition",
            "percentOfBase"
        );
        await OptimaTestData.PayTypeAsync(database, databaseName, 12, false, mapped);
        var sent = await OptimaTestData.AssignAsync(
            database,
            employment,
            mapped,
            300m,
            new DateOnly(2026, 10, 15)
        );
        await OptimaTestData.AssignAsync(
            database,
            employment,
            unmapped,
            50m,
            new DateOnly(2026, 1, 1)
        );
        await OptimaTestData.AssignAsync(
            database,
            employment,
            percent,
            10m,
            new DateOnly(2026, 1, 1)
        );
        // Kończy się przed listopadem — nie należy do miesiąca.
        await OptimaTestData.AssignAsync(
            database,
            employment,
            mapped,
            100m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 10, 14)
        );

        var created = await kadry.PostAsJsonAsync(
            "/api/optima/operations",
            new
            {
                kind = "ExportAllowances",
                companyId = company,
                year = 2026,
                month = 11,
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var operationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(agent, AllKinds)).StatusCode);

        var work = await agent.GetFromJsonAsync<JsonElement>(
            $"/api/optima/agent/operations/{operationId}/allowances"
        );
        Assert.Equal(2026, work.GetProperty("year").GetInt32());
        Assert.Equal(11, work.GetProperty("month").GetInt32());
        var item = Assert.Single(work.GetProperty("allowances").EnumerateArray());
        Assert.Equal(sent, item.GetProperty("allowanceId").GetGuid());
        Assert.Equal(12, item.GetProperty("optimaId").GetInt32());
        Assert.Equal(300m, item.GetProperty("amount").GetDecimal());
        Assert.Equal("PLN", item.GetProperty("currency").GetString());

        var operation = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/optima/operations/{operationId}"
        );
        var reasons = operation
            .GetProperty("skippedItems")
            .EnumerateArray()
            .Select(s => s.GetProperty("reason").GetString())
            .Order()
            .ToList();
        Assert.Equal(["notFixed", "notMapped"], reasons);
    }

    [Fact]
    public async Task Operation_for_unlinked_company_is_refused()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await TestDb.CompanyAsync(database);

        var response = await kadry.PostAsJsonAsync(
            "/api/optima/operations",
            new
            {
                kind = "ReadPayrolls",
                companyId = company,
                year = 2026,
                month = 10,
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("notLinked", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Read_payrolls_end_to_end_and_retry_ignores_the_older_attempt()
    {
        await OptimaTestData.ClearQueueAsync(database);
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var agent = OptimaTestData.AgentClient(
            database,
            await OptimaTestData.AgentKeyAsync(database)
        );
        var (company, databaseName, nip) = await OptimaTestData.LinkedCompanyAsync(database);
        var employee = await OptimaTestData.EmployeeAsync(database, company);

        var created = await kadry.PostAsJsonAsync(
            "/api/optima/operations",
            new
            {
                kind = "ReadPayrolls",
                companyId = company,
                year = 2026,
                month = 10,
            }
        );
        var operationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(agent, ReadKinds)).StatusCode);
        var period = await agent.GetFromJsonAsync<JsonElement>(
            $"/api/optima/agent/operations/{operationId}/payroll-period"
        );
        Assert.Equal(databaseName, period.GetProperty("databaseName").GetString());

        // Agent nie odpowiada; operator zleca ponowną próbę.
        var retry = await kadry.PostAsync($"/api/optima/operations/{operationId}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(
            2,
            (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attempt").GetInt32()
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await kadry.PostAsync($"/api/optima/operations/{operationId}/retry", null)).StatusCode
        );

        var url = $"/api/optima/agent/operations/{operationId}/result";
        // Spóźniony wynik pierwszej próby jest ignorowany.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await agent.PostAsJsonAsync(url, Payrolls(1, databaseName, nip, employee))).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(agent, ReadKinds)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await agent.PostAsJsonAsync(url, Payrolls(2, databaseName, nip, employee))).StatusCode
        );

        var lists = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/optima/companies/{company}/payrolls?year=2026&month=10"
        );
        var list = Assert.Single(lists.EnumerateArray());
        Assert.Equal("LP/10/2026", list.GetProperty("number").GetString());
        var payouts = list.GetProperty("payouts").EnumerateArray().ToList();
        Assert.Equal(2, payouts.Count);
        var known = payouts.Single(p => p.GetProperty("optimaId").GetInt32() == 901);
        Assert.Equal(employee, known.GetProperty("employeeId").GetGuid());
        Assert.Equal("Kowalski Jan", known.GetProperty("employeeName").GetString());
        Assert.Equal(4712.34m, known.GetProperty("net").GetDecimal());
        Assert.Equal(2, known.GetProperty("elements").GetArrayLength());
        Assert.Equal(
            JsonValueKind.Null,
            payouts
                .Single(p => p.GetProperty("optimaId").GetInt32() == 902)
                .GetProperty("employeeId")
                .ValueKind
        );
        Assert.True(
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.read_audit_entries WHERE employee_id = $1 AND resource = 'optima_payroll'",
                new NpgsqlParameter<Guid> { TypedValue = employee }
            ) >= 1
        );
    }

    [Fact]
    public async Task Result_with_a_malformed_payload_is_a_bad_request()
    {
        await OptimaTestData.ClearQueueAsync(database);
        using var agent = OptimaTestData.AgentClient(
            database,
            await OptimaTestData.AgentKeyAsync(database)
        );
        var operationId = await OptimaTestData.PendingOperationAsync(database);
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(agent, ReadKinds)).StatusCode);

        var response = await agent.PostAsJsonAsync(
            $"/api/optima/agent/operations/{operationId}/result",
            new
            {
                attempt = 1,
                succeeded = true,
                log = Array.Empty<object>(),
                payrollComponents = new[]
                {
                    new
                    {
                        databaseName = "CDN_X",
                        components = new[] { new { optimaId = "abc", name = "x" } },
                    },
                },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> ClaimAsync(HttpClient agent, string[] kinds) =>
        await agent.PostAsJsonAsync(
            "/api/optima/agent/operations/claim",
            new { agentVersion = "0.5.0.0", supportedKinds = kinds }
        );

    private static object Payrolls(int attempt, string databaseName, string nip, Guid employee) =>
        new
        {
            attempt,
            succeeded = true,
            log = new[]
            {
                new
                {
                    at = DateTimeOffset.UtcNow,
                    code = "database_read",
                    count = 2,
                },
            },
            payrolls = new
            {
                databaseName,
                taxNumber = nip,
                year = 2026,
                month = 10,
                lists = new[]
                {
                    new
                    {
                        optimaId = 41,
                        number = "LP/10/2026",
                        name = "Etat",
                        periodFrom = "2026-10-01",
                        periodTo = "2026-10-31",
                        documentDate = "2026-10-31",
                        isClosed = true,
                        payouts = new object[]
                        {
                            new
                            {
                                optimaId = 901,
                                optimaEmployeeId = 77,
                                employeeId = employee,
                                employeeCode = "IGNORED",
                                gross = 6500.00m,
                                net = 4712.34m,
                                isClosed = true,
                                elements = new[]
                                {
                                    new
                                    {
                                        optimaId = 5001,
                                        componentOptimaId = 1,
                                        name = "Wynagrodzenie zasadnicze",
                                        amount = 6000.00m,
                                    },
                                    new
                                    {
                                        optimaId = 5002,
                                        componentOptimaId = 12,
                                        name = "Premia",
                                        amount = 500.00m,
                                    },
                                },
                            },
                            new
                            {
                                optimaId = 902,
                                optimaEmployeeId = 78,
                                employeeId = (Guid?)null,
                                employeeCode = "OBCY",
                                gross = 3000.00m,
                                net = 2300.00m,
                                isClosed = true,
                                elements = Array.Empty<object>(),
                            },
                        },
                    },
                },
            },
        };
}
