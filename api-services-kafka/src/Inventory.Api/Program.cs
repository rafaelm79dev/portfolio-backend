using Confluent.Kafka;
using Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Features.Products;
using Inventory.Api.Health;
using Inventory.Api.Messaging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<InventoryDbContext>((sp, options) =>
    options
        .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Inventory"))
        .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ReservationService>();

var health = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck<InventoryDbContext>>("database", tags: ["ready"]);

var kafka = builder.Configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
if (kafka.Enabled)
{
    builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
    builder.Services.AddSingleton<IProducer<string, string>>(_ =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = kafka.MessageTimeoutMs
        }).Build());
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddSingleton<IMessageHandler, OrderCreatedHandler>();
    builder.Services.AddSingleton(sp => new ConsumerLoop(
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
    builder.Services.AddSingleton<IConsumerStatus>(sp => sp.GetRequiredService<ConsumerLoop>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsumerLoop>());

    health.AddCheck<KafkaHealthCheck>("kafka", tags: ["ready"]);
    health.AddCheck<ConsumerHealthCheck>("consumer", tags: ["ready"]);
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.MapGet("/products", GetProducts.List);
app.MapGet("/products/{id:guid}", GetProducts.ById);

// /health: the process answers (liveness). /ready: the database, Kafka and the consumer are all fine.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
