using ApiUsers.Domain;

namespace ApiUsers.Application.Auth;

/// <summary>Port that persists refresh tokens. Implemented in Infrastructure.</summary>
public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);

    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Atomically marks the token with <paramref name="currentHash"/> as used (revoked and replaced) and stores
    /// <paramref name="replacement"/>, but only if that token is still active. Returns false, changing nothing,
    /// when it was already used, revoked or expired. Under concurrency exactly one caller can succeed.
    /// </summary>
    Task<bool> TryRotateAsync(string currentHash, RefreshToken replacement, DateTime nowUtc, CancellationToken ct);

    /// <summary>Revokes every still-active token of the family.</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken ct);
}
