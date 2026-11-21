using ApiUsers.Application.Auth;
using ApiUsers.Application.Users;
using ApiUsers.Domain;
using Microsoft.Extensions.Options;

namespace ApiUsers.Tests;

/// <summary>
/// Unit tests of the refresh and logout rules with in-memory fakes of the ports (no database, no Docker),
/// so each branch can be forced instead of hoping two real requests race the right way.
/// </summary>
public class AuthServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FamilyId = Guid.NewGuid();

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeIdentity : IIdentityService
    {
        public UserResponse? SignInUser { get; set; } = new(UserId, "user@x.com", ["User"]);

        public Task<UserResponse?> GetUserIfCanSignInAsync(Guid userId, CancellationToken ct) => Task.FromResult(SignInUser);

        public Task<RegistrationResult> CreateUserAsync(string email, string password, string role, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<UserResponse?> ValidateCredentialsAsync(string email, string password, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> IsInRoleAsync(Guid userId, string role, CancellationToken ct) => throw new NotSupportedException();

        public Task<PagedResponse<UserResponse>> ListUsersAsync(int page, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTokens : ITokenGenerator
    {
        public AccessToken CreateAccessToken(UserResponse user, DateTime nowUtc) => new("access-token", nowUtc.AddMinutes(15));

        public string CreateRefreshTokenValue() => "new-refresh";

        public string HashRefreshToken(string refreshTokenValue) => "hash:" + refreshTokenValue;
    }

    private sealed class FakeRepository : IRefreshTokenRepository
    {
        public RefreshToken? Stored { get; set; }
        public bool RotateResult { get; set; } = true;
        public int RotateCalls { get; private set; }
        public RefreshToken? Replacement { get; private set; }
        public List<Guid> RevokedFamilies { get; } = [];

        public Task AddAsync(RefreshToken token, CancellationToken ct) => throw new NotSupportedException();

        public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
            Task.FromResult(Stored is not null && Stored.TokenHash == tokenHash ? Stored : null);

        public Task<bool> TryRotateAsync(string currentHash, RefreshToken replacement, DateTime nowUtc, CancellationToken ct)
        {
            RotateCalls++;
            Replacement = replacement;
            return Task.FromResult(RotateResult);
        }

        public Task RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken ct)
        {
            RevokedFamilies.Add(familyId);
            return Task.CompletedTask;
        }
    }

    private static RefreshToken StoredToken(DateTime? expiresAt = null, DateTime? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        FamilyId = FamilyId,
        TokenHash = "hash:old",
        CreatedAtUtc = Now.AddDays(-1),
        ExpiresAtUtc = expiresAt ?? Now.AddDays(1),
        RevokedAtUtc = revokedAt,
    };

    private static (AuthService Service, FakeRepository Repository, FakeIdentity Identity) Create(
        RefreshToken? stored, bool rotateResult = true)
    {
        var repository = new FakeRepository { Stored = stored, RotateResult = rotateResult };
        var identity = new FakeIdentity();
        var service = new AuthService(
            identity, new FakeTokens(), repository, Options.Create(new JwtOptions { RefreshTokenDays = 7 }), new FixedTime());
        return (service, repository, identity);
    }

    [Fact]
    public async Task Refresh_that_loses_the_rotation_race_revokes_the_family_and_returns_nothing()
    {
        var (service, repository, _) = Create(StoredToken(), rotateResult: false);

        var result = await service.RefreshAsync("old", default);

        Assert.Null(result);
        Assert.Equal(1, repository.RotateCalls);
        Assert.Equal([FamilyId], repository.RevokedFamilies);
    }

    [Fact]
    public async Task Refresh_that_wins_the_rotation_returns_a_new_pair_in_the_same_family_without_revoking()
    {
        var (service, repository, _) = Create(StoredToken());

        var result = await service.RefreshAsync("old", default);

        Assert.NotNull(result);
        Assert.Equal("new-refresh", result.RefreshToken);
        Assert.Equal(Now.AddDays(7), result.RefreshTokenExpiresAtUtc);
        Assert.Equal(FamilyId, repository.Replacement!.FamilyId);
        Assert.Equal("hash:new-refresh", repository.Replacement.TokenHash);
        Assert.Empty(repository.RevokedFamilies);
    }

    [Fact]
    public async Task Refresh_of_a_revoked_token_revokes_the_family_and_does_not_rotate()
    {
        var (service, repository, _) = Create(StoredToken(revokedAt: Now.AddHours(-1)));

        var result = await service.RefreshAsync("old", default);

        Assert.Null(result);
        Assert.Equal(0, repository.RotateCalls);
        Assert.Equal([FamilyId], repository.RevokedFamilies);
    }

    [Fact]
    public async Task Refresh_of_an_expired_token_returns_nothing_and_leaves_the_family_alone()
    {
        var (service, repository, _) = Create(StoredToken(expiresAt: Now.AddSeconds(-1)));

        var result = await service.RefreshAsync("old", default);

        Assert.Null(result);
        Assert.Equal(0, repository.RotateCalls);
        Assert.Empty(repository.RevokedFamilies);
    }

    [Fact]
    public async Task Refresh_of_an_unknown_token_returns_nothing()
    {
        var (service, repository, _) = Create(stored: null);

        var result = await service.RefreshAsync("old", default);

        Assert.Null(result);
        Assert.Equal(0, repository.RotateCalls);
        Assert.Empty(repository.RevokedFamilies);
    }

    [Fact]
    public async Task Refresh_for_an_account_that_cannot_sign_in_does_not_rotate()
    {
        var (service, repository, identity) = Create(StoredToken());
        identity.SignInUser = null;

        var result = await service.RefreshAsync("old", default);

        Assert.Null(result);
        Assert.Equal(0, repository.RotateCalls);
        Assert.Empty(repository.RevokedFamilies);
    }

    [Fact]
    public async Task Logout_revokes_the_family_of_a_token_owned_by_the_caller()
    {
        var (service, repository, _) = Create(StoredToken());

        await service.LogoutAsync(UserId, "old", default);

        Assert.Equal([FamilyId], repository.RevokedFamilies);
    }

    [Fact]
    public async Task Logout_does_nothing_for_a_token_owned_by_someone_else()
    {
        var (service, repository, _) = Create(StoredToken());

        await service.LogoutAsync(Guid.NewGuid(), "old", default);

        Assert.Empty(repository.RevokedFamilies);
    }
}
