using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiUsers.Application.Auth;
using ApiUsers.Application.Users;
using ApiUsers.Domain;
using ApiUsers.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ApiUsers.Tests;

[Collection("api")]
public class RolesTests(ApiFactory factory)
{
    private async Task<HttpClient> ClientAsync(string? email = null, string? password = null)
    {
        var client = factory.CreateClient();
        if (email is null)
        {
            return client;
        }
        var tokens = await TestHelpers.LoginAsync(client, email, password ?? TestHelpers.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync() =>
        await ClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    private async Task<HttpClient> UserClientAsync()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email);
        return await ClientAsync(email);
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private async Task<int> CountUsersAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync();
    }

    [Fact]
    public async Task Users_list_without_token_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_list_as_regular_user_returns_403()
    {
        var client = await UserClientAsync();

        var response = await client.GetAsync("/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Users_list_as_admin_returns_200_and_includes_admin()
    {
        var client = await AdminClientAsync();
        UserResponse? admin = null;

        for (var page = 1; admin is null; page++)
        {
            var response = await client.GetAsync($"/users?page={page}&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>())!;
            admin = body.Items.SingleOrDefault(u => u.Email == ApiFactory.AdminEmail);
            if (body.Items.Count == 0)
            {
                break;
            }
        }

        Assert.NotNull(admin);
        Assert.Equal([Roles.Admin], admin.Roles);
    }

    [Fact]
    public async Task Users_list_respects_page_size_and_reports_total()
    {
        await UserClientAsync();
        await UserClientAsync();
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/users?page=1&pageSize=2");

        var body = (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>())!;
        Assert.Equal(2, body.Items.Count);
        Assert.Equal(1, body.Page);
        Assert.Equal(2, body.PageSize);
        Assert.True(body.TotalCount >= 3);
    }

    [Theory]
    [InlineData("page=0&pageSize=10")]
    [InlineData("page=1&pageSize=0")]
    [InlineData("page=1&pageSize=101")]
    [InlineData("page=-1&pageSize=10")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task Users_list_with_invalid_paging_returns_400(string query)
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync($"/users?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_token_has_admin_role_claim()
    {
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);

        Assert.Equal(Roles.Admin, jwt.GetClaim("role").Value);
    }

    [Fact]
    public async Task Seeding_is_idempotent()
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();

        await DbSeeder.SeedAsync(factory.Services, config);
        await DbSeeder.SeedAsync(factory.Services, config);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        Assert.Single(admins, u => u.Email == ApiFactory.AdminEmail);
    }

    [Fact]
    public async Task Seeding_skips_admin_when_not_configured()
    {
        var before = await CountUsersAsync();

        await DbSeeder.SeedAsync(factory.Services, Config());

        Assert.Equal(before, await CountUsersAsync());
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        Assert.True(await roles.RoleExistsAsync(Roles.User));
        Assert.True(await roles.RoleExistsAsync(Roles.Admin));
    }

    [Fact]
    public async Task Seeding_with_weak_admin_password_fails_without_leaking_it()
    {
        const string weakPassword = "zzq";
        var config = Config(("Admin:Email", "weak-admin@x.com"), ("Admin:Password", weakPassword));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DbSeeder.SeedAsync(factory.Services, config));

        Assert.DoesNotContain(weakPassword, error.Message);
    }
}
