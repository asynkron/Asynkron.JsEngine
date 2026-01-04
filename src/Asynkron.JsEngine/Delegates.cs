namespace Asynkron.JsEngine;

/// <summary>
/// Host function handler that receives thisValue and arguments.
/// </summary>
public delegate JsValue JsHostHandler(JsValue thisValue, IReadOnlyList<JsValue> args);

/// <summary>
/// Simple host function handler that receives only arguments.
/// </summary>
public delegate JsValue JsSimpleHandler(IReadOnlyList<JsValue> args);

/// <summary>
/// Async host function handler that receives thisValue and arguments.
/// </summary>
public delegate Task<JsValue> JsAsyncHostHandler(JsValue thisValue, IReadOnlyList<JsValue> args);

/// <summary>
/// Simple async host function handler that receives only arguments.
/// </summary>
public delegate Task<JsValue> JsAsyncSimpleHandler(IReadOnlyList<JsValue> args);

/// <summary>
/// Module loader that resolves module specifier to source code.
/// </summary>
public delegate string ModuleLoader(string specifier, string? referrer);

/// <summary>
/// Simple module loader without referrer.
/// </summary>
public delegate string SimpleModuleLoader(string specifier);
