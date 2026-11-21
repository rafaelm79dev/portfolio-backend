namespace ApiUsers.Api.Auth;

// Nullable members on purpose: an incomplete JSON body must reach the handler so it can answer 400.
public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record TokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

public sealed record UserResponse(Guid Id, string Email, IReadOnlyList<string> Roles);
