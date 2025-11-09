using System.Globalization;
using System.Numerics;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript BigInt primitive type.
/// BigInt is an arbitrary precision integer that can represent integers beyond Number.MAX_SAFE_INTEGER.
/// </summary>
public sealed class JsBigInt : IEquatable<JsBigInt>
{
    public BigInteger Value { get; }

    public JsBigInt(BigInteger value)
    {
        Value = value;
    }

    public JsBigInt(long value) : this(new BigInteger(value))
    {
    }

    public JsBigInt(string value) : this(BigInteger.Parse(value, CultureInfo.InvariantCulture))
    {
    }

    public static JsBigInt Zero => new(BigInteger.Zero);
    public static JsBigInt One => new(BigInteger.One);
    public static JsBigInt MinusOne => new(BigInteger.MinusOne);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public override bool Equals(object? obj) => obj is JsBigInt other && Equals(other);

    public bool Equals(JsBigInt? other) => other is not null && Value.Equals(other.Value);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(JsBigInt? left, JsBigInt? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Value == right.Value;
    }

    public static bool operator !=(JsBigInt? left, JsBigInt? right) => !(left == right);

    public static bool operator <(JsBigInt left, JsBigInt right) => left.Value < right.Value;

    public static bool operator <=(JsBigInt left, JsBigInt right) => left.Value <= right.Value;

    public static bool operator >(JsBigInt left, JsBigInt right) => left.Value > right.Value;

    public static bool operator >=(JsBigInt left, JsBigInt right) => left.Value >= right.Value;

    // Arithmetic operators
    public static JsBigInt operator +(JsBigInt left, JsBigInt right) => new(left.Value + right.Value);

    public static JsBigInt operator -(JsBigInt left, JsBigInt right) => new(left.Value - right.Value);

    public static JsBigInt operator *(JsBigInt left, JsBigInt right) => new(left.Value * right.Value);

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

    public static JsBigInt operator -(JsBigInt operand) => new(-operand.Value);

    // Bitwise operators
    public static JsBigInt operator &(JsBigInt left, JsBigInt right) => new(left.Value & right.Value);

    public static JsBigInt operator |(JsBigInt left, JsBigInt right) => new(left.Value | right.Value);

    public static JsBigInt operator ^(JsBigInt left, JsBigInt right) => new(left.Value ^ right.Value);

    public static JsBigInt operator ~(JsBigInt operand) => new(~operand.Value);

    public static JsBigInt operator <<(JsBigInt left, int right)
    {
        // JavaScript behavior: negative shifts shift in the opposite direction
        if (right < 0)
        {
            return left >> -right;
        }
        return new JsBigInt(left.Value << right);
    }

    public static JsBigInt operator >>(JsBigInt left, int right)
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
