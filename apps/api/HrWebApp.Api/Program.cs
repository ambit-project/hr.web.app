using HrWebApp.Api.Auth;
using HrWebApp.Api.Common;
using HrWebApp.Api.Common.Errors;
using HrWebApp.Api.Database;
using HrWebApp.Api.Features.Audit;
using HrWebApp.Api.Features.Companies;
using HrWebApp.Api.Features.Documents;
using HrWebApp.Api.Features.Employees;
using HrWebApp.Api.Features.Employments;
using HrWebApp.Api.Features.LegalParameters;
using HrWebApp.Api.Features.Optima;
using HrWebApp.Api.Features.PayComponents;
using HrWebApp.Api.Features.Payroll;
using HrWebApp.Api.Features.Positions;
using HrWebApp.Api.Features.Ppk;
using HrWebApp.Api.Features.TaxDeclarations;
using HrWebApp.Api.Features.TaxOffices;
using HrWebApp.Api.Features.TimeAbsence;
using HrWebApp.Api.Features.Users;
using HrWebApp.Api.Features.ZusCodes;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiDocument();

// Każdy ProblemDetails niesie traceId (sam identyfikator W3C), pod którym błąd jest w logu i w śladach — użytkownik
// podaje go przy zgłoszeniu (wzór: ambit.es).
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.TraceId.ToString()
            ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
    }
);

// Kolejność ma znaczenie: najpierw błędy z rutyn (U…), potem wszystko inne.
builder.Services.AddExceptionHandler<DbErrorHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddSessionAuth(builder.Configuration);
builder.Services.AddPositions();
builder.Services.AddZusCodes();
builder.Services.AddTaxOffices();
builder.Services.AddCompanies();
builder.Services.AddEmployees();
builder.Services.AddEmployments();
builder.Services.AddTaxDeclarations();
builder.Services.AddPayComponents();
builder.Services.AddTimeAbsence();
builder.Services.AddPpk();
builder.Services.AddPayroll();
builder.Services.AddDocuments();
builder.Services.AddLegalParameters();
builder.Services.AddUsers();
builder.Services.AddOptima();
builder.Services.AddScoped<ListAuditHandler>();

var app = builder.Build();

app.UseExceptionHandler();

// Puste odpowiedzi błędów (nieznana ścieżka, zła metoda) też jako ProblemDetails.
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");

var api = app.MapGroup("/api").RequireAuthorization();
api.MapAuth();
api.MapPositions();
api.MapZusCodes();
api.MapTaxOffices();
api.MapCompanies();
api.MapEmployees();
api.MapEmployments();
api.MapTaxDeclarations();
api.MapPayComponents();
api.MapTimeAbsence();
api.MapPpk();
api.MapPayroll();
api.MapDocuments();
api.MapLegalParameters();
api.MapUsers();
api.MapAudit();
api.MapOptima();

// Agent integracji z Optimą: osobna grupa z kluczem agenta zamiast sesji (ADR-0010).
app.MapOptimaAgent();

app.Run();
