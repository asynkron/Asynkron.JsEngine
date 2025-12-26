#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
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
        return (double)dv.GetInt8(offset);
    }

    [JsHostMethod("setInt8", Length = 2d)]
    public JsValue SetInt8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (sbyte)(int)JsOps.ToNumber(args[1]) : (sbyte)0;
        dv.SetInt8(offset, value);
        return JsValue.Undefined;
    }

    [JsHostMethod("getUint8", Length = 1d)]
    public JsValue GetUint8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        return (double)dv.GetUint8(offset);
    }

    [JsHostMethod("setUint8", Length = 2d)]
    public JsValue SetUint8(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (byte)(int)JsOps.ToNumber(args[1]) : (byte)0;
        dv.SetUint8(offset, value);
        return JsValue.Undefined;
    }

    [JsHostMethod("getInt16", Length = 1d)]
    public JsValue GetInt16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return (double)dv.GetInt16(offset, littleEndian);
    }

    [JsHostMethod("setInt16", Length = 2d)]
    public JsValue SetInt16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (short)(int)JsOps.ToNumber(args[1]) : (short)0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        dv.SetInt16(offset, value, littleEndian);
        return JsValue.Undefined;
    }

    [JsHostMethod("getUint16", Length = 1d)]
    public JsValue GetUint16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return (double)dv.GetUint16(offset, littleEndian);
    }

    [JsHostMethod("setUint16", Length = 2d)]
    public JsValue SetUint16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (ushort)(int)JsOps.ToNumber(args[1]) : (ushort)0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        dv.SetUint16(offset, value, littleEndian);
        return JsValue.Undefined;
    }

    [JsHostMethod("getInt32", Length = 1d)]
    public JsValue GetInt32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return (double)dv.GetInt32(offset, littleEndian);
    }

    [JsHostMethod("setInt32", Length = 2d)]
    public JsValue SetInt32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (int)JsOps.ToNumber(args[1]) : 0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        dv.SetInt32(offset, value, littleEndian);
        return JsValue.Undefined;
    }

    [JsHostMethod("getUint32", Length = 1d)]
    public JsValue GetUint32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return (double)dv.GetUint32(offset, littleEndian);
    }

    [JsHostMethod("setUint32", Length = 2d)]
    public JsValue SetUint32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (uint)JsOps.ToNumber(args[1]) : 0u;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        dv.SetUint32(offset, value, littleEndian);
        return JsValue.Undefined;
    }

    [JsHostMethod("getFloat32", Length = 1d)]
    public JsValue GetFloat32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return (double)dv.GetFloat32(offset, littleEndian);
    }

    [JsHostMethod("setFloat32", Length = 2d)]
    public JsValue SetFloat32(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (float)JsOps.ToNumber(args[1]) : 0f;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        dv.SetFloat32(offset, value, littleEndian);
        return JsValue.Undefined;
    }

    [JsHostMethod("getFloat64", Length = 1d)]
    public JsValue GetFloat64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1].IsTruthy;
        return dv.GetFloat64(offset, littleEndian);
    }

    [JsHostMethod("setFloat64", Length = 2d)]
    public JsValue SetFloat64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var dv = RequireInstance(thisValue);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0.0;
        var littleEndian = args.Count > 2 && args[2].IsTruthy;
        dv.SetFloat64(offset, value, littleEndian);
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("getBigInt64", Length = 1d)]
    public JsValue GetBigInt64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DataView.prototype.getBigInt64
        throw new NotImplementedException("DataView.prototype.getBigInt64 is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getBigUint64", Length = 1d)]
    public JsValue GetBigUint64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DataView.prototype.getBigUint64
        throw new NotImplementedException("DataView.prototype.getBigUint64 is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getFloat16", Length = 1d)]
    public JsValue GetFloat16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DataView.prototype.getFloat16
        throw new NotImplementedException("DataView.prototype.getFloat16 is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("setBigInt64", Length = 2d)]
    public JsValue SetBigInt64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DataView.prototype.setBigInt64
        throw new NotImplementedException("DataView.prototype.setBigInt64 is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("setBigUint64", Length = 2d)]
    public JsValue SetBigUint64(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DataView.prototype.setBigUint64
        throw new NotImplementedException("DataView.prototype.setBigUint64 is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("setFloat16", Length = 2d)]
    public JsValue SetFloat16(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DataView.prototype.setFloat16
        throw new NotImplementedException("DataView.prototype.setFloat16 is not yet implemented");
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DataViewPrototype ??= Prototype as JsObject;
    }
}
