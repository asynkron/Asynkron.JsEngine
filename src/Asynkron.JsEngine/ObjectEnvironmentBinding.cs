#region

#endregion

namespace Asynkron.JsEngine;

internal readonly record struct ObjectEnvironmentBinding(
    IJsObjectLike BindingObject,
    string PropertyName,
    bool IsStrictReference,
    bool AllowMissingAssignment);
