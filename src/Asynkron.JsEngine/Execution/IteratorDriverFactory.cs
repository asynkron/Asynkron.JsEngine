using System.Collections.Generic;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal static class IteratorDriverFactory
{
    public static IteratorDriverPlan CreatePlan(ForEachStatement statement, BlockStatement rewrittenBody)
    {
        var kind = statement.Kind == ForEachKind.AwaitOf
            ? IteratorDriverKind.Await
            : IteratorDriverKind.Sync;

        return new IteratorDriverPlan(
            kind,
            statement.Iterable,
            statement.Target,
            statement.DeclarationKind,
            rewrittenBody);
    }
}

internal readonly record struct IteratorInstructionPlan(
    Symbol IteratorSlot,
    Symbol ValueSlot,
    int InitIndex,
    int MoveNextIndex);

internal static class IteratorInstructionTemplate
{
    public static IteratorInstructionPlan AppendInstructions(List<GeneratorInstruction> instructions,
        IteratorDriverPlan plan,
        int breakIndex)
    {
        var instructionStart = instructions.Count;
        var iteratorSymbol = Symbol.Intern(plan.Kind == IteratorDriverKind.Await
            ? $"__forAwait_iter_{instructionStart}"
            : $"__forOf_iter_{instructionStart}");
        var valueSymbol = Symbol.Intern(plan.Kind == IteratorDriverKind.Await
            ? $"__forAwait_value_{instructionStart}"
            : $"__forOf_value_{instructionStart}");

        var initIndex = instructions.Count;
        instructions.Add(new IteratorInitInstruction(plan.Kind, plan.Iterable, iteratorSymbol, -1));

        var moveNextIndex = instructions.Count;
        instructions.Add(new IteratorMoveNextInstruction(plan.Kind, iteratorSymbol, valueSymbol, breakIndex, -1));

        return new IteratorInstructionPlan(iteratorSymbol, valueSymbol, initIndex, moveNextIndex);
    }

    public static void Wire(IteratorInstructionPlan plan, int bodyEntryIndex, List<GeneratorInstruction> instructions)
    {
        instructions[plan.MoveNextIndex] =
            ((IteratorMoveNextInstruction)instructions[plan.MoveNextIndex]) with { Next = bodyEntryIndex };

        instructions[plan.InitIndex] =
            ((IteratorInitInstruction)instructions[plan.InitIndex]) with { Next = plan.MoveNextIndex };
    }
}
