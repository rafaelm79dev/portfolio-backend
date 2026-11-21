using System.ComponentModel.DataAnnotations;
using ApiUsers.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace ApiUsers.Api.Auth;

public static class AuthEndpoints
{
    private const string UserRole = "User";
    private const int MaxEmailLength = 256;    // matches the Identity column size
    private const int MaxPasswordLength = 128; // avoids hashing arbitrarily large inputs

    public static RouteGroupBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");
        group.MapPost("/register", RegisterAsync);
        return group;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest? request,
        UserManager<AppUser> users,
        RoleManager<AppRole> roles)
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

        var email = request!.Email!.Trim();
        var user = new AppUser { UserName = email, Email = email };

        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            if (created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName)
                                         or nameof(IdentityErrorDescriber.DuplicateEmail)))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered.");
            }

            return Results.ValidationProblem(created.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
        }

        // Idempotent and safe under concurrency: if another request created the role first, CreateAsync just fails.
        await roles.CreateAsync(new AppRole { Name = UserRole });
        var inRole = await users.AddToRoleAsync(user, UserRole);
        if (!inRole.Succeeded)
        {
            // Never leave a half-created account behind or report a role the user does not have.
            await users.DeleteAsync(user);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Could not complete registration.");
        }

        return Results.Json(new UserResponse(user.Id, email, [UserRole]), statusCode: StatusCodes.Status201Created);
    }
}
