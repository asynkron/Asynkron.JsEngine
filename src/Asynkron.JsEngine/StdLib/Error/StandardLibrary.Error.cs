using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateErrorConstructor(RealmState realm, string errorType = "Error")
    {
        return errorType switch
        {
            "Error" => ErrorConstructor.CreateConstructor(realm),
            "TypeError" => TypeErrorConstructor.CreateConstructor(realm),
            "RangeError" => RangeErrorConstructor.CreateConstructor(realm),
            "ReferenceError" => ReferenceErrorConstructor.CreateConstructor(realm),
            "SyntaxError" => SyntaxErrorConstructor.CreateConstructor(realm),
            "EvalError" => EvalErrorConstructor.CreateConstructor(realm),
            "URIError" => UriErrorConstructor.CreateConstructor(realm),
            "AggregateError" => AggregateErrorConstructor.CreateConstructor(realm),
            _ => ErrorConstructor.CreateConstructor(realm),
        };
    }
}
