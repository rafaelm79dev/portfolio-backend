using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ApiUsers.Application.Auth;

namespace ApiUsers.Tests;

public static class TestHelpers
{
    public const string Password = "Passw0rd!x";
    public const string WrongPassword = "Wr0ng!Password";

    /// <summary>Failed logins that lock an account: keep in sync with Lockout.MaxFailedAccessAttempts.</summary>
    public const int MaxFailedAttempts = 5;

    public static string NewEmail() => $"{Guid.NewGuid():N}@x.com";

    /// <summary>The value stored for a refresh token: Base64 of its SHA-256.</summary>
    public static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static async Task RegisterAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, password));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Registers a user with a fresh email and returns that email.</summary>
    public static async Task<string> RegisterNewUserAsync(HttpClient client)
    {
        var email = NewEmail();
        await RegisterAsync(client, email);
        return email;
    }

    public static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    /// <summary>Locks the account by failing to log in <see cref="MaxFailedAttempts"/> times.</summary>
    public static async Task LockOutAsync(HttpClient client, string email)
    {
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
        {
            await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, WrongPassword));
        }
    }
}
