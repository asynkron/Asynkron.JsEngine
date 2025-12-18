using System.Runtime.CompilerServices;
using System.Text;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// A rope-based string implementation for efficient string concatenation.
/// Instead of creating a new string for each concatenation (O(n²) for loops),
/// this creates a binary tree of string fragments that is flattened only when
/// the actual string content is needed.
/// </summary>
public sealed class JsRopeString
{
    // Threshold: if total length is small, just concatenate directly
    private const int DirectConcatThreshold = 64;

    // Maximum tree depth before we force flattening to avoid stack overflow
    private const int MaxDepth = 32;

    private readonly object _left;   // string or JsRopeString
    private readonly object _right;  // string or JsRopeString
    private readonly int _length;
    private readonly int _depth;
    private string? _cached;

    private JsRopeString(object left, object right, int length, int depth)
    {
        _left = left;
        _right = right;
        _length = length;
        _depth = depth;
    }

    /// <summary>
    /// Gets the total length of the string this rope represents.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    /// <summary>
    /// Concatenates two string values, returning either a rope or a direct string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object Concat(object left, object right)
    {
        var leftLen = GetLength(left);
        var rightLen = GetLength(right);
        var totalLen = leftLen + rightLen;

        // For small strings, direct concatenation is faster due to no tree overhead
        if (totalLen <= DirectConcatThreshold)
        {
            return GetString(left) + GetString(right);
        }

        var leftDepth = GetDepth(left);
        var rightDepth = GetDepth(right);
        var newDepth = Math.Max(leftDepth, rightDepth) + 1;

        // If tree is getting too deep, flatten to avoid stack issues
        if (newDepth > MaxDepth)
        {
            return GetString(left) + GetString(right);
        }

        return new JsRopeString(left, right, totalLen, newDepth);
    }

    /// <summary>
    /// Gets the flattened string value, caching the result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Flatten()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        return FlattenSlow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private string FlattenSlow()
    {
        var sb = new StringBuilder(_length);
        AppendTo(sb);
        _cached = sb.ToString();
        return _cached;
    }

    private void AppendTo(StringBuilder sb)
    {
        // Use an explicit stack to avoid recursion for deep trees
        var stack = new Stack<object>();
        stack.Push(_right);
        stack.Push(_left);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            switch (node)
            {
                case string s:
                    sb.Append(s);
                    break;
                case JsRopeString rope:
                    if (rope._cached is not null)
                    {
                        sb.Append(rope._cached);
                    }
                    else
                    {
                        // Push right first so left is processed first (LIFO)
                        stack.Push(rope._right);
                        stack.Push(rope._left);
                    }
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetLength(object value)
    {
        return value switch
        {
            string s => s.Length,
            JsRopeString rope => rope._length,
            _ => 0
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDepth(object value)
    {
        return value switch
        {
            JsRopeString rope => rope._depth,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the string value from either a raw string or a rope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetString(object value)
    {
        return value switch
        {
            string s => s,
            JsRopeString rope => rope.Flatten(),
            _ => string.Empty
        };
    }

    public override string ToString() => Flatten();
}
