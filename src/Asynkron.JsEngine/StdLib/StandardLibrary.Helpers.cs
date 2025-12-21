using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.BigIntHelper;
using static Asynkron.JsEngine.StdLib.BooleanHelper;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StringHelper;
using static Asynkron.JsEngine.StdLib.SymbolHelper;

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
                    candidate = jsValue.ToObject();
                    continue;
                case IJsObjectLike objectLike:
                    accessor = objectLike;
                    return true;
                case TypedAstSymbol symbol:
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
                    accessor = CreateNumberWrapper(JsOps.ToNumber(candidate), realm: realm);
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
                accessor = CreateStringWrapper(candidate.ObjectValue as string ?? string.Empty, realm: realm);
                return true;
            case JsValueKind.Symbol:
                if (candidate.ObjectValue is TypedAstSymbol symbol)
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
            case JsObject jsObject:
                return jsObject.HasProperty(propertyKey);
            case IJsObjectLike objectLike:
                if (objectLike.GetOwnPropertyDescriptor(propertyKey) is not null)
                {
                    return true;
                }

                var prototype = objectLike.Prototype;
                while (prototype is not null)
                {
                    if (prototype.HasProperty(propertyKey))
                    {
                        return true;
                    }

                    prototype = prototype.Prototype;
                }

                return objectLike.TryGetProperty(propertyKey, out _);
            default:
                return accessor.TryGetProperty(propertyKey, out _);
        }
    }
}
