namespace HrWebApp.Api.Features.TaxDeclarations;

public static class TaxDeclarationEndpoints
{
    public static IServiceCollection AddTaxDeclarations(this IServiceCollection services)
    {
        services.AddScoped<TaxDeclarationsHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapTaxDeclarations(this IEndpointRouteBuilder app)
    {
        var byEmployee = app.MapGroup("/employees/{id:guid}/tax-declarations")
            .WithTags("TaxDeclarations");
        byEmployee
            .MapGet("/", ListTaxDeclarations.Handle)
            .WithName("ListTaxDeclarations")
            .ProducesProblem(StatusCodes.Status404NotFound);
        byEmployee
            .MapPost("/", CreateTaxDeclaration.Handle)
            .WithName("CreateTaxDeclaration")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var group = app.MapGroup("/tax-declarations").WithTags("TaxDeclarations");
        group
            .MapPost("/{id:guid}/end", EndTaxDeclaration.Handle)
            .WithName("EndTaxDeclaration")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapDelete("/{id:guid}", DeleteTaxDeclaration.Handle)
            .WithName("DeleteTaxDeclaration")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
