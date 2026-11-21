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
        if (user is null)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var (entity, value) = NewRefreshToken(user.Id, Guid.NewGuid(), now);
        await refreshTokens.AddAsync(entity, ct);
        return ToResponse(tokens.CreateAccessToken(user, now), entity, value);
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair. Every token can be used once: presenting a token that was
    /// already used or revoked is treated as theft and revokes the whole family. Returns null when not valid.
    /// </summary>
    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var currentHash = tokens.HashRefreshToken(refreshToken);
        var stored = await refreshTokens.FindByHashAsync(currentHash, ct);
        if (stored is null)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!stored.IsActive(now))
        {
            if (stored.RevokedAtUtc is not null)
            {
                await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
            }
            return null;
        }

        var user = await identity.GetUserAsync(stored.UserId, ct);
        if (user is null)
        {
            return null;
        }

        var (replacement, value) = NewRefreshToken(user.Id, stored.FamilyId, now);
        if (!await refreshTokens.TryRotateAsync(currentHash, replacement, now, ct))
        {
            // Lost a race with another use of the same token: same situation as a reuse.
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
            return null;
        }

        return ToResponse(tokens.CreateAccessToken(user, now), replacement, value);
    }

    /// <summary>Revokes the session of the refresh token, if it belongs to <paramref name="userId"/>. Idempotent.</summary>
    public async Task LogoutAsync(Guid userId, string refreshToken, CancellationToken ct)
    {
        var stored = await refreshTokens.FindByHashAsync(tokens.HashRefreshToken(refreshToken), ct);
        if (stored is null || stored.UserId != userId)
        {
            return;
        }

        await refreshTokens.RevokeFamilyAsync(stored.FamilyId, time.GetUtcNow().UtcDateTime, ct);
    }

    private (RefreshToken Entity, string Value) NewRefreshToken(Guid userId, Guid familyId, DateTime now)
    {
        var value = tokens.CreateRefreshTokenValue();
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokens.HashRefreshToken(value),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(jwtOptions.Value.RefreshTokenDays),
        };
        return (entity, value);
    }

    private static TokenResponse ToResponse(AccessToken access, RefreshToken refresh, string refreshValue) =>
        new(access.Value, access.ExpiresAtUtc, refreshValue, refresh.ExpiresAtUtc);
}
