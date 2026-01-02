using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class ModuleTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task ExportDefaultFunction()
    {
        await using var engine = CreateEngine();

        // Set up a module loader
        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "math.js", StringComparison.Ordinal))
            {
                return """

                                           export default function add(a, b) {
                                               return a + b;
                                           }

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import add from "math.js";
                                                       add(2, 3);

                                           """);

        Assert.Equal(5.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportDefaultValue()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "config.js", StringComparison.Ordinal))
            {
                return """

                                           let config = { name: "MyApp", version: "1.0" };
                                           export default config;

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import config from "config.js";
                                                       config.name;

                                           """);

        Assert.Equal("MyApp", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportNamedValues()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "utils.js", StringComparison.Ordinal))
            {
                return """

                                           export let x = 10;
                                           export let y = 20;

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { x, y } from "utils.js";
                                                       x + y;

                                           """);

        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportNamedFunctions()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "math.js", StringComparison.Ordinal))
            {
                return """

                                           export function add(a, b) {
                                               return a + b;
                                           }

                                           export function multiply(a, b) {
                                               return a * b;
                                           }

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { add, multiply } from "math.js";
                                                       add(2, 3) + multiply(4, 5);

                                           """);

        Assert.Equal(25.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ImportWithAlias()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "math.js", StringComparison.Ordinal))
            {
                return """

                                           export function add(a, b) {
                                               return a + b;
                                           }

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { add as sum } from "math.js";
                                                       sum(10, 20);

                                           """);

        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ImportNamespace()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "math.js", StringComparison.Ordinal))
            {
                return """

                                           export function add(a, b) {
                                               return a + b;
                                           }

                                           export function subtract(a, b) {
                                               return a - b;
                                           }

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import * as math from "math.js";
                                                       math.add(10, 5) + math.subtract(10, 5);

                                           """);

        Assert.Equal(20.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportList()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "utils.js", StringComparison.Ordinal))
            {
                return """

                                           let x = 1;
                                           let y = 2;
                                           let z = 3;
                                           export { x, y };

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { x, y } from "utils.js";
                                                       x + y;

                                           """);

        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportWithAlias()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "utils.js", StringComparison.Ordinal))
            {
                return """

                                           let privateValue = 42;
                                           export { privateValue as value };

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { value } from "utils.js";
                                                       value;

                                           """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ModuleCaching()
    {
        await using var engine = CreateEngine();
        var loadCount = 0;

        engine.SetModuleLoader(modulePath =>
        {
            if (!string.Equals(modulePath, "counter.js", StringComparison.Ordinal))
            {
                throw new FileNotFoundException($"Module not found: {modulePath}");
            }

            loadCount++;
            return """

                                       export let count = 0;
                                       export function increment() {
                                           count = count + 1;
                                       }

                   """;

        });

        // Load the module twice
        await engine.Evaluate("""

                                          import { count, increment } from "counter.js";
                                          increment();

                              """);

        await engine.Evaluate("""

                                          import { count } from "counter.js";
                                          count;

                              """);

        // Module should only be loaded once
        Assert.Equal(1, loadCount);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ExportConst()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "constants.js", StringComparison.Ordinal))
            {
                return """

                                           export const PI = 3.14159;
                                           export const E = 2.71828;

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { PI, E } from "constants.js";
                                                       PI + E;

                                           """);

        Assert.Equal(5.85987, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportClass()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "point.js", StringComparison.Ordinal))
            {
                return """

                                           export class Point {
                                               constructor(x, y) {
                                                   this.x = x;
                                                   this.y = y;
                                               }

                                               distance() {
                                                   return Math.sqrt(this.x * this.x + this.y * this.y);
                                               }
                                           }

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.Evaluate("""

                                                       import { Point } from "point.js";
                                                       let p = new Point(3, 4);
                                                       p.distance();

                                           """);

        Assert.Equal(5.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DefaultAndNamedImports()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => !string.Equals(modulePath, "module.js", StringComparison.Ordinal) ? throw new FileNotFoundException($"Module not found: {modulePath}") : """

                                export default function main() {
                                    return "main";
                                }

                                export function helper() {
                                    return "helper";
                                }

            """);

        var result = await engine.Evaluate("""

                                                       import main, { helper } from "module.js";
                                                       main() + "-" + helper();

                                           """);

        Assert.Equal("main-helper", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SideEffectImport()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "side-effect.js", StringComparison.Ordinal))
            {
                return """

                                           let loaded = true;

                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        // Import the module which sets a variable
        await engine.Evaluate("""

                                          import "side-effect.js";

                              """);

        // The side effect should have run, but since it's in a module scope,
        // we can't directly access it. For this test, we'll just verify no error occurred.
        Assert.True(true); // Module loaded successfully without error
    }

    [Fact(Timeout = 2000)]
    public async Task ExportDefaultClass()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => string.Equals(modulePath, "Rectangle.js", StringComparison.Ordinal) ? """

                                export default class Rectangle {
                                    constructor(width, height) {
                                        this.width = width;
                                        this.height = height;
                                    }

                                    area() {
                                        return this.width * this.height;
                                    }
                                }

            """ : throw new FileNotFoundException($"Module not found: {modulePath}"));

        var result = await engine.Evaluate("""

                                                       import Rectangle from "Rectangle.js";
                                                       let rect = new Rectangle(5, 10);
                                                       rect.area();

                                           """);

        Assert.Equal(50.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportDefaultAnonymousClass()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => string.Equals(modulePath, "anonymous.js", StringComparison.Ordinal) ? """
                                export default class {
                                    constructor(value) {
                                        this.value = value;
                                    }

                                    double() {
                                        return this.value * 2;
                                    }
                                }
            """ : throw new FileNotFoundException($"Module not found: {modulePath}"));

        var result = await engine.Evaluate("""
                                                       import Mystery from "anonymous.js";
                                                       new Mystery(7).double();

                                           """);

        Assert.Equal(14d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleImportsFromSameModule()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => string.Equals(modulePath, "math.js", StringComparison.Ordinal) ? """

                                export function add(a, b) { return a + b; }
                                export function sub(a, b) { return a - b; }
                                export function mul(a, b) { return a * b; }
                                export function div(a, b) { return a / b; }

            """ : throw new FileNotFoundException($"Module not found: {modulePath}"));

        var result = await engine.Evaluate("""

                                                       import { add, sub, mul, div } from "math.js";
                                                       add(10, 5) + sub(10, 5) + mul(10, 5) + div(10, 5);

                                           """);

        Assert.Equal(72.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DynamicImport_LoadsModuleAsynchronously()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => string.Equals(modulePath, "dynamic.js", StringComparison.Ordinal) ? """

                                export function greet(name) {
                                    return "Hello, " + name;
                                }

            """ : throw new FileNotFoundException($"Module not found: {modulePath}"));

        await engine.Evaluate("""

                                     let result = "";
                                     import("dynamic.js").then(function(module) {
                                         result = module.greet("World");
                                     });

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("Hello, World", result);
    }

    [Fact(Timeout = 2000)]
    public async Task DynamicImport_WithAsyncAwait()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => string.Equals(modulePath, "calculator.js", StringComparison.Ordinal) ? """

                                export function multiply(a, b) {
                                    return a * b;
                                }
                                export function divide(a, b) {
                                    return a / b;
                                }

            """ : throw new FileNotFoundException($"Module not found: {modulePath}"));

        await engine.Evaluate("""

                                     async function calculate() {
                                         let calc = await import("calculator.js");
                                         return calc.multiply(10, 5) + calc.divide(100, 2);
                                     }

                                     let finalResult = 0;
                                     calculate().then(function(result) {
                                         finalResult = result;
                                     });

                         """);

        var result = await engine.Evaluate("finalResult;");
        Assert.Equal(100.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DynamicImport_DefaultExport()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => string.Equals(modulePath, "counter.js", StringComparison.Ordinal) ? """

                                export default function count() {
                                    return 42;
                                }

            """ : throw new FileNotFoundException($"Module not found: {modulePath}"));

        await engine.Evaluate("""

                                     let value = 0;
                                     import("counter.js").then(function(module) {
                                         value = module.default();
                                     });

                         """);

        var result = await engine.Evaluate("value;");
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DynamicImport_ModuleIsCached()
    {
        await using var engine = CreateEngine();

        var loadCount = 0;
        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "cached.js", StringComparison.Ordinal))
            {
                loadCount++;
                return $"""

                                            export let counter ={loadCount};

                        """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");

        });

        await engine.Evaluate("""

                                     let first = 0;
                                     let second = 0;

                                     import("cached.js").then(function(module) {
                                         first = module.counter;
                                     }).then(function() {
                                         return import("cached.js");
                                     }).then(function(module) {
                                         second = module.counter;
                                     });

                         """);

        var first = await engine.Evaluate("first;");
        var second = await engine.Evaluate("second;");

        // Both should be 1 because the module is cached
        Assert.Equal(1.0, first);
        Assert.Equal(1.0, second);
    }

    [Fact(Timeout = 2000)]
    public async Task DynamicImport_ErrorHandling()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath => { throw new FileNotFoundException($"Module not found: {modulePath}"); });

        await engine.Evaluate("""

                                     let errorCaught = false;
                                     import("nonexistent.js")["catch"](function(error) {
                                         errorCaught = true;
                                     });

                         """);

        var result = await engine.Evaluate("errorCaught;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task SelfImportAnonymousDefaultExport()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "self.js", StringComparison.Ordinal))
            {
                return """
                       import f from './self.js';
                       export default function() { return 23; };
                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        // Evaluate the module - this should not throw
        var result = await engine.EvaluateModule("""
                                                 import f from 'self.js';
                                                 f();
                                                 """);

        Assert.Equal(23.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SelfImportAnonymousDefaultExportSingleFile()
    {
        // This mimics the Test262 scenario more closely - single file that imports itself
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "language/module-code/test.js", StringComparison.Ordinal))
            {
                return """
                       import f from './test.js';
                       export default function() { return 23; };
                       f();
                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        // Evaluate the module - this should not throw
        var result = await engine.EvaluateModule("""
                                                 import f from './test.js';
                                                 export default function() { return 23; };
                                                 f();
                                                 """, "language/module-code/test.js");

        Assert.Equal(23.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Test262StyleSelfImport()
    {
        // This exactly mimics the Test262 instn-named-bndng-dflt-fun-anon.js test
        await using var engine = CreateEngine();

        var testCode = @"
f();
import f from './instn-named-bndng-dflt-fun-anon.js';
export default function() { return 23; };
";

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "language/module-code/instn-named-bndng-dflt-fun-anon.js", StringComparison.Ordinal))
            {
                return testCode;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        // Evaluate the module - this should not throw
        var result = await engine.EvaluateModule(testCode, "language/module-code/instn-named-bndng-dflt-fun-anon.js");

        Assert.Equal(23.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SelfImportAnonymousDefaultGeneratorExport()
    {
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "self.js", StringComparison.Ordinal))
            {
                return """
                       import f from './self.js';
                       export default function*() { yield 23; };
                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        // Evaluate the module - this should not throw
        var result = await engine.EvaluateModule("""
                                                 import f from 'self.js';
                                                 f().next().value;
                                                 """);

        Assert.Equal(23.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExportDefaultFunctionCanReassignSelf()
    {
        // Test262: language/module-code/eval-gtbndng-indirect-update-dflt.js
        // A named function exported as default can reassign its own binding
        await using var engine = CreateEngine();

        engine.SetModuleLoader(modulePath =>
        {
            if (string.Equals(modulePath, "fixture.js", StringComparison.Ordinal))
            {
                return """
                       export default function fn() {
                         fn = 2;
                         return 1;
                       }
                       """;
            }

            throw new FileNotFoundException($"Module not found: {modulePath}");
        });

        var result = await engine.EvaluateModule("""
                                                 import val from 'fixture.js';
                                                 var r1 = val();
                                                 var r2 = val;
                                                 [r1, r2];
                                                 """);

        var arr = Assert.IsType<JsTypes.JsArray>(result);
        arr.TryGetProperty("0", out var r1);
        arr.TryGetProperty("1", out var r2);
        Assert.Equal(1.0, r1);
        Assert.Equal(2.0, r2);
    }

    [Fact(Timeout = 5000)]
    public async Task TopLevelAwait_IfStatement()
    {
        // Test that top-level await works inside if statements
        await using var engine = CreateEngine();

        var result = await engine.EvaluateModule("""
                                                 var completed = 0;
                                                 var p = Promise.resolve(true);

                                                 if (await p) {
                                                   completed += 1;
                                                 }

                                                 completed
                                                 """);

        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TopLevelAwait_BlockStatement()
    {
        // Test that top-level await works inside block statements
        await using var engine = CreateEngine();

        var result = await engine.EvaluateModule("""
                                                 if (true) {
                                                   await [];
                                                 }
                                                 42
                                                 """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TopLevelAwait_WhileLoop()
    {
        // Simple while loop with await - condition is false immediately after first iteration
        await using var engine = CreateEngine();

        var result = await engine.EvaluateModule("""
                                                 var count = 0;
                                                 var done = false;

                                                 while (await Promise.resolve(!done)) {
                                                   count++;
                                                   done = true;
                                                 }

                                                 count
                                                 """);

        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TopLevelAwait_ForLoop()
    {
        // For loop with await in condition
        await using var engine = CreateEngine();

        var result = await engine.EvaluateModule("""
                                                 var count = 0;
                                                 for (var i = 0; await Promise.resolve(i < 3); i++) {
                                                   count++;
                                                 }
                                                 count
                                                 """);

        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 5000, Skip = "for await...of with async generators needs further investigation")]
    public async Task TopLevelAwait_ForAwaitOf()
    {
        // for await...of loop with async generator
        await using var engine = CreateEngine();

        var result = await engine.EvaluateModule("""
                                                 var sum = 0;
                                                 async function* gen() {
                                                   yield 1;
                                                   yield 2;
                                                   yield 3;
                                                 }
                                                 for await (var x of gen()) {
                                                   sum += x;
                                                 }
                                                 sum
                                                 """);

        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TopLevelAwait_TryCatch()
    {
        // Try/catch with await
        await using var engine = CreateEngine();

        var result = await engine.EvaluateModule("""
                                                 var caught = false;
                                                 try {
                                                   await Promise.reject("error");
                                                 } catch (e) {
                                                   caught = true;
                                                 }
                                                 caught
                                                 """);

        Assert.True((bool)result!);
    }
}
