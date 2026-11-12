using HrWebApp.Api.Auth;
using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using HrWebApp.Api.Features.Audit;
using HrWebApp.Api.Features.Companies;
using HrWebApp.Api.Features.Employees;
using HrWebApp.Api.Features.Employments;
using HrWebApp.Api.Features.LegalParameters;
using HrWebApp.Api.Features.Positions;
using HrWebApp.Api.Features.TaxOffices;
using HrWebApp.Api.Features.Users;
using HrWebApp.Api.Features.ZusCodes;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiDocument();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DbErrorHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddSessionAuth(builder.Configuration);
builder.Services.AddPositions();
builder.Services.AddZusCodes();
builder.Services.AddTaxOffices();
builder.Services.AddCompanies();
builder.Services.AddEmployees();
builder.Services.AddEmployments();
builder.Services.AddLegalParameters();
builder.Services.AddUsers();
builder.Services.AddScoped<ListAuditHandler>();

var app = builder.Build();

app.UseExceptionHandler();
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
api.MapLegalParameters();
api.MapUsers();
api.MapAudit();

app.Run();
