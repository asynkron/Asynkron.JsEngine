using System.Globalization;

namespace Asynkron.JsEngine.Evaluators;

internal static class PropertyAccessEvaluator
{
    internal static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        switch (target)
        {
            case JsArray jsArray when jsArray.TryGetProperty(propertyName, out value):
                return true;
            case TypedArrayBase typedArray:
                return TryGetTypedArrayProperty(typedArray, propertyName, out value);
            case JsArrayBuffer buffer:
                return TryGetArrayBufferProperty(buffer, propertyName, out value);
            case JsDataView dataView:
                return TryGetDataViewProperty(dataView, propertyName, out value);
            case JsMap jsMap:
                return TryGetMapProperty(jsMap, propertyName, out value);
            case JsSet jsSet:
                return TryGetSetProperty(jsSet, propertyName, out value);
            case JsWeakMap jsWeakMap:
                return jsWeakMap.TryGetProperty(propertyName, out value);
            case JsWeakSet jsWeakSet:
                return jsWeakSet.TryGetProperty(propertyName, out value);
            case JsObject jsObject:
                return TryGetObjectProperty(jsObject, propertyName, out value);
            case JsFunction function when function.TryGetProperty(propertyName, out value):
            case HostFunction hostFunction when hostFunction.TryGetProperty(propertyName, out value):
            case IDictionary<string, object?> dictionary when dictionary.TryGetValue(propertyName, out value):
                return true;
            case double num:
                return TryGetNumberProperty(num, propertyName, out value);
            case string str:
                return TryGetStringProperty(str, propertyName, out value);
        }

        value = null;
        return false;
    }

    internal static void AssignPropertyValue(object? target, string propertyName, object? value)
    {
        switch (target)
        {
            case JsArray jsArray:
                jsArray.SetProperty(propertyName, value);
                break;
            case JsObject jsObject:
                // Check for setter first
                var setter = jsObject.GetSetter(propertyName);
                if (setter != null)
                {
                    setter.Invoke([value], jsObject);
                }
                else
                {
                    jsObject.SetProperty(propertyName, value);
                }

                break;
            case JsFunction function:
                function.SetProperty(propertyName, value);
                break;
            case HostFunction hostFunction:
                hostFunction.SetProperty(propertyName, value);
                break;
            case IDictionary<string, object?> dictionary:
                dictionary[propertyName] = value;
                break;
            default:
                throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
        }
    }

    private static bool TryGetTypedArrayProperty(TypedArrayBase typedArray, string propertyName, out object? value)
    {
        switch (propertyName)
        {
            case "length":
                value = (double)typedArray.Length;
                return true;
            case "byteLength":
                value = (double)typedArray.ByteLength;
                return true;
            case "byteOffset":
                value = (double)typedArray.ByteOffset;
                return true;
            case "buffer":
                value = typedArray.Buffer;
                return true;
            case "BYTES_PER_ELEMENT":
                value = (double)typedArray.BytesPerElement;
                return true;
            case "subarray":
                value = new HostFunction(args =>
                {
                    var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : typedArray.Length;
                    return typedArray.Subarray(begin, end);
                });
                return true;
            case "set":
                value = new HostFunction(args =>
                {
                    if (args.Count == 0)
                    {
                        return JsSymbols.Undefined;
                    }

                    var offset = args.Count > 1 && args[1] is double d ? (int)d : 0;

                    switch (args[0])
                    {
                        case TypedArrayBase sourceTypedArray:
                            typedArray.Set(sourceTypedArray, offset);
                            break;
                        case JsArray sourceArray:
                            typedArray.Set(sourceArray, offset);
                            break;
                    }

                    return JsSymbols.Undefined;
                });
                return true;
            case "slice":
                value = CreateTypedArraySliceMethod(typedArray);
                return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetArrayBufferProperty(JsArrayBuffer buffer, string propertyName, out object? value)
    {
        switch (propertyName)
        {
            case "byteLength":
                value = (double)buffer.ByteLength;
                return true;
            case "slice":
                value = new HostFunction(args =>
                {
                    var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : buffer.ByteLength;
                    return buffer.Slice(begin, end);
                });
                return true;
            default:
                value = null;
                return false;
        }
    }

    private static bool TryGetDataViewProperty(JsDataView dataView, string propertyName, out object? value)
    {
        switch (propertyName)
        {
            case "buffer":
                value = dataView.Buffer;
                return true;
            case "byteLength":
                value = (double)dataView.ByteLength;
                return true;
            case "byteOffset":
                value = (double)dataView.ByteOffset;
                return true;
            case "getInt8":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    return (double)dataView.GetInt8(offset);
                });
                return true;
            case "setInt8":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (sbyte)(int)d2 : (sbyte)0;
                    dataView.SetInt8(offset, val);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getUint8":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    return (double)dataView.GetUint8(offset);
                });
                return true;
            case "setUint8":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (byte)(int)d2 : (byte)0;
                    dataView.SetUint8(offset, val);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getInt16":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    var littleEndian = args.Count > 1 && args[1] is bool and true;
                    return (double)dataView.GetInt16(offset, littleEndian);
                });
                return true;
            case "setInt16":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (short)(int)d2 : (short)0;
                    var littleEndian = args.Count > 2 && args[2] is bool and true;
                    dataView.SetInt16(offset, val, littleEndian);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getUint16":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    var littleEndian = args.Count > 1 && args[1] is bool and true;
                    return (double)dataView.GetUint16(offset, littleEndian);
                });
                return true;
            case "setUint16":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (ushort)(int)d2 : (ushort)0;
                    var littleEndian = args.Count > 2 && args[2] is bool and true;
                    dataView.SetUint16(offset, val, littleEndian);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getInt32":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    var littleEndian = args.Count > 1 && args[1] is bool and true;
                    return (double)dataView.GetInt32(offset, littleEndian);
                });
                return true;
            case "setInt32":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (int)d2 : 0;
                    var littleEndian = args.Count > 2 && args[2] is bool and true;
                    dataView.SetInt32(offset, val, littleEndian);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getUint32":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    var littleEndian = args.Count > 1 && args[1] is bool and true;
                    return (double)dataView.GetUint32(offset, littleEndian);
                });
                return true;
            case "setUint32":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (uint)d2 : 0;
                    var littleEndian = args.Count > 2 && args[2] is bool and true;
                    dataView.SetUint32(offset, val, littleEndian);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getFloat32":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    var littleEndian = args.Count > 1 && args[1] is bool and true;
                    return (double)dataView.GetFloat32(offset, littleEndian);
                });
                return true;
            case "setFloat32":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? (float)d2 : 0f;
                    var littleEndian = args.Count > 2 && args[2] is bool and true;
                    dataView.SetFloat32(offset, val, littleEndian);
                    return JsSymbols.Undefined;
                });
                return true;
            case "getFloat64":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                    var littleEndian = args.Count > 1 && args[1] is bool and true;
                    return dataView.GetFloat64(offset, littleEndian);
                });
                return true;
            case "setFloat64":
                value = new HostFunction(args =>
                {
                    var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                    var val = args.Count > 1 && args[1] is double d2 ? d2 : 0.0;
                    var littleEndian = args.Count > 2 && args[2] is bool and true;
                    dataView.SetFloat64(offset, val, littleEndian);
                    return JsSymbols.Undefined;
                });
                return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetMapProperty(JsMap jsMap, string propertyName, out object? value)
    {
        switch (propertyName)
        {
            case "size":
                value = (double)jsMap.Size;
                return true;
            default:
                return jsMap.TryGetProperty(propertyName, out value);
        }
    }

    private static bool TryGetSetProperty(JsSet jsSet, string propertyName, out object? value)
    {
        switch (propertyName)
        {
            case "size":
                value = (double)jsSet.Size;
                return true;
            default:
                return jsSet.TryGetProperty(propertyName, out value);
        }
    }

    private static bool TryGetObjectProperty(JsObject jsObject, string propertyName, out object? value)
    {
        // Check for getter first
        var getter = jsObject.GetGetter(propertyName);
        if (getter == null)
        {
            return jsObject.TryGetProperty(propertyName, out value);
        }

        value = getter.Invoke([], jsObject);
        return true;
    }

    private static bool TryGetNumberProperty(double num, string propertyName, out object? value)
    {
        // Handle number properties (Number.prototype methods)
        var numberWrapper = StandardLibrary.CreateNumberWrapper(num);
        if (numberWrapper.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetStringProperty(string str, string propertyName, out object? value)
    {
        // Handle string properties
        if (propertyName == "length")
        {
            value = (double)str.Length;
            return true;
        }

        // Handle numeric indices (bracket notation: str[0], str[1], etc.)
        if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 && index < str.Length)
        {
            value = str[index].ToString();
            return true;
        }

        // For string methods, create a wrapper object with methods
        var stringWrapper = StandardLibrary.CreateStringWrapper(str);
        if (stringWrapper.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static HostFunction CreateTypedArraySliceMethod(TypedArrayBase typedArray)
    {
        return new HostFunction(args =>
        {
            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : typedArray.Length;

            return typedArray switch
            {
                JsInt8Array arr => arr.Slice(begin, end),
                JsUint8Array arr => arr.Slice(begin, end),
                JsUint8ClampedArray arr => arr.Slice(begin, end),
                JsInt16Array arr => arr.Slice(begin, end),
                JsUint16Array arr => arr.Slice(begin, end),
                JsInt32Array arr => arr.Slice(begin, end),
                JsUint32Array arr => arr.Slice(begin, end),
                JsFloat32Array arr => arr.Slice(begin, end),
                JsFloat64Array arr => arr.Slice(begin, end),
                _ => throw new InvalidOperationException($"Unknown typed array type: {typedArray.GetType()}")
            };
        });
    }
}
