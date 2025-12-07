using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
class ProxyBase {
  constructor() {
    return new Proxy(this, {
      get(obj, prop) {
        return obj[prop];
      }
    });
  }
}

class Test extends ProxyBase {
  #f = 3;
  method() { return this.#f; }
}

const t = new Test();
""";

engine.Execute(script);

object? methodValue = engine.Evaluate("t.method");
Console.WriteLine($"method typeof: {methodValue?.GetType().Name ?? "<null>"}");
Console.WriteLine($"method callable: {methodValue is Asynkron.JsEngine.JsTypes.IJsCallable}");
Console.WriteLine($"method is Symbol.Undefined: {ReferenceEquals(methodValue, Asynkron.JsEngine.Symbol.Undefined)}");
Console.WriteLine($"proto matches: {engine.Evaluate(\"Object.getPrototypeOf(t) === Test.prototype\")}");
Console.WriteLine($"own names on target: {engine.Evaluate(\"Object.getOwnPropertyNames(t)\")}");
Console.WriteLine($"own names on proto: {engine.Evaluate(\"Object.getOwnPropertyNames(Test.prototype)\")}");
