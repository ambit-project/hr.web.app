namespace HrWebApp.Api.Features.Users;

public static class UserEndpoints
{
    public static IServiceCollection AddUsers(this IServiceCollection services)
    {
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<CreateUserHandler>();
        services.AddScoped<UpdateUserHandler>();
        services.AddScoped<SetUserPasswordHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users");
        group.MapGet("/", ListUsers.Handle).WithName("ListUsers");
        group.MapPost("/", CreateUser.Handle).WithName("CreateUser").ProducesValidationProblem();
        group
            .MapPut("/{id:guid}", UpdateUser.Handle)
            .WithName("UpdateUser")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapPut("/{id:guid}/password", SetUserPassword.Handle)
            .WithName("SetUserPassword")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
