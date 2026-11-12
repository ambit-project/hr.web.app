namespace HrWebApp.Api.Features.LegalParameters;

public static class LegalParameterEndpoints
{
    public static IServiceCollection AddLegalParameters(this IServiceCollection services)
    {
        services.AddScoped<ListLegalParametersHandler>();
        services.AddScoped<AddLegalParameterHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapLegalParameters(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/legal-parameters").WithTags("LegalParameters");
        group.MapGet("/", ListLegalParameters.Handle).WithName("ListLegalParameters");
        group
            .MapPost("/", AddLegalParameter.Handle)
            .WithName("AddLegalParameter")
            .ProducesValidationProblem();
        return app;
    }
}
