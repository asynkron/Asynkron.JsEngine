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
    public async Task YieldInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram()
    {
        var plan = await GetFunctionPlan("""
            function* yieldSimple(value) {
                yield value + 1;
            }
            """, "yieldSimple");

        var instruction = Assert.Single(plan.Instructions.OfType<YieldInstruction>(), i => i.YieldProgram is not null);
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

        var instruction = Assert.Single(plan.Instructions.OfType<YieldStarInstruction>(), i => i.IterableProgram is not null);
        Assert.Null(instruction.IterableExpression);
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
        Assert.Null(instruction.IterableExpression);
        Assert.Null(instruction.IterableProgram);
        Assert.NotNull(instruction.AwaitStateKey);
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.AwaitedProgram, op => op.Name.Name == "items");
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
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.RhsProgram,
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
, i => i.TargetSymbol.Name == "value");
        Assert.Null(declaration.Initializer);
        Assert.NotNull(declaration.InitializerProgram);

        var expressionStatement = Assert.Single(cache.Plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
        Assert.Null(expressionStatement.Expression);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            expressionStatement.ExpressionProgram,
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
        AssertProgramContains<LoadIdentifierExpressionOp>(
            instruction.IterableProgram,
            op => op.Name.Name == "items");
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

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
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

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
        Assert.Null(instruction.Expression);
        Assert.NotNull(instruction.ExpressionProgram);

        var operations = instruction.ExpressionProgram.Value.Operations;
        var loadSuperCallTargetIndex = Array.FindIndex(operations.ToArray(), op => op is LoadNamedSuperCallTargetExpressionOp);
        var innerCallIndex = Array.FindIndex(operations.ToArray(), op => op is CallExpressionOp);
        var outerSuperConstructIndex = Array.FindIndex(operations.ToArray(), op => op is SuperConstructExpressionOp);

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

        var instruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
        Assert.Null(instruction.Expression);
        Assert.NotNull(instruction.ExpressionProgram);

        var operations = instruction.ExpressionProgram.Value.Operations;
        var ensureIndex = Array.FindIndex(operations.ToArray(), op => op is EnsureSuperReferenceExpressionOp);
        var innerSuperConstructIndex = Array.FindIndex(operations.ToArray(), op => op is SuperConstructExpressionOp);
        var computedReadIndex = Array.FindIndex(operations.ToArray(), op => op is GetComputedSuperPropertyExpressionOp);

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
        Assert.Equal(2, instruction.ReturnProgram!.Value.Operations.OfType<CallExpressionOp>().Count());
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
        AssertProgramContains<StoreIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
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
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
        AssertProgramContains<BinaryExpressionOp>(instruction.ReturnProgram, op => op.Operator == BinaryOperator.Add);
        AssertProgramContains<StoreIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
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
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
        AssertProgramContains<JumpIfTrueExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<StoreIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
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
        AssertProgramContains<LoadIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
        AssertProgramContains<JumpIfNotNullishExpressionOp>(instruction.ReturnProgram);
        AssertProgramContains<StoreIdentifierExpressionOp>(instruction.ReturnProgram, op => op.Name.Name == "current");
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

        var expressionStatement = Assert.Single(cache.Plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
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
        AssertProgramContains<LoadFunctionLiteralExpressionOp>(instruction.InitializerProgram);
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
            instruction.InitializerProgram!.Value.Operations.OfType<DefineObjectAccessorExpressionOp>().Count());
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

        var assignment = Assert.Single(cache.Plan!.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
        Assert.Null(assignment.Expression);
        AssertProgramContains<StoreIdentifierExpressionOp>(
            assignment.ExpressionProgram,
            op => op.Name.Name == "value");
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

        var assignment = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>()
, i => i.ExpressionProgram is not null);
        Assert.Null(assignment.Expression);
        AssertProgramContains<ApplyBindingTargetExpressionOp>(
            assignment.ExpressionProgram,
            op => op.TargetProgram is ArrayBindingTargetProgram);
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

    private static void AssertProgramContains<TOp>(ExpressionProgram? program, Func<TOp, bool>? predicate = null)
        where TOp : ExpressionOp
    {
        Assert.NotNull(program);
        var match = program.Value.Operations.OfType<TOp>().FirstOrDefault(op => predicate is null || predicate(op));
        Assert.NotNull(match);
    }
}
