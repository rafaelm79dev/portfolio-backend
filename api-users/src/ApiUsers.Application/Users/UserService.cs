using ApiUsers.Application.Auth;

namespace ApiUsers.Application.Users;

public sealed class UserService(IIdentityService identity)
{
    /// <summary>Returns the user, or null when the account no longer exists.</summary>
    public Task<UserResponse?> GetCurrentAsync(Guid userId, CancellationToken ct) =>
        identity.GetUserAsync(userId, ct);

    /// <summary>One page of users ordered by email. <paramref name="page"/> starts at 1.</summary>
    public Task<PagedResponse<UserResponse>> ListAsync(int page, int pageSize, CancellationToken ct) =>
        identity.ListUsersAsync(page, pageSize, ct);
}
