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

        var filtered = MaterializeActivities(activities, predicate);
        return FormatLines(root, filtered, width);
    }

    public static void Write(Activity root,
        IEnumerable<Activity> activities,
        ITestOutputHelper output,
        int width = 80,
        Func<Activity, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(root);

        var filtered = MaterializeActivities(activities, predicate);
        foreach (var entry in FormatLines(root, filtered, width))
        {
            output.WriteLine(entry);
        }

        if (filtered.Count == 0)
        {
            output.WriteLine("No activities recorded.");
            return;
        }

        output.WriteLine("Activity Tags:");
        foreach (var activity in filtered)
        {
            output.WriteLine(FormattableString.Invariant($"- {activity.DisplayName}"));
            var hadTag = false;
            foreach (var tag in activity.Tags)
            {
                hadTag = true;
                output.WriteLine(
                    FormattableString.Invariant($"    {tag.Key} = {FormatTagValue(tag.Value)}"));
            }

            if (!hadTag)
            {
                output.WriteLine("    (no tags)");
            }
        }
    }

    private static List<Activity> MaterializeActivities(IEnumerable<Activity> activities,
        Func<Activity, bool>? predicate)
    {
        return activities?
                   .Where(a => predicate?.Invoke(a) ?? true)
                   .OrderBy(a => a.StartTimeUtc)
                   .ToList()
               ?? new List<Activity>();
    }

    private static IReadOnlyList<string> FormatLines(Activity root,
        IReadOnlyList<Activity> filtered,
        int width)
    {
        var reference = filtered.FirstOrDefault(a => string.Equals(a.DisplayName, "Program", StringComparison.Ordinal))
                         ?? filtered.FirstOrDefault()
                         ?? root;

        var rootStart = reference.StartTimeUtc;
        var rootDuration = GetEffectiveDuration(reference);

        var depths = CalculateDepths(filtered);
        var referenceDepth = depths.TryGetValue(reference, out var depth) ? depth : 0;

        return filtered.Select(activity =>
            FormatLine(activity,
                rootStart,
                rootDuration,
                width,
                referenceDepth,
                depths.TryGetValue(activity, out var activityDepth) ? activityDepth : 0)).ToList();
    }

    private static string FormatLine(Activity activity,
        DateTime rootStart,
        TimeSpan rootDuration,
        int width,
        int referenceDepth,
        int depth)
    {
        var duration = GetEffectiveDuration(activity);
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

        var baseName = ExtractDisplayName(activity.DisplayName);

        depth = Math.Max(0, depth - referenceDepth);
        var indent = depth == 0 ? string.Empty : new string(' ', depth);

        var label = FormattableString.Invariant($"{indent}{baseName}, {duration.TotalMilliseconds:F1}ms");
        const int labelWidth = 40;
        if (label.Length > labelWidth)
        {
            label = label[..labelWidth];
        }

        return $"{label.PadRight(labelWidth)} : {new string(buffer)}";
    }

    private static string ExtractDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "(unknown)";
        }

        var typeSeparator = displayName.IndexOf(':');
        if (typeSeparator >= 0 && typeSeparator + 1 < displayName.Length)
        {
            return displayName[(typeSeparator + 1)..].TrimStart();
        }

        return displayName.TrimStart();
    }

    private static Dictionary<Activity, int> CalculateDepths(IReadOnlyList<Activity> activities)
    {
        var depths = new Dictionary<Activity, int>();
        var stack = new Stack<(Activity activity, DateTime endTime)>();

        foreach (var activity in activities)
        {
            var effectiveEnd = activity.StartTimeUtc + GetEffectiveDuration(activity);

            while (stack.Count > 0 && stack.Peek().endTime <= activity.StartTimeUtc)
            {
                stack.Pop();
            }

            depths[activity] = stack.Count;
            stack.Push((activity, effectiveEnd));
        }

        return depths;
    }

    private static TimeSpan GetEffectiveDuration(Activity activity)
    {
        return activity.Duration > TimeSpan.Zero
            ? activity.Duration
            : TimeSpan.FromMilliseconds(0.1);
    }

    private static string FormatTagValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "<null>"
        };
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
