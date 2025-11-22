using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static HostFunction CreateErrorConstructor(Runtime.RealmState realm, string errorType = "Error")
    {
        JsObject? prototype = null;

        var errorConstructor = new HostFunction((thisValue, args) =>
        {
            var message = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "";
            var errorObj = thisValue as JsObject ?? new JsObject();

            if (prototype is not null && errorObj.Prototype is null)
            {
                errorObj.SetPrototype(prototype);
            }

            errorObj["name"] = errorType;
            errorObj["message"] = message;

            return errorObj;
        });

        prototype = new JsObject();
        if (!string.Equals(errorType, "Error", StringComparison.Ordinal) && realm.ErrorPrototype is not null)
        {
            prototype.SetPrototype(realm.ErrorPrototype);
        }
        else if (realm.ObjectPrototype is not null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }

        prototype.SetProperty("toString", new HostFunction((errThis, toStringArgs) =>
        {
            if (errThis is JsObject err)
            {
                var name = err.TryGetValue("name", out var n) ? n?.ToString() : errorType;
                var msg = err.TryGetValue("message", out var m) ? m?.ToString() : "";
                return string.IsNullOrEmpty(msg) ? name : $"{name}: {msg}";
            }

            return errorType;
        }));

        prototype.DefineProperty("constructor", new PropertyDescriptor
        {
            Value = errorConstructor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        errorConstructor.SetProperty("prototype", prototype);

        if (string.Equals(errorType, "Error", StringComparison.Ordinal))
        {
            realm.ErrorPrototype = prototype;
            ErrorPrototype = prototype;
        }

        if (string.Equals(errorType, "TypeError", StringComparison.Ordinal))
        {
            realm.TypeErrorPrototype = prototype;
            realm.TypeErrorConstructor = errorConstructor;
            TypeErrorPrototype = prototype;
            TypeErrorConstructor = errorConstructor;
        }

        if (string.Equals(errorType, "RangeError", StringComparison.Ordinal))
        {
            realm.RangeErrorConstructor = errorConstructor;
            RangeErrorConstructor = errorConstructor;
        }

        if (string.Equals(errorType, "SyntaxError", StringComparison.Ordinal))
        {
            realm.SyntaxErrorConstructor = errorConstructor;
            SyntaxErrorConstructor = errorConstructor;
            realm.SyntaxErrorPrototype = prototype;
            SyntaxErrorPrototype = prototype;
        }

        // Function.name
        errorConstructor.SetProperty("name", errorType);

        return errorConstructor;
    }

    /// <summary>
    /// Converts a value to a boolean following JavaScript truthiness rules.
    /// </summary>
    private static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }
}
