#region

using System.Runtime.InteropServices;
using Asynkron.JsEngine.JsTypes;

#endregion

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
            JsValue entryCompletionValue,
            int loopContinueTarget = -1,
            int loopBreakTarget = -1)
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
            /// For for-of loops: the break target index. When a break targets this index,
            /// the finally should be scheduled via LeaveTry flow (exiting the loop).
            /// </summary>
            public int LoopBreakTarget { get; } = loopBreakTarget;

            /// <summary>
            /// The environment that was active when entering the try block.
            /// Used to restore scope when jumping to catch/finally handlers.
            /// </summary>
            public JsEnvironment EntryEnvironment { get; } = entryEnvironment;

            /// <summary>
            /// The completion value that was active when entering the try block.
            /// Used to implement ES spec UpdateEmpty semantics: if the try/catch
            /// completes without producing a new value, result becomes undefined.
            /// </summary>
            public JsValue EntryCompletionValue { get; } = entryCompletionValue;

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
            /// The completion value from the try or catch block before entering finally.
            /// Per ES spec 13.15.8, if finally completes normally, this value should be
            /// used as the final completion value (not finally's own value).
            /// </summary>
            public JsValue TryCatchCompletionValue { get; set; } = JsValue.Undefined;
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
        /// Tracks loop context at runtime so break/continue from AST-evaluated code
        /// (via StatementInstruction) can resolve their jump targets.
        /// EntryCompletionValue is used in script mode to track whether the loop body produced a value.
        /// </summary>
        private readonly record struct LoopFrame(Symbol? Label, int BreakTarget, int ContinueTarget, JsValue EntryCompletionValue);

        private sealed class YieldStarState
        {
            public DelegatedYieldState? State { get; set; }
            public bool AwaitingResume { get; set; }
            public AbruptKind PendingAbrupt { get; set; }
            public JsValue PendingValue { get; set; }
        }
    }
}
