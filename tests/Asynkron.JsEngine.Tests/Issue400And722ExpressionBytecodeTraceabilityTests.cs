using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
[Trait("Issue", "400")]
[Trait("Issue", "722")]
public sealed class Issue400And722ExpressionBytecodeTraceabilityTests : IAsyncLifetime
{
    private static readonly Regex EvaluateExpressionPattern = new(@"EvaluateExpression\(", RegexOptions.Compiled);

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
    public async Task RepresentativeInstructions_UseExpressionProgramsInsteadOfAstPayloads()
    {
        var returnPlan = await GetFunctionPlan("""
            function proofReturn(left, right) {
                return left + right;
            }
            """, "proofReturn");
        var returnInstruction = Assert.Single(returnPlan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        Assert.Null(returnInstruction.ReturnExpression);
        Assert.False(returnInstruction.ReturnProgram!.Value.IsEmpty);

        var throwPlan = await GetFunctionPlan("""
            function proofThrow(value) {
                throw value + 1;
            }
            """, "proofThrow");
        var throwInstruction = Assert.Single(throwPlan.Instructions.OfType<ThrowInstruction>(), i => i.ThrowProgram is not null);
        Assert.Null(throwInstruction.Expression);
        Assert.False(throwInstruction.ThrowProgram!.Value.IsEmpty);

        var branchPlan = await GetFunctionPlan("""
            function proofBranch(value) {
                if (value > 1) {
                    return value;
                }

                return 0;
            }
            """, "proofBranch");
        var branchInstruction = Assert.Single(branchPlan.Instructions.OfType<BranchInstruction>());
        Assert.False(branchInstruction.ConditionProgram.IsEmpty);

        var expressionStatementPlan = await GetFunctionPlan("""
            function proofExpressionStatement(value) {
                value + 1;
                return value;
            }
            """, "proofExpressionStatement");
        var expressionStatementInstruction = Assert.Single(expressionStatementPlan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.Null(expressionStatementInstruction.Expression);
        Assert.False(expressionStatementInstruction.ExpressionProgram.IsEmpty);

        var declarationPlan = await GetFunctionPlan("""
            function proofDeclaration(value) {
                let current = value + 2;
                return current;
            }
            """, "proofDeclaration");
        var declarationInstruction = Assert.Single(declarationPlan.Instructions.OfType<SimpleVariableDeclarationInstruction>());
        Assert.Null(declarationInstruction.Initializer);
        Assert.False(declarationInstruction.InitializerProgram!.Value.IsEmpty);

        var assignmentPlan = await GetFunctionPlan("""
            function proofAssign(value) {
                let current = 0;
                current = value + 1;
                return current;
            }
            """, "proofAssign");
        var assignmentInstruction = Assert.Single(assignmentPlan.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(assignmentInstruction.ValueExpression);
        Assert.False(assignmentInstruction.ValueProgram!.Value.IsEmpty);

        var logicalCompoundPlan = await GetFunctionPlan("""
            function proofLogicalCompound(value) {
                let current = 0;
                current ||= value + 1;
                return current;
            }
            """, "proofLogicalCompound");
        var logicalCompoundInstruction = Assert.Single(logicalCompoundPlan.Instructions.OfType<LogicalCompoundAssignmentSlotInstruction>());
        Assert.Null(logicalCompoundInstruction.RhsExpression);
        Assert.False(logicalCompoundInstruction.RhsProgram!.Value.IsEmpty);

        var compoundPlan = await GetFunctionPlan("""
            function proofCompound(value) {
                let current = 0;
                current += value + 1;
                return current;
            }
            """, "proofCompound");
        var compoundInstruction = Assert.Single(compoundPlan.Instructions.OfType<CompoundAssignmentSlotInstruction>());
        Assert.Null(compoundInstruction.RhsExpression);
        Assert.False(compoundInstruction.RhsProgram!.Value.IsEmpty);

        var awaitAndDiscardPlan = await GetFunctionPlan("""
            async function proofAwaitAndDiscard(value) {
                await value;
                return value;
            }
            """, "proofAwaitAndDiscard");
        var awaitAndDiscardInstruction = Assert.Single(awaitAndDiscardPlan.Instructions.OfType<AwaitAndDiscardInstruction>());
        Assert.False(awaitAndDiscardInstruction.AwaitedProgram.IsEmpty);

        var awaitedAssignmentPlan = await GetFunctionPlan("""
            async function proofAwaitAssignment(value) {
                let current = 0;
                current = await value;
                return current;
            }
            """, "proofAwaitAssignment");
        var awaitedAssignmentInstruction = Assert.Single(awaitedAssignmentPlan.Instructions.OfType<AssignmentSlotInstruction>(), i => i.AwaitedProgram is not null);
        Assert.Null(awaitedAssignmentInstruction.ValueExpression);
        Assert.False(awaitedAssignmentInstruction.AwaitedProgram!.Value.IsEmpty);

        var awaitedLogicalCompoundPlan = await GetFunctionPlan("""
            async function proofAwaitLogicalCompound(value) {
                let current = 0;
                current ||= await value;
                return current;
            }
            """, "proofAwaitLogicalCompound");
        var awaitedLogicalCompoundInstruction = Assert.Single(awaitedLogicalCompoundPlan.Instructions.OfType<LogicalCompoundAssignmentSlotInstruction>(), i => i.AwaitedProgram is not null);
        Assert.Null(awaitedLogicalCompoundInstruction.RhsExpression);
        Assert.False(awaitedLogicalCompoundInstruction.AwaitedProgram!.Value.IsEmpty);

        var awaitedCompoundPlan = await GetFunctionPlan("""
            async function proofAwaitCompound(value) {
                let current = 0;
                current += await value;
                return current;
            }
            """, "proofAwaitCompound");
        var awaitedCompoundInstruction = Assert.Single(awaitedCompoundPlan.Instructions.OfType<CompoundAssignmentSlotInstruction>(), i => i.AwaitedProgram is not null);
        Assert.Null(awaitedCompoundInstruction.RhsExpression);
        Assert.False(awaitedCompoundInstruction.AwaitedProgram!.Value.IsEmpty);

        var yieldPlan = await GetFunctionPlan("""
            function* proofYield(value) {
                yield value + 1;
            }
            """, "proofYield");
        var yieldInstruction = Assert.Single(yieldPlan.Instructions.OfType<YieldInstruction>(), i => i.YieldProgram is not null);
        Assert.False(yieldInstruction.YieldProgram!.Value.IsEmpty);

        var yieldStarPlan = await GetFunctionPlan("""
            function* proofYieldStar(iterable) {
                yield* iterable;
            }
            """, "proofYieldStar");
        var yieldStarInstruction = Assert.Single(yieldStarPlan.Instructions.OfType<YieldStarInstruction>(), i => i.IterableProgram is not null);
        Assert.False(yieldStarInstruction.IterableProgram!.Value.IsEmpty);

        var withPlan = await GetFunctionPlan("""
            function proofWith(target) {
                with (target) {
                    return value;
                }
            }
            """, "proofWith");
        var withInstruction = Assert.Single(withPlan.Instructions.OfType<EnterWithInstruction>(), i => i.ObjectProgram is not null);
        Assert.False(withInstruction.ObjectProgram!.Value.IsEmpty);

        var forInPlan = await GetFunctionPlan("""
            function proofForIn(target) {
                for (const key in target) {
                    return key;
                }
            }
            """, "proofForIn");
        var forInInstruction = Assert.Single(forInPlan.Instructions.OfType<ForInInitInstruction>(), i => i.ObjectProgram is not null);
        Assert.False(forInInstruction.ObjectProgram!.Value.IsEmpty);

        var forOfPlan = await GetFunctionPlan("""
            function proofForOf(source) {
                for (const value of source) {
                    return value;
                }
            }
            """, "proofForOf");
        var forOfInstruction = Assert.Single(forOfPlan.Instructions.OfType<IteratorInitInstruction>(), i => i.IterableProgram is not null);
        Assert.False(forOfInstruction.IterableProgram!.Value.IsEmpty);

        var objectDestructuringPlan = await GetFunctionPlan("""
            function proofObjectDestructure(source) {
                let { first } = source;
                return first;
            }
            """, "proofObjectDestructure");
        var bindingInstruction = Assert.Single(objectDestructuringPlan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(bindingInstruction.Initializer);
        Assert.False(bindingInstruction.InitializerProgram!.Value.IsEmpty);

        var destructuringPlan = await GetFunctionPlan("""
            function proofDestructure(source) {
                let [first] = source;
                return first;
            }
            """, "proofDestructure");
        var destructuringInitInstruction = Assert.Single(destructuringPlan.Instructions.OfType<ArrayDestructuringInitInstruction>());
        Assert.Null(destructuringInitInstruction.SourceExpression);
        Assert.False(destructuringInitInstruction.SourceProgram.IsEmpty);

        var nestedObjectDestructuringPlan = await GetFunctionPlan("""
            function proofNestedObjectDestructure(source, fallbackKey) {
                let { [fallbackKey]: value = source.alt } = source;
                return value;
            }
            """, "proofNestedObjectDestructure");
        var nestedBindingInstruction = Assert.Single(nestedObjectDestructuringPlan.Instructions.OfType<BindingVariableDeclarationInstruction>());
        Assert.Null(nestedBindingInstruction.Initializer);
        Assert.False(nestedBindingInstruction.InitializerProgram!.Value.IsEmpty);
        AssertObjectBindingProgramsPresent(Assert.IsType<ObjectBindingTargetProgram>(nestedBindingInstruction.TargetProgram));
    }

    [Fact]
    public async Task ConstantPools_RecordLiteralStringObjectAndIdentifierEntries()
    {
        var plan = await GetFunctionPlan("""
            function proofConstants(alpha) {
                return {
                    count: 42,
                    value: alpha,
                    helper: function helperImpl() { return 1; },
                    Box: class Box {}
                };
            }
            """, "proofConstants");

        var instruction = Assert.Single(plan.Instructions.OfType<ReturnInstruction>(), i => i.ReturnProgram is not null);
        var program = instruction.ReturnProgram!.Value;

        Assert.True(program.ContainsLiteralConstant(value => value.IsNumber && value.NumberValue == 42.0));
        Assert.True(program.ContainsStringConstant("count"));
        Assert.True(program.ContainsStringConstant("value"));
        Assert.True(program.ContainsStringConstant("helper"));
        Assert.True(program.ContainsStringConstant("Box"));
        Assert.True(program.ContainsIdentifierConstant(identifier => identifier.Name.Name == "alpha"));
        Assert.True(program.ContainsObjectConstant<FunctionLiteralDescriptor>(
            descriptor => descriptor.Function.Name!.Name == "helperImpl"));
        Assert.True(program.ContainsObjectConstant<ClassExpression>(classExpression => classExpression.Name!.Name == "Box"));
    }

    [Fact]
    public void RuntimeScan_FindsNoEvaluateExpressionCallersOutsideLegacyDefinition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot.FullName, "src", "Asynkron.JsEngine", "Ast"),
            Path.Combine(repositoryRoot.FullName, "src", "Asynkron.JsEngine", "Execution")
        };

        var matches = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .SelectMany(file =>
            {
                var relativePath = Path.GetRelativePath(repositoryRoot.FullName, file).Replace('\\', '/');
                return File.ReadAllLines(file)
                    .Select((line, index) => new { line, index })
                    .Where(entry => EvaluateExpressionPattern.IsMatch(entry.line))
                    .Select(entry => $"{relativePath}:{entry.index + 1}:{entry.line.Trim()}");
            })
            .ToArray();

        var match = Assert.Single(matches);
        Assert.StartsWith("src/Asynkron.JsEngine/Ast/Legacy/ExpressionNodeExtensions.cs:", match, StringComparison.Ordinal);
        Assert.Contains("private static JsValue EvaluateExpression", match, StringComparison.Ordinal);
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

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Asynkron.JsEngine.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private static void AssertObjectBindingProgramsPresent(ObjectBindingTargetProgram targetProgram)
    {
        var property = Assert.Single(targetProgram.Properties);
        Assert.False(property.NameProgram!.Value.IsEmpty);
        Assert.False(property.DefaultProgram!.Value.IsEmpty);
    }
}
