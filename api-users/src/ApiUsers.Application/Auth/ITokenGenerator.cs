namespace ApiUsers.Application.Auth;

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

/// <summary>Port that creates and hashes tokens. Implemented in Infrastructure.</summary>
public interface ITokenGenerator
{
    AccessToken CreateAccessToken(UserResponse user, DateTime nowUtc);

    /// <summary>A new unguessable refresh token value (the plain value is only ever returned to the client).</summary>
    string CreateRefreshTokenValue();

    /// <summary>Deterministic hash used to store and look up refresh tokens.</summary>
    string HashRefreshToken(string refreshTokenValue);
}
