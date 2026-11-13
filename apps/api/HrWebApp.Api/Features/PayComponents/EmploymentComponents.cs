using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.PayComponents;

public static class ListEmploymentComponents
{
    /// <summary>Status: <c>future</c>, <c>active</c>, <c>ended</c>. Odczyt (kwoty) trafia do audytu odczytów.</summary>
    public sealed record Item(
        Guid Id,
        Guid ComponentTypeId,
        string Code,
        string Name,
        string Kind,
        string Calculation,
        decimal Value,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        Guid? ConsentId,
        string Status,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        EmploymentComponentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class AddEmploymentComponent
{
    /// <summary>Pusty <c>ValidTo</c> = do końca umowy. Potrącenie dobrowolne wymaga <c>ConsentId</c>.</summary>
    public sealed record Request(
        Guid? ComponentTypeId,
        decimal? Value,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        Guid? ConsentId
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        EmploymentComponentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.AddAsync(id, request, cancellationToken)));
}

public static class EndEmploymentComponent
{
    /// <summary>Pusta data cofa zakończenie.</summary>
    public sealed record Request(long RowVersion, DateOnly? ValidTo);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        EmploymentComponentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.EndAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteEmploymentComponent
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        EmploymentComponentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class EmploymentComponentsHandler(DbSession db)
{
    public async Task<List<ListEmploymentComponents.Item>> ListAsync(
        Guid employmentId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, component_type_id, code, name, kind, calculation, value, valid_from, valid_to, consent_id,
                   status, row_version
            FROM pay_components_app.employment_component_list($1)
            """,
            [Db.Param(employmentId)],
            reader => new ListEmploymentComponents.Item(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetFieldValue<DateOnly>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetString(10),
                reader.GetInt64(11)
            ),
            cancellationToken
        );

    public async Task<Guid> AddAsync(
        Guid employmentId,
        AddEmploymentComponent.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL pay_components_app.employment_component_add($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(employmentId),
                Db.Param(request.ComponentTypeId),
                Db.Param(request.Value),
                Db.Param(request.ValidFrom),
                Db.Param(request.ValidTo),
                Db.Param(request.ConsentId),
            ],
            cancellationToken
        );

    public async Task EndAsync(
        Guid id,
        EndEmploymentComponent.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.employment_component_end($1, $2, $3)",
            [Db.Param(id), Db.Param(request.RowVersion), Db.Param(request.ValidTo)],
            cancellationToken
        );

    public async Task DeleteAsync(Guid id, long rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.employment_component_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );
}
