namespace HrWebApp.Api.Features.Companies;

public static class CompanyEndpoints
{
    public static IServiceCollection AddCompanies(this IServiceCollection services)
    {
        services.AddScoped<ListCompaniesHandler>();
        services.AddScoped<CreateCompanyHandler>();
        services.AddScoped<UpdateCompanyHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapCompanies(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/companies").WithTags("Companies");
        group.MapGet("/", ListCompanies.Handle).WithName("ListCompanies");
        group
            .MapPost("/", CreateCompany.Handle)
            .WithName("CreateCompany")
            .ProducesValidationProblem();
        group
            .MapPut("/{id:guid}", UpdateCompany.Handle)
            .WithName("UpdateCompany")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
