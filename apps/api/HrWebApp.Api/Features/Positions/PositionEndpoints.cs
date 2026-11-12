namespace HrWebApp.Api.Features.Positions;

public static class PositionEndpoints
{
    public static IServiceCollection AddPositions(this IServiceCollection services)
    {
        services.AddScoped<ListPositionsHandler>();
        services.AddScoped<CreatePositionHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapPositions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/positions").WithTags("Positions");
        group.MapGet("/", ListPositions.Handle).WithName("ListPositions");
        group
            .MapPost("/", CreatePosition.Handle)
            .WithName("CreatePosition")
            .ProducesValidationProblem();
        return app;
    }
}
