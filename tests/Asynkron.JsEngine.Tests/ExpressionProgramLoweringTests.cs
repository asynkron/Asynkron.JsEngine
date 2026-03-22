using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
public sealed class ExpressionProgramLoweringTests : IAsyncLifetime
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

    [Fact]
    public async Task ReturnInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function returnSimple(left, right) {
                return left + right;
            }
            """, "returnSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>().Where(i => i.ReturnProgram is not null));
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task ThrowInstruction_SimpleLogicalExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function throwSimple(value) {
                throw value || 7;
            }
            """, "throwSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<ThrowInstruction>().Where(i => i.ThrowProgram is not null));
        Assert.Null(instruction.Expression);
        AssertProgramContains<JumpIfTrueExpressionOp>(instruction.ThrowProgram);
    }

    [Fact]
    public async Task SimpleVariableDeclaration_SimpleInitializer_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function declareSimple(value) {
                let next = value + 1;
                return next;
            }
            """, "declareSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<SimpleVariableDeclarationInstruction>()
            .Where(i => i.TargetSymbol.Name == "next"));
        Assert.Null(instruction.Initializer);
        Assert.NotNull(instruction.InitializerProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.InitializerProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task YieldInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function* yieldSimple(value) {
                yield value + 1;
            }
            """, "yieldSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldInstruction>().Where(i => i.YieldProgram is not null));
        Assert.Null(instruction.YieldExpression);
        AssertProgramContains<BinaryExpressionOp>(instruction.YieldProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task YieldStarInstruction_SimpleIdentifierIterable_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function* relay(items) {
                yield* items;
            }
            """, "relay");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldStarInstruction>().Where(i => i.IterableProgram is not null));
        Assert.Null(instruction.IterableExpression);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.IterableProgram, op => op.Name.Name == "items");
    }

    [Fact]
    public async Task AssignmentSlotInstruction_SimpleIdentifierValue_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function assignFrom(value) {
                let current = 0;
                current = value;
                return current;
            }
            """, "assignFrom");

        var instruction = Assert.Single(plan.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(instruction.ValueExpression);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ValueProgram, op => op.Name.Name == "value");
    }

    [Fact]
    public async Task LogicalCompoundAssignmentSlotInstruction_SimpleIdentifierValue_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function assignLogical(flag, value) {
                let current = flag;
                current ||= value;
                return current;
            }
            """, "assignLogical");

        var instruction = Assert.Single(plan.Instructions.OfType<LogicalCompoundAssignmentSlotInstruction>());
        Assert.Null(instruction.RhsExpression);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.RhsProgram, op => op.Name.Name == "value");
    }

    [Fact]
    public async Task CompoundAssignmentSlotInstruction_SimpleIdentifierValue_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function addValue(base, value) {
                let current = base;
                current += value;
                return current;
            }
            """, "addValue");

        var instruction = Assert.Single(plan.Instructions.OfType<CompoundAssignmentSlotInstruction>());
        Assert.Null(instruction.RhsExpression);
        Assert.True(instruction.RhsExpressionOps.HasValue);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            new ExpressionProgram(instruction.RhsExpressionOps.Value),
            op => op.Name.Name == "value");
    }

    [Fact]
    public async Task ScriptExpressionStatement_SimpleIdentifier_IsLoweredToExpressionProgram()
    {
        var program = _engine.ParseProgram("""
            let value = 41;
            value;
            """);

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var declaration = Assert.Single(cache.Plan.Instructions.OfType<SimpleVariableDeclarationInstruction>()
            .Where(i => i.TargetSymbol.Name == "value"));
        Assert.Null(declaration.Initializer);
        Assert.NotNull(declaration.InitializerProgram);

        var expressionStatement = Assert.Single(cache.Plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
            .Where(i => i.ExpressionOps is not null));
        Assert.Null(expressionStatement.Expression);
        Assert.True(expressionStatement.ExpressionOps.HasValue);
        var expressionOps = expressionStatement.ExpressionOps.Value;
        AssertProgramContains<LoadIdentifierExpressionOp>(
            new ExpressionProgram(expressionOps),
            op => op.Name.Name == "value");
    }

    [Fact]
    public async Task IteratorInitInstruction_SimpleIterableIdentifier_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function firstItem(items) {
                for (const item of items) {
                    return item;
                }
                return 0;
            }
            """, "firstItem");

        var instruction = Assert.Single(plan.Instructions.OfType<IteratorInitInstruction>());
        Assert.Null(instruction.IterableExpression);
        Assert.NotNull(instruction.IterableSource);
        Assert.True(instruction.IterableExpressionOps.HasValue);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            new ExpressionProgram(instruction.IterableExpressionOps.Value),
            op => op.Name.Name == "items");
    }

    [Fact]
    public async Task ForInInitInstruction_SimpleObjectIdentifier_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function firstKey(source) {
                for (const key in source) {
                    return key;
                }
                return "missing";
            }
            """, "firstKey");

        var instruction = Assert.Single(plan.Instructions.OfType<ForInInitInstruction>());
        Assert.Null(instruction.ObjectExpression);
        Assert.NotNull(instruction.ObjectSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ObjectProgram, op => op.Name.Name == "source");
    }

    [Fact]
    public async Task EnterWithInstruction_SimpleObjectIdentifier_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function readWith(scopeObj) {
                with (scopeObj) {
                    return answer;
                }
            }
            """, "readWith");

        var instruction = Assert.Single(plan.Instructions.OfType<EnterWithInstruction>());
        Assert.Null(instruction.ObjectExpression);
        Assert.NotNull(instruction.ObjectSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ObjectProgram, op => op.Name.Name == "scopeObj");
    }

    [Fact]
    public async Task ArrayDestructuringInitInstruction_SimpleIdentifierSource_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function sumFirstTwo(values) {
                const [first, second] = values;
                return first + second;
            }
            """, "sumFirstTwo");

        var instruction = Assert.Single(plan.Instructions.OfType<ArrayDestructuringInitInstruction>());
        Assert.Null(instruction.SourceExpression);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.SourceProgram, op => op.Name.Name == "values");
    }

    [Fact]
    public async Task BindingVariableDeclarationInstruction_ObjectInitializer_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function sumPoint() {
                const { x, y } = { x: 19, y: 23 };
                return x + y;
            }
            """, "sumPoint");

        var instruction = Assert.Single(plan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(instruction.Target);
        var targetProgram = Assert.IsType<ObjectBindingTargetProgram>(instruction.TargetProgram);
        Assert.Collection(
            targetProgram.Properties,
            property => Assert.Equal("x", property.Name),
            property => Assert.Equal("y", property.Name));
        Assert.Null(instruction.Initializer);
        AssertProgramContains<CreateObjectExpressionOp>(instruction.InitializerProgram);
        AssertProgramContains<DefineObjectPropertyExpressionOp>(instruction.InitializerProgram, op => op.PropertyName == "x");
        AssertProgramContains<DefineObjectPropertyExpressionOp>(instruction.InitializerProgram, op => op.PropertyName == "y");
    }

    [Fact]
    public async Task ArrayDestructuringInitInstruction_InlineArrayLiteralSource_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function sumInlineArray() {
                const [first, second] = [19, 23];
                return first + second;
            }
            """, "sumInlineArray");

        var instruction = Assert.Single(plan.Instructions.OfType<ArrayDestructuringInitInstruction>());
        Assert.Null(instruction.SourceExpression);
        AssertProgramContains<CreateArrayExpressionOp>(instruction.SourceProgram);
        AssertProgramContains<ArrayPushExpressionOp>(instruction.SourceProgram);
    }

    [Fact]
    public async Task ReturnInstruction_ConditionalExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function pick(flag, left, right) {
                return flag ? left : right;
            }
            """, "pick");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>().Where(i => i.ReturnProgram is not null));
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfFalseExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<JumpExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_NamedMemberExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function readX(point) {
                return point.x;
            }
            """, "readX");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>().Where(i => i.ReturnProgram is not null));
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<GetNamedPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "x");
    }

    [Fact]
    public async Task ReturnInstruction_ComputedMemberExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function readKey(point, key) {
                return point[key];
            }
            """, "readKey");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>().Where(i => i.ReturnProgram is not null));
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task BindingVariableDeclarationInstruction_ComputedObjectLiteralInitializer_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function readComputedPoint(key) {
                const { value } = { [key]: 42, value: 7 };
                return value;
            }
            """, "readComputedPoint");

        var instruction = Assert.Single(plan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(instruction.Target);
        Assert.IsType<ObjectBindingTargetProgram>(instruction.TargetProgram);
        Assert.Null(instruction.Initializer);
        AssertProgramContains<DefineComputedObjectPropertyExpressionOp>(instruction.InitializerProgram);
    }

    [Fact]
    public async Task BindingVariableDeclarationInstruction_ArrayBindingTarget_IsLoweredToBindingProgram()
    {
        var plan = await GetFunctionPlan("""
            function pickSecond(values) {
                const [first = 19, second] = values;
                return second;
            }
            """, "pickSecond");

        var instruction = Assert.Single(plan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(instruction.Target);
        var targetProgram = Assert.IsType<ArrayBindingTargetProgram>(instruction.TargetProgram);
        Assert.Collection(
            targetProgram.Elements,
            element =>
            {
                Assert.IsType<IdentifierBindingTargetProgram>(element.Target);
                Assert.NotNull(element.DefaultProgram);
            },
            element =>
            {
                Assert.IsType<IdentifierBindingTargetProgram>(element.Target);
                Assert.Null(element.DefaultProgram);
            });
    }

    [Fact]
    public async Task BindingVariableDeclarationInstruction_ObjectBindingTargetWithComputedKey_IsLoweredToBindingProgram()
    {
        var plan = await GetFunctionPlan("""
            function readComputed(source, key) {
                const { [key]: value = 23 } = source;
                return value;
            }
            """, "readComputed");

        var instruction = Assert.Single(plan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(instruction.Target);
        var targetProgram = Assert.IsType<ObjectBindingTargetProgram>(instruction.TargetProgram);
        var property = Assert.Single(targetProgram.Properties);
        Assert.NotNull(property.NameProgram);
        Assert.NotNull(property.DefaultProgram);
        Assert.IsType<IdentifierBindingTargetProgram>(property.Target);
        AssertProgramContains<LoadIdentifierExpressionOp>(property.NameProgram, op => op.Name.Name == "key");
    }

    [Fact]
    public async Task ReturnInstruction_OptionalComputedMemberExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function maybeRead(point, key) {
                return point?.[key];
            }
            """, "maybeRead");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>().Where(i => i.ReturnProgram is not null));
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task CatchDestructuring_UsesEnterCatchPlusBindingDeclaration()
    {
        var plan = await GetFunctionPlan("""
            function readThrown() {
                try {
                    throw { x: 19, y: 23 };
                } catch ({ x, y }) {
                    return x + y;
                }
            }
            """, "readThrown");

        var enterCatch = Assert.Single(plan.Instructions.OfType<EnterCatchInstruction>());
        Assert.NotNull(enterCatch.CatchParameterSymbol);

        var bindingInstruction = Assert.Single(plan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(bindingInstruction.Target);
        Assert.IsType<ObjectBindingTargetProgram>(bindingInstruction.TargetProgram);
        Assert.Null(bindingInstruction.Initializer);
        Assert.NotNull(bindingInstruction.InitializerProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            bindingInstruction.InitializerProgram,
            op => ReferenceEquals(op.Name, enterCatch.CatchParameterSymbol));
    }

    [Fact]
    public async Task PushEnvironmentInstruction_DoesNotRetainSourceBlockInPublishedPlan()
    {
        var plan = await GetFunctionPlan("""
            function scoped() {
                {
                    let value = 42;
                    return value;
                }
            }
            """, "scoped");

        var pushInstruction = Assert.Single(plan.Instructions.OfType<PushEnvironmentInstruction>());
        Assert.Null(pushInstruction.SourceBlock);
    }

    private async Task<ExecutionPlan> GetFunctionPlan(string source, string functionName)
    {
        var program = _engine.ParseProgram(source);
        await _engine.Evaluate(program);

        var function = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration declaration && declaration.Name.Name == functionName));

        var cache = ((IAstCacheable<ExecutionPlanCache>)function.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static void AssertProgramContains<TOp>(ExpressionProgram? program, Func<TOp, bool>? predicate = null)
        where TOp : ExpressionOp
    {
        Assert.NotNull(program);
        var match = program.Value.Operations.OfType<TOp>().FirstOrDefault(op => predicate is null || predicate(op));
        Assert.NotNull(match);
    }
}
