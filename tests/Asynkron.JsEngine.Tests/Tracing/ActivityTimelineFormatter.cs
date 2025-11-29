using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.Tracing;

public static class ActivityTimelineFormatter
{
    public static IReadOnlyList<string> Format(Activity root,
        IEnumerable<Activity> activities,
        int width = 80,
        Func<Activity, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);

        var filtered = activities?
                           .Where(a => predicate?.Invoke(a) ?? true)
                           .OrderBy(a => a.StartTimeUtc)
                           .ToList()
                       ?? new List<Activity>();

        var reference = filtered.FirstOrDefault(a => string.Equals(a.DisplayName, "Program", StringComparison.Ordinal))
                         ?? filtered.FirstOrDefault()
                         ?? root;

        var rootStart = reference.StartTimeUtc;
        var rootEnd = rootStart + (reference.Duration > TimeSpan.Zero
            ? reference.Duration
            : TimeSpan.FromTicks(Math.Max(1, filtered.LastOrDefault()?.Duration.Ticks ?? 1)));

        foreach (var activity in filtered)
        {
            if (activity.StartTimeUtc < rootStart)
            {
                rootStart = activity.StartTimeUtc;
            }

            var activityEnd = activity.StartTimeUtc + (activity.Duration > TimeSpan.Zero
                ? activity.Duration
                : TimeSpan.FromMilliseconds(0.1));
            if (activityEnd > rootEnd)
            {
                rootEnd = activityEnd;
            }
        }

        var rootDuration = rootEnd - rootStart;
        if (rootDuration <= TimeSpan.Zero)
        {
            rootDuration = TimeSpan.FromMilliseconds(1);
        }

        var lines = new List<string>();

        foreach (var activity in filtered)
        {
            lines.Add(FormatLine(activity, rootStart, rootDuration, width));
        }

        return lines;
    }

    public static void Write(Activity root,
        IEnumerable<Activity> activities,
        ITestOutputHelper output,
        int width = 80,
        Func<Activity, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var line in Format(root, activities, width, predicate: predicate))
        {
            output.WriteLine(line);
        }
    }

    private static string FormatLine(Activity activity,
        DateTime rootStart,
        TimeSpan rootDuration,
        int width)
    {
        var duration = activity.Duration > TimeSpan.Zero
            ? activity.Duration
            : TimeSpan.FromMilliseconds(0.1);
        var startOffset = activity.StartTimeUtc - rootStart;
        var startRatio = Math.Clamp(startOffset.TotalMilliseconds / rootDuration.TotalMilliseconds, 0, 1);
        var durationRatio = Math.Clamp(duration.TotalMilliseconds / rootDuration.TotalMilliseconds, 0, 1);

        var scaledWidth = width * 8;
        var startUnit = (int)Math.Round(startRatio * scaledWidth);
        var durationUnits = Math.Max(1, (int)Math.Round(durationRatio * scaledWidth));
        var endUnit = Math.Min(startUnit + durationUnits, scaledWidth);

        var buffer = new char[width];
        Array.Fill(buffer, ' ');
        for (var column = 0; column < width; column++)
        {
            var columnStart = column * 8;
            var columnEnd = columnStart + 8;
            var overlap = Math.Max(0, Math.Min(columnEnd, endUnit) - Math.Max(columnStart, startUnit));
            if (overlap <= 0)
            {
                continue;
            }

            var includesStart = startUnit >= columnStart && startUnit < columnEnd;
            var includesEnd = endUnit > columnStart && endUnit <= columnEnd;

            buffer[column] = overlap switch
            {
                >= 8 => '█',
                _ when includesStart && !includesEnd => SelectRightBlock(overlap / 8.0),
                _ when includesEnd && !includesStart => SelectLeftBlock(overlap / 8.0),
                _ when includesStart && includesEnd => SelectLeftBlock(overlap / 8.0),
                _ => SelectLeftBlock(overlap / 8.0)
            };
        }

        var label = FormattableString.Invariant($"{activity.DisplayName}, {duration.TotalMilliseconds:F1}ms");
        const int labelWidth = 40;
        if (label.Length > labelWidth)
        {
            label = label[..labelWidth];
        }

        return $"{label.PadRight(labelWidth)} : {new string(buffer)}";
    }

    private static char SelectLeftBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.875 => '▉',
            >= 0.75 => '▊',
            >= 0.625 => '▋',
            >= 0.5 => '▌',
            >= 0.375 => '▍',
            >= 0.25 => '▎',
            >= 0.125 => '▏',
            _ => ' '
        };
    }

    private static char SelectRightBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.5 => '▐',
            >= 0.125 => '▕',
            _ => ' '
        };
    }
}
