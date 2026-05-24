#region

using System.Collections.Generic;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Equality comparer implementing SameValueZero algorithm for Map/Set values.
///     Similar to strict equality (===) but treats NaN as equal to NaN.
/// </summary>
internal sealed class SameValueZeroComparer : IEqualityComparer<object>
{
    public static readonly SameValueZeroComparer Instance = new();
    public static readonly JsValueSameValueZeroComparer JsValueInstance = new();

    private SameValueZeroComparer()
    {
    }

    public new bool Equals(object? x, object? y)
    {
        // Handle null (shouldn't happen - we handle null/undefined separately)
        if (x == null && y == null)
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        // Handle NaN (NaN is equal to NaN in SameValueZero)
        if (x is double and double.NaN && y is double and double.NaN)
        {
            return true;
        }

        // Handle strings - use ordinal value equality (JavaScript semantics)
        if (x is string sx && y is string sy)
        {
            return string.Equals(sx, sy, StringComparison.Ordinal);
        }

        // For reference types, use reference equality
        if (!x.GetType().IsValueType || !y.GetType().IsValueType)
        {
            return ReferenceEquals(x, y);
        }

        // For value types, use Equals
        return x.Equals(y);
    }

    public int GetHashCode(object obj)
    {
        // Handle NaN - all NaN values should hash the same
        if (obj is double and double.NaN)
        {
            return 0; // All NaN values get the same hash
        }

        return obj.GetHashCode();
    }

    internal sealed class JsValueSameValueZeroComparer : IEqualityComparer<JsValue>
    {
        public bool Equals(JsValue x, JsValue y)
        {
            if (x.Kind != y.Kind)
            {
                return false;
            }

            return x.Kind switch
            {
                JsValueKind.Undefined => true,
                JsValueKind.Null => true,
                JsValueKind.Number => EqualNumbers(x.NumberValue, y.NumberValue),
                JsValueKind.String => string.Equals(x.AsString(), y.AsString(), StringComparison.Ordinal),
                JsValueKind.Symbol or JsValueKind.Object => ReferenceEquals(x.ObjectValue, y.ObjectValue),
                _ => x.Equals(y)
            };
        }

        public int GetHashCode(JsValue obj)
        {
            return obj.Kind switch
            {
                JsValueKind.Undefined => 0,
                JsValueKind.Null => 1,
                JsValueKind.Number => GetNumberHashCode(obj.NumberValue),
                JsValueKind.String => obj.AsString().GetHashCode(StringComparison.Ordinal),
                JsValueKind.Symbol or JsValueKind.Object => obj.ObjectValue?.GetHashCode() ?? 0,
                _ => obj.GetHashCode()
            };
        }

        private static bool EqualNumbers(double x, double y)
        {
            if (double.IsNaN(x) && double.IsNaN(y))
            {
                return true;
            }

            if (x == 0.0 && y == 0.0)
            {
                return true;
            }

            return x.Equals(y);
        }

        private static int GetNumberHashCode(double number)
        {
            if (double.IsNaN(number))
            {
                return 0;
            }

            if (number == 0.0)
            {
                return 1;
            }

            return number.GetHashCode();
        }
    }
}
