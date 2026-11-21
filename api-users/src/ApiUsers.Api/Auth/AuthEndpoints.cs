using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ApiUsers.Application.Auth;

namespace ApiUsers.Api.Auth;

public static class AuthEndpoints
{
    private const int MaxEmailLength = 256;    // matches the Identity column size
    private const int MaxPasswordLength = 128; // avoids hashing arbitrarily large inputs
    private const int MaxRefreshTokenLength = 512; // real tokens are 43 characters

    public static RouteGroupBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");
        group.MapPost("/register", RegisterAsync)
            .WithSummary("Registers a new user with the User role.")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/login", LoginAsync)
            .WithSummary("Exchanges email and password for an access token and a refresh token.")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapPost("/refresh", RefreshAsync)
            .WithSummary("Exchanges a refresh token (single use) for a new token pair.")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithSummary("Revokes the session of a refresh token that belongs to the caller.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return group;
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest? request, AuthService auth, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            return MissingRefreshToken();
        }
        if (request.RefreshToken.Length > MaxRefreshTokenLength)
        {
            return InvalidRefreshToken(); // no real token is this long: do not even look it up
        }

        var tokens = await auth.RefreshAsync(request.RefreshToken, ct);
        return tokens is null ? InvalidRefreshToken() : Results.Ok(tokens);
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest? request, ClaimsPrincipal principal, AuthService auth, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            return MissingRefreshToken();
        }
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return Results.Unauthorized();
        }

        if (request.RefreshToken.Length <= MaxRefreshTokenLength)
        {
            await auth.LogoutAsync(userId, request.RefreshToken, ct);
        }
        return Results.NoContent();
    }

    private static IResult MissingRefreshToken() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["RefreshToken"] = ["Refresh token is required."] });

    private static IResult InvalidRefreshToken() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid refresh token.");

    private static async Task<IResult> LoginAsync(LoginRequest? request, AuthService auth, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request?.Email) || request.Email.Trim().Length > MaxEmailLength)
        {
            errors["Email"] = ["Email is required."];
        }
        if (string.IsNullOrEmpty(request?.Password) || request.Password.Length > MaxPasswordLength)
        {
            errors["Password"] = ["Password is required."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var tokens = await auth.LoginAsync(request!.Email!.Trim(), request.Password!, ct);

        // One generic answer for unknown email, wrong password and locked account.
        return tokens is null
            ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid email or password.")
            : Results.Ok(tokens);
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest? request, AuthService auth, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request?.Email)
            || request.Email.Trim().Length > MaxEmailLength
            || !new EmailAddressAttribute().IsValid(request.Email))
        {
            errors["Email"] = ["A valid email is required."];
        }
        if (string.IsNullOrEmpty(request?.Password))
        {
            errors["Password"] = ["Password is required."];
        }
        else if (request.Password.Length > MaxPasswordLength)
        {
            errors["Password"] = [$"Password must be at most {MaxPasswordLength} characters."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await auth.RegisterAsync(request!.Email!.Trim(), request.Password!, ct);

        return result.Status switch
        {
            RegistrationStatus.Created => Results.Json(result.User, statusCode: StatusCodes.Status201Created),
            RegistrationStatus.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered."),
            RegistrationStatus.Invalid => Results.ValidationProblem(result.Errors!.ToDictionary(e => e.Key, e => e.Value)),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Could not complete registration."),
        };
    }
}
