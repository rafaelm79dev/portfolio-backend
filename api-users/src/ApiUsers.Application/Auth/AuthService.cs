using ApiUsers.Domain;

namespace ApiUsers.Application.Auth;

public sealed class AuthService(IIdentityService identity)
{
    public Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken ct) =>
        identity.CreateUserAsync(email, password, Roles.User, ct);
}
