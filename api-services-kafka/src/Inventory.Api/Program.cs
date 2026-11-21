using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Features.Products;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<InventoryDbContext>((sp, options) =>
    options
        .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Inventory"))
        .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ReservationService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.MapGet("/products", GetProducts.List);
app.MapGet("/products/{id:guid}", GetProducts.ById);

app.Run();

public partial class Program;
