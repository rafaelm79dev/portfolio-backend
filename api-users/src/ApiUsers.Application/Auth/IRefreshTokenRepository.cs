using ApiUsers.Domain;

namespace ApiUsers.Application.Auth;

/// <summary>Port that persists refresh tokens. Implemented in Infrastructure.</summary>
public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
}
