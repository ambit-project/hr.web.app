namespace HrWebApp.Api.Features.TaxOffices;

public static class TaxOfficeEndpoints
{
    public static IServiceCollection AddTaxOffices(this IServiceCollection services)
    {
        services.AddScoped<ListTaxOfficesHandler>();
        services.AddScoped<CreateTaxOfficeHandler>();
        services.AddScoped<UpdateTaxOfficeHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapTaxOffices(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tax-offices").WithTags("TaxOffices");
        group.MapGet("/", ListTaxOffices.Handle).WithName("ListTaxOffices");
        group
            .MapPost("/", CreateTaxOffice.Handle)
            .WithName("CreateTaxOffice")
            .ProducesValidationProblem();
        group
            .MapPut("/{id:guid}", UpdateTaxOffice.Handle)
            .WithName("UpdateTaxOffice")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
