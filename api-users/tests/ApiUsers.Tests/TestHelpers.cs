using System.Net.Http.Json;
using ApiUsers.Application.Auth;

namespace ApiUsers.Tests;

public static class TestHelpers
{
    public const string Password = "Passw0rd!x";

    public static string NewEmail() => $"{Guid.NewGuid():N}@x.com";

    public static async Task RegisterAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, password));
        response.EnsureSuccessStatusCode();
    }

    public static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }
}
