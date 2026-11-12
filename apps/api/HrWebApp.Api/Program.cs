using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using HrWebApp.Api.Features.Positions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiDocument();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DbErrorHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddPositions();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapPositions();

app.Run();
