using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Locale", PrototypeType = typeof(IntlLocalePrototype), Length = 1d, DisplayName = "Locale")]
public sealed partial class IntlLocaleConstructor : JsConstructor
{
    public IntlLocaleConstructor(JsObject prototype, RealmState realm)
        : base(prototype, realm)
    {
    }

    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var instance = PrepareThisObject(thisValue);

        if (args.Count > 0 && args[0] is string tag)
        {
            instance.SetProperty("__tag__", tag);
        }

        if (args.Count > 1 && args[1] is JsObject options && options.TryGetProperty("calendar", out var calendar))
        {
            instance.SetProperty("__calendar__", calendar);
        }

        return instance;
    }
}
