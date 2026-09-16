using Ledger.Application.Common;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ledger.Api.Observability;

public static class Telemetry
{
    /// <summary>
    /// Трейсы: HTTP-сервер, исходящий HTTP, Npgsql (каждый SQL — span), собственный ActivitySource «Ledger».
    /// Метрики: ASP.NET Core (latency/статусы по маршрутам), runtime, бизнес-метрики Meter «Ledger».
    /// Экспорт: Prometheus (/metrics) всегда; OTLP — если задан OTEL_EXPORTER_OTLP_ENDPOINT (Jaeger/Tempo/Grafana).
    /// </summary>
    public static IServiceCollection AddLedgerTelemetry(this IServiceCollection services, IConfiguration config, string serviceName)
    {
        var otlp = config["OTEL_EXPORTER_OTLP_ENDPOINT"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: typeof(Telemetry).Assembly.GetName().Version?.ToString()))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/metrics"))
                 .AddHttpClientInstrumentation()
                 .AddNpgsql()
                 .AddSource(LedgerMetrics.MeterName);
                if (otlp is not null) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddMeter(LedgerMetrics.MeterName)
                 .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Runtime")
                 .AddPrometheusExporter();
                if (otlp is not null) m.AddOtlpExporter();
            });
        return services;
    }
}
