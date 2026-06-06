using System.Collections.Generic;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Guard tests that lock the invariant violated by the recurring "first boundary"
/// double-emission crash class (PRs #3097, #3101, #3104 and the rollback-hardening pass):
/// a <c>TryAppendFirstBoundary*</c> helper that appends operand loads and then returns
/// false on a later validation failure WITHOUT rolling back leaves stray ops behind.
/// The general expression loop then re-emits the full expression, doubling the leading
/// ops and pushing the operand stack past <see cref="UnifiedBytecodeProgram.MaxStackDepth"/>,
/// crashing the VM with IndexOutOfRangeException.
///
/// For a representative corpus of admitted first-boundary shapes this test compiles the
/// production program and asserts the statically-required peak operand-stack depth never
/// exceeds the program's declared <see cref="UnifiedBytecodeProgram.MaxStackDepth"/>. If a
/// rollback regresses (or a future helper reintroduces the bug) a shape double-emits and the
/// computed peak exceeds the declared budget, failing here instead of crashing at runtime.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeFirstBoundaryStackDepthGuardTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    public static IEnumerable<object[]> AdmittedFirstBoundaryShapes()
    {
        // Each entry: a function whose body is exactly one admitted first-boundary shape.
        // Covers every vulnerable/at-risk helper family audited in the hardening pass.

        // NamedPropertySet: base.name = rhs
        yield return Shape("f", "function f(o, v) { return o.name = v; }");
        // NamedPropertySet with template-literal RHS span.
        yield return Shape("f", "function f(o, v) { return o.name = `a${v}b`; }");
        // NestedNamedPropertySet: base.a.b = rhs
        yield return Shape("f", "function f(o, v) { return o.a.b = v; }");
        // NamedCompoundPropertySet: base.name += rhs
        yield return Shape("f", "function f(o, v) { return o.name += v; }");
        // NamedPropertyUpdate: ++base.name
        yield return Shape("f", "function f(o) { return ++o.name; }");
        // NestedNamedPropertyUpdate: ++base.a.b
        yield return Shape("f", "function f(o) { return ++o.a.b; }");
        // NamedPropertyReadChain: base.a.b.c
        yield return Shape("f", "function f(o) { return o.a.b.c; }");
        // ComputedPropertyRead: base[key]
        yield return Shape("f", "function f(o, k) { return o[k]; }");
        // PropertyReadBinaryExpression: base.a.b + rhs
        yield return Shape("f", "function f(o, v) { return o.a.b + v; }");
        // PropertyReadBinaryExpression with array-literal RHS span.
        yield return Shape("f", "function f(o, v) { return o.a === [v]; }");
        // PropertyReadShortCircuitExpression: base.a.b && rhs
        yield return Shape("f", "function f(o, v) { return o.a.b && v; }");
        // OptionalNamedPropertyRead: base?.a
        yield return Shape("f", "function f(o) { return o?.a; }");
        // OptionalNamedPropertyReadChain: base?.a.b
        yield return Shape("f", "function f(o) { return o?.a.b; }");
        // OptionalNamedThenComputed: base?.a[k]
        yield return Shape("f", "function f(o, k) { return o?.a[k]; }");
        // OptionalComputedPropertyReadChain: base?.[k].a
        yield return Shape("f", "function f(o, k) { return o?.[k].a; }");
        // OptionalNamedPropertyDelete: delete base?.a
        yield return Shape("f", "function f(o) { return delete o?.a; }");
        // OptionalNamedThenNamedPropertyDelete: delete base?.a.b
        yield return Shape("f", "function f(o) { return delete o?.a.b; }");
        // OptionalNamedThenComputedPropertyDelete: delete base?.a[k]
        yield return Shape("f", "function f(o, k) { return delete o?.a[k]; }");
        // OptionalComputedPropertyDeleteWithJump: delete base?.[k]
        yield return Shape("f", "function f(o, k) { return delete o?.[k]; }");
        // OptionalComputedReadThenComputedPropertyDelete: delete base?.[k1][k2]
        yield return Shape("f", "function f(o, k1, k2) { return delete o?.[k1][k2]; }");
    }

    [Theory]
    [MemberData(nameof(AdmittedFirstBoundaryShapes))]
    public void AdmittedFirstBoundaryShape_RequiredStackDepthWithinDeclaredBudget(
        string functionName,
        string source)
    {
        var plan = GetFunctionPlan(source, functionName);

        var eligibility = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(eligibility.IsEligible, eligibility.Reason);

        var program = eligibility.Program;
        var requiredDepth = ComputeRequiredStackDepth(program);

        Assert.True(
            requiredDepth <= program.MaxStackDepth,
            $"Required operand-stack depth {requiredDepth} exceeds declared MaxStackDepth " +
            $"{program.MaxStackDepth} for shape '{source}'. This indicates a first-boundary " +
            "helper emitted operand loads and then bailed without rolling back, doubling the " +
            "leading ops via the general expression loop.");
    }

    /// <summary>
    /// Abstract-interpretation of operand-stack depth over the emitted instructions, following
    /// jump targets via a worklist over (pc, depth). Each modeled opcode has an explicit net
    /// stack delta; an unmodeled opcode fails the test loudly so the simulation can never
    /// silently under-count.
    /// </summary>
    private int ComputeRequiredStackDepth(UnifiedBytecodeProgram program)
    {
        var instructions = program.Instructions;
        var peak = 0;
        var visited = new Dictionary<int, int>();
        var worklist = new Stack<(int Pc, int Depth)>();
        worklist.Push((0, 0));

        while (worklist.Count > 0)
        {
            var (pc, depth) = worklist.Pop();
            while (pc >= 0 && pc < instructions.Length)
            {
                // If we already analyzed this pc at >= this depth, the path is subsumed.
                if (visited.TryGetValue(pc, out var seenDepth) && seenDepth >= depth)
                {
                    break;
                }

                visited[pc] = depth;

                var instruction = instructions[pc];
                var (delta, isJump, isUnconditionalJump, target) =
                    DescribeStackEffect(instruction);

                // Account for transient pushes before the net delta lands (e.g. an op that
                // consumes 1 and produces 1 still only needs `depth` slots). The shapes here
                // only push at most one transient value beyond the net, so the peak is the
                // max of pre- and post-delta depths.
                var afterDepth = depth + delta;
                peak = System.Math.Max(peak, System.Math.Max(depth, afterDepth));

                Assert.True(afterDepth >= 0,
                    $"Operand-stack underflow at pc {pc} ({instruction.OpCode}).");

                if (isJump)
                {
                    worklist.Push((target, afterDepth));
                    if (isUnconditionalJump)
                    {
                        break;
                    }
                }

                pc++;
                depth = afterDepth;
            }
        }

        return peak;
    }

    private (int Delta, bool IsJump, bool IsUnconditionalJump, int Target) DescribeStackEffect(
        UnifiedBytecodeInstruction instruction)
    {
        switch (instruction.OpCode)
        {
            // Pushes one value.
            case UnifiedBytecodeOpCode.LoadSlot:
            case UnifiedBytecodeOpCode.LoadDynamicIdentifier:
            case UnifiedBytecodeOpCode.LoadThis:
            case UnifiedBytecodeOpCode.LoadNewTarget:
            case UnifiedBytecodeOpCode.LoadLiteral:
                return (+1, false, false, 0);

            // Net-neutral unary transforms of the top of stack.
            case UnifiedBytecodeOpCode.GetNamedProperty:
            case UnifiedBytecodeOpCode.GetNamedPropertyOptional:
            case UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet:
            case UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet:
            case UnifiedBytecodeOpCode.RequireObjectCoercible:
            case UnifiedBytecodeOpCode.ResolvePropertyKey:
            case UnifiedBytecodeOpCode.UpdateNamedProperty:
            case UnifiedBytecodeOpCode.DeleteNamedProperty:
            case UnifiedBytecodeOpCode.ToString:
                return (0, false, false, 0);

            // Consume one extra than they produce (binary fold of two operands -> one).
            case UnifiedBytecodeOpCode.Binary:
            case UnifiedBytecodeOpCode.GetComputedProperty:
            case UnifiedBytecodeOpCode.SetNamedProperty:
            case UnifiedBytecodeOpCode.DeleteComputedProperty:
                return (-1, false, false, 0);

            // [object, key, value] -> [value]
            case UnifiedBytecodeOpCode.SetComputedProperty:
                return (-2, false, false, 0);

            // Pushes a duplicate of the current top, or a fresh empty container.
            case UnifiedBytecodeOpCode.DuplicateTop:
            case UnifiedBytecodeOpCode.CreateArray:
                return (+1, false, false, 0);

            // Append-to-container ops keep the container and consume the appended value.
            case UnifiedBytecodeOpCode.ArrayPush:
            case UnifiedBytecodeOpCode.ArraySpread:
                return (-1, false, false, 0);

            case UnifiedBytecodeOpCode.ArrayPushHole:
                return (0, false, false, 0);

            // Pure stack shuffles.
            case UnifiedBytecodeOpCode.SwapTopTwo:
                return (0, false, false, 0);

            case UnifiedBytecodeOpCode.Pop:
                return (-1, false, false, 0);

            // Unconditional jump preserves depth, transfers control.
            case UnifiedBytecodeOpCode.Jump:
                return (0, true, true, instruction.Operand);

            // Conditional / short-circuit jumps inspect (and, for ReplaceUndefined, keep) the
            // top; both branches continue with the current depth.
            case UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined:
            case UnifiedBytecodeOpCode.JumpIfShortCircuitFalse:
            case UnifiedBytecodeOpCode.JumpIfShortCircuitTrue:
            case UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish:
            case UnifiedBytecodeOpCode.JumpIfShortCircuited:
                return (0, true, false, instruction.Operand);

            // Function epilogue: consumes the return value (or operates on an empty stack
            // when the slot path is used). Treat as neutral terminator.
            case UnifiedBytecodeOpCode.Return:
            case UnifiedBytecodeOpCode.StoreSlot:
            case UnifiedBytecodeOpCode.UpdateSlot:
            case UnifiedBytecodeOpCode.InitializeSlot:
                // StoreSlot/Update/Initialize pop the value they persist.
                return instruction.OpCode == UnifiedBytecodeOpCode.Return
                    ? (0, false, true, 0)
                    : (-1, false, false, 0);

            default:
                Assert.Fail(
                    $"Unmodeled opcode '{instruction.OpCode}' in first-boundary stack-depth " +
                    "guard. Add an explicit stack delta so the simulation stays sound.");
                return (0, false, false, 0);
        }
    }

    private static object[] Shape(string functionName, string source) => [functionName, source];

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
