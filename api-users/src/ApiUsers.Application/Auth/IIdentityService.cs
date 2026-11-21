using ApiUsers.Application.Users;

namespace ApiUsers.Application.Auth;

/// <summary>Port to the identity store (users, passwords, roles). Implemented in Infrastructure.</summary>
public interface IIdentityService
{
    Task<RegistrationResult> CreateUserAsync(string email, string password, string role, CancellationToken ct);

    /// <summary>
    /// Returns the user when email and password match and the account is not locked out;
    /// null in every other case, so callers cannot tell why a login failed.
    /// </summary>
    Task<UserResponse?> ValidateCredentialsAsync(string email, string password, CancellationToken ct);

    /// <summary>Returns the user with its roles, or null when it does not exist.</summary>
    Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Like <see cref="GetUserAsync"/>, but also null when the account is locked out.</summary>
    Task<UserResponse?> GetUserIfCanSignInAsync(Guid userId, CancellationToken ct);

    /// <summary>Reads the role from the store, so a revoked role takes effect before the access token expires.</summary>
    Task<bool> IsInRoleAsync(Guid userId, string role, CancellationToken ct);

    /// <summary>One page of users (with roles) ordered by email. <paramref name="page"/> starts at 1.</summary>
    Task<PagedResponse<UserResponse>> ListUsersAsync(int page, int pageSize, CancellationToken ct);
}
