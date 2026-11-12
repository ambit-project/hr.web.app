using HrWebApp.Api.Database;
using HrWebApp.Api.Features.Positions;
using HrWebApp.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HrWebApp.Api.Tests.Features.Positions;

[Collection(DatabaseCollection.Name)]
public sealed class PositionHandlerTests(DatabaseFixture database)
{
    private readonly ApiFactory _factory = database.Api;

    [Fact]
    public async Task Created_position_is_listed_with_total()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var kadry = await TestDb.DevUserAsync(database, "kadry");
        foreach (var suffix in new[] { "C", "A", "B" })
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<DbSession>().ActorId = kadry;
            await scope
                .ServiceProvider.GetRequiredService<CreatePositionHandler>()
                .HandleAsync(
                    new CreatePosition.Request($"{marker} {suffix}", null),
                    CancellationToken.None
                );
        }

        await using var listScope = _factory.Services.CreateAsyncScope();
        listScope.ServiceProvider.GetRequiredService<DbSession>().ActorId = kadry;
        var page = await listScope
            .ServiceProvider.GetRequiredService<ListPositionsHandler>()
            .HandleAsync(new ListPositions.Query(marker, null, 2, 2), CancellationToken.None);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal([$"{marker} C"], page.Items.Select(i => i.Name));
    }
}
