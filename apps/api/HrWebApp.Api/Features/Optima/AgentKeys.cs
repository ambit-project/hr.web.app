using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using HrWebApp.Api.Features.Optima.Agent;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Optima;

public static class ListAgentKeys
{
    public sealed record Item(
        Guid Id,
        string Name,
        string KeyPrefix,
        DateTimeOffset CreatedAt,
        DateTimeOffset? RevokedAt,
        DateTimeOffset? LastSeenAt,
        string? LastAgentVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        AgentKeysHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(cancellationToken));
}

public static class CreateAgentKey
{
    public sealed record Request([property: MaxLength(100)] string? Name);

    /// <summary>Token pokazywany tylko raz — baza zna jego skrót. Wpisuje się go w instalatorze integratora.</summary>
    public sealed record Response(Guid Id, string Token);

    public static async Task<Ok<Response>> Handle(
        Request request,
        AgentKeysHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.CreateAsync(request.Name, cancellationToken));
}

public static class RevokeAgentKey
{
    public static async Task<NoContent> Handle(
        Guid id,
        AgentKeysHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.RevokeAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class AgentKeysHandler(DbSession db)
{
    public async Task<List<ListAgentKeys.Item>> ListAsync(CancellationToken cancellationToken) =>
        await db.QueryAsync(
            """
            SELECT id, name, key_prefix, created_at, revoked_at, last_seen_at, last_agent_version
            FROM optima_app.agent_key_list()
            """,
            [],
            reader => new ListAgentKeys.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)
            ),
            cancellationToken
        );

    public async Task<CreateAgentKey.Response> CreateAsync(
        string? name,
        CancellationToken cancellationToken
    )
    {
        var token = AgentToken.New();
        var id = await db.ScalarAsync<Guid>(
            "CALL optima_app.agent_key_create($1, $2, $3, NULL)",
            [
                Db.Param(name),
                Db.Param(AgentToken.Hash(token)),
                Db.Param(AgentToken.DisplayPrefix(token)),
            ],
            cancellationToken
        );
        return new CreateAgentKey.Response(id, token);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL optima_app.agent_key_revoke($1)",
            [Db.Param(id)],
            cancellationToken
        );
}
