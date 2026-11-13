using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Optima;

public static class ListOptimaDatabases
{
    /// <summary>
    /// Wiersz na aktywną spółkę i na bazę firmy zgłoszoną przez agenta. Baza bez spółki ma puste CompanyId, spółka
    /// bez bazy — puste DatabaseName. TaxNumberMatches: null, gdy agent nie podał NIP albo brak powiązania.
    /// </summary>
    public sealed record Item(
        Guid? CompanyId,
        string? CompanyShortName,
        string? CompanyNip,
        string? DatabaseName,
        string? TaxNumber,
        DateTimeOffset? LastSeenAt,
        bool? TaxNumberMatches
    );

    public static async Task<Ok<List<Item>>> Handle(
        OptimaDatabasesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(cancellationToken));
}

public static class SetCompanyDatabase
{
    /// <summary>Null usuwa powiązanie. Baza zgłoszona z innym NIP → 400 taxNumberMismatch.</summary>
    public sealed record Request(string? DatabaseName);

    public static async Task<NoContent> Handle(
        Guid companyId,
        Request request,
        OptimaDatabasesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.LinkAsync(companyId, request.DatabaseName, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ListPayTypes
{
    /// <summary>Rodzaj płacy (typ wypłaty) Optimy w bazie spółki i składnik hr.web.app, z którym jest powiązany.</summary>
    public sealed record Item(
        int OptimaId,
        string Name,
        string? ShortName,
        bool IsDeduction,
        int? SourceKind,
        int? StandardId,
        decimal? DefaultAmount,
        bool IsPkzp,
        bool IsSeizure,
        bool IsActive,
        bool IsWithdrawn,
        DateTimeOffset ImportedAt,
        DateTimeOffset? MissingSince,
        Guid? ComponentTypeId,
        string? ComponentCode,
        string? ComponentName
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid companyId,
        OptimaDatabasesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.PayTypesAsync(companyId, cancellationToken));
}

public static class MapPayType
{
    /// <summary>Null usuwa powiązanie. Błędy pola componentTypeId: notFound, inactive, kindMismatch, duplicate.</summary>
    public sealed record Request(Guid? ComponentTypeId);

    public static async Task<NoContent> Handle(
        Guid companyId,
        int optimaId,
        Request request,
        OptimaDatabasesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.MapAsync(companyId, optimaId, request.ComponentTypeId, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class OptimaDatabasesHandler(DbSession db)
{
    public async Task<List<ListOptimaDatabases.Item>> ListAsync(
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT company_id, company_short_name, company_nip, database_name, tax_number, last_seen_at,
                   tax_number_matches
            FROM optima_app.database_list()
            """,
            [],
            reader => new ListOptimaDatabases.Item(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetBoolean(6)
            ),
            cancellationToken
        );

    public async Task LinkAsync(
        Guid companyId,
        string? databaseName,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL optima_app.company_link_set($1, $2)",
            [Db.Param(companyId), Db.Param(databaseName)],
            cancellationToken
        );

    public async Task<List<ListPayTypes.Item>> PayTypesAsync(
        Guid companyId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT optima_id, name, short_name, is_deduction, source_kind, standard_id, default_amount, is_pkzp,
                   is_seizure, is_active, is_withdrawn, imported_at, missing_since, component_type_id,
                   component_code, component_name
            FROM optima_app.pay_type_list($1)
            """,
            [Db.Param(companyId)],
            reader => new ListPayTypes.Item(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)
            ),
            cancellationToken
        );

    public async Task MapAsync(
        Guid companyId,
        int optimaId,
        Guid? componentTypeId,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL optima_app.pay_type_map($1, $2, $3)",
            [Db.Param(companyId), Db.Param(optimaId), Db.Param(componentTypeId)],
            cancellationToken
        );
}
