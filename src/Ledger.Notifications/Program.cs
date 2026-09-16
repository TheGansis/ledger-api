using Ledger.Notifications;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis"] ?? "localhost:6379"));
builder.Services.AddHostedService<NotificationConsumer>();
builder.Build().Run();
