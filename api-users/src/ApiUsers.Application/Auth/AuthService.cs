using ApiUsers.Domain;
using Microsoft.Extensions.Options;

namespace ApiUsers.Application.Auth;

public sealed class AuthService(
    IIdentityService identity,
    ITokenGenerator tokens,
    IRefreshTokenRepository refreshTokens,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider time)
{
    public Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken ct) =>
        identity.CreateUserAsync(email, password, Roles.User, ct);

    /// <summary>Returns new tokens, or null when the credentials are not valid.</summary>
    public async Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await identity.ValidateCredentialsAsync(email, password, ct);
        return user is null ? null : await IssueTokensAsync(user, Guid.NewGuid(), ct);
    }

    private async Task<TokenResponse> IssueTokensAsync(UserResponse user, Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var access = tokens.CreateAccessToken(user, now);
        var refreshValue = tokens.CreateRefreshTokenValue();
        var refreshExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays);

        await refreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = tokens.HashRefreshToken(refreshValue),
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAt,
        }, ct);

        return new TokenResponse(access.Value, access.ExpiresAtUtc, refreshValue, refreshExpiresAt);
    }
}
