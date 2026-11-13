using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.PayComponents;

public static class ListComponentTypes
{
    /// <summary>
    /// Kind: <c>addition</c>, <c>deduction</c>. Calculation: <c>fixed</c>, <c>percentOfBase</c>,
    /// <c>rateTimesHours</c>, <c>engine</c> (systemowe). DeductionCategory: <c>advance</c>, <c>penalty</c>,
    /// <c>voluntaryEmployer</c>, <c>voluntaryOther</c>, a dla systemowych <c>alimony</c>, <c>enforcement</c>.
    /// </summary>
    public sealed record Item(
        Guid Id,
        string Code,
        string Name,
        string Kind,
        string Calculation,
        bool? InZusBase,
        bool? InHealthBase,
        bool? Taxable,
        bool? InSicknessBase,
        string? DeductionCategory,
        bool RequiresConsent,
        bool IsSystem,
        bool IsActive,
        bool IsUsed,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        bool? includeInactive,
        ComponentTypesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(includeInactive ?? false, cancellationToken));
}

public static class CreateComponentType
{
    public sealed record Request(
        [property: MaxLength(20)] string? Code,
        [property: MaxLength(200)] string? Name,
        string Kind,
        string? Calculation,
        bool? InZusBase,
        bool? InHealthBase,
        bool? Taxable,
        bool? InSicknessBase,
        string? DeductionCategory
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Request request,
        ComponentTypesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.CreateAsync(request, cancellationToken)));
}

public static class UpdateComponentType
{
    /// <summary>Kod i rodzaj są niezmienne; systemowe → 409 <c>systemComponent</c>; zmiana sposobu liczenia
    /// przypisanego składnika → 409 <c>componentInUse</c>.</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(200)] string? Name,
        string? Calculation,
        bool? InZusBase,
        bool? InHealthBase,
        bool? Taxable,
        bool? InSicknessBase,
        string? DeductionCategory,
        bool? IsActive
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        ComponentTypesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UpdateAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class ComponentTypesHandler(DbSession db)
{
    public async Task<List<ListComponentTypes.Item>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, code, name, kind, calculation, in_zus_base, in_health_base, taxable, in_sickness_base,
                   deduction_category, requires_consent, is_system, is_active, is_used, row_version
            FROM pay_components_app.component_type_list($1)
            """,
            [Db.Param(includeInactive)],
            reader => new ListComponentTypes.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12),
                reader.GetBoolean(13),
                reader.GetInt64(14)
            ),
            cancellationToken
        );

    public async Task<Guid> CreateAsync(
        CreateComponentType.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL pay_components_app.component_type_create($1, $2, $3, $4, $5, $6, $7, $8, $9, NULL)",
            [
                Db.Param(request.Code),
                Db.Param(request.Name),
                Db.Param(request.Kind),
                Db.Param(request.Calculation),
                Db.Param(request.InZusBase),
                Db.Param(request.InHealthBase),
                Db.Param(request.Taxable),
                Db.Param(request.InSicknessBase),
                Db.Param(request.DeductionCategory),
            ],
            cancellationToken
        );

    public async Task UpdateAsync(
        Guid id,
        UpdateComponentType.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.component_type_update($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Name),
                Db.Param(request.Calculation),
                Db.Param(request.InZusBase),
                Db.Param(request.InHealthBase),
                Db.Param(request.Taxable),
                Db.Param(request.InSicknessBase),
                Db.Param(request.DeductionCategory),
                Db.Param(request.IsActive),
            ],
            cancellationToken
        );
}
