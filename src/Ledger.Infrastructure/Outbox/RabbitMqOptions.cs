namespace Ledger.Infrastructure.Outbox;

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMq";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "ledger.events";
    /// <summary>Период опроса outbox, мс. В проде — LISTEN/NOTIFY или короткий poll; 500 мс — разумный дефолт.</summary>
    public int PollIntervalMs { get; set; } = 500;
    public int BatchSize { get; set; } = 100;
}
