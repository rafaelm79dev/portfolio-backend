using ApiUsers.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Tests;

[Collection("api")]
public class DataTests(ApiFactory factory)
{
    private static string NewEmail() => $"{Guid.NewGuid():N}@x.com";

    private async Task<AppUser> CreateUserAsync(IServiceScope scope)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = NewEmail();
        var user = new AppUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, "Passw0rd!x");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static RefreshToken NewToken(Guid userId, string hash) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        FamilyId = Guid.NewGuid(),
        TokenHash = hash,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
    };

    [Fact]
    public async Task Can_create_user_and_refresh_token()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await CreateUserAsync(scope);
        var hash = Guid.NewGuid().ToString("N");

        db.RefreshTokens.Add(NewToken(user.Id, hash));
        await db.SaveChangesAsync();

        var stored = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        Assert.Equal(hash, stored.TokenHash);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task TokenHash_is_unique()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await CreateUserAsync(scope);
        var hash = Guid.NewGuid().ToString("N");

        db.RefreshTokens.Add(NewToken(user.Id, hash));
        db.RefreshTokens.Add(NewToken(user.Id, hash));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
