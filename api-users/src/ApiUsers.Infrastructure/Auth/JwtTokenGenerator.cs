using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ApiUsers.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApiUsers.Infrastructure.Auth;

public sealed class JwtTokenGenerator : ITokenGenerator
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(UserResponse user, DateTime nowUtc)
    {
        var expiresAt = nowUtc.AddMinutes(_options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = expiresAt,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email,
                ["jti"] = Guid.NewGuid().ToString(),
                ["role"] = user.Roles.ToArray(),
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    public string CreateRefreshTokenValue() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public string HashRefreshToken(string refreshTokenValue) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenValue)));
}
