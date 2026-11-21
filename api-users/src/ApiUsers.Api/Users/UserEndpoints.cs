using System.Security.Claims;
using ApiUsers.Application.Users;

namespace ApiUsers.Api.Users;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users").RequireAuthorization();
        group.MapGet("/me", GetMeAsync);
        return group;
    }

    private static async Task<IResult> GetMeAsync(ClaimsPrincipal principal, UserService users, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return Results.Unauthorized();
        }

        // The token can outlive the account, so the user is always read from the store.
        var user = await users.GetCurrentAsync(userId, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }
}
