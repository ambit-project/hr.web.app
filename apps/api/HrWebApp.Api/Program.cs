using HrWebApp.Api.Auth;
using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using HrWebApp.Api.Features.Companies;
using HrWebApp.Api.Features.LegalParameters;
using HrWebApp.Api.Features.Positions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiDocument();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DbErrorHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddSessionAuth(builder.Configuration);
builder.Services.AddPositions();
builder.Services.AddCompanies();
builder.Services.AddLegalParameters();

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
api.MapCompanies();
api.MapLegalParameters();

app.Run();
