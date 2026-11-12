namespace HrWebApp.Api.Features.Positions;

public static class PositionEndpoints
{
    public static IServiceCollection AddPositions(this IServiceCollection services)
    {
        services.AddScoped<ListPositionsHandler>();
        services.AddScoped<CreatePositionHandler>();
        services.AddScoped<UpdatePositionHandler>();
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
        group
            .MapPut("/{id:guid}", UpdatePosition.Handle)
            .WithName("UpdatePosition")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
