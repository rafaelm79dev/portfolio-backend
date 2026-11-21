using ApiUsers.Application.Auth;
using ApiUsers.Application.Users;
using ApiUsers.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApiUsers.Infrastructure.Identity;

public sealed class IdentityService(UserManager<AppUser> users, AppDbContext db) : IIdentityService
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

        // Roles are created at startup by DbSeeder; a missing role is a deployment problem, not a user error.
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
        return await ToResponseAsync(user);
    }

    public async Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToResponseAsync(user);
    }

    public async Task<PagedResponse<UserResponse>> ListUsersAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        var totalCount = await query.CountAsync(ct);

        var pageUsers = await query
            .OrderBy(u => u.NormalizedEmail).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        // One query for the roles of the whole page instead of one per user.
        var ids = pageUsers.Select(u => u.Id).ToList();
        var roleNames = await (from userRole in db.UserRoles
                               join role in db.Roles on userRole.RoleId equals role.Id
                               where ids.Contains(userRole.UserId)
                               select new { userRole.UserId, role.Name })
            .ToListAsync(ct);
        var rolesByUser = roleNames.ToLookup(r => r.UserId, r => r.Name!);

        var items = pageUsers.Select(u => new UserResponse(u.Id, u.Email!, [.. rolesByUser[u.Id]])).ToList();
        return new PagedResponse<UserResponse>(items, page, pageSize, totalCount);
    }

    private async Task<UserResponse> ToResponseAsync(AppUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return new UserResponse(user.Id, user.Email!, [.. roles]);
    }
}
