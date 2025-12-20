using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace ProfileRunner;

public sealed class AllocationEventListener : IDisposable
{
    private readonly ConcurrentDictionary<string, AllocationInfo> _allocations =
        new(StringComparer.Ordinal);
    private bool _isEnabled;
    private EventPipeSession? _session;
    private Task? _processingTask;

    public IReadOnlyDictionary<string, AllocationInfo> Allocations => _allocations;

    public void Start()
    {
        if (_session != null)
        {
            _isEnabled = true;
            return;
        }

        _isEnabled = true;

        var client = new DiagnosticsClient(Environment.ProcessId);
        var providers = new List<EventPipeProvider>
        {
            new(
                "Microsoft-Windows-DotNETRuntime",
                EventLevel.Verbose,
                (long)ClrTraceEventParser.Keywords.GC)
        };

        _session = client.StartEventPipeSession(providers, false);
        _processingTask = Task.Run(() => ProcessSession(_session));
    }

    public void Stop()
    {
        _isEnabled = false;
        if (_session == null)
        {
            return;
        }

        try
        {
            _session.Stop();
        }
        catch
        {
            // Ignore shutdown errors.
        }

        _processingTask?.Wait(TimeSpan.FromSeconds(5));
        _session.Dispose();
        _session = null;
        _processingTask = null;
    }

    public void Reset()
    {
        _allocations.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    public IReadOnlyList<AllocationInfo> GetTopAllocations(int topN = 50)
    {
        return _allocations.Values
            .OrderByDescending(a => a.TotalBytes)
            .Take(topN)
            .Select(a => new AllocationInfo
            {
                TypeName = a.TypeName,
                Count = a.Count,
                TotalBytes = a.TotalBytes
            })
            .ToList();
    }

    public void PrintReport(IReadOnlyList<AllocationInfo> topAllocations)
    {
        Console.WriteLine();
        Console.WriteLine("=== ALLOCATION BY TYPE (sampled) ===");
        Console.WriteLine();
        Console.WriteLine($"{"Type",-70} {"Count",12} {"Total",15}");
        Console.WriteLine(new string('-', 100));

        long grandTotal = 0;
        long grandCount = 0;

        foreach (var alloc in topAllocations)
        {
            var displayName = alloc.TypeName.Length > 68
                ? "..." + alloc.TypeName[^65..]
                : alloc.TypeName;

            var countText = alloc.Count.ToString("N0", CultureInfo.InvariantCulture);
            var bytesText = FormatBytes(alloc.TotalBytes);
            Console.WriteLine($"{displayName,-70} {countText,12} {bytesText,15}");
            grandTotal += alloc.TotalBytes;
            grandCount += alloc.Count;
        }

        Console.WriteLine(new string('-', 100));
        var grandCountText = grandCount.ToString("N0", CultureInfo.InvariantCulture);
        var grandTotalText = FormatBytes(grandTotal);
        Console.WriteLine($"{"TOTAL (shown)",-70} {grandCountText,12} {grandTotalText,15}");
        Console.WriteLine();
    }

    private void ProcessSession(EventPipeSession session)
    {
        try
        {
            using var source = new EventPipeEventSource(session.EventStream);
            source.Clr.GCAllocationTick += data =>
            {
                if (!_isEnabled)
                {
                    return;
                }

                var typeName = string.IsNullOrWhiteSpace(data.TypeName) ? "Unknown" : data.TypeName;
                var allocationBytes = data.AllocationAmount64 < 0 ? 0 : data.AllocationAmount64;

                _allocations.AddOrUpdate(
                    typeName,
                    _ => new AllocationInfo
                    {
                        TypeName = typeName,
                        Count = 1,
                        TotalBytes = allocationBytes
                    },
                    (_, existing) =>
                    {
                        existing.Count++;
                        existing.TotalBytes += allocationBytes;
                        return existing;
                    });
            };

            source.Process();
        }
        catch
        {
            // Ignore processing errors.
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
    }

    public sealed class AllocationInfo
    {
        public string TypeName { get; set; } = string.Empty;
        public long Count { get; set; }
        public long TotalBytes { get; set; }
    }
}
