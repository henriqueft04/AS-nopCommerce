using System.Diagnostics;

namespace Nop.Core.Telemetry;

/// <summary>
/// Central ActivitySource for nopCommerce tracing.
/// Lives in Nop.Core so it can be used from any layer without introducing
/// a dependency on the OTel SDK packages — ActivitySource is part of
/// System.Diagnostics in the base class library.
/// </summary>
public static class NopActivitySource
{
    public const string Name = "nopCommerce";
    public const string Version = "5.00";

    public static readonly ActivitySource Instance = new(Name, Version);
}
