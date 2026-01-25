#region

using System;
using System.Globalization;
using System.Threading;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class IntlCollatorInternalSlots
{
    private HostFunction? _boundCompare;

    public required string Locale { get; init; }
    public required string Usage { get; init; }
    public required string Sensitivity { get; init; }
    public required bool IgnorePunctuation { get; init; }
    public required bool Numeric { get; init; }
    public required string CaseFirst { get; init; }
    public required string Collation { get; init; }
    public string LocaleMatcher { get; init; } = "best fit";
    public CompareInfo CompareInfo { get; init; } = CultureInfo.InvariantCulture.CompareInfo;

    public HostFunction GetOrCreateBoundCompare(Func<HostFunction> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var existing = Volatile.Read(ref _boundCompare);
        if (existing is not null)
        {
            return existing;
        }

        var created = factory();
        var prior = Interlocked.CompareExchange(ref _boundCompare, created, null);
        return prior ?? created;
    }
}
