#region

using System.Runtime.InteropServices;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private sealed class AwaitState
        {
            public bool HasResult { get; set; }
            public bool IsThrow { get; set; }
            public JsValue Result { get; set; } = JsValue.Undefined;
        }

        [StructLayout(LayoutKind.Auto)]
        internal readonly record struct AsyncGeneratorStepResult(
            AsyncGeneratorStepKind Kind,
            JsValue Value,
            bool Done,
            JsValue PendingPromise);

        internal enum AsyncGeneratorStepKind
        {
            Yield,
            Completed,
            Throw,
            Pending
        }

        internal enum ResumeMode
        {
            Next,
            Throw,
            Return
        }

        private enum GeneratorState
        {
            Start,
            Suspended,
            Executing,
            Completed
        }

        private enum ResumePayloadKind
        {
            None,
            Value,
            Throw,
            Return
        }

        private enum AbruptKind
        {
            None,
            Return,
            Throw,
            Break,
            Continue
        }

        private sealed class TryFrame(
            int handlerIndex,
            Symbol? catchSlotSymbol,
            int finallyIndex,
            int endFinallyIndex,
            JsEnvironment entryEnvironment,
            int loopContinueTarget = -1)
        {
            public int HandlerIndex { get; } = handlerIndex;
            public Symbol? CatchSlotSymbol { get; } = catchSlotSymbol;
            public int FinallyIndex { get; } = finallyIndex;

            /// <summary>
            /// Index of the EndFinally instruction for this try/finally.
            /// Used when break/continue inside a finally block needs to jump to EndFinally.
            /// </summary>
            public int EndFinallyIndex { get; } = endFinallyIndex;

            /// <summary>
            /// For for-of loops: the continue target index. When a continue targets this index,
            /// the finally should NOT be scheduled (we're staying in the loop).
            /// </summary>
            public int LoopContinueTarget { get; } = loopContinueTarget;

            /// <summary>
            /// The environment that was active when entering the try block.
            /// Used to restore scope when jumping to catch/finally handlers.
            /// </summary>
            public JsEnvironment EntryEnvironment { get; } = entryEnvironment;

            public bool CatchUsed { get; set; }
            public bool FinallyScheduled { get; set; }
            public PendingCompletion PendingCompletion { get; set; } = PendingCompletion.None;

            /// <summary>
            /// The value that was thrown and caught by this try/catch.
            /// Set by HandleAbruptCompletion when routing to a catch handler.
            /// Read by EnterCatchInstruction to bind the catch parameter.
            /// </summary>
            public JsValue ThrownValue { get; set; } = JsValue.Undefined;

            /// <summary>
            /// The script completion value saved when entering finally.
            /// If finally completes normally, this value is restored (with Unit→undefined).
            /// See ExecutionPlanRunner class documentation for details.
            /// </summary>
            public JsValue SavedCompletionValue { get; set; } = JsValue.Unit;
        }

        private readonly record struct PendingCompletion(AbruptKind Kind, object? Value, int ResumeTarget)
        {
            public static PendingCompletion None { get; } = new(AbruptKind.None, null, -1);

            public static PendingCompletion FromNormal(int resumeTarget)
            {
                return new PendingCompletion(AbruptKind.None, null, resumeTarget);
            }

            public static PendingCompletion FromAbrupt(AbruptKind kind, object? value)
            {
                return new PendingCompletion(kind, value, -1);
            }
        }

        /// <summary>
        /// Tracks breakable construct context at runtime so break/continue from AST-evaluated code
        /// (via StatementInstruction) can resolve their jump targets.
        /// Works for both loops and switch statements.
        /// </summary>
        private readonly record struct BreakableFrame(Symbol? Label, int BreakTarget, int ContinueTarget);

        private sealed class YieldStarState
        {
            public DelegatedYieldState? State { get; set; }
            public bool AwaitingResume { get; set; }
            public AbruptKind PendingAbrupt { get; set; }
            public JsValue PendingValue { get; set; }
        }
    }
}
