using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Regression guard for the short-circuit flag column maintained by the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) across the stack-permuting
///     opcodes <c>SwapTopTwo</c> / <c>RotateTopThreeRight</c> / <c>DuplicateTop</c> /
///     <c>DuplicateTopTwo</c>.
///
///     Background: the synchronous <c>Execute</c> path keeps the short-circuit flag array
///     (<c>stackShortCircuitFlags</c>) aligned with the operand stack as those opcodes shuffle values
///     (via <c>SwapShortCircuitFlags</c> / <c>RotateShortCircuitFlagsRight</c> / <c>CopyShortCircuitFlag</c>).
///     The resumable VM originally permuted the operand stack WITHOUT permuting the flag column. That
///     asymmetry was unreachable in practice — the resumable eligibility gate
///     (<c>TryFindResumablePlanDecline</c>) declines every shape that would emit
///     <c>JumpIfShortCircuited</c>, the only opcode that sets
///     <see cref="UnifiedBytecodeProgram.RequiresShortCircuitStackFlags" /> and thus allocates the flag
///     column — so no admitted resumable program ever exercised the gap. But a future relaxation of that
///     gate could admit a flag-bearing program and silently corrupt flag alignment across a permute.
///
///     These tests close that trap by exercising the ported flag maintenance directly: each builds a
///     minimal resumable program with the flag column allocated (it contains a
///     <c>JumpIfShortCircuited</c>), sets the short-circuit flag on an operand via a nullish optional
///     read, runs ONE permute opcode, then branches on <c>JumpIfShortCircuited</c> reading the operand
///     slot the flagged value was moved to. If the flag column tracked the permute, the branch is taken
///     and the program returns <see cref="ShortCircuitedResult" />; otherwise it falls through to
///     <see cref="NotShortCircuitedResult" />. Every case must return
///     <see cref="ShortCircuitedResult" />, which only holds if the permute maintained the flag column.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumablePermuteFlagTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const double ShortCircuitedResult = 100;
    private const double NotShortCircuitedResult = 200;

    // SwapTopTwo: flag the bottom-of-two operand, push a fresh top, swap. The flag must follow its value
    // to the new top so JumpIfShortCircuited (reading the top) is taken.
    [Fact]
    public void SwapTopTwo_MovesShortCircuitFlagWithValue()
    {
        // 0: push undefined                -> stack [undef]            flags [_]
        // 1: GetNamedPropertyOptional 'a'  -> nullish base short-circuits: stack [undef*]   flags [1]
        // 2: push 7                        -> stack [undef*, 7]        flags [1, 0]
        // 3: SwapTopTwo                    -> stack [7, undef*]        flags [0, 1]  (only with the port)
        // 4: JumpIfShortCircuited -> 7     -> top flag set => jump
        // 5: push 200 / 6: return          (not short-circuited path)
        // 7: push 100 / 8: return          (short-circuited path)
        var result = RunPermuteProgram(
            ImmutableArray.Create(
                Op(UnifiedBytecodeOpCode.LoadLiteral, 0),
                Op(UnifiedBytecodeOpCode.GetNamedPropertyOptional, 0),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 1),
                Op(UnifiedBytecodeOpCode.SwapTopTwo),
                Op(UnifiedBytecodeOpCode.JumpIfShortCircuited, 7),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 2),
                Op(UnifiedBytecodeOpCode.Return),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 3),
                Op(UnifiedBytecodeOpCode.Return)),
            maxStackDepth: 6);

        Assert.Equal(ShortCircuitedResult, result);
    }

    // RotateTopThreeRight: (a, b, c=top) -> (c, a, b=top). Flag the MIDDLE operand (b); after the rotate
    // b is the new top, so its flag must be at the top for JumpIfShortCircuited to fire.
    [Fact]
    public void RotateTopThreeRight_MovesShortCircuitFlagWithValue()
    {
        // 0: push 1                        -> [1]
        // 1: push undefined                -> [1, undef]
        // 2: GetNamedPropertyOptional 'a'  -> [1, undef*]            flags [0, 1]   (b is flagged)
        // 3: push 9                        -> [1, undef*, 9]         flags [0, 1, 0]
        // 4: RotateTopThreeRight           -> [9, 1, undef*]         flags [0, 0, 1] (only with the port)
        // 5: JumpIfShortCircuited -> 8
        // 6: push 200 / 7: return
        // 8: push 100 / 9: return
        var result = RunPermuteProgram(
            ImmutableArray.Create(
                Op(UnifiedBytecodeOpCode.LoadLiteral, 4),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 0),
                Op(UnifiedBytecodeOpCode.GetNamedPropertyOptional, 0),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 5),
                Op(UnifiedBytecodeOpCode.RotateTopThreeRight),
                Op(UnifiedBytecodeOpCode.JumpIfShortCircuited, 8),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 2),
                Op(UnifiedBytecodeOpCode.Return),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 3),
                Op(UnifiedBytecodeOpCode.Return)),
            maxStackDepth: 6);

        Assert.Equal(ShortCircuitedResult, result);
    }

    // DuplicateTop: flag the top, duplicate it; the copy must inherit the flag.
    [Fact]
    public void DuplicateTop_CopiesShortCircuitFlagToDuplicate()
    {
        // 0: push undefined                -> [undef]
        // 1: GetNamedPropertyOptional 'a'  -> [undef*]               flags [1]
        // 2: DuplicateTop                  -> [undef*, undef*]       flags [1, 1]  (copy flag only with port)
        // 3: JumpIfShortCircuited -> 6     (reads the duplicated top)
        // 4: push 200 / 5: return
        // 6: push 100 / 7: return
        var result = RunPermuteProgram(
            ImmutableArray.Create(
                Op(UnifiedBytecodeOpCode.LoadLiteral, 0),
                Op(UnifiedBytecodeOpCode.GetNamedPropertyOptional, 0),
                Op(UnifiedBytecodeOpCode.DuplicateTop),
                Op(UnifiedBytecodeOpCode.JumpIfShortCircuited, 6),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 2),
                Op(UnifiedBytecodeOpCode.Return),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 3),
                Op(UnifiedBytecodeOpCode.Return)),
            maxStackDepth: 6);

        Assert.Equal(ShortCircuitedResult, result);
    }

    // DuplicateTopTwo: flag the LOWER of the top two; after the dup-two its copy lands two slots up.
    // Pop the topmost copy so JumpIfShortCircuited reads the copied flag of the lower operand.
    [Fact]
    public void DuplicateTopTwo_CopiesShortCircuitFlagsOfBothOperands()
    {
        // 0: push undefined                -> [undef]
        // 1: GetNamedPropertyOptional 'a'  -> [undef*]               flags [1]            (lower = flagged)
        // 2: push 5                        -> [undef*, 5]            flags [1, 0]
        // 3: DuplicateTopTwo               -> [undef*, 5, undef*, 5] flags [1, 0, 1, 0]   (copies only with port)
        // 4: Pop                           -> [undef*, 5, undef*]    flags [1, 0, 1]
        // 5: JumpIfShortCircuited -> 8     (reads the copied lower operand at slot 2)
        // 6: push 200 / 7: return
        // 8: push 100 / 9: return
        var result = RunPermuteProgram(
            ImmutableArray.Create(
                Op(UnifiedBytecodeOpCode.LoadLiteral, 0),
                Op(UnifiedBytecodeOpCode.GetNamedPropertyOptional, 0),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 6),
                Op(UnifiedBytecodeOpCode.DuplicateTopTwo),
                Op(UnifiedBytecodeOpCode.Pop),
                Op(UnifiedBytecodeOpCode.JumpIfShortCircuited, 8),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 2),
                Op(UnifiedBytecodeOpCode.Return),
                Op(UnifiedBytecodeOpCode.LoadLiteral, 3),
                Op(UnifiedBytecodeOpCode.Return)),
            maxStackDepth: 6);

        Assert.Equal(ShortCircuitedResult, result);
    }

    private static UnifiedBytecodeInstruction Op(UnifiedBytecodeOpCode opCode, int operand = 0) =>
        new(opCode, operand);

    private static double RunPermuteProgram(
        ImmutableArray<UnifiedBytecodeInstruction> instructions,
        int maxStackDepth)
    {
        // LiteralConstants: 0=undefined (nullish optional base), 1=7, 2=NotShortCircuitedResult,
        // 3=ShortCircuitedResult, 4=1, 5=9, 6=5.
        var literals = ImmutableArray.Create(
            JsValue.Undefined,
            JsValue.FromDouble(7),
            JsValue.FromDouble(NotShortCircuitedResult),
            JsValue.FromDouble(ShortCircuitedResult),
            JsValue.FromDouble(1),
            JsValue.FromDouble(9),
            JsValue.FromDouble(5));

        var program = new UnifiedBytecodeProgram(
            Instructions: instructions,
            MaxStackDepth: maxStackDepth,
            SlotCount: 0,
            LiteralConstants: literals,
            StringConstants: ImmutableArray.Create("a"),
            SlotNames: ImmutableArray<string?>.Empty,
            ParameterSlotIndices: ImmutableArray<int>.Empty,
            LexicalSlotIndices: ImmutableArray<int>.Empty,
            CallTargetConstants: ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ScopeDescriptors: ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            TryDescriptors: ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            CatchDescriptors: ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            DriverDescriptors: ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty,
            RequiresShortCircuitStackFlags: true);

        // The flag column is allocated iff RequiresShortCircuitStackFlags — assert the premise so a
        // future change to that wiring can't make this test silently vacuous.
        var state = new UnifiedBytecodeResumeState(program, Array.Empty<JsValue>());
        Assert.NotNull(state.OperandStackShortCircuitFlags);

        var context = new EvaluationContext(new RealmState());
        var result = UnifiedBytecodeVirtualMachine.ExecuteResumable(
            state,
            UnifiedBytecodeResumeMode.Next,
            JsValue.Undefined,
            context);

        Assert.Equal(UnifiedBytecodeStepKind.Completed, result.Kind);
        return result.Value.AsDouble();
    }
}
