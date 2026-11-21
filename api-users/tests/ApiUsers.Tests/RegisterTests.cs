using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiUsers.Api.Auth;

namespace ApiUsers.Tests;

[Collection("api")]
public class RegisterTests(ApiFactory factory)
{
    private const string ValidPassword = "Passw0rd!x";

    private static string NewEmail() => $"{Guid.NewGuid():N}@x.com";

    private Task<HttpResponseMessage> RegisterAsync(string? email, string? password) =>
        factory.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest(email, password));

    [Fact]
    public async Task Register_valid_returns_201_with_user_role()
    {
        var email = NewEmail();

        var response = await RegisterAsync(email, ValidPassword);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(["User"], user.Roles);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        var email = NewEmail();
        await RegisterAsync(email, ValidPassword);

        var response = await RegisterAsync(email, ValidPassword);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_email_is_case_insensitive()
    {
        var local = Guid.NewGuid().ToString("N");
        await RegisterAsync($"{local}@x.com", ValidPassword);

        var response = await RegisterAsync($"{local.ToUpperInvariant()}@X.com", ValidPassword);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_weak_password_returns_400()
    {
        var response = await RegisterAsync(NewEmail(), "abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Register_invalid_email_returns_400()
    {
        var response = await RegisterAsync("no-es-email", ValidPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_overlong_email_returns_400()
    {
        var email = new string('a', 251) + "@x.com";

        var response = await RegisterAsync(email, ValidPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_overlong_password_returns_400()
    {
        var password = "Aa1!" + new string('a', 125);

        var response = await RegisterAsync(NewEmail(), password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{\"email\":\"a@x.com\"}")]
    public async Task Register_empty_or_incomplete_body_returns_400(string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
