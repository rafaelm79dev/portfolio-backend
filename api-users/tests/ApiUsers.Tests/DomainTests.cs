using ApiUsers.Domain;

namespace ApiUsers.Tests;

public class DomainTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken NewToken(DateTime expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        FamilyId = Guid.NewGuid(),
        TokenHash = "hash",
        CreatedAtUtc = Now.AddDays(-1),
        ExpiresAtUtc = expiresAtUtc,
    };

    [Fact]
    public void RefreshToken_is_active_before_expiry_and_not_revoked()
    {
        var token = NewToken(Now.AddMinutes(1));

        Assert.True(token.IsActive(Now));
    }

    [Fact]
    public void RefreshToken_is_not_active_after_expiry()
    {
        var token = NewToken(Now.AddMinutes(-1));

        Assert.False(token.IsActive(Now));
    }

    [Fact]
    public void RefreshToken_is_not_active_when_revoked()
    {
        var token = NewToken(Now.AddDays(7));

        token.Revoke(Now);

        Assert.False(token.IsActive(Now));
    }

    [Fact]
    public void Revoke_twice_keeps_first_timestamp()
    {
        var token = NewToken(Now.AddDays(7));

        token.Revoke(Now);
        token.Revoke(Now.AddHours(5));

        Assert.Equal(Now, token.RevokedAtUtc);
    }
}
