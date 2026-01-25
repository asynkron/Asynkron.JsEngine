#region

using System.Numerics;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("DataView", ToStringTag = "DataView", InstanceType = typeof(JsDataView), TryGetMethod = "TryGetInternal")]
public sealed partial class DataViewPrototype
{
    [JsHostGetter("buffer")]
    public JsValue Buffer(JsValue thisValue)
    {
        var dv = RequireInstance(thisValue);
        return JsValue.FromObjectUnsafe(dv.Buffer);
    }

    [JsHostGetter("byteLength")]
    public JsValue ByteLength(JsValue thisValue)
    {
        var dv = RequireInstance(thisValue);
        return (double)dv.ByteLength;
    }

    [JsHostGetter("byteOffset")]
    public JsValue ByteOffset(JsValue thisValue)
    {
        var dv = RequireInstance(thisValue);
        return (double)dv.ByteOffset;
    }

    [JsHostMethod("getInt8", Length = 1d)]
    public JsValue GetInt8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        return WithRangeError(() => (double)dv.GetInt8(offset));
    }

    [JsHostMethod("setInt8", Length = 2d)]
    public JsValue SetInt8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (sbyte)(int)JsOps.ToNumber(args[1]) : (sbyte)0;
        return WithRangeError(() =>
        {
            dv.SetInt8(offset, value);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getUint8", Length = 1d)]
    public JsValue GetUint8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        return WithRangeError(() => (double)dv.GetUint8(offset));
    }

    [JsHostMethod("setUint8", Length = 2d)]
    public JsValue SetUint8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (byte)(int)JsOps.ToNumber(args[1]) : (byte)0;
        return WithRangeError(() =>
        {
            dv.SetUint8(offset, value);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getInt16", Length = 1d)]
    public JsValue GetInt16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() => (double)dv.GetInt16(offset, littleEndian));
    }

    [JsHostMethod("setInt16", Length = 2d)]
    public JsValue SetInt16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (short)(int)JsOps.ToNumber(args[1]) : (short)0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetInt16(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getUint16", Length = 1d)]
    public JsValue GetUint16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() => (double)dv.GetUint16(offset, littleEndian));
    }

    [JsHostMethod("setUint16", Length = 2d)]
    public JsValue SetUint16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (ushort)(int)JsOps.ToNumber(args[1]) : (ushort)0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetUint16(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getInt32", Length = 1d)]
    public JsValue GetInt32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() => (double)dv.GetInt32(offset, littleEndian));
    }

    [JsHostMethod("setInt32", Length = 2d)]
    public JsValue SetInt32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (int)JsOps.ToNumber(args[1]) : 0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetInt32(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getUint32", Length = 1d)]
    public JsValue GetUint32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() => (double)dv.GetUint32(offset, littleEndian));
    }

    [JsHostMethod("setUint32", Length = 2d)]
    public JsValue SetUint32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (uint)JsOps.ToNumber(args[1]) : 0u;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetUint32(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getFloat32", Length = 1d)]
    public JsValue GetFloat32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? ToIndex(args[0], Realm) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() => (double)dv.GetFloat32(offset, littleEndian));
    }

    [JsHostMethod("setFloat32", Length = 2d)]
    public JsValue SetFloat32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (float)JsOps.ToNumber(args[1]) : 0f;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetFloat32(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getFloat64", Length = 1d)]
    public JsValue GetFloat64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() => dv.GetFloat64(offset, littleEndian));
    }

    [JsHostMethod("setFloat64", Length = 2d)]
    public JsValue SetFloat64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0.0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetFloat64(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("getBigInt64", Length = 1d)]
    public JsValue GetBigInt64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? ToIndex(args[0], Realm) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() =>
        {
            var value = dv.GetBigInt64(offset, littleEndian);
            // Convert to BigInt JsValue for JavaScript BigInt semantics.
            return JsValue.FromObjectUnsafe(new JsBigInt(value));
        });
    }

    [JsHostMethod("getBigUint64", Length = 1d)]
    public JsValue GetBigUint64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? ToIndex(args[0], Realm) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() =>
        {
            var value = dv.GetBigUint64(offset, littleEndian);
            // Preserve full unsigned range by constructing BigInteger directly.
            return JsValue.FromObjectUnsafe(new JsBigInt(new BigInteger(value)));
        });
    }

    [JsHostMethod("getFloat16", Length = 1d)]
    public JsValue GetFloat16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? ToIndex(args[0], Realm) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return WithRangeError(() =>
        {
            var value = dv.GetFloat16(offset, littleEndian);
            // Convert Half to double for JavaScript.
            return (double)value;
        });
    }

    [JsHostMethod("setBigInt64", Length = 2d)]
    public JsValue SetBigInt64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;

        // Convert to BigInt and wrap to signed 64-bit as the spec expects.
        var value = args.Count > 1 ? ToBigInt64(ToBigInt(args[1], realmState: Realm).Value) : 0L;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetBigInt64(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("setBigUint64", Length = 2d)]
    public JsValue SetBigUint64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;

        // Convert to BigInt and wrap to unsigned 64-bit as the spec expects.
        var value = args.Count > 1 ? ToBigUint64(ToBigInt(args[1], realmState: Realm).Value) : 0UL;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetBigUint64(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    [JsHostMethod("setFloat16", Length = 2d)]
    public JsValue SetFloat16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (Half)JsOps.ToNumber(args[1]) : (Half)0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        return WithRangeError(() =>
        {
            dv.SetFloat16(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DataViewPrototype ??= Prototype as JsObject;
    }

    private JsValue WithRangeError(Func<JsValue> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Align with JS: DataView bounds checks throw RangeError.
            throw ThrowRangeError("Offset is outside the bounds of the DataView", realm: Realm);
        }
    }
}
