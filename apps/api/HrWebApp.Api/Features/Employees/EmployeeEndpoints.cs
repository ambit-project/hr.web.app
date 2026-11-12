namespace HrWebApp.Api.Features.Employees;

public static class EmployeeEndpoints
{
    public static IServiceCollection AddEmployees(this IServiceCollection services)
    {
        services.AddScoped<ListEmployeesHandler>();
        services.AddScoped<GetEmployeeHandler>();
        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<UpdateEmployeeHandler>();
        services.AddScoped<DeleteEmployeeHandler>();
        services.AddScoped<EmployeeAddressesHandler>();
        services.AddScoped<EmployeeBankAccountsHandler>();
        services.AddScoped<EmployeeAccountHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapEmployees(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/employees").WithTags("Employees");
        group.MapGet("/", ListEmployees.Handle).WithName("ListEmployees");
        group
            .MapPost("/", CreateEmployee.Handle)
            .WithName("CreateEmployee")
            .ProducesValidationProblem();
        group
            .MapGet("/{id:guid}", GetEmployee.Handle)
            .WithName("GetEmployee")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPut("/{id:guid}", UpdateEmployee.Handle)
            .WithName("UpdateEmployee")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapDelete("/{id:guid}", DeleteEmployee.Handle)
            .WithName("DeleteEmployee")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapGet("/{id:guid}/addresses", ListEmployeeAddresses.Handle)
            .WithName("ListEmployeeAddresses");
        group
            .MapPost("/{id:guid}/addresses", AddEmployeeAddress.Handle)
            .WithName("AddEmployeeAddress")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapGet("/{id:guid}/bank-accounts", ListEmployeeBankAccounts.Handle)
            .WithName("ListEmployeeBankAccounts");
        group
            .MapPost("/{id:guid}/bank-accounts", AddEmployeeBankAccount.Handle)
            .WithName("AddEmployeeBankAccount")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPost("/{id:guid}/bank-accounts/close", CloseEmployeeBankAccount.Handle)
            .WithName("CloseEmployeeBankAccount")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPut("/{id:guid}/account", LinkEmployeeAccount.Handle)
            .WithName("LinkEmployeeAccount")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapDelete("/{id:guid}/account", UnlinkEmployeeAccount.Handle)
            .WithName("UnlinkEmployeeAccount");
        group
            .MapGet("/{id:guid}/read-audit", ListEmployeeReadAudit.Handle)
            .WithName("ListEmployeeReadAudit");
        return app;
    }
}
