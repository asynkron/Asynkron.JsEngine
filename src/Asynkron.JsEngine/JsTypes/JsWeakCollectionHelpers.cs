#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal static class JsWeakCollectionHelpers
{
    public static object? ExtractWeakKeyObject(JsValue value)
    {
        if (value.IsNull || value.IsUndefined || value.IsString || value.IsNumber || value.IsBoolean)
        {
            return null;
        }

        var obj = value.ObjectValue;

        while (obj is JsValue nested)
        {
            obj = nested.ObjectValue;
        }

        return IsWeakCollectionObject(obj) ? obj : null;
    }

    private static bool IsWeakCollectionObject(object? value)
    {
        switch (value)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case string:
                return false;
        }

        if (value.GetType().IsValueType)
        {
            return false;
        }

        return value is not JsSymbol;
    }
}
