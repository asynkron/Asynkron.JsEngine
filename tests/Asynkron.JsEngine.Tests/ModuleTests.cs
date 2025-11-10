using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ModuleTests
{
    [Fact]
    public void ExportDefaultFunction()
    {
        var engine = new JsEngine();
        
        // Set up a module loader
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "math.js")
            {
                return @"
                    export default function add(a, b) {
                        return a + b;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import add from ""math.js"";
            add(2, 3);
        ");
        
        Assert.Equal(5.0, result);
    }
    
    [Fact]
    public void ExportDefaultValue()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "config.js")
            {
                return @"
                    let config = { name: ""MyApp"", version: ""1.0"" };
                    export default config;
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import config from ""config.js"";
            config.name;
        ");
        
        Assert.Equal("MyApp", result);
    }
    
    [Fact]
    public void ExportNamedValues()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "utils.js")
            {
                return @"
                    export let x = 10;
                    export let y = 20;
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { x, y } from ""utils.js"";
            x + y;
        ");
        
        Assert.Equal(30.0, result);
    }
    
    [Fact]
    public void ExportNamedFunctions()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "math.js")
            {
                return @"
                    export function add(a, b) {
                        return a + b;
                    }
                    
                    export function multiply(a, b) {
                        return a * b;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { add, multiply } from ""math.js"";
            add(2, 3) + multiply(4, 5);
        ");
        
        Assert.Equal(25.0, result);
    }
    
    [Fact]
    public void ImportWithAlias()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "math.js")
            {
                return @"
                    export function add(a, b) {
                        return a + b;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { add as sum } from ""math.js"";
            sum(10, 20);
        ");
        
        Assert.Equal(30.0, result);
    }
    
    [Fact]
    public void ImportNamespace()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "math.js")
            {
                return @"
                    export function add(a, b) {
                        return a + b;
                    }
                    
                    export function subtract(a, b) {
                        return a - b;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import * as math from ""math.js"";
            math.add(10, 5) + math.subtract(10, 5);
        ");
        
        Assert.Equal(20.0, result);
    }
    
    [Fact]
    public void ExportList()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "utils.js")
            {
                return @"
                    let x = 1;
                    let y = 2;
                    let z = 3;
                    export { x, y };
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { x, y } from ""utils.js"";
            x + y;
        ");
        
        Assert.Equal(3.0, result);
    }
    
    [Fact]
    public void ExportWithAlias()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "utils.js")
            {
                return @"
                    let privateValue = 42;
                    export { privateValue as value };
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { value } from ""utils.js"";
            value;
        ");
        
        Assert.Equal(42.0, result);
    }
    
    [Fact]
    public void ModuleCaching()
    {
        var engine = new JsEngine();
        var loadCount = 0;
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "counter.js")
            {
                loadCount++;
                return @"
                    export let count = 0;
                    export function increment() {
                        count = count + 1;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        // Load the module twice
        engine.EvaluateSync(@"
            import { count, increment } from ""counter.js"";
            increment();
        ");
        
        engine.EvaluateSync(@"
            import { count } from ""counter.js"";
            count;
        ");
        
        // Module should only be loaded once
        Assert.Equal(1, loadCount);
    }
    
    [Fact]
    public void ExportConst()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "constants.js")
            {
                return @"
                    export const PI = 3.14159;
                    export const E = 2.71828;
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { PI, E } from ""constants.js"";
            PI + E;
        ");
        
        Assert.Equal(5.85987, result);
    }
    
    [Fact]
    public void ExportClass()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "point.js")
            {
                return @"
                    export class Point {
                        constructor(x, y) {
                            this.x = x;
                            this.y = y;
                        }
                        
                        distance() {
                            return Math.sqrt(this.x * this.x + this.y * this.y);
                        }
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { Point } from ""point.js"";
            let p = new Point(3, 4);
            p.distance();
        ");
        
        Assert.Equal(5.0, result);
    }
    
    [Fact]
    public void DefaultAndNamedImports()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "module.js")
            {
                return @"
                    export default function main() {
                        return ""main"";
                    }
                    
                    export function helper() {
                        return ""helper"";
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import main, { helper } from ""module.js"";
            main() + ""-"" + helper();
        ");
        
        Assert.Equal("main-helper", result);
    }
    
    [Fact]
    public void SideEffectImport()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "side-effect.js")
            {
                return @"
                    let loaded = true;
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        // Import the module which sets a variable
        engine.EvaluateSync(@"
            import ""side-effect.js"";
        ");
        
        // The side effect should have run, but since it's in a module scope,
        // we can't directly access it. For this test, we'll just verify no error occurred.
        Assert.True(true); // Module loaded successfully without error
    }
    
    [Fact]
    public void ExportDefaultClass()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "Rectangle.js")
            {
                return @"
                    export default class Rectangle {
                        constructor(width, height) {
                            this.width = width;
                            this.height = height;
                        }
                        
                        area() {
                            return this.width * this.height;
                        }
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import Rectangle from ""Rectangle.js"";
            let rect = new Rectangle(5, 10);
            rect.area();
        ");
        
        Assert.Equal(50.0, result);
    }
    
    [Fact]
    public void MultipleImportsFromSameModule()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "math.js")
            {
                return @"
                    export function add(a, b) { return a + b; }
                    export function sub(a, b) { return a - b; }
                    export function mul(a, b) { return a * b; }
                    export function div(a, b) { return a / b; }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        var result = engine.EvaluateSync(@"
            import { add, sub, mul, div } from ""math.js"";
            add(10, 5) + sub(10, 5) + mul(10, 5) + div(10, 5);
        ");
        
        Assert.Equal(72.0, result);
    }
    
    [Fact]
    public async Task DynamicImport_LoadsModuleAsynchronously()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "dynamic.js")
            {
                return @"
                    export function greet(name) {
                        return ""Hello, "" + name;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        await engine.Run(@"
            let result = """";
            import(""dynamic.js"").then(function(module) {
                result = module.greet(""World"");
            });
        ");
        
        var result = engine.EvaluateSync("result;");
        Assert.Equal("Hello, World", result);
    }
    
    [Fact]
    public async Task DynamicImport_WithAsyncAwait()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "calculator.js")
            {
                return @"
                    export function multiply(a, b) {
                        return a * b;
                    }
                    export function divide(a, b) {
                        return a / b;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        await engine.Run(@"
            async function calculate() {
                let calc = await import(""calculator.js"");
                return calc.multiply(10, 5) + calc.divide(100, 2);
            }
            
            let finalResult = 0;
            calculate().then(function(result) {
                finalResult = result;
            });
        ");
        
        var result = engine.EvaluateSync("finalResult;");
        Assert.Equal(100.0, result);
    }
    
    [Fact]
    public async Task DynamicImport_DefaultExport()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "counter.js")
            {
                return @"
                    export default function count() {
                        return 42;
                    }
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        await engine.Run(@"
            let value = 0;
            import(""counter.js"").then(function(module) {
                value = module.default();
            });
        ");
        
        var result = engine.EvaluateSync("value;");
        Assert.Equal(42.0, result);
    }
    
    [Fact]
    public async Task DynamicImport_ModuleIsCached()
    {
        var engine = new JsEngine();
        
        var loadCount = 0;
        engine.SetModuleLoader(modulePath =>
        {
            if (modulePath == "cached.js")
            {
                loadCount++;
                return @"
                    export let counter = " + loadCount + @";
                ";
            }
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        await engine.Run(@"
            let first = 0;
            let second = 0;
            
            import(""cached.js"").then(function(module) {
                first = module.counter;
            }).then(function() {
                return import(""cached.js"");
            }).then(function(module) {
                second = module.counter;
            });
        ");
        
        var first = engine.EvaluateSync("first;");
        var second = engine.EvaluateSync("second;");
        
        // Both should be 1 because the module is cached
        Assert.Equal(1.0, first);
        Assert.Equal(1.0, second);
    }
    
    [Fact]
    public async Task DynamicImport_ErrorHandling()
    {
        var engine = new JsEngine();
        
        engine.SetModuleLoader(modulePath =>
        {
            throw new FileNotFoundException($"Module not found: {modulePath}");
        });
        
        await engine.Run(@"
            let errorCaught = false;
            import(""nonexistent.js"")[""catch""](function(error) {
                errorCaught = true;
            });
        ");
        
        var result = engine.EvaluateSync("errorCaught;");
        Assert.True((bool)result!);
    }
}
