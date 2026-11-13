namespace HrWebApp.Api.Features.Ppk;

public static class PpkEndpoints
{
    public static IServiceCollection AddPpk(this IServiceCollection services)
    {
        services.AddScoped<PpkSettingsHandler>();
        services.AddScoped<PpkParticipationHandler>();
        services.AddScoped<PpkReenrollmentHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapPpk(this IEndpointRouteBuilder app)
    {
        var company = app.MapGroup("/companies/{id:guid}/ppk").WithTags("Ppk");
        company
            .MapGet("/settings", ListPpkSettings.Handle)
            .WithName("ListPpkSettings")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapPost("/settings", AddPpkSettings.Handle)
            .WithName("AddPpkSettings")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapGet("/candidates", ListPpkCandidates.Handle)
            .WithName("ListPpkCandidates")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapGet("/reenrollment/{year:int}", GetPpkReenrollment.Handle)
            .WithName("GetPpkReenrollment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapPost("/reenrollment/{year:int}/informed", MarkPpkReenrollmentInformed.Handle)
            .WithName("MarkPpkReenrollmentInformed")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        company
            .MapPost("/reenrollment/{year:int}/apply", ApplyPpkReenrollment.Handle)
            .WithName("ApplyPpkReenrollment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var employee = app.MapGroup("/employees/{id:guid}/ppk").WithTags("Ppk");
        employee
            .MapGet("/", GetEmployeePpk.Handle)
            .WithName("GetEmployeePpk")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        employee
            .MapPost("/", EnrollInPpk.Handle)
            .WithName("EnrollInPpk")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        employee
            .MapDelete("/", DeletePpkParticipation.Handle)
            .WithName("DeletePpkParticipation")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        employee
            .MapPost("/declarations", CreatePpkDeclaration.Handle)
            .WithName("CreatePpkDeclaration")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGroup("/ppk/declarations")
            .WithTags("Ppk")
            .MapDelete("/{id:guid}", DeletePpkDeclaration.Handle)
            .WithName("DeletePpkDeclaration")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGroup("/my/{employeeId:guid}")
            .WithTags("MyPpk")
            .MapGet("/ppk", GetMyPpk.Handle)
            .WithName("GetMyPpk")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
