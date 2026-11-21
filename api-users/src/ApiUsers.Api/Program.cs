using ApiUsers.Api.Auth;
using ApiUsers.Api.OpenApi;
using ApiUsers.Api.Security;
using ApiUsers.Api.Users;
using ApiUsers.Application;
using ApiUsers.Infrastructure;
using ApiUsers.Infrastructure.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddJwtAuthentication();
builder.Services.AddAppRateLimiting();
builder.Services.AddAppForwardedHeaders();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityDocumentTransformer>();
    options.AddOperationTransformer<BearerSecurityOperationTransformer>();
});

var app = builder.Build();

await app.MigrateDatabaseAsync();
await app.SeedDatabaseAsync();

// First in the pipeline: HTTPS redirection and the rate limiter must see the real scheme and client IP.
// Opt-in (ForwardedHeaders:Enabled), only for the listed proxies.
app.UseAppForwardedHeaders();

// HTTPS is enforced everywhere except local development. Behind a proxy that terminates TLS,
// enable ForwardedHeaders so the original scheme is known and the redirect does not loop.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// The API description is only a development aid: it is not exposed in other environments.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithSummary("Liveness check.");
app.MapAuth();
app.MapUsers();

app.Run();

// Exposes the entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program;
