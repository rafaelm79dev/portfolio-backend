using Confluent.Kafka;
using Contracts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Api.Data;
using Orders.Api.Features.Orders;
using Orders.Api.Health;
using Orders.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<OrdersDbContext>((sp, options) =>
    options
        .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Orders"))
        .UseSnakeCaseNamingConvention());

var health = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck<OrdersDbContext>>("database", tags: ["ready"]);

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
    builder.Services.AddSingleton<IMessageHandler, StockResultHandler>();
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
        Options.Create(new ConsumerLoopOptions { Topic = Topics.InventoryEvents }),
        sp.GetRequiredService<ILogger<ConsumerLoop>>()));
    builder.Services.AddSingleton<IConsumerStatus>(sp => sp.GetRequiredService<ConsumerLoop>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsumerLoop>());

    health.AddCheck<KafkaHealthCheck>("kafka", tags: ["ready"]);
    health.AddCheck<ConsumerHealthCheck>("consumer", tags: ["ready"]);
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
}

app.MapPost("/orders", CreateOrder.Handle);
app.MapGet("/orders/{id:guid}", GetOrder.Handle);

// /health: the process answers (liveness). /ready: the database, Kafka and the consumer are all fine.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
