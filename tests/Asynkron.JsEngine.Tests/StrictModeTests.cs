namespace Asynkron.JsEngine.Tests;

public class StrictModeTests
{
    [Fact]
    public async Task StrictMode_DetectedAndParsed()
    {
        // Verify that "use strict" directive is detected and added to the AST
        var engine = new JsEngine();
        
        var program = engine.Parse(@"
            ""use strict"";
            let x = 10;
        ");
        
        // Check that the program contains a UseStrict directive
        Assert.NotNull(program);
        Assert.NotNull(program.Rest);
        
        // The first statement after Program should be UseStrict
        var firstStmt = program.Rest.Head;
        Assert.IsType<Cons>(firstStmt);
        var firstStmtCons = (Cons)firstStmt;
        Assert.True(ReferenceEquals(firstStmtCons.Head, JsSymbols.UseStrict));
    }

    [Fact]
    public async Task StrictMode_ErrorMessageFormat()
    {
        // In strict mode, assigning to an undefined variable should throw a ReferenceError
        var engine = new JsEngine();
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate(@"
                ""use strict"";
                undeclaredVariable = 10;
            "));
        
        Assert.Contains("ReferenceError", ex.Message);
        Assert.Contains("is not defined", ex.Message);
    }

    [Fact]
    public async Task StrictMode_ProperDeclarationsWork()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            let a = 1;
            const b = 2;
            var c = 3;
            a + b + c;
        ");
        
        Assert.Equal(6.0, result);
    }

    [Fact]
    public async Task StrictMode_DetectedInFunctionBody()
    {
        var engine = new JsEngine();
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate(@"
                function testFunction() {
                    ""use strict"";
                    undeclaredVar = 5;
                }
                testFunction();
            "));
        
        Assert.Contains("is not defined", ex.Message);
    }

    [Fact]
    public async Task StrictMode_NestedFunctions()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            function outer() {
                let x = 10;
                function inner() {
                    let y = 20;
                    return x + y;
                }
                return inner();
            }
            outer();
        ");
        
        Assert.Equal(30.0, result);
    }

    [Fact]
    public async Task StrictMode_WithClasses()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            class MyClass {
                constructor(value) {
                    this.value = value;
                }
                getValue() {
                    return this.value;
                }
            }
            let obj = new MyClass(42);
            obj.getValue();
        ");
        
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task StrictMode_AssignmentToConstFails()
    {
        var engine = new JsEngine();
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate(@"
                ""use strict"";
                const x = 10;
                x = 20;
            "));
        
        Assert.Contains("constant", ex.Message);
    }

    [Fact]
    public async Task StrictMode_LetDeclarationsWork()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            {
                let x = 5;
                let y = 10;
                x + y;
            }
        ");
        
        Assert.Equal(15.0, result);
    }

    [Fact]
    public async Task StrictMode_InBlockScope()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            {
                ""use strict"";
                let x = 100;
                x;
            }
        ");
        
        Assert.Equal(100.0, result);
    }

    [Fact]
    public async Task StrictMode_MultipleStatements()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            let sum = 0;
            let i = 1;
            while (i <= 10) {
                sum = sum + i;
                i = i + 1;
            }
            sum;
        ");
        
        Assert.Equal(55.0, result);
    }

    [Fact]
    public async Task StrictMode_WithForLoops()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            let result = 0;
            for (let i = 0; i < 5; i = i + 1) {
                result = result + i;
            }
            result;
        ");
        
        Assert.Equal(10.0, result);
    }

    [Fact]
    public async Task StrictMode_WithObjectLiterals()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            let obj = {
                x: 10,
                y: 20,
                sum: function() {
                    return this.x + this.y;
                }
            };
            obj.sum();
        ");
        
        Assert.Equal(30.0, result);
    }

    [Fact]
    public async Task StrictMode_WithArrays()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            ""use strict"";
            let arr = [1, 2, 3, 4, 5];
            let sum = 0;
            let i = 0;
            while (i < arr.length) {
                sum = sum + arr[i];
                i = i + 1;
            }
            sum;
        ");
        
        Assert.Equal(15.0, result);
    }
}
