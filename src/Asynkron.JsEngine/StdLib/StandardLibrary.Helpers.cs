using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static bool TryGetObject(object? candidate, RealmState realm, out IJsObjectLike accessor)
    {
        switch (candidate)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                accessor = null!;
                return false;
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
