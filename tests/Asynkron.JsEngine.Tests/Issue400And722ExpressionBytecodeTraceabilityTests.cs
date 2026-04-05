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
            async function proofThrow(valuePromise) {
                throw await valuePromise;
            }
            """, "proofThrow");
        var throwInstruction = Assert.Single(throwPlan.Instructions.OfType<ThrowInstruction>(), i => i.AwaitedProgram is not null);
        Assert.Null(throwInstruction.Expression);
        Assert.False(throwInstruction.AwaitedProgram!.Value.IsEmpty);

        var assignmentPlan = await GetFunctionPlan("""
            async function proofAssign(valuePromise) {
                let current = 0;
                current = (await valuePromise) + 1;
                return current;
            }
            """, "proofAssign");
        var assignmentInstruction = Assert.Single(assignmentPlan.Instructions.OfType<AssignmentSlotInstruction>());
        Assert.Null(assignmentInstruction.ValueExpression);
        Assert.False(assignmentInstruction.ValueProgram!.Value.IsEmpty);

        var awaitPlan = await GetFunctionPlan("""
            async function proofAwait(value) {
                await value;
                return 1;
            }
            """, "proofAwait");
        var awaitInstruction = Assert.Single(awaitPlan.Instructions.OfType<AwaitAndDiscardInstruction>());
        Assert.False(awaitInstruction.AwaitedProgram.IsEmpty);
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
        Assert.True(program.ContainsObjectConstant<FunctionLiteralDescriptor>(d => d.Function.Name!.Name == "helperImpl"));
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
        Assert.StartsWith("src/Asynkron.JsEngine/Ast/Legacy/ExpressionNodeExtensions.cs:222:", match, StringComparison.Ordinal);
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
}
