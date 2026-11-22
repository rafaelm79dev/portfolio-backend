using Confluent.Kafka;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Api.Data;
using Orders.Api.Features.Orders;
using Orders.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<OrdersDbContext>((sp, options) =>
    options
        .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Orders"))
        .UseSnakeCaseNamingConvention());

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));

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
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddSingleton<IMessageHandler, StockResultHandler>();
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
        Options.Create(new ConsumerLoopOptions { Topic = Topics.InventoryEvents }),
        sp.GetRequiredService<ILogger<ConsumerLoop>>()));
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
}

app.MapPost("/orders", CreateOrder.Handle);
app.MapGet("/orders/{id:guid}", GetOrder.Handle);

app.Run();

public partial class Program;
