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

        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException)
        {
            // Two simultaneous requests with the same email can both pass Identity's duplicate check;
            // the unique index then rejects the second insert. That is a conflict, not a server error.
            if (await users.FindByEmailAsync(email) is not null)
            {
                return new RegistrationResult(RegistrationStatus.Conflict);
            }
            throw;
        }

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
            SpendHashingTime(password);
            return null;
        }

        if (await users.IsLockedOutAsync(user))
        {
            SpendHashingTime(password);
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

    // Paying the hashing cost on every failed path keeps the response time from revealing
    // whether an email exists or an account is locked out.
    private void SpendHashingTime(string password) => users.PasswordHasher.HashPassword(new AppUser(), password);

    public async Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToResponseAsync(user);
    }

    public async Task<UserResponse?> GetUserIfCanSignInAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || await users.IsLockedOutAsync(user))
        {
            return null;
        }
        return await ToResponseAsync(user);
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is not null && await users.IsInRoleAsync(user, role);
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
