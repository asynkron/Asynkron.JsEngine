using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("DataView", ToStringTag = "DataView")]
public sealed partial class DataViewPrototype : JsPrototype
{
    [JsHostGetter("buffer")]
    public object Buffer(object? thisValue)
    {
        var dv = RequireDataView(thisValue, Realm);
        return dv.Buffer;
    }

    [JsHostGetter("byteLength")]
    public object ByteLength(object? thisValue)
    {
        var dv = RequireDataView(thisValue, Realm);
        return (double)dv.ByteLength;
    }

    [JsHostGetter("byteOffset")]
    public object ByteOffset(object? thisValue)
    {
        var dv = RequireDataView(thisValue, Realm);
        return (double)dv.ByteOffset;
    }

    [JsHostMethod("getInt8", Length = 1d)]
    public object GetInt8(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        return (double)dv.GetInt8(offset);
    }

    [JsHostMethod("setInt8", Length = 2d)]
    public object SetInt8(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (sbyte)(int)JsOps.ToNumber(args[1]) : (sbyte)0;
        dv.SetInt8(offset, value);
        return Symbol.Undefined;
    }

    [JsHostMethod("getUint8", Length = 1d)]
    public object GetUint8(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        return (double)dv.GetUint8(offset);
    }

    [JsHostMethod("setUint8", Length = 2d)]
    public object SetUint8(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (byte)(int)JsOps.ToNumber(args[1]) : (byte)0;
        dv.SetUint8(offset, value);
        return Symbol.Undefined;
    }

    [JsHostMethod("getInt16", Length = 1d)]
    public object GetInt16(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1] is bool and true;
        return (double)dv.GetInt16(offset, littleEndian);
    }

    [JsHostMethod("setInt16", Length = 2d)]
    public object SetInt16(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (short)(int)JsOps.ToNumber(args[1]) : (short)0;
        var littleEndian = args.Count > 2 && args[2] is bool and true;
        dv.SetInt16(offset, value, littleEndian);
        return Symbol.Undefined;
    }

    [JsHostMethod("getUint16", Length = 1d)]
    public object GetUint16(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1] is bool and true;
        return (double)dv.GetUint16(offset, littleEndian);
    }

    [JsHostMethod("setUint16", Length = 2d)]
    public object SetUint16(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (ushort)(int)JsOps.ToNumber(args[1]) : (ushort)0;
        var littleEndian = args.Count > 2 && args[2] is bool and true;
        dv.SetUint16(offset, value, littleEndian);
        return Symbol.Undefined;
    }

    [JsHostMethod("getInt32", Length = 1d)]
    public object GetInt32(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1] is bool and true;
        return (double)dv.GetInt32(offset, littleEndian);
    }

    [JsHostMethod("setInt32", Length = 2d)]
    public object SetInt32(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (int)JsOps.ToNumber(args[1]) : 0;
        var littleEndian = args.Count > 2 && args[2] is bool and true;
        dv.SetInt32(offset, value, littleEndian);
        return Symbol.Undefined;
    }

    [JsHostMethod("getUint32", Length = 1d)]
    public object GetUint32(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1] is bool and true;
        return (double)dv.GetUint32(offset, littleEndian);
    }

    [JsHostMethod("setUint32", Length = 2d)]
    public object SetUint32(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (uint)JsOps.ToNumber(args[1]) : 0u;
        var littleEndian = args.Count > 2 && args[2] is bool and true;
        dv.SetUint32(offset, value, littleEndian);
        return Symbol.Undefined;
    }

    [JsHostMethod("getFloat32", Length = 1d)]
    public object GetFloat32(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1] is bool and true;
        return (double)dv.GetFloat32(offset, littleEndian);
    }

    [JsHostMethod("setFloat32", Length = 2d)]
    public object SetFloat32(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? (float)JsOps.ToNumber(args[1]) : 0f;
        var littleEndian = args.Count > 2 && args[2] is bool and true;
        dv.SetFloat32(offset, value, littleEndian);
        return Symbol.Undefined;
    }

    [JsHostMethod("getFloat64", Length = 1d)]
    public object GetFloat64(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var littleEndian = args.Count > 1 && args[1] is bool and true;
        return dv.GetFloat64(offset, littleEndian);
    }

    [JsHostMethod("setFloat64", Length = 2d)]
    public object SetFloat64(object? thisValue, IReadOnlyList<object?> args)
    {
        var dv = RequireDataView(thisValue, Realm);
        var offset = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        var value = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0.0;
        var littleEndian = args.Count > 2 && args[2] is bool and true;
        dv.SetFloat64(offset, value, littleEndian);
        return Symbol.Undefined;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DataViewPrototype ??= Prototype as JsObject;
    }
}
