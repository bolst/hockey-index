using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace HockeyIndex.Api.Infrastructure.Observability;

public static class LoggingSetup
{
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ServiceName = "hockey-index-api";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

        builder.Services.AddSingleton<HockeyIndexMetrics>();

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(ServiceName))
                .WithMetrics(metrics => metrics
                    .AddMeter(HockeyIndexMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation())
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation())
                .UseOtlpExporter();
        }

        return builder;
    }
}
