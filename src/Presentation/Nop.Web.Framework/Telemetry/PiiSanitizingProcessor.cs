using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace Nop.Web.Framework.Telemetry;

/// <summary>
/// OTel span processor that scrubs known-sensitive attribute keys before
/// spans are exported.
///
/// nopCommerce passes full domain objects (Customer, Order) through its
/// service layer.  Rather than remembering which fields are safe at every
/// instrumentation point, this single processor enforces the boundary
/// centrally: any span attribute whose key matches a sensitive pattern, or
/// whose string value looks like an email address, is removed before export.
///
/// This is registered in ObservabilityStartup as the last processor in the
/// pipeline so it runs regardless of which exporter is used.
/// </summary>
public sealed class PiiSanitizingProcessor : BaseProcessor<Activity>
{
    // Keys that must never appear in exported spans
    private static readonly HashSet<string> _blockedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "customer.email",
        "customer.username",
        "customer.password",
        "billing.email",
        "billing.address",
        "billing.card",
        "payment.card_number",
        "payment.cvv",
        "http.request.header.cookie",
        "http.request.header.authorization",
    };

    // Patterns in key names that indicate sensitive data
    private static readonly string[] _blockedKeySubstrings =
    [
        "password", "secret", "token", "card", "cvv", "ssn", "email"
    ];

    private static readonly Regex _emailPattern =
        new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override void OnEnd(Activity activity)
    {
        if (activity is null)
            return;

        var keysToRemove = new List<string>();

        foreach (var tag in activity.TagObjects)
        {
            if (IsSensitiveKey(tag.Key))
            {
                keysToRemove.Add(tag.Key);
                continue;
            }

            // Remove any string value that contains an email address
            if (tag.Value is string strValue && _emailPattern.IsMatch(strValue))
                keysToRemove.Add(tag.Key);
        }

        foreach (var key in keysToRemove)
            activity.SetTag(key, null);
    }

    private static bool IsSensitiveKey(string key)
    {
        if (_blockedKeys.Contains(key))
            return true;

        foreach (var fragment in _blockedKeySubstrings)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
