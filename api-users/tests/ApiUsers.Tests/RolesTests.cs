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
        var everyone = await AllUsersAsync(client);

        var admin = Assert.Single(everyone, u => u.Email == ApiFactory.AdminEmail);
        Assert.Equal([Roles.Admin], admin.Roles);
    }

    [Fact]
    public async Task Users_list_respects_page_size_and_reports_total()
    {
        // The database is shared with the other tests, so compare against the total before adding users.
        // The "api" collection runs its tests one at a time, which makes the difference exact.
        var client = await AdminClientAsync();
        var totalBefore = await TotalUsersAsync(client);
        await UserClientAsync();
        await UserClientAsync();

        var response = await client.GetAsync("/users?page=1&pageSize=2");

        var body = (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>())!;
        Assert.Equal(2, body.Items.Count);
        Assert.Equal(1, body.Page);
        Assert.Equal(2, body.PageSize);
        Assert.Equal(totalBefore + 2, body.TotalCount);
    }

    private static async Task<List<UserResponse>> AllUsersAsync(HttpClient admin)
    {
        const int pageSize = 100;
        var all = new List<UserResponse>();
        var totalCount = 0;
        var page = 1;
        do
        {
            var body = await PageAsync(admin, page, pageSize);
            all.AddRange(body.Items);
            totalCount = body.TotalCount;
            page++;
        }
        while (all.Count < totalCount);
        return all;
    }

    private static async Task<PagedResponse<UserResponse>> PageAsync(HttpClient admin, int page, int pageSize)
    {
        var response = await admin.GetAsync($"/users?page={page}&pageSize={pageSize}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>())!;
    }

    private static async Task<int> TotalUsersAsync(HttpClient admin)
    {
        var response = await admin.GetAsync("/users?page=1&pageSize=1");
        return (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>())!.TotalCount;
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
    public async Task Seeding_does_not_promote_existing_account_with_a_different_password()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email); // registered by someone else, own password
        var config = Config(("Admin:Email", email), ("Admin:Password", "An0ther!Passw0rd"));

        await DbSeeder.SeedAsync(factory.Services, config); // must neither throw nor promote

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.False(await users.IsInRoleAsync(user!, Roles.Admin));
    }

    [Fact]
    public async Task Seeding_promotes_existing_account_when_the_configured_password_matches()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email);
        var config = Config(("Admin:Email", email), ("Admin:Password", TestHelpers.Password));

        await DbSeeder.SeedAsync(factory.Services, config);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.True(await users.IsInRoleAsync(user!, Roles.Admin));
    }

    [Fact]
    public async Task Seeding_with_weak_admin_password_fails_without_leaking_it()
    {
        const string weakPassword = "zzq";
        var config = Config(("Admin:Email", "weak-admin@x.com"), ("Admin:Password", weakPassword));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DbSeeder.SeedAsync(factory.Services, config));

        Assert.DoesNotContain(weakPassword, error.Message);
    }

    [Theory]
    [InlineData("Admin:Email", "only-email@x.com")]
    [InlineData("Admin:Password", "Passw0rd!xyz")]
    public async Task Seeding_creates_no_admin_when_only_the_email_or_only_the_password_is_configured(string key, string value)
    {
        var before = await CountUsersAsync();

        await DbSeeder.SeedAsync(factory.Services, Config((key, value)));

        Assert.Equal(before, await CountUsersAsync());
    }

    [Fact]
    public async Task Seeding_keeps_the_current_password_of_an_existing_admin_when_the_configured_one_changes()
    {
        const string otherPassword = "Other!Passw0rd99";
        var changed = Config(("Admin:Email", ApiFactory.AdminEmail), ("Admin:Password", otherPassword));

        await DbSeeder.SeedAsync(factory.Services, changed);

        var original = await factory.CreateClient().PostAsJsonAsync(
            "/auth/login", new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var replaced = await factory.CreateClient().PostAsJsonAsync(
            "/auth/login", new LoginRequest(ApiFactory.AdminEmail, otherPassword));
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replaced.StatusCode);
    }

    [Fact]
    public async Task Users_list_pages_do_not_overlap_and_follow_email_order()
    {
        await UserClientAsync();
        await UserClientAsync();
        await UserClientAsync();
        var client = await AdminClientAsync();

        var first = await PageAsync(client, 1, 2);
        var second = await PageAsync(client, 2, 2);

        Assert.Equal(2, first.Items.Count);
        Assert.NotEmpty(second.Items);
        Assert.Empty(first.Items.Select(u => u.Id).Intersect(second.Items.Select(u => u.Id)));
        var emails = first.Items.Concat(second.Items).Select(u => u.Email.ToUpperInvariant()).ToList();
        Assert.Equal(emails.OrderBy(e => e, StringComparer.Ordinal).ToList(), emails);
    }
}
