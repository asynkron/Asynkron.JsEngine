using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Asynkron.JsParser;

/// <summary>
/// Represents the kind of literal value in the AST.
/// </summary>
public enum LiteralValueKind
{
    Undefined = 0,
    Null = 1,
    Boolean = 2,
    Number = 3,
    String = 4,
    BigInt = 5
}

/// <summary>
/// A lightweight representation of JavaScript literal values for the AST.
/// This is separate from JsValue to keep the parser decoupled from the runtime.
/// </summary>
public readonly struct LiteralValue : IEquatable<LiteralValue>
{
    public readonly LiteralValueKind Kind;
    public readonly double NumberValue;
    public readonly object? ObjectValue;

    #region Static Singletons

    public static readonly LiteralValue Undefined = new(LiteralValueKind.Undefined, 0.0, null);
    public static readonly LiteralValue Null = new(LiteralValueKind.Null, 0.0, null);
    public static readonly LiteralValue True = new(LiteralValueKind.Boolean, 1.0, null);
    public static readonly LiteralValue False = new(LiteralValueKind.Boolean, 0.0, null);

    #endregion

    #region Constructors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralValue(LiteralValueKind kind, double numberValue, object? objectValue)
    {
        Kind = kind;
        NumberValue = numberValue;
        ObjectValue = objectValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiteralValue(double value)
    {
        Kind = LiteralValueKind.Number;
        NumberValue = value;
        ObjectValue = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiteralValue(bool value)
    {
        Kind = LiteralValueKind.Boolean;
        NumberValue = value ? 1.0 : 0.0;
        ObjectValue = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiteralValue(string value)
    {
        Kind = LiteralValueKind.String;
        NumberValue = 0.0;
        ObjectValue = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiteralValue(BigInteger value)
    {
        Kind = LiteralValueKind.BigInt;
        NumberValue = 0.0;
        ObjectValue = value;
    }

    #endregion

    #region Type Checks

    public bool IsUndefined
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == LiteralValueKind.Undefined;
    }

    public bool IsNull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == LiteralValueKind.Null;
    }

    public bool IsBoolean
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == LiteralValueKind.Boolean;
    }

    public bool IsNumber
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == LiteralValueKind.Number;
    }

    public bool IsString
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == LiteralValueKind.String;
    }

    public bool IsBigInt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == LiteralValueKind.BigInt;
    }

    #endregion

    #region Value Accessors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double AsDouble() => NumberValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AsBoolean() => NumberValue != 0.0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string AsString() => (string)ObjectValue!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BigInteger AsBigInt() => (BigInteger)ObjectValue!;

    #endregion

    #region Equality

    public bool Equals(LiteralValue other)
    {
        if (Kind != other.Kind)
            return false;

        return Kind switch
        {
            LiteralValueKind.Undefined => true,
            LiteralValueKind.Null => true,
            LiteralValueKind.Boolean => NumberValue == other.NumberValue,
            LiteralValueKind.Number => NumberValue.Equals(other.NumberValue),
            LiteralValueKind.String => string.Equals((string)ObjectValue!, (string)other.ObjectValue!, StringComparison.Ordinal),
            LiteralValueKind.BigInt => ((BigInteger)ObjectValue!).Equals((BigInteger)other.ObjectValue!),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is LiteralValue other && Equals(other);

    public override int GetHashCode()
    {
        return Kind switch
        {
            LiteralValueKind.Undefined => 0,
            LiteralValueKind.Null => 1,
            LiteralValueKind.Boolean => NumberValue != 0.0 ? 3 : 2,
            LiteralValueKind.Number => NumberValue.GetHashCode(),
            LiteralValueKind.String => ((string)ObjectValue!).GetHashCode(StringComparison.Ordinal),
            LiteralValueKind.BigInt => ObjectValue!.GetHashCode(),
            _ => 0
        };
    }

    public static bool operator ==(LiteralValue left, LiteralValue right) => left.Equals(right);
    public static bool operator !=(LiteralValue left, LiteralValue right) => !left.Equals(right);

    #endregion

    #region ToString

    public override string ToString()
    {
        return Kind switch
        {
            LiteralValueKind.Undefined => "undefined",
            LiteralValueKind.Null => "null",
            LiteralValueKind.Boolean => NumberValue != 0.0 ? "true" : "false",
            LiteralValueKind.Number => NumberValue.ToString(CultureInfo.InvariantCulture),
            LiteralValueKind.String => $"\"{ObjectValue}\"",
            LiteralValueKind.BigInt => $"{ObjectValue}n",
            _ => "undefined"
        };
    }

    #endregion

    #region Implicit Conversions

    public static implicit operator LiteralValue(double value) => new(value);
    public static implicit operator LiteralValue(bool value) => value ? True : False;
    public static implicit operator LiteralValue(string value) => new(value);
    public static implicit operator LiteralValue(BigInteger value) => new(value);

    #endregion
}
