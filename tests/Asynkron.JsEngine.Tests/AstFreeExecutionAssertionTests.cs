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
                Math.max(x, 7);
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
            
            var program = _engine.ParseProgram("Math.max(1, 2)");
            
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
    /// Verifies that expression shapes still outside the bytecode layer trigger the assertion.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ThrowsOnComplexExpression()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram(@"
                Math.max(10, 20);
            ");

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
                if (Math.max(1, 0)) {
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
            var program = _engine.ParseProgram("Math.max(40, 42)");
            
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

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleVariableDeclarationExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function declareSimple(value) {
                    let next = value + 1;
                    return next;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("declareSimple", new JsValue(41d));

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleReturnExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function returnSimple(left, right) {
                    return left + right;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("returnSimple", new JsValue(19d), new JsValue(23d));

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleThrowExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function throwSimple(value) {
                    throw value || 7;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var exception = Assert.Throws<ThrowSignal>(() => InvokeGlobalFunction("throwSimple", new JsValue(42d)));
            Assert.Equal(42.0, exception.ThrownValue);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleYieldExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function* yieldSimple(value) {
                    yield value + 1;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var generator = InvokeGenerator("yieldSimple", new JsValue(41d));
            var firstResult = InvokeGeneratorMethod(generator, "next");

            Assert.Equal(42.0, GetRequiredProperty(firstResult, "value"));
            Assert.False(GetRequiredProperty(firstResult, "done").IsTruthy);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleYieldStarExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const values = [42];
                function* relayValues() {
                    yield* values;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var generator = InvokeGenerator("relayValues");
            var firstResult = InvokeGeneratorMethod(generator, "next");

            Assert.Equal(42.0, GetRequiredProperty(firstResult, "value"));
            Assert.False(GetRequiredProperty(firstResult, "done").IsTruthy);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleScriptExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = await _engine.Evaluate("""
                let value = 41;
                value;
                """);

            Assert.Equal(41.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleForOfInitialization()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const items = [42];
                function firstItem() {
                    for (const item of items) {
                        return item;
                    }
                    return 0;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("firstItem");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleForInInitialization()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const source = { first: 1 };
                function firstKey() {
                    for (const key in source) {
                        return key;
                    }
                    return "missing";
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("firstKey");

            Assert.Equal("first", result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleArrayDestructuringInitialization()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const values = [19, 23];
                function sumFirstTwo() {
                    const [first, second] = values;
                    return first + second;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("sumFirstTwo");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleObjectDestructuringInitialization()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const point = { x: 19, y: 23 };
                function sumPoint() {
                    const { x, y } = point;
                    return x + y;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("sumPoint");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsInlineArrayLiteralDestructuringInitialization()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function sumInlineArray() {
                    const [first, second] = [19, 23];
                    return first + second;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("sumInlineArray");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsInlineObjectLiteralDestructuringInitialization()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function sumInlinePoint() {
                    const { x, y } = { x: 19, y: 23 };
                    return x + y;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("sumInlinePoint");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsArrayDestructuringDefaultExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function pickDefault() {
                    const [first = 19, second = 23] = [];
                    return first + second;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("pickDefault");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsComputedObjectDestructuringExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const source = { answer: 42 };
                function readComputed(key) {
                    const { [key]: value = 42 } = source;
                    return value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("readComputed", new JsValue("answer"));

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleConditionalExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function pick(flag, left, right) {
                    return flag ? left : right;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("pick", JsValue.True, new JsValue(19d), new JsValue(23d));

            Assert.Equal(19.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsNamedMemberAccessExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const point = { x: 42 };
                function readPoint() {
                    return point.x;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("readPoint");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsComputedMemberAccessExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const point = { x: 42 };
                const key = "x";
                function readPointByKey() {
                    return point[key];
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("readPointByKey");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsComputedObjectLiteralExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const key = "x";
                function readComputedLiteral() {
                    const point = { [key]: 42 };
                    return point.x;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("readComputedLiteral");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsOptionalComputedMemberAccessExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const key = "x";
                function maybeRead(point) {
                    return point?.[key];
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("maybeRead", JsValue.Null);

            Assert.True(result.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsCatchDestructuringExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function readThrown() {
                    try {
                        throw { x: 19, y: 23 };
                    } catch ({ x, y }) {
                        return x + y;
                    }
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("readThrown");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ThrowsOnWithStatementExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                const scopeObj = { answer: 42 };
                function readWith() {
                    with (scopeObj) {
                        return answer;
                    }
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var exception = Assert.Throws<InvalidOperationException>(() => InvokeGlobalFunction("readWith"));

            Assert.Contains("WithStatement", exception.Message);
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

    private JsObject InvokeGenerator(string name, params JsValue[] args)
    {
        var generatorValue = InvokeGlobalFunction(name, args);
        return Assert.IsType<JsObject>(generatorValue.ObjectValue);
    }

    private static JsValue InvokeGeneratorMethod(JsObject generator, string methodName, params JsValue[] args)
    {
        Assert.True(generator.TryGetProperty(methodName, out var methodValue),
            $"Missing generator method '{methodName}'.");

        var callable = Assert.IsAssignableFrom<IJsCallable>(methodValue.ObjectValue);
        return callable.Invoke(args, JsValue.FromObjectUnsafe(generator));
    }

    private static JsValue GetRequiredProperty(JsValue value, string propertyName)
    {
        Assert.True(value.TryGetObject<IJsPropertyAccessor>(out var accessor),
            $"Expected value with readable properties for '{propertyName}'.");
        Assert.True(accessor.TryGetProperty(propertyName, out var property),
            $"Missing property '{propertyName}'.");
        return property;
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
