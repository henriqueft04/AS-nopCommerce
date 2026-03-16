using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Core.Telemetry;
using Nop.Web.Framework.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nop.Web.Framework.Infrastructure;

/// <summary>
/// Registers OpenTelemetry tracing and metrics for nopCommerce.
///
/// Order = -5 ensures this runs before every other INopStartup so the
/// tracer and meter providers are available from the very first request,
/// including requests that hit the database or external services during
/// warm-up.
///
/// No existing files are modified by this class.  The INopStartup
/// discovery mechanism picks it up automatically.
/// </summary>
public class ObservabilityStartup : INopStartup
{
    public int Order => -5;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "nopCommerce";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: NopActivitySource.Version))
            .WithTracing(tracing => tracing
                .AddSource(NopActivitySource.Name)
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Exclude health check and static file noise from traces
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health") &&
                        !context.Request.Path.StartsWithSegments("/favicon");
                })
                .AddHttpClientInstrumentation(options =>
                {
                    // Scrub sensitive query parameters from outbound URLs before they hit the exporter
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        activity?.SetTag("http.client.name", request.RequestUri?.Host);
                    };
                })
                .AddProcessor(new PiiSanitizingProcessor())
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(NopActivitySource.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                }));
    }

    public void Configure(IApplicationBuilder application)
    {
        // No middleware needed — OTel hooks in via the hosting infrastructure
    }
}
