using ApiUsers.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Infrastructure.Data;

public static class DbSeeder
{
    /// <summary>
    /// Creates the roles and, when <c>Admin:Email</c> and <c>Admin:Password</c> are both configured, the initial
    /// admin account. Safe to run more than once: existing roles and accounts are left untouched
    /// (an existing admin keeps its current password).
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
