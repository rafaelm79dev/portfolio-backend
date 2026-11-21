namespace ApiUsers.Api.Data;

/// <summary>
/// A refresh token issued to a user. Only the SHA-256 hash of the token is stored.
/// All tokens produced by successive rotations share the same <see cref="FamilyId"/>,
/// so a reuse attack can revoke the whole chain at once.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
