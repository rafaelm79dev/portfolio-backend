using ApiUsers.Application.Auth;
using ApiUsers.Domain;
using ApiUsers.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiUsers.Infrastructure.Auth;

public sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<bool> TryRotateAsync(string currentHash, RefreshToken replacement, DateTime nowUtc, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // One conditional UPDATE decides the winner: a concurrent caller blocks on the row and then sees it revoked.
        // The condition mirrors RefreshToken.IsActive.
        var updated = await db.RefreshTokens
            .Where(t => t.TokenHash == currentHash && t.RevokedAtUtc == null && t.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.ReplacedByTokenHash, replacement.TokenHash), ct);
        if (updated != 1)
        {
            return false;
        }

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, nowUtc), ct);
}
