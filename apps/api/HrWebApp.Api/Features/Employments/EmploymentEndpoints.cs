namespace HrWebApp.Api.Features.Employments;

public static class EmploymentEndpoints
{
    public static IServiceCollection AddEmployments(this IServiceCollection services)
    {
        services.AddScoped<ListEmploymentsHandler>();
        services.AddScoped<CreateEmploymentHandler>();
        services.AddScoped<GetEmploymentHandler>();
        services.AddScoped<EmploymentChangesHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapEmployments(this IEndpointRouteBuilder app)
    {
        var byEmployee = app.MapGroup("/employees/{id:guid}/employments").WithTags("Employments");
        byEmployee
            .MapGet("/", ListEmployments.Handle)
            .WithName("ListEmployments")
            .ProducesProblem(StatusCodes.Status404NotFound);
        byEmployee
            .MapPost("/", CreateEmployment.Handle)
            .WithName("CreateEmployment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var group = app.MapGroup("/employments").WithTags("Employments");
        group
            .MapGet("/{id:guid}", GetEmployment.Handle)
            .WithName("GetEmployment")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPut("/{id:guid}", UpdateEmployment.Handle)
            .WithName("UpdateEmployment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapPost("/{id:guid}/terminate", TerminateEmployment.Handle)
            .WithName("TerminateEmployment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapDelete("/{id:guid}", DeleteEmployment.Handle)
            .WithName("DeleteEmployment")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPost("/{id:guid}/terms", AddEmploymentTerms.Handle)
            .WithName("AddEmploymentTerms")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapDelete("/{id:guid}/terms/last", DeleteLastEmploymentTerms.Handle)
            .WithName("DeleteLastEmploymentTerms")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}
