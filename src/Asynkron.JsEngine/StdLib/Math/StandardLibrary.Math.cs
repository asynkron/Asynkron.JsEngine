using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateMathObject(RealmState? realm = null)
    {
        realm ??= new RealmState();
        return (JsObject)MathPrototype.CreatePrototype(realm);
    }
}
