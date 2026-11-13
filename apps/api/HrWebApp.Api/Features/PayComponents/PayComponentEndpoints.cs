namespace HrWebApp.Api.Features.PayComponents;

public static class PayComponentEndpoints
{
    public static IServiceCollection AddPayComponents(this IServiceCollection services)
    {
        services.AddScoped<ComponentTypesHandler>();
        services.AddScoped<EmploymentComponentsHandler>();
        services.AddScoped<ConsentsHandler>();
        services.AddScoped<GarnishmentsHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapPayComponents(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/pay-component-types").WithTags("PayComponentTypes");
        types.MapGet("/", ListComponentTypes.Handle).WithName("ListPayComponentTypes");
        types
            .MapPost("/", CreateComponentType.Handle)
            .WithName("CreatePayComponentType")
            .ProducesValidationProblem();
        types
            .MapPut("/{id:guid}", UpdateComponentType.Handle)
            .WithName("UpdatePayComponentType")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var byEmployment = app.MapGroup("/employments/{id:guid}/components")
            .WithTags("EmploymentComponents");
        byEmployment
            .MapGet("/", ListEmploymentComponents.Handle)
            .WithName("ListEmploymentComponents")
            .ProducesProblem(StatusCodes.Status404NotFound);
        byEmployment
            .MapPost("/", AddEmploymentComponent.Handle)
            .WithName("AddEmploymentComponent")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        var components = app.MapGroup("/employment-components").WithTags("EmploymentComponents");
        components
            .MapPost("/{id:guid}/end", EndEmploymentComponent.Handle)
            .WithName("EndEmploymentComponent")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        components
            .MapDelete("/{id:guid}", DeleteEmploymentComponent.Handle)
            .WithName("DeleteEmploymentComponent")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var consentsByEmployee = app.MapGroup("/employees/{id:guid}/deduction-consents")
            .WithTags("DeductionConsents");
        consentsByEmployee
            .MapGet("/", ListConsents.Handle)
            .WithName("ListDeductionConsents")
            .ProducesProblem(StatusCodes.Status404NotFound);
        consentsByEmployee
            .MapPost("/", CreateConsent.Handle)
            .WithName("CreateDeductionConsent")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        var consents = app.MapGroup("/deduction-consents").WithTags("DeductionConsents");
        consents
            .MapPut("/{id:guid}", UpdateConsent.Handle)
            .WithName("UpdateDeductionConsent")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        consents
            .MapPost("/{id:guid}/end", EndConsent.Handle)
            .WithName("EndDeductionConsent")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        consents
            .MapDelete("/{id:guid}", DeleteConsent.Handle)
            .WithName("DeleteDeductionConsent")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var garnishmentsByEmployee = app.MapGroup("/employees/{id:guid}/garnishments")
            .WithTags("Garnishments");
        garnishmentsByEmployee
            .MapGet("/", ListGarnishments.Handle)
            .WithName("ListGarnishments")
            .ProducesProblem(StatusCodes.Status404NotFound);
        garnishmentsByEmployee
            .MapPost("/", CreateGarnishment.Handle)
            .WithName("CreateGarnishment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        var garnishments = app.MapGroup("/garnishments").WithTags("Garnishments");
        garnishments
            .MapPut("/{id:guid}", UpdateGarnishment.Handle)
            .WithName("UpdateGarnishment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        garnishments
            .MapPost("/{id:guid}/status", SetGarnishmentStatus.Handle)
            .WithName("SetGarnishmentStatus")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        garnishments
            .MapDelete("/{id:guid}", DeleteGarnishment.Handle)
            .WithName("DeleteGarnishment")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
