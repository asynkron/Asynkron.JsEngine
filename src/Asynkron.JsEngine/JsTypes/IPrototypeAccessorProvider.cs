namespace Asynkron.JsEngine.JsTypes;

internal interface IPrototypeAccessorProvider
{
    IJsPropertyAccessor? PrototypeAccessor { get; }
}
