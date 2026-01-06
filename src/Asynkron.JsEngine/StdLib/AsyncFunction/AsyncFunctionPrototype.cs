#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncFunction prototype object (%AsyncFunction.prototype%).
/// </summary>
[JsPrototype("AsyncFunction", ToStringTag = "AsyncFunction")]
public sealed partial class AsyncFunctionPrototype : JsPrototype
{
    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        if (Realm.FunctionPrototype is { } functionPrototype)
        {
            Prototype.SetPrototype(functionPrototype);
        }

        Realm.AsyncFunctionPrototype ??= Prototype as JsObject;
    }
}
