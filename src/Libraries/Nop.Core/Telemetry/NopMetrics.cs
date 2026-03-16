using System.Diagnostics.Metrics;

namespace Nop.Core.Telemetry;

/// <summary>
/// Custom metrics for nopCommerce.
/// Uses System.Diagnostics.Metrics (built into .NET) so no OTel SDK
/// dependency is needed in Nop.Core.  The OTel SDK hooks into this
/// Meter at startup via ObservabilityStartup.
/// </summary>
public static class NopMetrics
{
    private static readonly Meter Meter = new(NopActivitySource.Name, NopActivitySource.Version);

    /// <summary>
    /// Distribution of result counts returned by product searches.
    ///
    /// Why this matters: a sustained shift in this histogram toward zero —
    /// without a corresponding spike in HTTP errors — is an early signal
    /// that the catalog has a silent availability problem (mass product
    /// deactivation, search provider failure, or a pricing/ACL misconfiguration
    /// that filters everything out).  An operator can alert when the p50 drops
    /// below a threshold and investigate before customers start complaining.
    ///
    /// Tags:
    ///   has_keyword    – whether the search had a text query
    ///   has_category   – whether a category filter was applied
    /// </summary>
    public static readonly Histogram<int> SearchResultCount = Meter.CreateHistogram<int>(
        name: "catalog.search.result_count",
        unit: "{products}",
        description: "Number of products returned per search query");

    /// <summary>
    /// Counts price calculations, tagged by whether a discount was applied.
    ///
    /// Why this matters: during an active promotional campaign every price
    /// calculation for an eligible product should produce discount_applied=true.
    /// If that tag's rate drops to zero while the campaign is supposed to be
    /// live, the discount configuration is broken.  This metric surfaces that
    /// failure in the metrics dashboard before the first customer support ticket
    /// arrives, because the root cause is pricing logic — not an HTTP error.
    ///
    /// Tags:
    ///   discount_applied – "true" if any discount reduced the final price
    /// </summary>
    public static readonly Counter<long> PriceDiscountCalculations = Meter.CreateCounter<long>(
        name: "catalog.price.discount_calculations",
        unit: "{calculations}",
        description: "Price calculations tracked by whether a discount was applied");
}
