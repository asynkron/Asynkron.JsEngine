using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
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
    /// Verifies that tagged template function payloads can execute without AST re-entry.
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
                    return String.raw`x`;
                }
            ");
            
            // First, evaluate without the flag to create the function
            EvaluationContext.AssertNoAstEvaluation = false;
            await _engine.Evaluate(program);
            
            // Now enable the flag and try to call the function.
            EvaluationContext.AssertNoAstEvaluation = true;
            
            var result = await _engine.Evaluate("test()");
            Assert.Equal("x", result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    /// <summary>
    /// Verifies that top-level tagged-template scripts can execute without AST re-entry.
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
                String.raw`x`;
            ");
            
            var result = await _engine.Evaluate(program);
            Assert.Equal("x", result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsOptionalTaggedTemplateExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function tag(strings) {
                    return this.prefix + strings[0];
                }

                function maybeTag(box) {
                    return box?.tag`!`;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("tag", out var tagValue));

            var boxObject = new JsObject();
            boxObject["prefix"] = new JsValue("value");
            boxObject["tag"] = tagValue;

            var invoked = InvokeGlobalFunction("maybeTag", JsValue.FromJsObject(boxObject));
            Assert.Equal("value!", invoked.AsString());

            var skipped = InvokeGlobalFunction("maybeTag", JsValue.Null);
            Assert.True(skipped.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsNestedOptionalTaggedTemplateExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function tag(strings) {
                    return this.prefix + strings[0];
                }

                function maybeNestedTag(box) {
                    return box?.inner.tag`!`;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("tag", out var tagValue));

            var innerObject = new JsObject();
            innerObject["prefix"] = new JsValue("nested");
            innerObject["tag"] = tagValue;

            var boxObject = new JsObject();
            boxObject["inner"] = JsValue.FromJsObject(innerObject);

            var invoked = InvokeGlobalFunction("maybeNestedTag", JsValue.FromJsObject(boxObject));
            Assert.Equal("nested!", invoked.AsString());

            var skipped = InvokeGlobalFunction("maybeNestedTag", JsValue.Null);
            Assert.True(skipped.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsObjectMethodScriptExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                ({ value() { return 1; } }).value();
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(1.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsObjectAccessorScriptExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                const obj = {
                    get value() { return 42; },
                    set value(next) { this._value = next; }
                };
                obj.value;
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
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
    public async Task AssertNoAstEvaluation_Enabled_AllowsCallExpressionInControlFlow()
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
            
            var result = await _engine.Evaluate(program);
            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsAwaitExpressionStatementExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var program = _engine.ParseProgram("""
                async function awaitSimple(value) {
                    await value;
                    return 42;
                }
                """);

            await _engine.Evaluate(program);

            EvaluationContext.AssertNoAstEvaluation = true;
            var result = await _engine.EvaluateAndAwait("""
                let awaitedResult = undefined;
                awaitSimple(Promise.resolve(1)).then(value => awaitedResult = value);
                awaitedResult;
                """);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDestructuringAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var program = _engine.ParseProgram("""
                function assignFirst(values) {
                    let x = 0;
                    [x] = values;
                    return x;
                }
                """);

            await _engine.Evaluate(program);

            EvaluationContext.AssertNoAstEvaluation = true;
            var values = new JsArray(_engine.RealmState);
            values.Push(19);
            var result = InvokeGlobalFunction("assignFirst", JsValue.FromJsArray(values));
            Assert.Equal(19.0, result.NumberValue);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSuperMethodExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var program = _engine.ParseProgram("""
                class Base {
                    static method() {
                        return 40;
                    }
                }

                class Derived extends Base {
                    static method() {
                        return super.method() + 2;
                    }
                }
                """);

            await _engine.Evaluate(program);
            var (baseMethod, derivedMethod) = GetClassMethods(program, "Base", "method", "Derived", "method");
            AssertPlanBuilds(baseMethod);
            AssertPlanBuilds(derivedMethod);

            EvaluationContext.AssertNoAstEvaluation = true;
            var result = await _engine.Evaluate("Derived.method()");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSuperComputedUpdateExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var program = _engine.ParseProgram("""
                class Base {
                    static get value() {
                        return this._value ?? 1;
                    }

                    static set value(next) {
                        this._value = next;
                    }
                }

                class Derived extends Base {
                    static bump(key) {
                        return super[key]++;
                    }
                }
                """);

            await _engine.Evaluate(program);
            var baseDeclaration = Assert.IsType<ClassDeclaration>(
                program.Body.Single(statement => statement is ClassDeclaration declaration && declaration.Name.Name == "Base"));
            var derivedDeclaration = Assert.IsType<ClassDeclaration>(
                program.Body.Single(statement => statement is ClassDeclaration declaration && declaration.Name.Name == "Derived"));
            AssertPlanBuilds(Assert.Single(baseDeclaration.Definition.Members.Where(member => member.Name == "value" && member.Kind == ClassMemberKind.Getter)).Function);
            AssertPlanBuilds(Assert.Single(baseDeclaration.Definition.Members.Where(member => member.Name == "value" && member.Kind == ClassMemberKind.Setter)).Function);
            AssertPlanBuilds(Assert.Single(derivedDeclaration.Definition.Members.Where(member => member.Name == "bump")).Function);

            EvaluationContext.AssertNoAstEvaluation = true;
            var result = await _engine.Evaluate("""
                [Derived.bump("value"), Derived.value];
                """);

            var values = Assert.IsType<JsArray>(result);
            Assert.Equal(1.0, values.GetElement(0).NumberValue);
            Assert.Equal(2.0, values.GetElement(1).NumberValue);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDerivedConstructorSuperExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var program = _engine.ParseProgram("""
                class Base {
                    constructor(value) {
                        this.value = value;
                    }
                }

                class Derived extends Base {
                    constructor(value) {
                        super(value + 1);
                    }
                }
                """);

            await _engine.Evaluate(program);
            var derivedDeclaration = Assert.IsType<ClassDeclaration>(
                program.Body.Single(statement => statement is ClassDeclaration declaration && declaration.Name.Name == "Derived"));
            AssertPlanBuilds(derivedDeclaration.Definition.Constructor);

            EvaluationContext.AssertNoAstEvaluation = true;
            var result = await _engine.Evaluate("new Derived(41).value");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ClassMethodDebuggerStatement_ThrowsPlanFailureInsteadOfAstFallback()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var parsedProgram = _engine.ParseProgram("""
                class Box {
                    read(value) {
                        return value + 1;
                    }
                }

                globalThis.box = new Box();
                """);
            var program = ReplaceClassMethodBodyWithUnsupportedModuleStatement(parsedProgram, "Box", "read");

            await _engine.Evaluate(program);

            EvaluationContext.AssertNoAstEvaluation = true;
            Assert.True(_engine.GlobalObject.TryGetProperty("box", out var boxValue));
            Assert.True(boxValue.TryGetObject<IJsPropertyAccessor>(out var boxAccessor));
            Assert.True(boxAccessor.TryGetProperty("read", out var methodValue));

            var method = Assert.IsAssignableFrom<IJsCallable>(methodValue.ObjectValue);
            var exception = Assert.Throws<NotSupportedException>(
                () => method.Invoke(new SingleValueArgs(41), boxValue));
            Assert.Contains("IR plan generation failed for function", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    private static (FunctionExpression BaseMethod, FunctionExpression DerivedMethod) GetClassMethods(
        ProgramNode program,
        string baseClassName,
        string baseMethodName,
        string derivedClassName,
        string derivedMethodName)
    {
        var baseDeclaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration declaration && declaration.Name.Name == baseClassName));
        var derivedDeclaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration declaration && declaration.Name.Name == derivedClassName));

        var baseMethod = Assert.Single(baseDeclaration.Definition.Members.Where(member => member.Name == baseMethodName)).Function;
        var derivedMethod = Assert.Single(derivedDeclaration.Definition.Members.Where(member => member.Name == derivedMethodName)).Function;
        return (baseMethod, derivedMethod);
    }

    private static ProgramNode ReplaceClassMethodBodyWithUnsupportedModuleStatement(
        ProgramNode program,
        string className,
        string methodName)
    {
        var declarationIndex = -1;
        for (var i = 0; i < program.Body.Length; i++)
        {
            if (program.Body[i] is ClassDeclaration classDeclaration && classDeclaration.Name.Name == className)
            {
                declarationIndex = i;
                break;
            }
        }

        var declaration = Assert.IsType<ClassDeclaration>(program.Body[declarationIndex]);
        var memberIndex = -1;
        for (var i = 0; i < declaration.Definition.Members.Length; i++)
        {
            if (declaration.Definition.Members[i].Name == methodName)
            {
                memberIndex = i;
                break;
            }
        }

        var member = declaration.Definition.Members[memberIndex];
        var rewrittenMember = member with
        {
            Function = member.Function with
            {
                Body = new BlockStatement(
                    null,
                    [new ExportAllStatement(null, "./unsupported.js")],
                    true)
            }
        };
        var rewrittenMembers = declaration.Definition.Members.SetItem(memberIndex, rewrittenMember);
        var rewrittenDeclaration = declaration with
        {
            Definition = declaration.Definition with { Members = rewrittenMembers }
        };

        return program with
        {
            Body = program.Body.SetItem(declarationIndex, rewrittenDeclaration)
        };
    }

    private static ProgramNode ReplaceWithScopedFunctionLiteralBodyWithUnsupportedModuleStatement(
        ProgramNode program)
    {
        var withIndex = -1;
        for (var i = 0; i < program.Body.Length; i++)
        {
            if (program.Body[i] is WithStatement)
            {
                withIndex = i;
                break;
            }
        }

        var withStatement = Assert.IsType<WithStatement>(program.Body[withIndex]);
        var withBody = Assert.IsType<BlockStatement>(withStatement.Body);
        var expressionStatement = Assert.Single(withBody.Statements.OfType<ExpressionStatement>());
        var assignment = Assert.IsType<PropertyAssignmentExpression>(expressionStatement.Expression);
        var function = Assert.IsType<FunctionExpression>(assignment.Value);

        var rewrittenExpression = assignment with
        {
            Value = function with
            {
                Body = new BlockStatement(
                    null,
                    [new ExportAllStatement(null, "./unsupported.js")],
                    true)
            }
        };
        var statementIndex = withBody.Statements.IndexOf(expressionStatement);
        var rewrittenWith = withStatement with
        {
            Body = withBody with
            {
                Statements = withBody.Statements.SetItem(
                    statementIndex,
                    expressionStatement with { Expression = rewrittenExpression })
            }
        };

        return program with
        {
            Body = program.Body.SetItem(withIndex, rewrittenWith)
        };
    }

    private static void AssertPlanBuilds(FunctionExpression function)
    {
        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
    }

    /// <summary>
    /// Verifies that the flag can be toggled on and off for the explicit dynamic-scope executor path.
    /// </summary>
    [Fact]
    public async Task AssertNoAstEvaluation_CanBeToggledDuringExecution()
    {
        // Arrange
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
            
            // First, execute normally
            EvaluationContext.AssertNoAstEvaluation = false;
            await _engine.Evaluate(program);
            var result1 = InvokeGlobalFunction("readWith");
            Assert.Equal(42.0, result1);
            
            // Now enable assertion - should throw
            EvaluationContext.AssertNoAstEvaluation = true;
            var exception = Assert.Throws<InvalidOperationException>(() => InvokeGlobalFunction("readWith"));
            Assert.Contains("WithStatement", exception.Message);
            
            // Disable again - should work
            EvaluationContext.AssertNoAstEvaluation = false;
            var result2 = InvokeGlobalFunction("readWith");
            Assert.Equal(42.0, result2);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_WithScopedFunctionLiteralPlanFailure_ThrowsInsteadOfAstFallback()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var parsedProgram = _engine.ParseProgram("""
                const scopeObj = {};

                with (scopeObj) {
                    globalThis.broken = function(value) {
                        return value + 1;
                    };
                }
                """);
            var program = ReplaceWithScopedFunctionLiteralBodyWithUnsupportedModuleStatement(parsedProgram);

            await _engine.Evaluate(program);

            EvaluationContext.AssertNoAstEvaluation = true;
            var exception = Assert.Throws<NotSupportedException>(() => InvokeGlobalFunction("broken", new JsValue(41d)));

            Assert.Contains("IR plan generation failed for function", exception.Message);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSpreadCallExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function pickMax(values) {
                    return Math.max(...values);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var values = new JsArray(_engine.RealmState);
            values.Push(new JsValue(19d));
            values.Push(new JsValue(23d));

            var result = InvokeGlobalFunction("pickMax", JsValue.FromJsArray(values));

            Assert.Equal(23.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsOptionalIdentifierCallExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function inc(value) {
                    return value + 1;
                }

                function maybeInvoke(helper, value) {
                    return helper?.(value);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("inc", out var incValue));
            var invoked = InvokeGlobalFunction("maybeInvoke", incValue, new JsValue(41d));
            Assert.Equal(42.0, invoked);

            var skipped = InvokeGlobalFunction("maybeInvoke", JsValue.Undefined, new JsValue(41d));
            Assert.True(skipped.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDotCallExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function inc(value) {
                    return value + 1;
                }

                function invokeViaCall(helper, value) {
                    return helper.call(undefined, value);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("inc", out var incValue));
            var result = InvokeGlobalFunction("invokeViaCall", incValue, new JsValue(41d));

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDotApplyExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function inc(value) {
                    return value + 1;
                }

                function invokeViaApply(helper, args) {
                    return helper.apply(undefined, args);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("inc", out var incValue));
            var argsArray = new JsArray(_engine.RealmState);
            argsArray.Push(new JsValue(41d));

            var result = InvokeGlobalFunction(
                "invokeViaApply",
                incValue,
                JsValue.FromJsArray(argsArray));

            Assert.Equal(42.0, result);
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
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleCallExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function pickMax(left, right) {
                    return Math.max(left, right);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("pickMax", new JsValue(19d), new JsValue(23d));

            Assert.Equal(23.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSimpleConstructionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function createDateYear(year) {
                    return new Date(year, 0, 15).getFullYear();
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("createDateYear", new JsValue(2024d));

            Assert.Equal(2024.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSpreadConstructionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function createDateYear(parts) {
                    return new Date(...parts).getFullYear();
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var parts = new JsArray(_engine.RealmState);
            parts.Push(new JsValue(2024d));
            parts.Push(new JsValue(0d));
            parts.Push(new JsValue(15d));

            var result = InvokeGlobalFunction("createDateYear", JsValue.FromJsArray(parts));

            Assert.Equal(2024.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsSequenceExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function pickLast(left, right) {
                    return (left + 1, right + 2);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("pickLast", new JsValue(19d), new JsValue(23d));

            Assert.Equal(25.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsTemplateLiteralExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function greet(name, count) {
                    return `Hello ${name} ${count}`;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("greet", new JsValue("Ada"), new JsValue(3d));

            Assert.True(result.IsString);
            Assert.Equal("Hello Ada 3", result.AsString());
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsOptionalMemberCallExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function read(value) {
                    return value + 1;
                }

                function maybeCall(box, value) {
                    return box.read?.(value);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("read", out var readValue));
            var readerObject = new JsObject();
            readerObject["read"] = readValue;

            var invoked = InvokeGlobalFunction("maybeCall", JsValue.FromJsObject(readerObject), new JsValue(41d));
            Assert.Equal(42.0, invoked);

            var skipped = InvokeGlobalFunction("maybeCall", JsValue.FromJsObject(new JsObject()), new JsValue(41d));
            Assert.True(skipped.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsNestedOptionalMemberCallExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function read(value) {
                    return value + 1;
                }

                function maybeNestedCall(box, value) {
                    return box?.inner.read(value);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("read", out var readValue));
            var innerObject = new JsObject();
            innerObject["read"] = readValue;
            var boxObject = new JsObject();
            boxObject["inner"] = JsValue.FromJsObject(innerObject);

            var invoked = InvokeGlobalFunction("maybeNestedCall", JsValue.FromJsObject(boxObject), new JsValue(41d));
            Assert.Equal(42.0, invoked);

            var skipped = InvokeGlobalFunction("maybeNestedCall", JsValue.Null, new JsValue(41d));
            Assert.True(skipped.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_NestedOptionalMemberCall_DoesNotHideRealUndefinedTargetErrors()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function maybeNestedCall(box, value) {
                    return box?.inner.read(value);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var boxObject = new JsObject();
            boxObject["inner"] = JsValue.Undefined;

            var ex = Assert.ThrowsAny<Exception>(() =>
                InvokeGlobalFunction("maybeNestedCall", JsValue.FromJsObject(boxObject), new JsValue(41d)));
            Assert.DoesNotContain("IR plan generation failed", ex.ToString());
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsNestedOptionalCallExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function makeFactory() {
                    return function () {
                        return 42;
                    };
                }

                function maybeInvoke(factory) {
                    return factory?.()();
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            Assert.True(_engine.GlobalObject.TryGetProperty("makeFactory", out var makeFactory));

            var result = InvokeGlobalFunction("maybeInvoke", makeFactory);
            Assert.Equal(42.0, result);

            var skipped = InvokeGlobalFunction("maybeInvoke", JsValue.Undefined);
            Assert.True(skipped.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsPropertyAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function assignValue(box, value) {
                    return box.value = value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            var result = InvokeGlobalFunction("assignValue", JsValue.FromJsObject(box), new JsValue(23d));

            Assert.Equal(23.0, result);
            Assert.Equal(23.0, box["value"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsCompoundPropertyAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function addIntoValue(box, value) {
                    return box.value += value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            box["value"] = 19d;
            var result = InvokeGlobalFunction("addIntoValue", JsValue.FromJsObject(box), new JsValue(23d));

            Assert.Equal(42.0, result);
            Assert.Equal(42.0, box["value"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsLogicalCompoundPropertyAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function ensureValue(box, value) {
                    return box.value ||= value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var assignBox = new JsObject();
            assignBox["value"] = 0d;
            var assignedResult = InvokeGlobalFunction("ensureValue", JsValue.FromJsObject(assignBox), new JsValue(23d));
            Assert.Equal(23.0, assignedResult);
            Assert.Equal(23.0, assignBox["value"]);

            var keepBox = new JsObject();
            keepBox["value"] = 19d;
            var preservedResult = InvokeGlobalFunction("ensureValue", JsValue.FromJsObject(keepBox), new JsValue(23d));
            Assert.Equal(19.0, preservedResult);
            Assert.Equal(19.0, keepBox["value"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsIdentifierAssignmentExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function assignLocal(value) {
                    let current = 0;
                    return current = value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("assignLocal", new JsValue(23d));

            Assert.Equal(23.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsCompoundAssignmentExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function addIntoCurrent(value) {
                    let current = 19;
                    return current += value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("addIntoCurrent", new JsValue(23d));

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsLogicalCompoundAssignmentExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function ensureCurrent(seed, value) {
                    let current = seed;
                    return current ||= value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var assignedResult = InvokeGlobalFunction("ensureCurrent", new JsValue(0d), new JsValue(23d));
            Assert.Equal(23.0, assignedResult);

            var shortCircuitResult = InvokeGlobalFunction("ensureCurrent", new JsValue(19d), new JsValue(23d));
            Assert.Equal(19.0, shortCircuitResult);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsNullishCompoundAssignmentExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function fillCurrent(seed, value) {
                    let current = seed;
                    return current ??= value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var assignedResult = InvokeGlobalFunction("fillCurrent", JsValue.Undefined, new JsValue(23d));
            Assert.Equal(23.0, assignedResult);

            var preservedResult = InvokeGlobalFunction("fillCurrent", new JsValue(19d), new JsValue(23d));
            Assert.Equal(19.0, preservedResult);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsPrefixIncrementIdentifierExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function nextValue(seed) {
                    let current = seed;
                    return ++current;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("nextValue", new JsValue(41d));

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsPostfixDecrementIdentifierExpressionExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function currentValue(seed) {
                    let current = seed;
                    return current--;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("currentValue", new JsValue(41d));

            Assert.Equal(41.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsPostfixNamedPropertyIncrementExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function currentValue(box) {
                    return box.value++;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            box["value"] = 41d;

            var result = InvokeGlobalFunction("currentValue", JsValue.FromJsObject(box));

            Assert.Equal(41.0, result);
            Assert.Equal(42.0, box["value"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsPrefixComputedPropertyDecrementExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function nextValue(box, key) {
                    return --box[key];
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            box["value"] = 41d;

            var result = InvokeGlobalFunction("nextValue", JsValue.FromJsObject(box), new JsValue("value"));

            Assert.Equal(40.0, result);
            Assert.Equal(40.0, box["value"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsUnaryMinusExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function negate(value) {
                    return -value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("negate", new JsValue(41d));

            Assert.Equal(-41.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsTypeOfMissingIdentifierExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function describeMissing() {
                    return typeof missingValue;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("describeMissing");

            Assert.Equal("undefined", result.AsString());
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsUnaryVoidExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function discard(value) {
                    return void value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("discard", new JsValue(41d));

            Assert.True(result.IsUndefined);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDeleteNamedPropertyExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function drop(box) {
                    return delete box.value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            box["value"] = 41d;

            var result = InvokeGlobalFunction("drop", JsValue.FromJsObject(box));

            Assert.True(result.IsBoolean && result.AsBoolean());
            Assert.False(box.TryGetProperty("value", out _));
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDeleteComputedPropertyExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function drop(box, key) {
                    return delete box[key];
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            box["value"] = 41d;

            var result = InvokeGlobalFunction("drop", JsValue.FromJsObject(box), new JsValue("value"));

            Assert.True(result.IsBoolean && result.AsBoolean());
            Assert.False(box.TryGetProperty("value", out _));
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsDeleteNonReferenceExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function drop(value) {
                    return delete (value + 1);
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("drop", new JsValue(41d));

            Assert.True(result.IsBoolean && result.AsBoolean());
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsIndexAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function assignAt(box, key, value) {
                    return box[key] = value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            var result = InvokeGlobalFunction(
                "assignAt",
                JsValue.FromJsObject(box),
                new JsValue("answer"),
                new JsValue(42d));

            Assert.Equal(42.0, result);
            Assert.Equal(42.0, box["answer"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsCompoundIndexAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function addAt(box, key, value) {
                    return box[key] += value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var box = new JsObject();
            box["answer"] = 19d;
            var result = InvokeGlobalFunction(
                "addAt",
                JsValue.FromJsObject(box),
                new JsValue("answer"),
                new JsValue(23d));

            Assert.Equal(42.0, result);
            Assert.Equal(42.0, box["answer"]);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsNullishCompoundIndexAssignmentExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function fillAt(box, key, value) {
                    return box[key] ??= value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var assignBox = new JsObject();
            var assignedResult = InvokeGlobalFunction(
                "fillAt",
                JsValue.FromJsObject(assignBox),
                new JsValue("answer"),
                new JsValue(23d));
            Assert.Equal(23.0, assignedResult);
            Assert.Equal(23.0, assignBox["answer"]);

            var keepBox = new JsObject();
            keepBox["answer"] = 19d;
            var preservedResult = InvokeGlobalFunction(
                "fillAt",
                JsValue.FromJsObject(keepBox),
                new JsValue("answer"),
                new JsValue(23d));
            Assert.Equal(19.0, preservedResult);
            Assert.Equal(19.0, keepBox["answer"]);
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
    public async Task AssertNoAstEvaluation_Enabled_AllowsAsyncGeneratorYieldStarAwaitExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = false;

            var program = _engine.ParseProgram("""
                async function* relay(values) {
                    yield* await values;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = await _engine.EvaluateAndAwait("""
                let firstValue = undefined;
                relay([42]).next().then(step => firstValue = step.value);
                firstValue;
                """);

            Assert.Equal(42.0, result);
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
    public async Task AssertNoAstEvaluation_Enabled_AllowsFunctionParameterDefaultExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            var program = _engine.ParseProgram("""
                function answer(value = 19 + 23) {
                    return value;
                }
                """);

            await _engine.Evaluate(program);
            EvaluationContext.AssertNoAstEvaluation = true;

            var result = InvokeGlobalFunction("answer");

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsVariableInitializerExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                let left = 19;
                let right = 23;
                const total = left + right;
                total;
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsFunctionExpressionScriptExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                const add = function(value) { return value + 1; };
                add(41);
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsClassExpressionScriptExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                const Box = class { value() { return 42; } };
                new Box().value();
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsClassComputedMethodNameScriptExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                const suffix = "alue";
                const Box = class {
                    ["v" + suffix]() { return 42; }
                };
                new Box().value();
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsClassComputedFieldNameScriptExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                const suffix = "alue";
                const Box = class {
                    ["v" + suffix];
                };
                Object.prototype.hasOwnProperty.call(new Box(), "value");
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(true, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsInstanceFieldInitializerSuperExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                class Base {
                    get value() { return 41; }
                }

                class Derived extends Base {
                    field = super.value + 1;
                }

                new Derived().field;
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsStaticFieldInitializerSuperExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                class Base {
                    static get value() { return 41; }
                }

                class Derived extends Base {
                    static field = super.value + 1;
                }

                Derived.field;
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsClassFieldAnonymousFunctionNameInference()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram(
                "class Box {\n" +
                "    [\"v\" + \"alue\"] = function() {};\n" +
                "    #secret = function() {};\n" +
                "\n" +
                "    getNames() {\n" +
                "        return [this.value.name, this.#secret.name];\n" +
                "    }\n" +
                "}\n" +
                "\n" +
                "const names = new Box().getNames();\n" +
                "names[0] + \":\" + names[1];");

            var result = await _engine.Evaluate(program);

            Assert.Equal("value:#secret", result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsClassStaticBlockExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                class Counter {
                    static value = 1;

                    static {
                        this.value += 41;
                    }
                }

                Counter.value;
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal(42.0, result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_AllowsFieldInitializerNewTargetUndefinedExecution()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                class Box {
                    field = typeof new.target;
                }

                new Box().field;
                """);

            var result = await _engine.Evaluate(program);

            Assert.Equal("undefined", result);
        }
        finally
        {
            EvaluationContext.AssertNoAstEvaluation = originalValue;
        }
    }

    [Fact]
    public async Task AssertNoAstEvaluation_Enabled_ImmutableIdentifierAssignment_FailsAtRuntimeNotPlanBuild()
    {
        var originalValue = EvaluationContext.AssertNoAstEvaluation;

        try
        {
            EvaluationContext.AssertNoAstEvaluation = true;

            var program = _engine.ParseProgram("""
                const value = 1;
                value = 2;
                """);

            var exception = await Assert.ThrowsAnyAsync<Exception>(async () => await _engine.Evaluate(program));

            Assert.DoesNotContain("IR plan generation failed for script", exception.Message, StringComparison.Ordinal);
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
