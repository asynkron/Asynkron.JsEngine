using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace AllocationProfile;

/// <summary>
/// Listens to GC allocation tick events and tracks allocations by type.
/// Uses the Microsoft-Windows-DotNETRuntime ETW provider.
/// </summary>
public sealed class AllocationEventListener : EventListener
{
    private const int GCAllocationTick = 10; // GCAllocationTick_V4 event

    private readonly ConcurrentDictionary<string, AllocationInfo> _allocations = new();
    private bool _isEnabled;

    public IReadOnlyDictionary<string, AllocationInfo> Allocations => _allocations;

    public void Start()
    {
        _isEnabled = true;
    }

    public void Stop()
    {
        _isEnabled = false;
    }

    public void Reset()
    {
        _allocations.Clear();
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
        {
            // Enable GC events with allocation tick keyword (0x1 = GC, 0x80000 = GCAllocationTick)
            EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)0x80001);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (!_isEnabled || eventData.EventId != GCAllocationTick)
            return;

        try
        {
            // GCAllocationTick_V4 payload:
            // [0] AllocationAmount (uint)
            // [1] AllocationKind (uint) - 0=small, 1=large
            // [2] ClrInstanceID (ushort)
            // [3] AllocationAmount64 (ulong)
            // [4] TypeID (IntPtr)
            // [5] TypeName (string)
            // [6] HeapIndex (uint)
            // [7] Address (IntPtr)
            // [8] ObjectSize (ulong)

            if (eventData.Payload == null || eventData.Payload.Count < 6)
                return;

            var allocationAmount = eventData.Payload[0] is uint amount ? amount : 0;
            var typeName = eventData.Payload[5]?.ToString() ?? "Unknown";

            // Clean up type name
            if (string.IsNullOrWhiteSpace(typeName))
                typeName = "Unknown";

            _allocations.AddOrUpdate(
                typeName,
                _ => new AllocationInfo { TypeName = typeName, Count = 1, TotalBytes = allocationAmount },
                (_, existing) =>
                {
                    existing.Count++;
                    existing.TotalBytes += allocationAmount;
                    return existing;
                });
        }
        catch
        {
            // Ignore parsing errors
        }
    }

    public void PrintReport(int topN = 50)
    {
        Console.WriteLine();
        Console.WriteLine("=== ALLOCATION BY TYPE (sampled) ===");
        Console.WriteLine();
        Console.WriteLine($"{"Type",-70} {"Count",12} {"Total",15}");
        Console.WriteLine(new string('-', 100));

        var sorted = _allocations.Values
            .OrderByDescending(a => a.TotalBytes)
            .Take(topN);

        long grandTotal = 0;
        long grandCount = 0;

        foreach (var alloc in sorted)
        {
            var displayName = alloc.TypeName.Length > 68
                ? "..." + alloc.TypeName[^65..]
                : alloc.TypeName;

            Console.WriteLine($"{displayName,-70} {alloc.Count,12:N0} {FormatBytes(alloc.TotalBytes),15}");
            grandTotal += alloc.TotalBytes;
            grandCount += alloc.Count;
        }

        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"TOTAL (shown)",-70} {grandCount,12:N0} {FormatBytes(grandTotal),15}");
        Console.WriteLine();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    public class AllocationInfo
    {
        public string TypeName { get; set; } = "";
        public long Count { get; set; }
        public long TotalBytes { get; set; }
    }
}
