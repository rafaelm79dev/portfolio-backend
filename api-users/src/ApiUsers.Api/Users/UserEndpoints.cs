using System.Security.Claims;
using ApiUsers.Application.Auth;
using ApiUsers.Application.Users;
using ApiUsers.Domain;

namespace ApiUsers.Api.Users;

public static class UserEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxPage = 1_000_000; // keeps (page - 1) * pageSize far from int overflow

    public static RouteGroupBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users").RequireAuthorization();
        group.MapGet("/me", GetMeAsync)
            .WithSummary("Returns the authenticated user.")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/", ListAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
            .WithSummary("Lists users, one page at a time (Admin only).")
            .Produces<PagedResponse<UserResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
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

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, UserService users, CancellationToken ct, int page = 1, int pageSize = DefaultPageSize)
    {
        // The role claim in the token can be up to 15 minutes old: confirm against the store that the caller is still an admin.
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var callerId) || !await users.IsAdminAsync(callerId, ct))
        {
            return Results.Forbid();
        }

        var errors = new Dictionary<string, string[]>();
        if (page is < 1 or > MaxPage)
        {
            errors["page"] = [$"page must be between 1 and {MaxPage}."];
        }
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return Results.Ok(await users.ListAsync(page, pageSize, ct));
    }
}
