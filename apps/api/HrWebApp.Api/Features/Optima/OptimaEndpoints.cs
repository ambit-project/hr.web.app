using HrWebApp.Api.Features.Optima.Agent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Optima;

/// <summary>Integracja z Comarch ERP Optima przez agenta (specyfikacja 011, ADR-0010).</summary>
public static class OptimaEndpoints
{
    public static IServiceCollection AddOptima(this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(
                AgentKeyAuthenticationHandler.SchemeName,
                null
            );
        services
            .AddAuthorizationBuilder()
            .AddPolicy(AgentKeyAuthenticationHandler.PolicyName, policy =>
                policy
                    .AddAuthenticationSchemes(AgentKeyAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
            );

        services.AddScoped<AgentHandler>();
        services.AddScoped<AgentKeysHandler>();
        services.AddScoped<OperationsHandler>();
        services.AddScoped<OptimaDatabasesHandler>();
        services.AddScoped<OptimaPayrollsHandler>();
        return services;
    }

    /// <summary>Endpointy operatora w grupie /api (sesja użytkownika).</summary>
    public static IEndpointRouteBuilder MapOptima(this IEndpointRouteBuilder app)
    {
        var optima = app.MapGroup("/optima").WithTags("Optima");

        optima.MapGet("/agent-keys", ListAgentKeys.Handle).WithName("ListOptimaAgentKeys");
        optima
            .MapPost("/agent-keys", CreateAgentKey.Handle)
            .WithName("CreateOptimaAgentKey")
            .ProducesValidationProblem();
        optima
            .MapDelete("/agent-keys/{id:guid}", RevokeAgentKey.Handle)
            .WithName("RevokeOptimaAgentKey")
            .ProducesProblem(StatusCodes.Status404NotFound);

        optima.MapGet("/databases", ListOptimaDatabases.Handle).WithName("ListOptimaDatabases");
        optima
            .MapPut("/companies/{companyId:guid}/database", SetCompanyDatabase.Handle)
            .WithName("SetOptimaCompanyDatabase")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        optima
            .MapGet("/companies/{companyId:guid}/pay-types", ListPayTypes.Handle)
            .WithName("ListOptimaPayTypes")
            .ProducesProblem(StatusCodes.Status404NotFound);
        optima
            .MapPut("/companies/{companyId:guid}/pay-types/{optimaId:int}", MapPayType.Handle)
            .WithName("MapOptimaPayType")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        optima
            .MapGet("/companies/{companyId:guid}/payrolls", GetOptimaPayrolls.Handle)
            .WithName("GetOptimaPayrolls")
            .ProducesProblem(StatusCodes.Status404NotFound);

        optima
            .MapPost("/operations", CreateOperation.Handle)
            .WithName("CreateOptimaOperation")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        optima.MapGet("/operations", ListOperations.Handle).WithName("ListOptimaOperations");
        optima
            .MapGet("/operations/{id:guid}", GetOperation.Handle)
            .WithName("GetOptimaOperation")
            .ProducesProblem(StatusCodes.Status404NotFound);
        optima
            .MapPost("/operations/{id:guid}/retry", RetryOperation.Handle)
            .WithName("RetryOptimaOperation")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    /// <summary>
    /// Endpointy agenta — poza grupą /api z sesją, z własną polityką: przyjmują tylko klucz agenta. Ścieżki zgodne
    /// z kontraktem integratora (docs/hr-application-api.md).
    /// </summary>
    public static IEndpointRouteBuilder MapOptimaAgent(this IEndpointRouteBuilder app)
    {
        var agent = app.MapGroup("/api/optima/agent")
            .RequireAuthorization(AgentKeyAuthenticationHandler.PolicyName)
            .WithTags("OptimaAgent");

        agent.MapGet("/connection", AgentConnection.Handle).WithName("OptimaAgentConnection");
        agent.MapPost("/operations/claim", ClaimOperation.Handle).WithName("OptimaAgentClaim");
        agent
            .MapGet("/operations/{id:guid}/employees", GetOperationEmployees.Handle)
            .WithName("OptimaAgentEmployees")
            .ProducesProblem(StatusCodes.Status404NotFound);
        agent
            .MapGet("/operations/{id:guid}/deductions", GetOperationDeductions.Handle)
            .WithName("OptimaAgentDeductions")
            .ProducesProblem(StatusCodes.Status404NotFound);
        agent
            .MapGet("/operations/{id:guid}/allowances", GetOperationAllowances.Handle)
            .WithName("OptimaAgentAllowances")
            .ProducesProblem(StatusCodes.Status404NotFound);
        agent
            .MapGet("/operations/{id:guid}/payroll-period", GetPayrollPeriod.Handle)
            .WithName("OptimaAgentPayrollPeriod")
            .ProducesProblem(StatusCodes.Status404NotFound);
        agent
            .MapPost("/operations/{id:guid}/result", ReportResult.Handle)
            .WithName("OptimaAgentResult")
            .WithMetadata(new RequestSizeLimitAttribute(ReportResult.MaxBodyBytes))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
