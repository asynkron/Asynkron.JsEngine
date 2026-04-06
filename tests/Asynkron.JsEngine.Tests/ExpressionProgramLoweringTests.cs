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

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        Assert.Equal(2, instruction.ReturnProgram!.Value.MaxStackDepth);
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

        var instruction = Assert.Single(plan.Instructions.OfType<ThrowInstruction>());
        Assert.Null(instruction.Expression);
        AssertProgramContains<JumpIfTrueExpressionOp>(instruction.ThrowProgram);
    }

    [Fact]
    public async Task ThrowInstruction_AwaitedIdentifier_UsesAwaitedProgram()
    {
        var plan = await GetFunctionPlan("""
            async function throwAwaited(valuePromise) {
                throw await valuePromise;
            }
            """, "throwAwaited");

        var instruction = Assert.Single(plan.Instructions.OfType<ThrowInstruction>(), i => i.AwaitedProgram is not null);
        Assert.Null(instruction.Expression);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.AwaitedProgram,
            op => op.Name.Name == "valuePromise");
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
, i => i.TargetSymbol.Name == "next");
        Assert.Null(instruction.Initializer);
        Assert.NotNull(instruction.InitializerProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.InitializerProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task SimpleVariableDeclaration_AwaitedInitializer_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function declareSimple(valuePromise) {
                let next = (await valuePromise) + 1;
                return next;
            }
            """, "declareSimple");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "valuePromise");

        var instruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.TargetSymbol.Name == "next");
        Assert.Null(instruction.Initializer);
        Assert.NotNull(instruction.InitializerProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.InitializerProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.InitializerProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiDeclaratorSimpleVariableDeclaration_NestedAwait_RewritesOffSuspendingInstruction()
    {
        var plan = await GetFunctionPlan("""
            async function declarePair(valuePromise) {
                let first = (await valuePromise) + 1, second = first + 1;
                return first + second;
            }
            """, "declarePair");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "valuePromise");

        var firstInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.TargetSymbol.Name == "first");
        Assert.NotNull(firstInstruction.InitializerProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            firstInstruction.InitializerProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));

        var secondInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.TargetSymbol.Name == "second");
        Assert.NotNull(secondInstruction.InitializerProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            secondInstruction.InitializerProgram,
            op => op.Name.Name == "first");

        Assert.DoesNotContain(
            plan.Instructions,
            static instruction => instruction.GetType().Name.Contains("SuspendingSimpleVariableDeclaration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task YieldInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function* yieldSimple(value) {
                yield value + 1;
            }
            """, "yieldSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldInstruction>(), i => i.YieldProgram is not null);
        AssertProgramContains<BinaryExpressionOp>(instruction.YieldProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task YieldInstruction_NestedAwaitOperand_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function* yieldNested(valuePromise) {
                yield (await valuePromise) + 1;
            }
            """, "yieldNested");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "valuePromise");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldInstruction>(), i => i.YieldProgram is not null);
        AssertProgramContains<BinaryExpressionOp>(instruction.YieldProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.YieldProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task YieldStarInstruction_SimpleIdentifierIterable_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function* relay(items) {
                yield* items;
            }
            """, "relay");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldStarInstruction>(), i => i.IterableProgram is not null);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.IterableProgram, op => op.Name.Name == "items");
    }

    [Fact]
    public async Task YieldStarInstruction_AwaitExpression_IsLoweredToAwaitedProgram()
    {
        var plan = await GetFunctionPlan("""
            async function* relay(items) {
                yield* await items;
            }
            """, "relay");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldStarInstruction>()
, i => i.AwaitedProgram is not null);
        Assert.Null(instruction.IterableProgram);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.AwaitedProgram, op => op.Name.Name == "items");
    }

    [Fact]
    public async Task YieldStarInstruction_NestedAwaitIterable_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function* relay(items) {
                yield* (await items).values();
            }
            """, "relay");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "items");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldStarInstruction>(), i => i.IterableProgram is not null);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.IterableProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
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
    public async Task AssignmentSlotInstruction_NestedAwaitValue_RewritesToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            async function assignFrom(valuePromise) {
                let current = 0;
                current = (await valuePromise) + 1;
                return current;
            }
            """, "assignFrom");

        var instruction = Assert.Single(plan.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(instruction.ValueExpression);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ValueProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_resume", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Instructions.OfType<EvaluateAndDiscardInstruction>(), _ => true);
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
    public async Task LogicalCompoundAssignmentSlotInstruction_NestedAwaitValue_RewritesOffSuspendingInstruction()
    {
        var plan = await GetFunctionPlan("""
            async function assignLogical(flag, valuePromise) {
                let current = flag;
                current ||= (await valuePromise) + 1;
                return current;
            }
            """, "assignLogical");

        Assert.Contains(
            plan.Instructions.OfType<AssignmentSlotInstruction>(),
            instruction => instruction.ValueProgram is not null);
        Assert.Contains(
            plan.Instructions.OfType<BranchInstruction>(),
            _ => true);
        Assert.DoesNotContain(plan.Instructions.OfType<EvaluateAndDiscardInstruction>(), _ => true);
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
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.RhsProgram,
            op => op.Name.Name == "value");
    }

    [Fact]
    public async Task CompoundAssignmentSlotInstruction_NestedAwaitValue_RewritesOffSuspendingInstruction()
    {
        var plan = await GetFunctionPlan("""
            async function addValue(base, valuePromise) {
                let current = base;
                current += (await valuePromise) + 1;
                return current;
            }
            """, "addValue");

        var instruction = Assert.Single(plan.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(instruction.ValueExpression);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ValueProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_resume", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Instructions.OfType<EvaluateAndDiscardInstruction>(), _ => true);
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
, i => i.TargetSymbol.Name == "value");
        Assert.Null(declaration.Initializer);
        Assert.NotNull(declaration.InitializerProgram);

        var expressionStatement = Assert.Single(cache.Plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(expressionStatement.Expression);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            expressionStatement.ExpressionProgram,
            op => op.Name.Name == "value");
    }

    [Fact]
    public async Task ExpressionStatement_AwaitedExpression_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function probe(valuePromise) {
                (await valuePromise) + 1;
                return 1;
            }
            """, "probe");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "valuePromise");

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        AssertProgramContains<BinaryExpressionOp>(instruction.ExpressionProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ExpressionProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
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
        Assert.NotNull(instruction.IterableSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.IterableProgram,
            op => op.Name.Name == "items");
    }

    [Fact]
    public async Task IteratorInitInstruction_AwaitedIterable_UsesAwaitedProgram()
    {
        var plan = await GetFunctionPlan("""
            async function firstItem(itemsPromise) {
                for (const item of await itemsPromise) {
                    return item;
                }
                return 0;
            }
            """, "firstItem");

        var instruction = Assert.Single(plan.Instructions.OfType<IteratorInitInstruction>(), i => i.AwaitedProgram is not null);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.AwaitedProgram,
            op => op.Name.Name == "itemsPromise");
    }

    [Fact]
    public async Task IteratorInitInstruction_NestedAwaitedIterable_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function firstItem(itemsPromise) {
                for (const item of (await itemsPromise).values()) {
                    return item;
                }
                return 0;
            }
            """, "firstItem");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "itemsPromise");

        var instruction = Assert.Single(plan.Instructions.OfType<IteratorInitInstruction>(), i => i.IterableProgram is not null);
        Assert.NotNull(instruction.IterableSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.IterableProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IteratorInitInstruction_ComputedAwaitedProperty_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function firstItem(groups, keyPromise) {
                for (const item of groups[await keyPromise]) {
                    return item;
                }
                return 0;
            }
            """, "firstItem");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "keyPromise");

        var instruction = Assert.Single(plan.Instructions.OfType<IteratorInitInstruction>(), i => i.IterableProgram is not null);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.IterableProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.IterableProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AwaitAndDiscardInstruction_SimpleIdentifier_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            async function awaitSimple(value) {
                await value;
                return 1;
            }
            """, "awaitSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<AwaitAndDiscardInstruction>());
        Assert.False(instruction.AwaitedProgram.IsEmpty);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.AwaitedProgram,
            op => op.Name.Name == "value");
    }

    [Fact]
    public async Task ClassMethod_SuperMemberAccess_IsLoweredToExpressionProgram()
    {
        var plan = await GetClassMethodPlan("""
            class Base {
                get value() {
                    return 41;
                }
            }

            class Derived extends Base {
                read() {
                    return super.value + 1;
                }
            }
            """, "Derived", "read");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<GetNamedSuperPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
    }

    [Fact]
    public async Task ClassMethod_SuperMemberCall_IsLoweredToExpressionProgram()
    {
        var plan = await GetClassMethodPlan("""
            class Base {
                method() {
                    return 40;
                }
            }

            class Derived extends Base {
                method() {
                    return super.method() + 2;
                }
            }
            """, "Derived", "method");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedSuperCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "method");
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ClassMethod_SuperPropertyAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetClassMethodPlan("""
            class Base {
                set value(next) {
                    this._value = next;
                }
            }

            class Derived extends Base {
                write(next) {
                    return super.value = next;
                }
            }
            """, "Derived", "write");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<SetNamedSuperPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "next");
    }

    [Fact]
    public async Task ClassMethod_SuperComputedUpdate_IsLoweredToExpressionProgram()
    {
        var plan = await GetClassMethodPlan("""
            class Base {
                get value() {
                    return this._value ?? 1;
                }

                set value(next) {
                    this._value = next;
                }
            }

            class Derived extends Base {
                bump(key) {
                    return super[key]++;
                }
            }
            """, "Derived", "bump");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UpdateComputedSuperPropertyExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "key");
    }

    [Fact]
    public async Task DerivedConstructor_SuperCall_IsLoweredToExpressionProgram()
    {
        var plan = await GetClassConstructorPlan("""
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
            """, "Derived");

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(instruction.Expression);
        AssertProgramContains<SuperConstructExpressionOp>(instruction.ExpressionProgram, op => op.ArgumentCount == 1);
        AssertProgramContains<BinaryExpressionOp>(instruction.ExpressionProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task DerivedConstructor_NestedSuperMethodArgument_IsLoweredBeforeOuterSuperConstruct()
    {
        var plan = await GetClassConstructorPlan("""
            class Base {
                method() {
                    return 1;
                }
            }

            class Derived extends Base {
                constructor() {
                    super(super.method());
                }
            }
            """, "Derived");

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(instruction.Expression);
        var operations = instruction.ExpressionProgram.GetOps().ToArray();
        var loadSuperCallTargetIndex = Array.FindIndex(operations, op => op.Kind == ExpressionOpKind.LoadNamedSuperCallTarget);
        var innerCallIndex = Array.FindIndex(operations, op => op.Kind == ExpressionOpKind.Call);
        var outerSuperConstructIndex = Array.FindIndex(operations, op => op.Kind == ExpressionOpKind.SuperConstruct);

        Assert.True(loadSuperCallTargetIndex >= 0);
        Assert.True(innerCallIndex > loadSuperCallTargetIndex);
        Assert.True(outerSuperConstructIndex > innerCallIndex);
    }

    [Fact]
    public async Task DerivedConstructor_ComputedSuperRead_ChecksThisBeforePropertyExpression()
    {
        var plan = await GetClassConstructorPlan("""
            class Base {}

            class Derived extends Base {
                constructor() {
                    super[super()];
                }
            }
            """, "Derived");

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(instruction.Expression);
        var operations = instruction.ExpressionProgram.GetOps().ToArray();
        var ensureIndex = Array.FindIndex(operations, op => op.Kind == ExpressionOpKind.EnsureSuperReference);
        var innerSuperConstructIndex = Array.FindIndex(operations, op => op.Kind == ExpressionOpKind.SuperConstruct);
        var computedReadIndex = Array.FindIndex(operations, op => op.Kind == ExpressionOpKind.GetComputedSuperProperty);

        Assert.True(ensureIndex >= 0);
        Assert.True(innerSuperConstructIndex > ensureIndex);
        Assert.True(computedReadIndex > innerSuperConstructIndex);
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
        Assert.NotNull(instruction.ObjectSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ObjectProgram, op => op.Name.Name == "source");
    }

    [Fact]
    public async Task ForInInitInstruction_AwaitedObject_UsesAwaitedProgram()
    {
        var plan = await GetFunctionPlan("""
            async function firstKey(sourcePromise) {
                for (const key in await sourcePromise) {
                    return key;
                }
                return "missing";
            }
            """, "firstKey");

        var instruction = Assert.Single(plan.Instructions.OfType<ForInInitInstruction>(), i => i.AwaitedProgram is not null);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.AwaitedProgram,
            op => op.Name.Name == "sourcePromise");
    }

    [Fact]
    public async Task ForInInitInstruction_NestedAwaitedObject_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function firstKey(sourcePromise) {
                for (const key in (await sourcePromise).entries) {
                    return key;
                }
                return "missing";
            }
            """, "firstKey");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "sourcePromise");

        var instruction = Assert.Single(plan.Instructions.OfType<ForInInitInstruction>(), i => i.ObjectProgram is not null);
        Assert.NotNull(instruction.ObjectSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ObjectProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForInInitInstruction_ComputedAwaitedProperty_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function firstKey(groups, keyPromise) {
                for (const key in groups[await keyPromise]) {
                    return key;
                }
                return "missing";
            }
            """, "firstKey");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "keyPromise");

        var instruction = Assert.Single(plan.Instructions.OfType<ForInInitInstruction>(), i => i.ObjectProgram is not null);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ObjectProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ObjectProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
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
        Assert.NotNull(instruction.ObjectSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ObjectProgram, op => op.Name.Name == "scopeObj");
    }

    [Fact]
    public async Task EnterWithInstruction_AwaitedObject_UsesAwaitedProgram()
    {
        var plan = await GetFunctionPlan("""
            async function readWith(scopePromise) {
                with (await scopePromise) {
                    return answer;
                }
            }
            """, "readWith");

        var instruction = Assert.Single(plan.Instructions.OfType<EnterWithInstruction>(), i => i.AwaitedProgram is not null);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.AwaitedProgram,
            op => op.Name.Name == "scopePromise");
    }

    [Fact]
    public async Task EnterWithInstruction_NestedAwaitedObject_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function readWith(scopePromise) {
                with ((await scopePromise).nested) {
                    return answer;
                }
            }
            """, "readWith");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "scopePromise");

        var instruction = Assert.Single(plan.Instructions.OfType<EnterWithInstruction>(), i => i.ObjectProgram is not null);
        Assert.NotNull(instruction.ObjectSource);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ObjectProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnterWithInstruction_ComputedAwaitedProperty_UsesSyntheticAwaitedTemp()
    {
        var plan = await GetFunctionPlan("""
            async function readWith(groups, keyPromise) {
                with (groups[await keyPromise]) {
                    return answer;
                }
            }
            """, "readWith");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "keyPromise");

        var instruction = Assert.Single(plan.Instructions.OfType<EnterWithInstruction>(), i => i.ObjectProgram is not null);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ObjectProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ObjectProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnterWithInstruction_YieldingObject_RewritesOffSuspendingInstruction()
    {
        var plan = await GetFunctionPlan("""
            function* readWith(scopeObj) {
                with (yield scopeObj) {
                    return answer;
                }
            }
            """, "readWith");

        var instruction = Assert.Single(plan.Instructions.OfType<EnterWithInstruction>(), i => i.ObjectProgram is not null);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.ObjectProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_resume", StringComparison.Ordinal));
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
    public async Task BindingVariableDeclarationInstruction_AwaitedInitializer_UsesAwaitedProgram()
    {
        var plan = await GetFunctionPlan("""
            async function sumPoint(pointPromise) {
                const { x, y } = await pointPromise;
                return x + y;
            }
            """, "sumPoint");

        var instruction = Assert.Single(
            plan.Instructions.OfType<BindingVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.AwaitedProgram,
            op => op.Name.Name == "pointPromise");
    }

    [Fact]
    public async Task MultiDeclaratorBindingVariableDeclaration_NestedAwait_RewritesOffSuspendingInstruction()
    {
        var plan = await GetFunctionPlan("""
            async function sumPoint(pointPromise) {
                let seed = 1, { x, y } = (await pointPromise).point;
                return seed + x + y;
            }
            """, "sumPoint");

        var tempInstruction = Assert.Single(
            plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.AwaitedProgram is not null && i.TargetSymbol.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));
        AssertProgramContains<LoadIdentifierExpressionOp>(
            tempInstruction.AwaitedProgram,
            op => op.Name.Name == "pointPromise");

        var bindingInstruction = Assert.Single(
            plan.Instructions.OfType<BindingVariableDeclarationInstruction>(),
            i => i.InitializerProgram is not null);
        Assert.Null(bindingInstruction.Initializer);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            bindingInstruction.InitializerProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_", StringComparison.Ordinal));

        Assert.DoesNotContain(
            plan.Instructions,
            static instruction => instruction.GetType().Name.Contains("SuspendingBindingVariableDeclaration", StringComparison.Ordinal));
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

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
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

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
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

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_IdentifierCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function invokeHelper(helper, value) {
                return helper(value);
            }
            """, "invokeHelper");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && !op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_OptionalIdentifierCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function invokeMaybe(helper, value) {
                return helper?.(value);
            }
            """, "invokeMaybe");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram, op => op.ReplaceWithUndefined);
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && !op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_MemberCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function pickMax(left, right) {
                return Math.max(left, right);
            }
            """, "pickMax");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "max");
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 2 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_SpreadCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function pickMax(values) {
                return Math.max(...values);
            }
            """, "pickMax");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "max");
        AssertProgramContains<CallExpressionOp>(
            instruction.ReturnProgram,
            op => op.ArgumentCount == 1 && op.HasExplicitThis && !op.SpreadMask.IsDefaultOrEmpty && op.SpreadMask[0]);
    }

    [Fact]
    public async Task ReturnInstruction_MultiSpreadCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function joinAll(left, middle, right) {
                return String.raw(...left, middle, ...right);
            }
            """, "joinAll");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "raw");
        AssertProgramContains<CallExpressionOp>(
            instruction.ReturnProgram,
            op => op.ArgumentCount == 3 &&
                  op.HasExplicitThis &&
                  !op.SpreadMask.IsDefaultOrEmpty &&
                  op.SpreadMask.SequenceEqual([true, false, true]));
    }

    [Fact]
    public async Task ReturnInstruction_DotCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function invokeViaCall(helper, value) {
                return helper.call(undefined, value);
            }
            """, "invokeViaCall");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "call");
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 2 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_DotApplyExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function invokeViaApply(helper, args) {
                return helper.apply(undefined, args);
            }
            """, "invokeViaApply");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "apply");
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 2 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_OptionalMemberCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function maybeCall(box, value) {
                return box.read?.(value);
            }
            """, "maybeCall");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "read");
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram, op => op.ReplaceWithUndefined);
        AssertProgramContains<SwapTopTwoExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_NestedOptionalMemberCallTarget_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function maybeNestedCall(box, value) {
                return box?.inner.read(value);
            }
            """, "maybeNestedCall");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfShortCircuitedExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "read");
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_NestedOptionalCallExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function maybeInvoke(factory) {
                return factory?.()();
            }
            """, "maybeInvoke");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfShortCircuitedExpressionOp>(instruction.ReturnProgram);
        Assert.Equal(
            2,
            instruction.ReturnProgram!.Value.GetOps(ExpressionOpKind.Call).Count());
    }

    [Fact]
    public async Task ReturnInstruction_NewExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function createDate(year) {
                return new Date(year, 0, 15);
            }
            """, "createDate");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ConstructExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 3);
    }

    [Fact]
    public async Task ReturnInstruction_SpreadNewExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function createDate(parts) {
                return new Date(...parts);
            }
            """, "createDate");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ConstructExpressionOp>(
            instruction.ReturnProgram,
            op => op.ArgumentCount == 1 && !op.SpreadMask.IsDefaultOrEmpty && op.SpreadMask[0]);
    }

    [Fact]
    public async Task ReturnInstruction_MultiSpreadNewExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function createDate(years, months, day) {
                return new Date(...years, ...months, day);
            }
            """, "createDate");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ConstructExpressionOp>(
            instruction.ReturnProgram,
            op => op.ArgumentCount == 3 &&
                  !op.SpreadMask.IsDefaultOrEmpty &&
                  op.SpreadMask.SequenceEqual([true, true, false]));
    }

    [Fact]
    public async Task ReturnInstruction_SequenceExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function pickLast(left, right) {
                return (left + 1, right + 2);
            }
            """, "pickLast");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<PopExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task ReturnInstruction_AssignmentAcrossDeleteSequence_ResolvesIdentifierReferenceBeforeRhs()
    {
        var plan = await GetFunctionPlan("""
            function assignAfterDelete(scope) {
                var x = 0;
                with (scope) {
                    return x = (delete scope.x, 2);
                }
            }
            """, "assignAfterDelete");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        AssertProgramContains<ResolveIdentifierReferenceExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "x");
        AssertProgramContains<DeleteNamedPropertyExpressionOp>(
            instruction.ReturnProgram,
            op => op.PropertyName == "x");
        AssertProgramContains<StoreResolvedIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "x");
    }

    [Fact]
    public async Task ReturnInstruction_TemplateLiteral_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function greet(name, count) {
                return `Hello ${name} ${count}`;
            }
            """, "greet");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ToStringExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task ReturnInstruction_PropertyAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function assignValue(box, value) {
                return box.value = value;
            }
            """, "assignValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<SetNamedPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
    }

    [Fact]
    public async Task ReturnInstruction_CompoundPropertyAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function addIntoValue(box, value) {
                return box.value += value;
            }
            """, "addIntoValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<DuplicateTopExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<GetNamedPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<SetNamedPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
    }

    [Fact]
    public async Task ReturnInstruction_LogicalCompoundPropertyAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function ensureValue(box, value) {
                return box.value ||= value;
            }
            """, "ensureValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<DuplicateTopExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<GetNamedPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
        AssertProgramContains<JumpIfTrueExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<SwapTopTwoExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<SetNamedPropertyExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "value");
    }

    [Fact]
    public async Task ReturnInstruction_AssignmentExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function assignLocal(value) {
                let current = 0;
                return current = value;
            }
            """, "assignLocal");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ResolveIdentifierReferenceExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
        AssertProgramContains<StoreResolvedIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
    }

    [Fact]
    public async Task ReturnInstruction_CompoundAssignmentExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function addIntoCurrent(value) {
                let current = 19;
                return current += value;
            }
            """, "addIntoCurrent");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ResolveIdentifierReferenceExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
        AssertProgramContains<LoadResolvedIdentifierValueExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<StoreResolvedIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
    }

    [Fact]
    public async Task ReturnInstruction_LogicalCompoundAssignmentExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function ensureCurrent(value) {
                let current = 0;
                return current ||= value;
            }
            """, "ensureCurrent");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ResolveIdentifierReferenceExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
        AssertProgramContains<LoadResolvedIdentifierValueExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<JumpIfTrueExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<StoreResolvedIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
    }

    [Fact]
    public async Task ReturnInstruction_NullishCompoundAssignmentExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function fillCurrent(value) {
                let current = undefined;
                return current ??= value;
            }
            """, "fillCurrent");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<ResolveIdentifierReferenceExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
        AssertProgramContains<LoadResolvedIdentifierValueExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<JumpIfNotNullishExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<StoreResolvedIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current");
    }

    [Fact]
    public async Task ReturnInstruction_PrefixIncrementIdentifierExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function nextValue(seed) {
                let current = seed;
                return ++current;
            }
            """, "nextValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UpdateIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current" && op.IsIncrement && op.IsPrefix);
    }

    [Fact]
    public async Task ReturnInstruction_PostfixDecrementIdentifierExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function currentValue(seed) {
                let current = seed;
                return current--;
            }
            """, "currentValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UpdateIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "current" && !op.IsIncrement && !op.IsPrefix);
    }

    [Fact]
    public async Task ReturnInstruction_PostfixNamedPropertyIncrement_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function currentValue(box) {
                return box.value++;
            }
            """, "currentValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UpdateNamedPropertyExpressionOp>(
            instruction.ReturnProgram,
            op => op.PropertyName == "value" && op.IsIncrement && !op.IsPrefix);
    }

    [Fact]
    public async Task ReturnInstruction_PrefixComputedPropertyDecrement_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function nextValue(box, key) {
                return --box[key];
            }
            """, "nextValue");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UpdateComputedPropertyExpressionOp>(
            instruction.ReturnProgram,
            op => !op.IsIncrement && op.IsPrefix);
    }

    [Fact]
    public async Task ReturnInstruction_UnaryMinus_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function negate(value) {
                return -value;
            }
            """, "negate");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UnaryMinusExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_TypeOfIdentifier_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function describe(value) {
                return typeof value;
            }
            """, "describe");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<TypeOfIdentifierExpressionOp>(
            instruction.ReturnProgram,
            op => op.Name.Name == "value");
    }

    [Fact]
    public async Task ReturnInstruction_UnaryVoid_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function discard(value) {
                return void value;
            }
            """, "discard");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<UnaryVoidExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_DeleteNamedProperty_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function drop(box) {
                return delete box.value;
            }
            """, "drop");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<DeleteNamedPropertyExpressionOp>(
            instruction.ReturnProgram,
            op => op.PropertyName == "value");
    }

    [Fact]
    public async Task ReturnInstruction_DeleteComputedProperty_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function drop(box, key) {
                return delete box[key];
            }
            """, "drop");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<DeleteComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_DeleteOptionalNamedProperty_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function drop(box) {
                return delete box?.value;
            }
            """, "drop");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram, op => !op.ReplaceWithUndefined);
        AssertProgramContains<DeleteNamedPropertyExpressionOp>(
            instruction.ReturnProgram,
            op => op.PropertyName == "value");
        AssertProgramContains<LoadLiteralExpressionOp>(
            instruction.ReturnProgram,
            op => op.Value.IsBoolean && op.Value.AsBoolean());
    }

    [Fact]
    public async Task ReturnInstruction_DeleteOptionalComputedProperty_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function drop(box, key) {
                return delete box?.[key];
            }
            """, "drop");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram, op => !op.ReplaceWithUndefined);
        AssertProgramContains<DeleteComputedPropertyExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<LoadLiteralExpressionOp>(
            instruction.ReturnProgram,
            op => op.Value.IsBoolean && op.Value.AsBoolean());
    }

    [Fact]
    public async Task ReturnInstruction_DeleteNonReferenceExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function drop(value) {
                return delete (value + 1);
            }
            """, "drop");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<PopExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<LoadLiteralExpressionOp>(instruction.ReturnProgram, op => op.Value.IsBoolean && op.Value.AsBoolean());
    }

    [Fact]
    public async Task FunctionPlan_TaggedTemplateExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function test() {
                return String.raw`x`;
            }
            """, "test");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<LoadTemplateObjectExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ScriptPlan_TaggedTemplateExpression_IsLoweredToExpressionProgram()
    {
        var program = _engine.ParseProgram("""
            String.raw`x`;
            """);

        var result = await _engine.Evaluate(program);
        Assert.Equal("x", result);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var expressionStatement = Assert.Single(cache.Plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(expressionStatement.Expression);
        AssertProgramContains<LoadTemplateObjectExpressionOp>(expressionStatement.ExpressionProgram);
        AssertProgramContains<CallExpressionOp>(
            expressionStatement.ExpressionProgram,
            op => op.ArgumentCount == 1 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_OptionalTaggedTemplateExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function maybeTag(box) {
                return box?.tag`x`;
            }
            """, "maybeTag");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram, op => op.ReplaceWithUndefined);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "tag");
        AssertProgramContains<LoadTemplateObjectExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_NestedOptionalTaggedTemplateTarget_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function maybeNestedTag(box) {
                return box?.inner.tag`x`;
            }
            """, "maybeNestedTag");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfShortCircuitedExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<LoadNamedCallTargetExpressionOp>(instruction.ReturnProgram, op => op.PropertyName == "tag");
        AssertProgramContains<LoadTemplateObjectExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<CallExpressionOp>(instruction.ReturnProgram, op => op.ArgumentCount == 1 && op.HasExplicitThis);
    }

    [Fact]
    public async Task ReturnInstruction_IndexAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function assignAt(box, key, value) {
                return box[key] = value;
            }
            """, "assignAt");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<SetComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_CompoundIndexAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function addAt(box, key, value) {
                return box[key] += value;
            }
            """, "addAt");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<DuplicateTopTwoExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<SetComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task ReturnInstruction_NullishCompoundIndexAssignment_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function fillAt(box, key, value) {
                return box[key] ??= value;
            }
            """, "fillAt");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<DuplicateTopTwoExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<JumpIfNotNullishExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<RotateTopThreeRightExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<SetComputedPropertyExpressionOp>(instruction.ReturnProgram);
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

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(instruction.ReturnExpression);
        AssertProgramContains<JumpIfNullishExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<GetComputedPropertyExpressionOp>(instruction.ReturnProgram);
    }

    [Fact]
    public async Task CatchDestructuring_UsesEnterCatchBindingProgram()
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
        var catchBindingProgram = Assert.IsType<ObjectBindingTargetProgram>(enterCatch.CatchBindingProgram);
        Assert.Empty(plan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Collection(
            catchBindingProgram.Properties,
            property =>
            {
                Assert.Equal("x", property.Name);
                Assert.IsType<IdentifierBindingTargetProgram>(property.Target);
                Assert.Null(property.NameProgram);
                Assert.Null(property.DefaultProgram);
            },
            property =>
            {
                Assert.Equal("y", property.Name);
                Assert.IsType<IdentifierBindingTargetProgram>(property.Target);
                Assert.Null(property.NameProgram);
                Assert.Null(property.DefaultProgram);
            });
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

    [Fact]
    public async Task FunctionDeclarationInstruction_CallablePlan_IsCachedIntoRuntimeDescriptor()
    {
        var plan = await GetScriptPlan("""
            function read(value) {
                return value + 1;
            }
            """);

        var instruction = Assert.Single(
            plan.Instructions.OfType<FunctionDeclarationInstruction>(),
            i => i.Descriptor is not null);
        var descriptor = Assert.IsType<FunctionDeclarationDescriptor>(instruction.Descriptor);

        Assert.Equal("read", descriptor.Name.Name);
        Assert.True(descriptor.PlanSeed.Succeeded);
        Assert.NotNull(descriptor.PlanSeed.Plan);
    }

    [Fact]
    public async Task FunctionDeclarationInstruction_CallablePlanFailure_IsCachedIntoRuntimeDescriptor()
    {
        var parsedProgram = _engine.ParseProgram("""
            function read(value) {
                return value + 1;
            }
            """);
        var program = ReplaceFunctionDeclarationBodyWithUnsupportedModuleStatement(parsedProgram, "read");
        await _engine.Evaluate(program);

        var plan = GetScriptPlan(program);
        var instruction = Assert.Single(
            plan.Instructions.OfType<FunctionDeclarationInstruction>(),
            i => i.Descriptor is not null);
        var descriptor = Assert.IsType<FunctionDeclarationDescriptor>(instruction.Descriptor);

        Assert.False(descriptor.PlanSeed.Succeeded);
        Assert.Null(descriptor.PlanSeed.Plan);
        Assert.NotNull(descriptor.PlanSeed.Failure);
        Assert.Contains("ExportAllStatement", descriptor.PlanSeed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimpleVariableDeclaration_AnonymousFunctionInitializer_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function makeAdder() {
                const add = function(value) { return value + 1; };
                return add;
            }
            """, "makeAdder");

        var instruction = Assert.Single(plan.Instructions.OfType<SimpleVariableDeclarationInstruction>()
, i => i.TargetSymbol.Name == "add");
        Assert.Null(instruction.Initializer);
        AssertProgramContains<LoadFunctionLiteralExpressionOp>(
            instruction.InitializerProgram,
            op => op.FunctionPlanSeed.Succeeded && op.Function.Name is null);
    }

    [Fact]
    public async Task FunctionLiteral_CallablePlanFailures_AreCachedIntoExpressionProgram()
    {
        var parsedProgram = _engine.ParseProgram("""
            const broken = function(value) {
                return value + 1;
            };
            broken;
            """);
        var program = ReplaceVariableFunctionInitializerBodyWithUnsupportedModuleStatement(parsedProgram, "broken");

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");

        var instruction = Assert.Single(
            cache.Plan!.Instructions.OfType<SimpleVariableDeclarationInstruction>(),
            i => i.TargetSymbol.Name == "broken");
        var loadOp = Assert.Single(instruction.InitializerProgram!.Value.GetOps(ExpressionOpKind.LoadFunctionLiteral));

        Assert.False(loadOp.FunctionPlanSeed.Succeeded);
        Assert.Null(loadOp.FunctionPlanSeed.Plan);
        Assert.NotNull(loadOp.FunctionPlanSeed.Failure);
        Assert.Contains("ExportAllStatement", loadOp.FunctionPlanSeed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FunctionDeclaration_CallablePlanFailures_AreCachedIntoDeclarationInstruction()
    {
        var parsedProgram = _engine.ParseProgram("""
            function outer() {
                if (true) {
                    function broken(value) {
                        return value + 1;
                    }
                }

                return 0;
            }
            """);
        var program = ReplaceNestedFunctionDeclarationBodyWithUnsupportedModuleStatement(parsedProgram, "outer", "broken");

        await _engine.Evaluate(program);

        var outer = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration declaration && declaration.Name.Name == "outer"));
        var cache = ((IAstCacheable<ExecutionPlanCache>)outer.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Outer plan should build. Failure: {cache.FailureReason}");

        var instruction = Assert.Single(
            cache.Plan!.Instructions.OfType<FunctionDeclarationInstruction>(),
            static i => i.Descriptor is { Name.Name: "broken" });
        var descriptor = Assert.IsType<FunctionDeclarationDescriptor>(instruction.Descriptor);

        Assert.False(descriptor.PlanSeed.Succeeded);
        Assert.Null(descriptor.PlanSeed.Plan);
        Assert.NotNull(descriptor.PlanSeed.Failure);
        Assert.Contains("ExportAllStatement", descriptor.PlanSeed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FunctionDeclarationInstruction_CallablePlans_AreCachedIntoRuntimeDescriptor()
    {
        var program = _engine.ParseProgram("""
            "use strict";
            {
                function declared(value) {
                    return value + 1;
                }
            }
            """);

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");

        var instruction = Assert.Single(
            cache.Plan!.Instructions.OfType<FunctionDeclarationInstruction>(),
            i => i.Descriptor is not null);
        var descriptor = instruction.Descriptor;
        Assert.NotNull(descriptor);
        Assert.Equal("declared", descriptor.Value.Name.Name);
        Assert.True(descriptor.Value.PlanSeed.Succeeded);
        Assert.NotNull(descriptor.Value.PlanSeed.Plan);
    }

    [Fact]
    public async Task FunctionDeclarationInstruction_CallablePlanFailures_AreCachedIntoRuntimeDescriptor()
    {
        var parsedProgram = _engine.ParseProgram("""
            "use strict";
            {
                function broken(value) {
                    return value + 1;
                }
            }
            """);
        var program = ReplaceBlockFunctionDeclarationBodyWithUnsupportedModuleStatement(parsedProgram, "broken");

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");

        var instruction = Assert.Single(
            cache.Plan!.Instructions.OfType<FunctionDeclarationInstruction>(),
            i => i.Descriptor is { } descriptor && descriptor.Name.Name == "broken");
        var descriptor = Assert.IsType<FunctionDeclarationDescriptor>(instruction.Descriptor);

        Assert.False(descriptor.PlanSeed.Succeeded);
        Assert.Null(descriptor.PlanSeed.Plan);
        Assert.NotNull(descriptor.PlanSeed.Failure);
        Assert.Contains("ExportAllStatement", descriptor.PlanSeed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimpleVariableDeclaration_AnonymousClassInitializer_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function makeBox() {
                const Box = class { value() { return 42; } };
                return Box;
            }
            """, "makeBox");

        var instruction = Assert.Single(plan.Instructions.OfType<SimpleVariableDeclarationInstruction>()
, i => i.TargetSymbol.Name == "Box");
        Assert.Null(instruction.Initializer);
        AssertProgramContains<LoadClassLiteralExpressionOp>(instruction.InitializerProgram);
    }

    [Fact]
    public async Task SimpleVariableDeclaration_ObjectMethodLiteral_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function makeObject() {
                const obj = { value() { return 42; } };
                return obj;
            }
            """, "makeObject");

        var instruction = Assert.Single(plan.Instructions.OfType<SimpleVariableDeclarationInstruction>()
, i => i.TargetSymbol.Name == "obj");
        Assert.Null(instruction.Initializer);
        AssertProgramContains<DefineObjectMethodExpressionOp>(instruction.InitializerProgram, op => op.PropertyName == "value");
    }

    [Fact]
    public async Task SimpleVariableDeclaration_ObjectAccessors_AreLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function makeObject() {
                const obj = {
                    get value() { return 42; },
                    set value(next) { this._value = next; }
                };
                return obj;
            }
            """, "makeObject");

        var instruction = Assert.Single(plan.Instructions.OfType<SimpleVariableDeclarationInstruction>()
, i => i.TargetSymbol.Name == "obj");
        Assert.Null(instruction.Initializer);
        Assert.Equal(
            2,
            instruction.InitializerProgram!.Value.GetOps(ExpressionOpKind.DefineObjectAccessor).Count());
    }

    [Fact]
    public async Task ScriptExpressionStatement_ImmutableIdentifierAssignment_StillBuildsIrPlan()
    {
        var program = _engine.ParseProgram("""
            const value = 1;
            value = 2;
            """);

        await Assert.ThrowsAnyAsync<Exception>(async () => await _engine.Evaluate(program));

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");

        var assignment = Assert.Single(cache.Plan!.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(assignment.ValueExpression);
        AssertProgramContains<LoadLiteralExpressionOp>(
            assignment.ValueProgram,
            op => op.Value.IsNumber && op.Value.NumberValue == 2.0);
    }

    [Fact]
    public async Task ScriptExpressionStatement_MutableIdentifierAssignment_UsesAssignmentSlotInstruction()
    {
        var program = _engine.ParseProgram("""
            let value = 1;
            value = 2;
            """);

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var assignment = Assert.Single(cache.Plan.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(assignment.ValueExpression);
        AssertProgramContains<LoadLiteralExpressionOp>(
            assignment.ValueProgram,
            op => op.Value.IsNumber && op.Value.NumberValue == 2.0);
    }

    [Fact]
    public async Task ScriptExpressionStatement_MutableLogicalCompoundAssignment_UsesLogicalAssignmentInstruction()
    {
        var program = _engine.ParseProgram("""
            let value = 0;
            value ||= 2;
            """);

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var assignment = Assert.Single(cache.Plan.Instructions.OfType<LogicalCompoundAssignmentSlotInstruction>());
        Assert.Null(assignment.RhsExpression);
        AssertProgramContains<LoadLiteralExpressionOp>(
            assignment.RhsProgram,
            op => op.Value.IsNumber && op.Value.NumberValue == 2.0);
    }

    [Fact]
    public async Task DestructuringAssignmentExpressionStatement_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function assignFirst(values) {
                let x = 0;
                [x] = values;
                return x;
            }
            """, "assignFirst");

        var assignment = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(assignment.Expression);
        AssertProgramContains<ApplyBindingTargetExpressionOp>(
            assignment.ExpressionProgram,
            op => op.TargetProgram is ArrayBindingTargetProgram);
    }

    [Fact]
    public async Task ClassDefinition_ExtendsExpression_IsLoweredToExpressionProgramCache()
    {
        var cache = await GetClassDefinitionProgramCache("""
            class Base {}
            class Derived extends Base {
                value() {
                    return 42;
                }
            }
            """, "Derived");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.ExtendsProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            cache.ExtendsProgram,
            op => op.Name.Name == "Base");
    }

    [Fact]
    public async Task ClassDefinition_ComputedMethodName_IsLoweredToExpressionProgramCache()
    {
        var cache = await GetClassDefinitionProgramCache("""
            const suffix = "alue";
            class Box {
                ["v" + suffix]() {
                    return 42;
                }
            }
            """, "Box");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.NotEmpty(cache.MemberNamePrograms);
        var program = Assert.Single(cache.MemberNamePrograms);
        Assert.NotNull(program);
        AssertProgramContains<BinaryExpressionOp>(
            program,
            op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task ClassDefinition_ComputedFieldName_IsLoweredToExpressionProgramCache()
    {
        var cache = await GetClassDefinitionProgramCache("""
            const suffix = "alue";
            class Box {
                ["v" + suffix] = 42;
            }
            """, "Box");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.NotEmpty(cache.FieldNamePrograms);
        var program = Assert.Single(cache.FieldNamePrograms);
        Assert.NotNull(program);
        AssertProgramContains<BinaryExpressionOp>(
            program,
            op => op.Operator == BinaryOperator.Add);
    }

    [Fact]
    public async Task ClassDefinition_FieldInitializers_AreLoweredToExpressionProgramCache()
    {
        var cache = await GetClassDefinitionProgramCache("""
            class Box {
                value = 1 + 2;
                static total = 3 + 4;
            }
            """, "Box");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.Equal(2, cache.FieldInitializerPrograms.Length);
        Assert.All(cache.FieldInitializerPrograms, program => Assert.NotNull(program));
    }

    [Fact]
    public async Task ClassDefinition_FieldMetadata_IsLoweredIntoRuntimeDescriptor()
    {
        var cache = await GetClassDefinitionProgramCache("""
            class Box {
                ["v" + "alue"] = function() {};
                #secret = function() {};
                static total = 1;
            }
            """, "Box");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.Equal(3, cache.Definition.Fields.Length);

        var computedField = cache.Definition.Fields[0];
        Assert.True(computedField.IsComputed);
        Assert.True(computedField.AllowsAnonymousFunctionNameInference);

        var privateField = cache.Definition.Fields[1];
        Assert.Equal("#secret", privateField.DeclaredName);
        Assert.True(privateField.IsPrivate);
        Assert.True(privateField.AllowsAnonymousFunctionNameInference);

        var staticField = cache.Definition.Fields[2];
        Assert.Equal("total", staticField.DeclaredName);
        Assert.True(staticField.IsStatic);
        Assert.False(staticField.AllowsAnonymousFunctionNameInference);
    }

    [Fact]
    public async Task ClassDefinition_MemberMetadata_IsLoweredIntoRuntimeDescriptor()
    {
        var cache = await GetClassDefinitionProgramCache("""
            class Box {
                static ["v" + "alue"]() {
                    return 1;
                }

                get size() {
                    return 2;
                }

                #secret() {
                    return 3;
                }
            }
            """, "Box");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.Equal(3, cache.Definition.Members.Length);

        var computedStaticMethod = cache.Definition.Members[0];
        Assert.Equal(ClassMemberKind.Method, computedStaticMethod.Kind);
        Assert.True(computedStaticMethod.IsStatic);
        Assert.True(computedStaticMethod.IsComputed);
        Assert.False(computedStaticMethod.IsPrivate);

        var getter = cache.Definition.Members[1];
        Assert.Equal(ClassMemberKind.Getter, getter.Kind);
        Assert.Equal("size", getter.Name);
        Assert.False(getter.IsStatic);
        Assert.False(getter.IsComputed);

        var privateMethod = cache.Definition.Members[2];
        Assert.Equal("#secret", privateMethod.Name);
        Assert.True(privateMethod.IsPrivate);
        Assert.Empty(privateMethod.Callable.Function.Parameters);
    }

    [Fact]
    public async Task ClassDefinition_CallablePlans_AreCachedIntoRuntimeDescriptor()
    {
        var cache = await GetClassDefinitionProgramCache("""
            class Box {
                constructor(value) {
                    this.value = value + 1;
                }

                read() {
                    return this.value;
                }
            }
            """, "Box");

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");
        Assert.True(cache.Definition.Constructor.PlanSeed.Succeeded);
        Assert.NotNull(cache.Definition.Constructor.PlanSeed.Plan);

        var member = Assert.Single(cache.Definition.Members);
        Assert.True(member.Callable.PlanSeed.Succeeded);
        Assert.NotNull(member.Callable.PlanSeed.Plan);
    }

    [Fact]
    public async Task ClassDefinition_CallablePlanFailures_AreCachedIntoRuntimeDescriptor()
    {
        var parsedProgram = _engine.ParseProgram("""
            class Box {
                read(value) {
                    return value + 1;
                }
            }
            """);
        var program = ReplaceClassMethodBodyWithUnsupportedModuleStatement(parsedProgram, "Box", "read");
        await _engine.Evaluate(program);

        var declaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration classDeclaration &&
                                             classDeclaration.Name.Name == "Box"));
        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)declaration.Definition).GetOrCreateCache();

        Assert.True(cache.Succeeded, $"Class definition program cache should build. Failure: {cache.FailureReason}");

        var member = Assert.Single(cache.Definition.Members);
        Assert.False(member.Callable.PlanSeed.Succeeded);
        Assert.Null(member.Callable.PlanSeed.Plan);
        Assert.NotNull(member.Callable.PlanSeed.Failure);
        Assert.Contains("ExportAllStatement", member.Callable.PlanSeed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassDeclarationInstruction_UsesCachedRuntimeMetadata()
    {
        var program = _engine.ParseProgram("""
            class Box {
                ["value"]() {
                    return 42;
                }

                total = 1 + 2;

                static {
                    this.count = 1;
                }
            }
            """);

        await _engine.Evaluate(program);

        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var instruction = Assert.Single(cache.Plan.Instructions.OfType<ClassDeclarationInstruction>());
        Assert.True(
            instruction.Descriptor.ProgramCache.Succeeded,
            $"Class definition program cache should build. Failure: {instruction.Descriptor.ProgramCache.FailureReason}");
        Assert.Equal("Box", instruction.Descriptor.Name.Name);
        Assert.Single(instruction.Descriptor.ProgramCache.Definition.Members);
        Assert.True(instruction.Descriptor.ProgramCache.Definition.Constructor.PlanSeed.Succeeded);
        Assert.True(instruction.Descriptor.ProgramCache.Definition.Members[0].Callable.PlanSeed.Succeeded);
        var field = Assert.Single(instruction.Descriptor.ProgramCache.Definition.Fields);
        Assert.Equal("total", field.DeclaredName);
        Assert.False(field.IsComputed);
        Assert.Single(instruction.Descriptor.ProgramCache.Definition.StaticBlockPlans);
        AssertProgramContains<LoadLiteralExpressionOp>(
            instruction.Descriptor.ProgramCache.MemberNamePrograms.Single(),
            op => op.Value.AsString() == "value");
    }

    [Fact]
    public async Task ClassStaticBlock_AssignmentBody_BuildsIrPlan()
    {
        var plan = await GetClassStaticBlockPlan("""
            class Box {
                static {
                    this.value = 42;
                }
            }
            """, "Box");

        Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
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

    private async Task<ExecutionPlan> GetScriptPlan(string source)
    {
        var program = _engine.ParseProgram(source);
        await _engine.Evaluate(program);
        return GetScriptPlan(program);
    }

    private static ExecutionPlan GetScriptPlan(ProgramNode program)
    {
        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Script plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private async Task<ExecutionPlan> GetClassMethodPlan(string source, string className, string methodName, bool isStatic = false)
    {
        var program = _engine.ParseProgram(source);
        await _engine.Evaluate(program);

        var declaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration classDeclaration &&
                                             classDeclaration.Name.Name == className));

        var method = Assert.Single(declaration.Definition.Members.Where(member =>
            member.Name == methodName &&
            member.IsStatic == isStatic));

        var cache = ((IAstCacheable<ExecutionPlanCache>)method.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private async Task<ExecutionPlan> GetClassConstructorPlan(string source, string className)
    {
        var program = _engine.ParseProgram(source);
        await _engine.Evaluate(program);

        var declaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration classDeclaration &&
                                             classDeclaration.Name.Name == className));

        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Definition.Constructor).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private async Task<ExecutionPlan> GetClassStaticBlockPlan(string source, string className)
    {
        var program = _engine.ParseProgram(source);
        await _engine.Evaluate(program);

        var declaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration classDeclaration &&
                                             classDeclaration.Name.Name == className));

        var block = Assert.Single(declaration.Definition.StaticBlocks);
        var cache = ((IAstCacheable<StaticBlockPlanCache>)block).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private async Task<ClassDefinitionProgramCache> GetClassDefinitionProgramCache(string source, string className)
    {
        var program = _engine.ParseProgram(source);
        await _engine.Evaluate(program);

        var declaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration classDeclaration &&
                                             classDeclaration.Name.Name == className));

        return ((IAstCacheable<ClassDefinitionProgramCache>)declaration.Definition).GetOrCreateCache();
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

    private static ProgramNode ReplaceFunctionDeclarationBodyWithUnsupportedModuleStatement(
        ProgramNode program,
        string functionName)
    {
        var declarationIndex = -1;
        for (var i = 0; i < program.Body.Length; i++)
        {
            if (program.Body[i] is FunctionDeclaration functionDeclaration &&
                functionDeclaration.Name.Name == functionName)
            {
                declarationIndex = i;
                break;
            }
        }

        var declaration = Assert.IsType<FunctionDeclaration>(program.Body[declarationIndex]);
        var rewrittenDeclaration = declaration with
        {
            Function = declaration.Function with
            {
                Body = new BlockStatement(
                    null,
                    [new ExportAllStatement(null, "./unsupported.js")],
                    true)
            }
        };

        return program with
        {
            Body = program.Body.SetItem(declarationIndex, rewrittenDeclaration)
        };
    }

    private static ProgramNode ReplaceBlockFunctionDeclarationBodyWithUnsupportedModuleStatement(
        ProgramNode program,
        string functionName)
    {
        var blockIndex = -1;
        for (var i = 0; i < program.Body.Length; i++)
        {
            if (program.Body[i] is BlockStatement)
            {
                blockIndex = i;
                break;
            }
        }

        var block = Assert.IsType<BlockStatement>(program.Body[blockIndex]);
        var declarationIndex = -1;
        for (var i = 0; i < block.Statements.Length; i++)
        {
            if (block.Statements[i] is FunctionDeclaration functionDeclaration &&
                functionDeclaration.Name.Name == functionName)
            {
                declarationIndex = i;
                break;
            }
        }

        var blockFunctionDeclaration = Assert.IsType<FunctionDeclaration>(block.Statements[declarationIndex]);
        var rewrittenDeclaration = blockFunctionDeclaration with
        {
            Function = blockFunctionDeclaration.Function with
            {
                Body = new BlockStatement(
                    null,
                    [new ExportAllStatement(null, "./unsupported.js")],
                    true)
            }
        };

        return program with
        {
            Body = program.Body.SetItem(
                blockIndex,
                block with
                {
                    Statements = block.Statements.SetItem(declarationIndex, rewrittenDeclaration)
                })
        };
    }

    private static ProgramNode ReplaceVariableFunctionInitializerBodyWithUnsupportedModuleStatement(
        ProgramNode program,
        string variableName)
    {
        var declaration = Assert.IsType<VariableDeclaration>(
            program.Body.Single(statement => statement is VariableDeclaration variableDeclaration &&
                                             variableDeclaration.Declarators.Any(declarator =>
                                                 declarator.Target is IdentifierBinding identifier &&
                                                 identifier.Name.Name == variableName)));

        var declarationIndex = program.Body.IndexOf(declaration);
        var declaratorIndex = -1;
        for (var i = 0; i < declaration.Declarators.Length; i++)
        {
            if (declaration.Declarators[i].Target is IdentifierBinding identifier &&
                identifier.Name.Name == variableName)
            {
                declaratorIndex = i;
                break;
            }
        }

        var declarator = declaration.Declarators[declaratorIndex];
        var function = Assert.IsType<FunctionExpression>(declarator.Initializer);
        var rewrittenDeclarator = declarator with
        {
            Initializer = function with
            {
                Body = new BlockStatement(
                    null,
                    [new ExportAllStatement(null, "./unsupported.js")],
                    true)
            }
        };
        var rewrittenDeclaration = declaration with
        {
            Declarators = declaration.Declarators.SetItem(declaratorIndex, rewrittenDeclarator)
        };

        return program with
        {
            Body = program.Body.SetItem(declarationIndex, rewrittenDeclaration)
        };
    }

    private static ProgramNode ReplaceNestedFunctionDeclarationBodyWithUnsupportedModuleStatement(
        ProgramNode program,
        string outerFunctionName,
        string nestedFunctionName)
    {
        var outerDeclaration = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration outerFunctionDeclaration &&
                                             outerFunctionDeclaration.Name.Name == outerFunctionName));
        var outerDeclarationIndex = program.Body.IndexOf(outerDeclaration);

        var ifStatement = Assert.IsType<IfStatement>(outerDeclaration.Function.Body.Statements[0]);
        var block = Assert.IsType<BlockStatement>(ifStatement.Then);
        var nestedDeclaration = Assert.IsType<FunctionDeclaration>(
            block.Statements.Single(statement => statement is FunctionDeclaration nestedFunctionDeclaration &&
                                                nestedFunctionDeclaration.Name.Name == nestedFunctionName));
        var nestedDeclarationIndex = block.Statements.IndexOf(nestedDeclaration);

        var rewrittenNestedDeclaration = nestedDeclaration with
        {
            Function = nestedDeclaration.Function with
            {
                Body = new BlockStatement(
                    null,
                    [new ExportAllStatement(null, "./unsupported.js")],
                    true)
            }
        };
        var rewrittenBlock = block with
        {
            Statements = block.Statements.SetItem(nestedDeclarationIndex, rewrittenNestedDeclaration)
        };
        var rewrittenOuterDeclaration = outerDeclaration with
        {
            Function = outerDeclaration.Function with
            {
                Body = outerDeclaration.Function.Body with
                {
                    Statements = outerDeclaration.Function.Body.Statements.SetItem(
                        0,
                        ifStatement with { Then = rewrittenBlock })
                }
            }
        };

        return program with
        {
            Body = program.Body.SetItem(outerDeclarationIndex, rewrittenOuterDeclaration)
        };
    }

    private static void AssertProgramContains<TOp>(ExpressionProgram? program, Func<ExpressionOpView, bool>? predicate = null)
        where TOp : IExpressionOpMarker
    {
        Assert.NotNull(program);
        Assert.Contains(
            program.Value.GetOps(TOp.Kind),
            op => predicate is null || predicate(op));
    }
}
