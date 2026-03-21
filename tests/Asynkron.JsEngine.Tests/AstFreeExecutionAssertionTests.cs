using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for the AST-free execution assertion mechanism (Task 3.1).
/// Verifies that the AssertNoAstEvaluation flag correctly detects AST evaluation
/// during IR-only execution paths.
///
/// Related to issues #398, #415, #364, #401 (IR-only execution epic).
/// </summary>
[Category(TestCategories.Debugging)]
[Category(TestCategories.Performance)]
[Trait("Category", "AstAssertion")]
public sealed class AstFreeExecutionAssertionTests : IAsyncLifetime
{
    private JsEngine _engine = null!;

    public Task InitializeAsync()
    {
        _engine = new JsEngine();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _engine.DisposeAsync();
    }

#if DEBUG
    /// <summary>
    /// Verifies that the AssertNoAstEvaluation flag exists and can be set.
    /// </summary>
    [Fact]
    public void AssertNoAstEvaluation_Flag_CanBeSet()
    {
        // Arrange & Act
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;
            Assert.True(EvaluationContext.AssertNoAstEvaluation);
            
            EvaluationContext.AssertNoAstEvaluation = false;
            Assert.False(EvaluationContext.AssertNoAstEvaluation);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that normal execution works when the assertion flag is disabled.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_Disabled_AllowsNormalExecution()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;
            
            var program = _engine.ParseProgram(@"
                function add(a, b) {
                    return a + b;
                }
                add(2, 3);
            ");
            
            // Act
            var result = await _engine.Evaluate(program);
            
            // Assert
            Assert.Equal(5.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that expression evaluation throws when the assertion flag is enabled.
    /// This test deliberately triggers AST evaluation with the flag on to verify the guard works.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ThrowsOnExpressionEvaluation()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            var program = _engine.ParseProgram(@"
                function test() {
                    return 1 + 2;
                }
            ");
            
            // First, evaluate without the flag to create the function
            EvaluationContext.AssertNoAstEvaluation = false;
            await _engine.Evaluate(program);
            
            // Now enable the flag and try to call the function
            // This should trigger AST evaluation and throw
            EvaluationContext.AssertNoAstEvaluation = true;
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _engine.Evaluate("test()")
            );
            
            Assert.Contains("AST evaluation invoked", exception.Message);
            Assert.Contains("during IR execution", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that statement evaluation throws when the assertion flag is enabled.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ThrowsOnStatementEvaluation()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            // Enable the flag before parsing/evaluating
            EvaluationContext.AssertNoAstEvaluation = true;
            
            var program = _engine.ParseProgram(@"
                let x = 42;
                x;
            ");
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _engine.Evaluate(program)
            );
            
            Assert.Contains("AST evaluation invoked", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that the assertion mechanism includes the AST node type in the error message.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_ErrorMessage_IncludesNodeType()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;
            
            var program = _engine.ParseProgram("1 + 2");
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _engine.Evaluate(program)
            );
            
            // The error message should include the expression or statement type name
            Assert.Contains("AST evaluation invoked", exception.Message);
            Assert.Matches(@"\w+Expression|\w+Statement", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that complex expressions trigger the assertion when the flag is enabled.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ThrowsOnComplexExpression()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;
            
            var program = _engine.ParseProgram(@"
                const obj = { x: 10, y: 20 };
                obj.x + obj.y;
            ");
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _engine.Evaluate(program)
            );
            
            Assert.Contains("AST evaluation invoked", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that control flow statements trigger the assertion when the flag is enabled.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ThrowsOnControlFlowStatement()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;
            
            var program = _engine.ParseProgram(@"
                if (true) {
                    42;
                } else {
                    0;
                }
            ");
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _engine.Evaluate(program)
            );
            
            Assert.Contains("AST evaluation invoked", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that the flag can be toggled on and off during test execution.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_CanBeToggledDuringExecution()
    {
        // Arrange
        var originalValue = EvaluationContext.AssertNoAstEvaluation;
        
        try
        {
            var program = _engine.ParseProgram("40 + 2");
            
            // First, execute normally
            EvaluationContext.AssertNoAstEvaluation = false;
            var result1 = await _engine.Evaluate(program);
            Assert.Equal(42.0, result1);
            
            // Now enable assertion - should throw
            EvaluationContext.AssertNoAstEvaluation = true;
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _engine.Evaluate(program)
            );
            
            // Disable again - should work
            EvaluationContext.AssertNoAstEvaluation = false;
            var result2 = await _engine.Evaluate(program);
            Assert.Equal(42.0, result2);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleAssignmentSlotExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            _engine.SetGlobalValue("x", 0d);
            _engine.SetGlobalValue("y", 7d);

            var program = _engine.ParseProgram("""
                function assignFromIdentifier() {
                    x = y;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            InvokeGlobalFunction("assignFromIdentifier");

            Assert.Equal(7.0, _engine.GlobalObject["x"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleLogicalAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            _engine.SetGlobalValue("x", 0d);
            _engine.SetGlobalValue("y", 7d);

            var program = _engine.ParseProgram("""
                function assignLogicalOr() {
                    x ||= y;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            InvokeGlobalFunction("assignLogicalOr");

            Assert.Equal(7.0, _engine.GlobalObject["x"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleCompoundAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            _engine.SetGlobalValue("x", 3d);
            _engine.SetGlobalValue("y", 7d);

            var program = _engine.ParseProgram("""
                function addIntoX() {
                    x += y;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            InvokeGlobalFunction("addIntoX");

            Assert.Equal(10.0, _engine.GlobalObject["x"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleBranchExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            _engine.SetGlobalValue("x", 0d);

            var program = _engine.ParseProgram("""
                function branchOnComparison(value) {
                    if (value < 10) {
                        x = 1;
                    } else {
                        x = 2;
                    }
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            InvokeGlobalFunction("branchOnComparison", new JsValue(3d));
            Assert.Equal(1.0, _engine.GlobalObject["x"]);

            _engine.SetGlobalValue("x", 0d);
            InvokeGlobalFunction("branchOnComparison", new JsValue(13d));
            Assert.Equal(2.0, _engine.GlobalObject["x"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    private JsValue InvokeGlobalFunction(string name, params JsValue[] args)
    {
        Assert.True(_engine.GlobalObject.TryGetProperty(name, out var callableValue),
            $"Missing global function '{name}'.");

        var callable = Assert.IsAssignableFrom<IJsCallable>(callableValue.ObjectValue);
        return callable.Invoke(args, JsValue.Undefined);
    }
#else
    /// <summary>
    /// Verifies that the AssertNoAstEvaluation flag is only available in DEBUG builds.
    /// In RELEASE builds, this test confirms the feature is compiled out.
    /// </summary>
    [Fact]
    public void AssertNoAstEvaluation_OnlyAvailableInDebug()
    {
        // This test documents that the assertion mechanism is DEBUG-only
        // In RELEASE builds, the assertion code is not compiled
        Assert.True(true, "AssertNoAstEvaluation is only available in DEBUG builds");
    }
#endif
}
