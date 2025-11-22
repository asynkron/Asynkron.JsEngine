using System.Globalization;
using System.Numerics;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript BigInt primitive type.
///     BigInt is an arbitrary precision integer that can represent integers beyond Number.MAX_SAFE_INTEGER.
/// </summary>
public sealed class JsBigInt(BigInteger value) : IEquatable<JsBigInt>
{
    public JsBigInt(long value) : this(new BigInteger(value))
    {
    }

    public JsBigInt(string value) : this(BigInteger.Parse(value.Replace("_", string.Empty),
        CultureInfo.InvariantCulture))
    {
    }

    public BigInteger Value { get; } = value;

    public static JsBigInt Zero => new(BigInteger.Zero);
    public static JsBigInt One => new(BigInteger.One);
    public static JsBigInt MinusOne => new(BigInteger.MinusOne);

    public bool Equals(JsBigInt? other)
    {
        return other is not null && Value.Equals(other.Value);
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsBigInt other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(JsBigInt? left, JsBigInt? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Value == right.Value;
    }

    public static bool operator !=(JsBigInt? left, JsBigInt? right)
    {
        return !(left == right);
    }

    public static bool operator <(JsBigInt left, JsBigInt right)
    {
        return left.Value < right.Value;
    }

    public static bool operator <=(JsBigInt left, JsBigInt right)
    {
        return left.Value <= right.Value;
    }

    public static bool operator >(JsBigInt left, JsBigInt right)
    {
        return left.Value > right.Value;
    }

    public static bool operator >=(JsBigInt left, JsBigInt right)
    {
        return left.Value >= right.Value;
    }

    // Arithmetic operators
    public static JsBigInt operator +(JsBigInt left, JsBigInt right)
    {
        return new JsBigInt(left.Value + right.Value);
    }

    public static JsBigInt operator -(JsBigInt left, JsBigInt right)
    {
        return new JsBigInt(left.Value - right.Value);
    }

    public static JsBigInt operator *(JsBigInt left, JsBigInt right)
    {
        return new JsBigInt(left.Value * right.Value);
    }

    public static JsBigInt operator /(JsBigInt left, JsBigInt right)
    {
        if (right.Value == BigInteger.Zero)
        {
            throw new DivideByZeroException("Division by zero");
        }

        return new JsBigInt(left.Value / right.Value);
    }

    public static JsBigInt operator %(JsBigInt left, JsBigInt right)
    {
        if (right.Value == BigInteger.Zero)
        {
            throw new DivideByZeroException("Division by zero");
        }

        return new JsBigInt(left.Value % right.Value);
    }

    public static JsBigInt operator -(JsBigInt operand)
    {
        return new JsBigInt(-operand.Value);
    }

    // Bitwise operators
    public static JsBigInt operator &(JsBigInt left, JsBigInt right)
    {
        return new JsBigInt(left.Value & right.Value);
    }

    public static JsBigInt operator |(JsBigInt left, JsBigInt right)
    {
        return new JsBigInt(left.Value | right.Value);
    }

    public static JsBigInt operator ^(JsBigInt left, JsBigInt right)
    {
        return new JsBigInt(left.Value ^ right.Value);
    }

    public static JsBigInt operator ~(JsBigInt operand)
    {
        return new JsBigInt(~operand.Value);
    }

    public static JsBigInt operator <<(JsBigInt left, int right)
    {
        // JavaScript behavior: negative shifts shift in the opposite direction
        if (right < 0)
        {
            return left >> -right;
        }

        return new JsBigInt(left.Value << right);
    }

    public static JsBigInt operator >> (JsBigInt left, int right)
    {
        // JavaScript behavior: negative shifts shift in the opposite direction
        if (right < 0)
        {
            return left << -right;
        }

        return new JsBigInt(left.Value >> right);
    }

    public static JsBigInt Pow(JsBigInt baseValue, JsBigInt exponent)
    {
        if (exponent.Value < 0)
        {
            throw new InvalidOperationException("Exponent must be non-negative for BigInt");
        }

        // BigInteger.Pow only accepts int, so we need to check the range
        if (exponent.Value > int.MaxValue)
        {
            throw new InvalidOperationException("Exponent is too large");
        }

        return new JsBigInt(BigInteger.Pow(baseValue.Value, (int)exponent.Value));
    }
}
