using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests;

public class StrictModeTests
{
    [Fact(Timeout = 2000)]
    public async Task StrictMode_DetectedAndParsed()
    {
        // Verify that "use strict" directive is detected and added to the AST
        await using var engine = new JsEngine();

        var program = engine.Parse("""

                                               "use strict";
                                               let x = 10;

                                   """);

        Assert.True(program.IsStrict);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_ErrorMessageFormat()
    {
        // In strict mode, assigning to an undefined variable should throw a ReferenceError
        await using var engine = new JsEngine();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("""

                            "use strict";
                            undeclaredVariable = 10;

            """));

        Assert.Contains("ReferenceError", ex.Message);
        Assert.Contains("is not defined", ex.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_ProperDeclarationsWork()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       let a = 1;
                                                       const b = 2;
                                                       var c = 3;
                                                       a + b + c;

                                           """);

        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_DetectedInFunctionBody()
    {
        await using var engine = new JsEngine();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("""

                            function testFunction() {
                                "use strict";
                                undeclaredVar = 5;
                            }
                            testFunction();

            """));

        Assert.Contains("is not defined", ex.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_NestedFunctions()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       function outer() {
                                                           let x = 10;
                                                           function inner() {
                                                               let y = 20;
                                                               return x + y;
                                                           }
                                                           return inner();
                                                       }
                                                       outer();

                                           """);

        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_WithClasses()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
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

                                           """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_AssignmentToConstFails()
    {
        await using var engine = new JsEngine();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("""

                            "use strict";
                            const x = 10;
                            x = 20;

            """));

        Assert.Contains("constant", ex.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_LetDeclarationsWork()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       {
                                                           let x = 5;
                                                           let y = 10;
                                                           x + y;
                                                       }

                                           """);

        Assert.Equal(15.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_InBlockScope()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       {
                                                           "use strict";
                                                           let x = 100;
                                                           x;
                                                       }

                                           """);

        Assert.Equal(100.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_MultipleStatements()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       let sum = 0;
                                                       let i = 1;
                                                       while (i <= 10) {
                                                           sum = sum + i;
                                                           i = i + 1;
                                                       }
                                                       sum;

                                           """);

        Assert.Equal(55.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_WithForLoops()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       let result = 0;
                                                       for (let i = 0; i < 5; i = i + 1) {
                                                           result = result + i;
                                                       }
                                                       result;

                                           """);

        Assert.Equal(10.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_WithObjectLiterals()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       let obj = {
                                                           x: 10,
                                                           y: 20,
                                                           sum: function() {
                                                               return this.x + this.y;
                                                           }
                                                       };
                                                       obj.sum();

                                           """);

        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictMode_WithArrays()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       let arr = [1, 2, 3, 4, 5];
                                                       let sum = 0;
                                                       let i = 0;
                                                       while (i < arr.length) {
                                                           sum = sum + arr[i];
                                                           i = i + 1;
                                                       }
                                                       sum;

                                           """);

        Assert.Equal(15.0, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task NonStrictMode_AllowsUndefinedVariableAssignment()
    {
        // In non-strict mode (default), assigning to an undefined variable should create a global variable
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
                            x = 7;
                            x;
                        """);

        Assert.Equal(7.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NonStrictMode_CanReadBackAssignedVariable()
    {
        // Verify that the created variable persists and can be read back
        await using var engine = new JsEngine();

        await engine.Evaluate("myGlobalVar = 42;");
        var result = await engine.Evaluate("myGlobalVar + 1;");

        Assert.Equal(43.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NonStrictMode_MultipleUndefinedAssignments()
    {
        // Multiple undefined variable assignments should work
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
                            a = 1;
                            b = 2;
                            c = 3;
                            a + b + c;
                        """);

        Assert.Equal(6.0, result);
    }
}
