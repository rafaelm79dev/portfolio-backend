using ApiUsers.Api.Auth;
using ApiUsers.Api.Data;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppDatabase();
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AppDbContext>();

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();

app.Run();

// Exposes the entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program;
