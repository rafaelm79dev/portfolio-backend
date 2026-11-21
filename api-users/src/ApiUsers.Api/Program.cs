using ApiUsers.Api.Auth;
using ApiUsers.Application;
using ApiUsers.Infrastructure;
using ApiUsers.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();

app.Run();

// Exposes the entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program;
