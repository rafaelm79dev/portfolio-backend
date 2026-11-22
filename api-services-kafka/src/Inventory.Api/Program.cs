using Confluent.Kafka;
using Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Features.Products;
using Inventory.Api.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<InventoryDbContext>((sp, options) =>
    options
        .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Inventory"))
        .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ReservationService>();

var kafka = builder.Configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
if (kafka.Enabled)
{
    builder.Services.AddSingleton<IProducer<string, string>>(_ =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = kafka.MessageTimeoutMs
        }).Build());
    builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddSingleton<IMessageHandler, OrderCreatedHandler>();
    builder.Services.AddHostedService(sp => new ConsumerLoop(
        new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = kafka.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build(),
        sp.GetRequiredService<IProducer<string, string>>(),
        sp.GetRequiredService<IMessageHandler>(),
        Options.Create(new ConsumerLoopOptions { Topic = Topics.OrdersEvents }),
        sp.GetRequiredService<ILogger<ConsumerLoop>>()));
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.MapGet("/products", GetProducts.List);
app.MapGet("/products/{id:guid}", GetProducts.ById);

app.Run();

public partial class Program;
