var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposes the entry point to the integration test project (WebApplicationFactory<Program>).
public partial class Program;
