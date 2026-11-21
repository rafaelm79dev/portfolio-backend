using ApiUsers.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApiUsers.Infrastructure.Data;

public static class DbSeeder
{
    /// <summary>
    /// Creates the roles and, when <c>Admin:Email</c> and <c>Admin:Password</c> are both configured, the initial
    /// admin account. Safe to run more than once: existing roles are left untouched and an existing admin keeps
    /// its current password. An existing non-admin account with that email is promoted only when its password
    /// matches <c>Admin:Password</c>; otherwise a warning is logged and it stays a regular user.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        foreach (var name in new[] { Roles.User, Roles.Admin })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                Require(await roles.CreateAsync(new AppRole { Name = name }), $"create role '{name}'");
            }
        }

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return;
        }

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new AppUser { UserName = email, Email = email };
            Require(await users.CreateAsync(admin, password), "create the admin account");
        }
        else if (!await users.IsInRoleAsync(admin, Roles.Admin) && !await users.CheckPasswordAsync(admin, password))
        {
            // Registration is open and emails are not verified, so this account may have been created by someone
            // else before the admin was configured. Promoting it would hand them the Admin role. Skipping (instead
            // of throwing) keeps the application starting, so registering this email cannot be used to block it.
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder)).LogWarning(
                "Admin:Email belongs to an existing non-admin account whose password does not match Admin:Password. " +
                "The Admin role was not assigned. Remove that account or use an unregistered email.");
            return;
        }

        if (!await users.IsInRoleAsync(admin, Roles.Admin))
        {
            Require(await users.AddToRoleAsync(admin, Roles.Admin), "assign the Admin role");
        }
    }

    // The message lists Identity's reasons (for example password rules) and never the password itself.
    private static void Require(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
