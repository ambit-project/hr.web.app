using System.Text.Json;
using System.Text.RegularExpressions;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using NpgsqlTypes;

namespace HrWebApp.Api.Features.Optima.Agent;

/// <summary>
/// Endpointy dla Ambit Optima Integrator (kontrakt: docs/hr-application-api.md w repo integratora, specyfikacja 011).
/// Agent pyta o pracę (claim) co 500 ms; operacja ma id, rodzaj i numer próby.
/// </summary>
public static class AgentConnection
{
    /// <summary>Sprawdza klucz i niczego nie zmienia (instalator integratora).</summary>
    public static NoContent Handle() => TypedResults.NoContent();
}

public static class ClaimOperation
{
    public sealed record Request(string? AgentVersion, IReadOnlyList<string>? SupportedKinds);

    /// <summary>Kind: ReadPayrollComponents, AddEmployees, ExportDeductions, ExportAllowances, ReadPayrolls.</summary>
    public sealed record Response(Guid Id, string Kind, int Attempt);

    public static async Task<Results<Ok<Response>, NoContent>> Handle(
        Request request,
        HttpContext context,
        AgentHandler handler,
        CancellationToken cancellationToken
    )
    {
        var operation = await handler.ClaimAsync(
            AgentKeyAuthenticationHandler.KeyHash(context),
            request,
            cancellationToken
        );
        return operation is null ? TypedResults.NoContent() : TypedResults.Ok(operation);
    }
}

public static class GetOperationEmployees
{
    /// <summary>
    /// DocumentKind: IdentityCard (dokument w formacie dowodu osobistego) albo Other. CitizenshipCode, phone,
    /// buildingNumber, unitNumber, note i documentValidUntil są zawsze puste — hr.web.app ich nie prowadzi.
    /// </summary>
    public sealed record Employee(
        Guid EmployeeId,
        string EmployeeCode,
        string LastName,
        string FirstName,
        DateOnly? DateOfBirth,
        string? Pesel,
        string? CitizenshipCode,
        string? Email,
        string? Phone,
        string? Street,
        string? BuildingNumber,
        string? UnitNumber,
        string? PostalCode,
        string? City,
        string? AddressCountryCode,
        string? Note,
        string? DocumentKind,
        string? DocumentNumber,
        DateOnly? DocumentValidUntil,
        string? PositionName,
        DateOnly? StartsOn,
        DateOnly? EndsOn
    )
    {
        /// <summary>Dane osobowe — wypisanie rekordu nie może ich wynieść do logu.</summary>
        public override string ToString() => nameof(Employee);
    }

    public sealed record Response(string DatabaseName, IReadOnlyList<Employee> Employees);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        HttpContext context,
        AgentHandler handler,
        CancellationToken cancellationToken
    ) =>
        TypedResults.Ok(
            await handler.EmployeesAsync(
                AgentKeyAuthenticationHandler.KeyHash(context),
                id,
                cancellationToken
            )
        );
}

public static class GetOperationDeductions
{
    public sealed record Item(
        Guid DeductionId,
        Guid EmployeeId,
        string EmployeeCode,
        int OptimaId,
        decimal Amount,
        string Currency
    );

    public sealed record Response(
        string DatabaseName,
        int Year,
        int Month,
        IReadOnlyList<Item> Deductions
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        HttpContext context,
        AgentHandler handler,
        CancellationToken cancellationToken
    )
    {
        var (header, items) = await handler.ComponentsAsync(
            AgentKeyAuthenticationHandler.KeyHash(context),
            id,
            "ExportDeductions",
            cancellationToken
        );
        return TypedResults.Ok(
            new Response(
                header.DatabaseName,
                header.Year,
                header.Month,
                [
                    .. items.Select(i => new Item(
                        i.ItemId,
                        i.EmployeeId,
                        i.EmployeeCode,
                        i.OptimaId,
                        i.Amount,
                        "PLN"
                    )),
                ]
            )
        );
    }
}

public static class GetOperationAllowances
{
    public sealed record Item(
        Guid AllowanceId,
        Guid EmployeeId,
        string EmployeeCode,
        int OptimaId,
        decimal Amount,
        string Currency
    );

    public sealed record Response(
        string DatabaseName,
        int Year,
        int Month,
        IReadOnlyList<Item> Allowances
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        HttpContext context,
        AgentHandler handler,
        CancellationToken cancellationToken
    )
    {
        var (header, items) = await handler.ComponentsAsync(
            AgentKeyAuthenticationHandler.KeyHash(context),
            id,
            "ExportAllowances",
            cancellationToken
        );
        return TypedResults.Ok(
            new Response(
                header.DatabaseName,
                header.Year,
                header.Month,
                [
                    .. items.Select(i => new Item(
                        i.ItemId,
                        i.EmployeeId,
                        i.EmployeeCode,
                        i.OptimaId,
                        i.Amount,
                        "PLN"
                    )),
                ]
            )
        );
    }
}

public static class GetPayrollPeriod
{
    public sealed record Response(string DatabaseName, int Year, int Month);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        HttpContext context,
        AgentHandler handler,
        CancellationToken cancellationToken
    )
    {
        var header = await handler.HeaderAsync(
            AgentKeyAuthenticationHandler.KeyHash(context),
            id,
            "ReadPayrolls",
            cancellationToken
        );
        return TypedResults.Ok(new Response(header.DatabaseName, header.Year, header.Month));
    }
}

public static class ReportResult
{
    /// <summary>
    /// Wynik jednej próby. Log: [{at, code, databaseName?, errorNumber?, count?, employeeId?, deductionId?,
    /// allowanceId?, warningCodes?}]. PayrollComponents tylko przy ReadPayrollComponents, payrolls tylko przy
    /// ReadPayrolls. Powtórzony wynik i wynik starszej próby są ignorowane (204).
    /// </summary>
    public sealed record Request(
        int Attempt,
        bool Succeeded,
        string? ErrorCode,
        JsonElement? Log,
        JsonElement? PayrollComponents,
        JsonElement? Payrolls
    );

    /// <summary>Limit treści wyniku (lista płac jednej firmy za miesiąc mieści się z zapasem).</summary>
    public const long MaxBodyBytes = 20 * 1024 * 1024;

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        HttpContext context,
        AgentHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.ResultAsync(
            AgentKeyAuthenticationHandler.KeyHash(context),
            id,
            request,
            cancellationToken
        );
        return TypedResults.NoContent();
    }
}

public sealed partial class AgentHandler(DbSession db)
{
    public sealed record Header(string DatabaseName, int Year, int Month);

    public sealed record ComponentItem(
        Guid ItemId,
        Guid EmployeeId,
        string EmployeeCode,
        int OptimaId,
        decimal Amount
    );

    public async Task<ClaimOperation.Response?> ClaimAsync(
        byte[] keyHash,
        ClaimOperation.Request request,
        CancellationToken cancellationToken
    )
    {
        var rows = await db.QueryAsync(
            "CALL optima_app.agent_claim($1, $2, $3, NULL, NULL, NULL)",
            [
                Db.Param(keyHash),
                Db.Param(request.AgentVersion),
                Db.Param((request.SupportedKinds ?? []).ToArray()),
            ],
            reader =>
                reader.IsDBNull(0)
                    ? null
                    : new ClaimOperation.Response(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetInt32(2)
                    ),
            cancellationToken
        );
        return rows.SingleOrDefault();
    }

    public async Task<Header> HeaderAsync(
        byte[] keyHash,
        Guid operationId,
        string kind,
        CancellationToken cancellationToken
    )
    {
        var rows = await db.QueryAsync(
            "SELECT database_name, year, month FROM optima_app.agent_operation($1, $2, $3)",
            [Db.Param(keyHash), Db.Param(operationId), Db.Param(kind)],
            reader => new Header(
                reader.GetString(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
            ),
            cancellationToken
        );
        return rows.Single();
    }

    public async Task<GetOperationEmployees.Response> EmployeesAsync(
        byte[] keyHash,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        var header = await HeaderAsync(keyHash, operationId, "AddEmployees", cancellationToken);
        var employees = await db.QueryAsync(
            """
            SELECT employee_id, employee_code, last_name, first_name, date_of_birth, pesel, email, street,
                   postal_code, city, address_country_code, identity_document, position_name, starts_on, ends_on
            FROM optima_app.agent_employees($1, $2)
            """,
            [Db.Param(keyHash), Db.Param(operationId)],
            reader =>
            {
                var document = reader.IsDBNull(11) ? null : reader.GetString(11);
                return new GetOperationEmployees.Employee(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    null,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    null,
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    null,
                    null,
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    null,
                    DocumentKind(document),
                    document,
                    null,
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetFieldValue<DateOnly>(13),
                    reader.IsDBNull(14) ? null : reader.GetFieldValue<DateOnly>(14)
                );
            },
            cancellationToken
        );
        return new GetOperationEmployees.Response(header.DatabaseName, employees);
    }

    public async Task<(Header Header, List<ComponentItem> Items)> ComponentsAsync(
        byte[] keyHash,
        Guid operationId,
        string kind,
        CancellationToken cancellationToken
    )
    {
        var header = await HeaderAsync(keyHash, operationId, kind, cancellationToken);
        var items = await db.QueryAsync(
            """
            SELECT item_id, employee_id, employee_code, optima_id, amount
            FROM optima_app.agent_components($1, $2, $3)
            """,
            [Db.Param(keyHash), Db.Param(operationId), Db.Param(kind)],
            reader => new ComponentItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetDecimal(4)
            ),
            cancellationToken
        );
        return (header, items);
    }

    public async Task ResultAsync(
        byte[] keyHash,
        Guid operationId,
        ReportResult.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL optima_app.agent_result($1, $2, $3, $4, $5, $6, $7, $8)",
            [
                Db.Param(keyHash),
                Db.Param(operationId),
                Db.Param(request.Attempt),
                Db.Param(request.Succeeded),
                Db.Param(request.ErrorCode),
                Json(request.Log),
                Json(request.PayrollComponents),
                Json(request.Payrolls),
            ],
            cancellationToken
        );

    /// <summary>Optima przechowuje tylko dowód osobisty i paszport; z wolnego tekstu rozpoznajemy dowód.</summary>
    private static string? DocumentKind(string? document) =>
        document is null ? null
        : IdentityCardPattern().IsMatch(document) ? "IdentityCard"
        : "Other";

    private static NpgsqlParameter Json(JsonElement? value) =>
        new()
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value =
                value is null || value.Value.ValueKind == JsonValueKind.Null
                    ? DBNull.Value
                    : value.Value.GetRawText(),
        };

    [GeneratedRegex("^[A-Z]{3}[0-9]{6}$")]
    private static partial Regex IdentityCardPattern();
}
