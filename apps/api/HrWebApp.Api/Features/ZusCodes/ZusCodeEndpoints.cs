namespace HrWebApp.Api.Features.ZusCodes;

public static class ZusCodeEndpoints
{
    public static IServiceCollection AddZusCodes(this IServiceCollection services)
    {
        services.AddScoped<ListZusCodesHandler>();
        services.AddScoped<CreateZusCodeHandler>();
        services.AddScoped<UpdateZusCodeHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapZusCodes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/zus-codes").WithTags("ZusCodes");
        group.MapGet("/", ListZusCodes.Handle).WithName("ListZusCodes");
        group
            .MapPost("/", CreateZusCode.Handle)
            .WithName("CreateZusCode")
            .ProducesValidationProblem();
        group
            .MapPut("/{id:guid}", UpdateZusCode.Handle)
            .WithName("UpdateZusCode")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
