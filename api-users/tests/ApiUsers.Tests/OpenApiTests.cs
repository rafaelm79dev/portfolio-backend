using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ApiUsers.Tests;

[Collection("api")]
public class OpenApiTests(ApiFactory factory)
{
    private async Task<JsonElement> GetDocumentAsync()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    [InlineData("/auth/refresh")]
    [InlineData("/auth/logout")]
    [InlineData("/users/me")]
    [InlineData("/users")]
    public async Task OpenApi_document_lists_every_endpoint(string path)
    {
        var document = await GetDocumentAsync();

        Assert.True(document.GetProperty("paths").TryGetProperty(path, out _), $"{path} is missing");
    }

    [Fact]
    public async Task OpenApi_document_declares_bearer_security_scheme()
    {
        var document = await GetDocumentAsync();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task Only_protected_operations_require_the_bearer_scheme()
    {
        var document = await GetDocumentAsync();
        var paths = document.GetProperty("paths");

        Assert.True(paths.GetProperty("/users/me").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/users").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/auth/logout").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/auth/register").GetProperty("post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Scalar_ui_is_served_in_development()
    {
        var response = await factory.CreateClient().GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task Api_documentation_is_not_served_in_production(string path)
    {
        var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var response = await production.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
