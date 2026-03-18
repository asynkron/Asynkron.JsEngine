#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.BigIntHelper;
using static Asynkron.JsEngine.StdLib.BooleanHelper;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StringHelper;
using static Asynkron.JsEngine.StdLib.SymbolHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static bool TryGetObject(object? candidate, RealmState realm, out IJsObjectLike accessor)
    {
        while (true)
        {
            switch (candidate)
            {
                case null:
                case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                    accessor = null!;
                    return false;
                case JsValue jsValue:
                    // Delegate to the JsValue-specific overload which handles all kinds properly
                    return TryGetObject(jsValue, realm, out accessor);
                case IJsObjectLike objectLike:
                    accessor = objectLike;
                    return true;
                case JsSymbol symbol:
                    accessor = CreateSymbolWrapper(symbol, realm: realm);
                    return true;
                case bool b:
                    accessor = CreateBooleanWrapper(b, realm: realm);
                    return true;
                case string s:
                    accessor = CreateStringWrapper(s, realm: realm);
                    return true;
                case JsBigInt bigInt:
                    accessor = CreateBigIntWrapper(bigInt, realm: realm);
                    return true;
                case double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte:
                    accessor = CreateNumberWrapper(Convert.ToDouble(candidate, CultureInfo.InvariantCulture), realm: realm);
                    return true;
                default:
                    accessor = null!;
                    return false;
            }
        }
    }

    /// <summary>
    /// JsValue overload for TryGetObject. Handles JsValue kinds directly without boxing.
    /// </summary>
    internal static bool TryGetObject(JsValue candidate, RealmState? realm, out IJsObjectLike accessor)
    {
        switch (candidate.Kind)
        {
            case JsValueKind.Undefined:
            case JsValueKind.Null:
                accessor = null!;
                return false;
            case JsValueKind.Boolean:
                accessor = CreateBooleanWrapper(candidate.NumberValue != 0, realm: realm);
                return true;
            case JsValueKind.Number:
                accessor = CreateNumberWrapper(candidate.NumberValue, realm: realm);
                return true;
            case JsValueKind.String:
                var strValue = candidate.ObjectValue switch
                {
                    string s => s,
                    JsRopeString rope => rope.Flatten(),
                    _ => string.Empty
                };
                accessor = CreateStringWrapper(strValue, realm: realm);
                return true;
            case JsValueKind.Symbol:
                if (candidate.ObjectValue is JsSymbol symbol)
                {
                    accessor = CreateSymbolWrapper(symbol, realm: realm);
                    return true;
                }

                accessor = null!;
                return false;
            case JsValueKind.BigInt:
                if (candidate.ObjectValue is JsBigInt bigInt)
                {
                    accessor = CreateBigIntWrapper(bigInt, realm: realm);
                    return true;
                }

                accessor = null!;
                return false;
            case JsValueKind.Object:
                if (candidate.ObjectValue is IJsObjectLike objectLike)
                {
                    accessor = objectLike;
                    return true;
                }

                accessor = null!;
                return false;
            default:
                accessor = null!;
                return false;
        }
    }

    internal static bool HasProperty(IJsPropertyAccessor accessor, string propertyKey)
    {
        switch (accessor)
        {
            case JsProxy proxy:
                return proxy.HasProperty(propertyKey);
            case TypedArrayBase typedArray:
                return typedArray.HasProperty(propertyKey);
            case JsObject jsObject:
                return jsObject.HasProperty(propertyKey);
            case IJsObjectLike objectLike:
                if (objectLike.GetOwnPropertyDescriptor(propertyKey) is not null)
                {
                    return true;
                }

                // Get prototype via PrototypeAccessor to handle non-JsObject prototypes like JsProxy
                object? currentProto = objectLike is IPrototypeAccessorProvider { PrototypeAccessor: { } protoAccessor }
                    ? protoAccessor
                    : objectLike.Prototype;

                while (currentProto is not null)
                {
                    switch (currentProto)
                    {
                        case JsProxy protoProxy:
                            // Proxy prototype - delegate to its HasProperty which invokes the 'has' trap
                            return protoProxy.HasProperty(propertyKey);
                        case JsObject jsProto:
                            if (jsProto.HasProperty(propertyKey))
                            {
                                return true;
                            }
                            // Move to next prototype
                            currentProto = jsProto is IPrototypeAccessorProvider { PrototypeAccessor: { } nextAccessor }
                                ? nextAccessor
                                : jsProto.Prototype;
                            break;
                        case IJsObjectLike objLikeProto:
                            if (objLikeProto.GetOwnPropertyDescriptor(propertyKey) is not null)
                            {
                                return true;
                            }
                            currentProto = objLikeProto is IPrototypeAccessorProvider { PrototypeAccessor: { } nextObjAccessor }
                                ? nextObjAccessor
                                : objLikeProto.Prototype;
                            break;
                        default:
                            // Non-IJsObjectLike - fall back to TryGetProperty
                            if (currentProto is IJsPropertyAccessor propAccessor &&
                                propAccessor.TryGetProperty(propertyKey, out _))
                            {
                                return true;
                            }
                            currentProto = null;
                            break;
                    }
                }

                return false;
            default:
                return accessor.TryGetProperty(propertyKey, out _);
        }
    }
}
