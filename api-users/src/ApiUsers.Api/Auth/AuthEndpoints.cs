using System.ComponentModel.DataAnnotations;
using ApiUsers.Application.Auth;

namespace ApiUsers.Api.Auth;

public static class AuthEndpoints
{
    private const int MaxEmailLength = 256;    // matches the Identity column size
    private const int MaxPasswordLength = 128; // avoids hashing arbitrarily large inputs

    public static RouteGroupBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");
        group.MapPost("/register", RegisterAsync);
        return group;
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
