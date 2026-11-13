using System.Net;
using System.Text.Json;
using HrWebApp.Api.Common.Errors;
using HrWebApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace HrWebApp.Api.Tests.Common;

[Collection(DatabaseCollection.Name)]
public sealed class ProblemDetailsTests(DatabaseFixture database)
{
    [Fact]
    public async Task Database_validation_error_carries_title_code_and_trace_id()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");

        var response = await kadry.PostAsJsonAsync("/api/positions", new { name = "" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Validation failed", body.GetProperty("title").GetString());
        Assert.Equal("U0001", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Unknown_route_answers_with_problem_details()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");

        var response = await kadry.GetAsync("/api/no-such-thing");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public async Task Unhandled_exception_is_500_and_shows_internals_only_in_development(
        string environmentName,
        bool showsInternals
    )
    {
        var captured = new CapturingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            captured,
            new HostingEnvironment { EnvironmentName = environmentName },
            NullLogger<GlobalExceptionHandler>.Instance
        );
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("secret table name"),
            CancellationToken.None
        );

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(
            showsInternals,
            captured.Problem!.Detail?.Contains("secret", StringComparison.Ordinal) ?? false
        );
        Assert.Equal(showsInternals, captured.Problem.Extensions.ContainsKey("exception"));
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public Microsoft.AspNetCore.Mvc.ProblemDetails? Problem { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }
}
