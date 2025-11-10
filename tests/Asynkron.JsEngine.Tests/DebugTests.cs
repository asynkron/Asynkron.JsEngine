using Asynkron.JsEngine;
using System.Threading.Channels;

namespace Asynkron.JsEngine.Tests;

public class DebugTests
{
    [Fact]
    public async Task DebugFunction_CapturesBasicVariables()
    {
        var engine = new JsEngine();
        
        var source = @"
            let x = 42;
            let y = 'hello';
            __debug();
        ";
        
        // Execute and get debug message
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Verify variables are captured
        Assert.True(debugMessage.Variables.ContainsKey("x"));
        Assert.Equal(42d, debugMessage.Variables["x"]);
        Assert.True(debugMessage.Variables.ContainsKey("y"));
        Assert.Equal("hello", debugMessage.Variables["y"]);
    }

    [Fact]
    public async Task DebugFunction_CapturesLoopCounter()
    {
        var engine = new JsEngine();
        
        var source = @"
            for (var i = 0; i < 10; i++) {
                if (i === 5) {
                    __debug();
                }
            }
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Verify loop counter is captured with correct value
        Assert.True(debugMessage.Variables.ContainsKey("i"));
        Assert.Equal(5d, debugMessage.Variables["i"]);
    }

    [Fact]
    public async Task DebugFunction_CapturesAllIterationsInLoop()
    {
        var engine = new JsEngine();
        
        var source = @"
            for (var i = 0; i < 3; i++) {
                __debug();
            }
        ";
        
        engine.Evaluate(source);
        
        // Should have 3 debug messages
        var msg1 = await engine.DebugMessages().ReadAsync();
        Assert.Equal(0d, msg1.Variables["i"]);
        
        var msg2 = await engine.DebugMessages().ReadAsync();
        Assert.Equal(1d, msg2.Variables["i"]);
        
        var msg3 = await engine.DebugMessages().ReadAsync();
        Assert.Equal(2d, msg3.Variables["i"]);
    }

    [Fact]
    public async Task DebugFunction_CapturesFunctionScope()
    {
        var engine = new JsEngine();
        
        var source = @"
            let globalVar = 'global';
            
            function testFunc(param) {
                let localVar = 'local';
                __debug();
            }
            
            testFunc(123);
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Should capture function parameter and local variable
        Assert.True(debugMessage.Variables.ContainsKey("param"));
        Assert.Equal(123d, debugMessage.Variables["param"]);
        Assert.True(debugMessage.Variables.ContainsKey("localVar"));
        Assert.Equal("local", debugMessage.Variables["localVar"]);
        
        // Should also capture global variable due to scope chain
        Assert.True(debugMessage.Variables.ContainsKey("globalVar"));
        Assert.Equal("global", debugMessage.Variables["globalVar"]);
    }

    [Fact]
    public async Task DebugFunction_CapturesNestedScopes()
    {
        var engine = new JsEngine();
        
        var source = @"
            let outer = 'outer';
            
            function outerFunc() {
                let middle = 'middle';
                
                function innerFunc() {
                    let inner = 'inner';
                    __debug();
                }
                
                innerFunc();
            }
            
            outerFunc();
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Should capture all variables in the scope chain
        Assert.True(debugMessage.Variables.ContainsKey("inner"));
        Assert.Equal("inner", debugMessage.Variables["inner"]);
        Assert.True(debugMessage.Variables.ContainsKey("middle"));
        Assert.Equal("middle", debugMessage.Variables["middle"]);
        Assert.True(debugMessage.Variables.ContainsKey("outer"));
        Assert.Equal("outer", debugMessage.Variables["outer"]);
    }

    [Fact]
    public async Task DebugFunction_CapturesCallStack()
    {
        var engine = new JsEngine();
        
        var source = @"
            function outer() {
                inner();
            }
            
            function inner() {
                __debug();
            }
            
            outer();
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Should have a call stack
        Assert.NotNull(debugMessage.CallStack);
        Assert.NotEmpty(debugMessage.CallStack);
        
        // The call stack should contain function calls
        var callStackDescriptions = debugMessage.CallStack.Select(f => f.Description).ToList();
        Assert.Contains(callStackDescriptions, d => d.Contains("inner"));
        Assert.Contains(callStackDescriptions, d => d.Contains("outer"));
    }

    [Fact]
    public async Task DebugFunction_CapturesLoopInCallStack()
    {
        var engine = new JsEngine();
        
        var source = @"
            for (var i = 0; i < 1; i++) {
                __debug();
            }
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Should have a call stack with a for loop frame
        Assert.NotNull(debugMessage.CallStack);
        Assert.NotEmpty(debugMessage.CallStack);
        
        var hasForLoop = debugMessage.CallStack.Any(f => f.OperationType == "for");
        Assert.True(hasForLoop, "Expected to find a 'for' loop in the call stack");
    }

    [Fact]
    public async Task DebugFunction_CapturesWhileLoopInCallStack()
    {
        var engine = new JsEngine();
        
        var source = @"
            var count = 0;
            while (count < 1) {
                __debug();
                count++;
            }
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Should have a call stack with a while loop frame
        Assert.NotNull(debugMessage.CallStack);
        
        var hasWhileLoop = debugMessage.CallStack.Any(f => f.OperationType == "while");
        Assert.True(hasWhileLoop, "Expected to find a 'while' loop in the call stack");
    }

    [Fact]
    public async Task DebugFunction_CallStackShowsDepth()
    {
        var engine = new JsEngine();
        
        var source = @"
            function level1() {
                level2();
            }
            
            function level2() {
                level3();
            }
            
            function level3() {
                __debug();
            }
            
            level1();
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // The innermost frame should have the highest depth
        Assert.NotEmpty(debugMessage.CallStack);
        
        // First frame in the list is the innermost
        var innermostFrame = debugMessage.CallStack.First();
        Assert.True(innermostFrame.Depth >= 0);
        
        // Frames should be ordered from innermost to outermost
        int previousDepth = int.MaxValue;
        foreach (var frame in debugMessage.CallStack)
        {
            Assert.True(frame.Depth <= previousDepth);
            previousDepth = frame.Depth;
        }
    }

    [Fact]
    public async Task DebugFunction_CapturesControlFlowState()
    {
        var engine = new JsEngine();
        
        var source = @"
            let x = 42;
            __debug();
        ";
        
        engine.Evaluate(source);
        
        var debugMessage = await engine.DebugMessages().ReadAsync();
        
        // Control flow should be "None" in normal execution
        Assert.Equal("None", debugMessage.ControlFlowState);
    }

    [Fact]
    public async Task DebugFunction_WorksInNestedLoops()
    {
        var engine = new JsEngine();
        
        var source = @"
            for (var i = 0; i < 2; i++) {
                for (var j = 0; j < 2; j++) {
                    __debug();
                }
            }
        ";
        
        engine.Evaluate(source);
        
        // Should have 4 debug messages (2x2)
        var messages = new List<DebugMessage>();
        for (int k = 0; k < 4; k++)
        {
            messages.Add(await engine.DebugMessages().ReadAsync());
        }
        
        // Verify all combinations are captured
        Assert.Equal(0d, messages[0].Variables["i"]);
        Assert.Equal(0d, messages[0].Variables["j"]);
        
        Assert.Equal(0d, messages[1].Variables["i"]);
        Assert.Equal(1d, messages[1].Variables["j"]);
        
        Assert.Equal(1d, messages[2].Variables["i"]);
        Assert.Equal(0d, messages[2].Variables["j"]);
        
        Assert.Equal(1d, messages[3].Variables["i"]);
        Assert.Equal(1d, messages[3].Variables["j"]);
    }
}
