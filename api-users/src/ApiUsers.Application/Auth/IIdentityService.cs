namespace ApiUsers.Application.Auth;

/// <summary>Port to the identity store (users, passwords, roles). Implemented in Infrastructure.</summary>
public interface IIdentityService
{
    Task<RegistrationResult> CreateUserAsync(string email, string password, string role, CancellationToken ct);
}
