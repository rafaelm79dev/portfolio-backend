using ApiUsers.Application.Auth;
using ApiUsers.Domain;
using ApiUsers.Infrastructure.Data;

namespace ApiUsers.Infrastructure.Auth;

public sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }
}
