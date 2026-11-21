using ApiUsers.Api.Auth;
using ApiUsers.Api.Users;
using ApiUsers.Application;
using ApiUsers.Infrastructure;
using ApiUsers.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddJwtAuthentication();

var app = builder.Build();

await app.MigrateDatabaseAsync();
await app.SeedDatabaseAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();
app.MapUsers();

app.Run();

// Exposes the entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program;
