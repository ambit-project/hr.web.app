namespace HrWebApp.Api.Features.Payroll;

public static class PayrollEndpoints
{
    public static IServiceCollection AddPayroll(this IServiceCollection services)
    {
        services.AddScoped<PayrollSettingsHandler>();
        services.AddScoped<PayrollsHandler>();
        services.AddScoped<PayrollEntriesHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapPayroll(this IEndpointRouteBuilder app)
    {
        var company = app.MapGroup("/companies/{id:guid}").WithTags("Payroll");
        company
            .MapGet("/payroll-settings", ListPayrollSettings.Handle)
            .WithName("ListPayrollSettings")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapPost("/payroll-settings", AddPayrollSettings.Handle)
            .WithName("AddPayrollSettings")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        company
            .MapGet("/payrolls", ListPayrolls.Handle)
            .WithName("ListPayrolls")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapPost("/payrolls", CreatePayroll.Handle)
            .WithName("CreatePayroll")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var payroll = app.MapGroup("/payrolls/{id:guid}").WithTags("Payroll");
        payroll
            .MapGet("/", GetPayroll.Handle)
            .WithName("GetPayroll")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        payroll
            .MapDelete("/", DeletePayroll.Handle)
            .WithName("DeletePayroll")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        payroll
            .MapPost("/calculate", CalculatePayroll.Handle)
            .WithName("CalculatePayroll")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        payroll
            .MapPost("/close", ClosePayroll.Handle)
            .WithName("ClosePayroll")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        payroll
            .MapGet("/members", ListPayrollMembers.Handle)
            .WithName("ListPayrollMembers")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        payroll
            .MapPut("/members", SetPayrollMembers.Handle)
            .WithName("SetPayrollMembers")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        payroll
            .MapGet("/entries", ListPayrollEntries.Handle)
            .WithName("ListPayrollEntries")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGroup("/payroll-entries")
            .WithTags("Payroll")
            .MapGet("/{id:guid}", GetPayrollEntry.Handle)
            .WithName("GetPayrollEntry")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGroup("/employees/{id:guid}")
            .WithTags("Payroll")
            .MapGet("/year-totals/{year:int}", GetYearTotals.Handle)
            .WithName("GetPayrollYearTotals")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
