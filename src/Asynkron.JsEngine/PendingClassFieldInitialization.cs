namespace Asynkron.JsEngine;

internal readonly record struct PendingClassFieldInitialization(
    object Constructor,
    JsEnvironment Environment);
