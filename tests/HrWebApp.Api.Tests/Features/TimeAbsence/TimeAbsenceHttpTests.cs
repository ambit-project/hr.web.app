using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.TimeAbsence;

[Collection(DatabaseCollection.Name)]
public sealed class TimeAbsenceHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Kadry_generate_calendar_designate_day_and_register_leave()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await TestDb.CompanyAsync(database);

        var generated = await kadry.PostAsync($"/api/companies/{company}/calendar/2026", null);
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        Assert.Equal(
            2,
            (await generated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("saturdayHolidays")
                .GetInt32()
        );
        var again = await kadry.PostAsync($"/api/companies/{company}/calendar/2026", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("calendarExists", await again.Content.ReadAsStringAsync());

        var designated = await kadry.PutAsJsonAsync(
            $"/api/companies/{company}/calendar/days/2026-12-31",
            new { kind = "designatedOff" }
        );
        Assert.Equal(HttpStatusCode.NoContent, designated.StatusCode);
        var months = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/companies/{company}/calendar/2026"
        );
        Assert.Equal(12, months!.Length);
        Assert.Equal(160m, months[11].GetProperty("hours").GetDecimal());
        Assert.False(months[11].GetProperty("missingDesignated").GetBoolean());
        var days = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/companies/{company}/calendar/2026/12"
        );
        Assert.Equal("designatedOff", days![30].GetProperty("kind").GetString());
        Assert.Equal("Wigilia Bożego Narodzenia", days[23].GetProperty("holidayName").GetString());

        var employment = await EmploymentAsync(company, "employment");
        var saved = await kadry.PutAsJsonAsync(
            $"/api/employments/{employment}/leave/2026",
            new { baseDays = 26, firstJob = false }
        );
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var created = await kadry.PostAsJsonAsync(
            $"/api/employments/{employment}/absences",
            new
            {
                absenceTypeId = "UW",
                dateFrom = "2026-07-06",
                dateTo = "2026-07-10",
            }
        );
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var sickWithoutCertificate = await kadry.PostAsJsonAsync(
            $"/api/employments/{employment}/absences",
            new
            {
                absenceTypeId = "CH",
                dateFrom = "2026-08-03",
                dateTo = "2026-08-04",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, sickWithoutCertificate.StatusCode);
        Assert.Contains(
            "certificateNumber",
            await sickWithoutCertificate.Content.ReadAsStringAsync()
        );

        var leave = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/employments/{employment}/leave/2026"
        );
        Assert.Equal(208m, leave.GetProperty("entitledHours").GetDecimal());
        Assert.Equal(40m, leave.GetProperty("usedHours").GetDecimal());
        Assert.Equal(21m, leave.GetProperty("balanceDays").GetDecimal());

        var norm = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/employments/{employment}/norm/2026?month=11"
        );
        Assert.Equal(160m, Assert.Single(norm!).GetProperty("normHours").GetDecimal());

        var absence = Assert.Single(
            (
                await kadry.GetFromJsonAsync<JsonElement[]>(
                    $"/api/employments/{employment}/absences?year=2026"
                )
            )!
        );
        Assert.Equal(40m, absence.GetProperty("workHours").GetDecimal());
        var deleted = await kadry.DeleteAsync(
            $"/api/absences/{absence.GetProperty("id").GetGuid()}?rowVersion={absence.GetProperty("rowVersion").GetInt64()}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Kadry_record_overtime_and_mandate_hours()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await TestDb.CompanyAsync(database);
        var employment = await EmploymentAsync(company, "employment");
        var mandate = await EmploymentAsync(company, "mandate");

        var overtime = await kadry.PostAsJsonAsync(
            $"/api/employments/{employment}/overtime",
            new
            {
                date = "2026-03-02",
                hours = 2.5m,
                rate = 50,
            }
        );
        Assert.Equal(HttpStatusCode.OK, overtime.StatusCode);
        var wrongKind = await kadry.PostAsJsonAsync(
            $"/api/employments/{mandate}/overtime",
            new
            {
                date = "2026-03-02",
                hours = 2.5m,
                rate = 50,
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, wrongKind.StatusCode);
        Assert.Contains("kindNotAllowed", await wrongKind.Content.ReadAsStringAsync());
        var list = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/employments/{employment}/overtime?year=2026"
        );
        Assert.Equal(2.5m, list.GetProperty("totalHours").GetDecimal());
        Assert.False(list.GetProperty("aboveLimit").GetBoolean());

        var hours = await kadry.PostAsJsonAsync(
            $"/api/employments/{mandate}/mandate-hours",
            new
            {
                dateFrom = "2026-03-01",
                dateTo = "2026-03-31",
                hours = 80m,
            }
        );
        Assert.Equal(HttpStatusCode.OK, hours.StatusCode);
        var item = Assert.Single(
            (
                await kadry.GetFromJsonAsync<JsonElement[]>(
                    $"/api/employments/{mandate}/mandate-hours?year=2026"
                )
            )!
        );
        Assert.False(item.GetProperty("confirmed").GetBoolean());
        var confirmed = await kadry.PutAsJsonAsync(
            $"/api/mandate-hours/{item.GetProperty("id").GetGuid()}",
            new
            {
                rowVersion = item.GetProperty("rowVersion").GetInt64(),
                dateFrom = "2026-03-01",
                dateTo = "2026-03-31",
                hours = 80m,
                confirmedAt = "2026-04-01",
                confirmationForm = "documentary",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    [Fact]
    public async Task Pracownik_reads_own_leave_and_absences_and_gets_403_elsewhere()
    {
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var own = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT l.employee_id FROM platform.user_employee_links l JOIN platform.users u ON u.id = l.user_id WHERE u.login = 'pracownik' LIMIT 1"
        );
        var company = await TestDb.CompanyAsync(database);
        var employment = await EmploymentAsync(company, "employment");
        var foreign = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT employee_id FROM employment.employments WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = employment }
        );

        var balance = Assert.Single(
            (await pracownik.GetFromJsonAsync<JsonElement[]>($"/api/my/{own}/leave/2026"))!
        );
        var absences = await pracownik.GetFromJsonAsync<JsonElement[]>(
            $"/api/my/{own}/absences/2026"
        );
        var types = await pracownik.GetFromJsonAsync<JsonElement[]>("/api/absence-types");
        var foreignBalance = await pracownik.GetAsync($"/api/my/{foreign}/leave/2026");
        var list = await pracownik.GetAsync($"/api/employments/{employment}/absences?year=2026");
        var holiday = await kadry.PostAsJsonAsync(
            "/api/holidays",
            new { date = "2150-01-02", name = "Test" }
        );

        Assert.Equal(184m, balance.GetProperty("balanceHours").GetDecimal());
        Assert.Equal(23m, balance.GetProperty("balanceDays").GetDecimal());
        Assert.Equal(2, absences!.Length);
        Assert.Equal(11, types!.Length);
        Assert.Equal(HttpStatusCode.Forbidden, foreignBalance.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, holiday.StatusCode);
    }

    private async Task<Guid> EmploymentAsync(Guid company, string kind)
    {
        var employee = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, left(gen_random_uuid()::text, 20), 'Jan', 'Kowalski', DATE '1985-01-01') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        var employment = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date, end_date)
            VALUES ($1, $2, gen_random_uuid()::text, $3, CASE WHEN $3 = 'employment' THEN 'indefinite' END,
                    DATE '2026-01-01', DATE '2026-01-01', CASE WHEN $3 <> 'employment' THEN DATE '2026-12-31' END)
            RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = employee },
            new NpgsqlParameter<Guid> { TypedValue = company },
            new NpgsqlParameter<string> { TypedValue = kind }
        );
        await TestDb.AdminScalarAsync<object>(
            database,
            """
            INSERT INTO employment.terms (employment_id, valid_from, fte_numerator, fte_denominator, pay_basis,
                                          base_amount, insurances, cost_kind)
            SELECT m.id, m.start_date, CASE WHEN m.kind = 'employment' THEN 1 END,
                   CASE WHEN m.kind = 'employment' THEN 1 END,
                   CASE WHEN m.kind = 'employment' THEN 'monthly' ELSE 'hourly' END, 6000,
                   ARRAY['pension', 'disability', 'sickness', 'health'], 'standard'
            FROM employment.employments m WHERE m.id = $1
            """,
            new NpgsqlParameter<Guid> { TypedValue = employment }
        );
        return employment;
    }
}
