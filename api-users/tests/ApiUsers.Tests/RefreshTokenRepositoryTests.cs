using ApiUsers.Application.Auth;
using ApiUsers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Tests;

/// <summary>Deterministic checks of the rotation rules, without relying on two requests racing each other.</summary>
[Collection("api")]
public class RefreshTokenRepositoryTests(ApiFactory factory)
{
    private IServiceScope NewScope() => factory.Services.CreateScope();

    private static IRefreshTokenRepository Repository(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

    private async Task<RefreshToken> StoredTokenAsync()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        using var scope = NewScope();
        var stored = await Repository(scope).FindByHashAsync(TestHelpers.HashToken(tokens.RefreshToken), default);
        return stored!;
    }

    private static RefreshToken ReplacementOf(RefreshToken stored, string tokenHash) => new()
    {
        Id = Guid.NewGuid(),
        UserId = stored.UserId,
        FamilyId = stored.FamilyId,
        TokenHash = tokenHash,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
    };

    private static string NewHash() => TestHelpers.HashToken(Guid.NewGuid().ToString());

    [Fact]
    public async Task TryRotate_succeeds_once_and_fails_for_the_same_token_afterwards()
    {
        var stored = await StoredTokenAsync();
        var firstReplacement = NewHash();
        var secondReplacement = NewHash();

        using var firstScope = NewScope();
        var first = await Repository(firstScope).TryRotateAsync(
            stored.TokenHash, ReplacementOf(stored, firstReplacement), DateTime.UtcNow, default);
        using var secondScope = NewScope();
        var second = await Repository(secondScope).TryRotateAsync(
            stored.TokenHash, ReplacementOf(stored, secondReplacement), DateTime.UtcNow, default);

        Assert.True(first);
        Assert.False(second);
        using var checkScope = NewScope();
        Assert.NotNull(await Repository(checkScope).FindByHashAsync(firstReplacement, default));
        Assert.Null(await Repository(checkScope).FindByHashAsync(secondReplacement, default)); // the loser stored nothing
    }

    [Fact]
    public async Task TryRotate_rolls_back_when_the_replacement_cannot_be_stored()
    {
        var stored = await StoredTokenAsync();
        using var scope = NewScope();

        // Reusing the current hash violates the unique index, so the INSERT fails after the UPDATE succeeded.
        await Assert.ThrowsAsync<DbUpdateException>(() => Repository(scope).TryRotateAsync(
            stored.TokenHash, ReplacementOf(stored, stored.TokenHash), DateTime.UtcNow, default));

        using var checkScope = NewScope();
        var after = await Repository(checkScope).FindByHashAsync(stored.TokenHash, default);
        Assert.NotNull(after);
        Assert.Null(after.RevokedAtUtc);          // the UPDATE was rolled back with the transaction
        Assert.Null(after.ReplacedByTokenHash);
    }

    [Fact]
    public async Task TryRotate_refuses_a_token_at_or_after_its_expiry()
    {
        var stored = await StoredTokenAsync();
        using var scope = NewScope();
        var repository = Repository(scope);

        var atExpiry = await repository.TryRotateAsync(
            stored.TokenHash, ReplacementOf(stored, NewHash()), stored.ExpiresAtUtc, default);
        var afterExpiry = await repository.TryRotateAsync(
            stored.TokenHash, ReplacementOf(stored, NewHash()), stored.ExpiresAtUtc.AddSeconds(1), default);

        Assert.False(atExpiry);
        Assert.False(afterExpiry);
    }

    [Fact]
    public async Task RevokeFamily_only_revokes_tokens_of_that_family()
    {
        var mine = await StoredTokenAsync();
        var other = await StoredTokenAsync();
        using var scope = NewScope();
        var repository = Repository(scope);

        await repository.RevokeFamilyAsync(mine.FamilyId, DateTime.UtcNow, default);

        using var checkScope = NewScope();
        var checkRepository = Repository(checkScope);
        Assert.NotNull((await checkRepository.FindByHashAsync(mine.TokenHash, default))!.RevokedAtUtc);
        Assert.Null((await checkRepository.FindByHashAsync(other.TokenHash, default))!.RevokedAtUtc);
    }
}
