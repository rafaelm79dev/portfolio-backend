using ApiUsers.Application.Auth;
using ApiUsers.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace ApiUsers.Infrastructure.Identity;

public sealed class IdentityService(UserManager<AppUser> users, RoleManager<AppRole> roles) : IIdentityService
{
    public async Task<RegistrationResult> CreateUserAsync(string email, string password, string role, CancellationToken ct)
    {
        var user = new AppUser { UserName = email, Email = email };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            if (created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName)
                                         or nameof(IdentityErrorDescriber.DuplicateEmail)))
            {
                return new RegistrationResult(RegistrationStatus.Conflict);
            }

            var errors = created.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
            return new RegistrationResult(RegistrationStatus.Invalid, Errors: errors);
        }

        // Idempotent and safe under concurrency: if another request created the role first, CreateAsync just fails.
        await roles.CreateAsync(new AppRole { Name = role });
        var inRole = await users.AddToRoleAsync(user, role);
        if (!inRole.Succeeded)
        {
            // Never leave a half-created account behind or report a role the user does not have.
            await users.DeleteAsync(user);
            return new RegistrationResult(RegistrationStatus.Failed);
        }

        return new RegistrationResult(RegistrationStatus.Created, new UserResponse(user.Id, email, [role]));
    }

    public async Task<UserResponse?> ValidateCredentialsAsync(string email, string password, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Spend the same hashing time as a real check so response time does not reveal whether the email exists.
            users.PasswordHasher.HashPassword(new AppUser(), password);
            return null;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user);
        var roles = await users.GetRolesAsync(user);
        return new UserResponse(user.Id, user.Email!, [.. roles]);
    }
}
