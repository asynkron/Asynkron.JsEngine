using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.Locale")]
public sealed partial class IntlLocalePrototype(JsObject prototype, RealmState realm) : JsPrototype(prototype, realm)
{
    [JsHostGetter("calendar", DisplayName = "get calendar")]
    private object? GetCalendar(object? thisValue)
    {
        if (thisValue is JsObject self && self.TryGetProperty("__calendar__", out var value))
        {
            return value;
        }

        return Symbol.Undefined;
    }
}
