#region

using Asynkron.JsEngine.Execution;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        /// <summary>
        /// State for async function execution (await handling).
        /// Only allocated when the function actually uses async/await.
        /// </summary>
        private sealed class AsyncState
        {
            public bool AsyncStepMode;
            public Symbol? PendingAwaitKey;
            public JsValue PendingPromise;
            public ResumePayloadKind PendingResumeKind;
            public JsValue PendingResumeValue = JsValue.Undefined;
        }

        /// <summary>
        /// State for generator yield tracking.
        /// Only allocated when the function is a generator.
        /// </summary>
        private sealed class YieldState
        {
            public readonly YieldResumeContext ResumeContext = new();
            public int LastYieldIndex = -1;
            public int LastYieldSourceEnd = -1;
            public int LastYieldSourceStart = -1;
        }

        /// <summary>
        /// State for for-of iterator handling.
        /// Only allocated when the function uses for-of loops.
        /// </summary>
        private sealed class IteratorState
        {
            public IteratorDriverState? CurrentDriverState;
            public JsEnvironment? ResumedWithEnvironment;
        }

        /// <summary>
        /// State for try/catch/finally handling.
        /// Only allocated when the function has try blocks.
        /// </summary>
        private sealed class TryCatchState
        {
            public readonly Stack<TryFrame> TryStack = new();
            public JsEnvironment? RestoredEnvironmentFromTry;
        }

        /// <summary>
        /// State for breakable construct (loop/switch) break/continue handling.
        /// Only allocated when the function has loops or switch statements.
        /// </summary>
        private sealed class BreakableState
        {
            public readonly Stack<BreakableFrame> BreakableStack = new();
        }

        /// <summary>
        /// State for 'with' statement scope tracking.
        /// Only allocated when the function uses 'with' (rare, forbidden in strict mode).
        /// </summary>
        private sealed class WithState
        {
            public readonly Stack<Symbol> ActiveWithScopes = new();
        }

        /// <summary>
        /// State for for-in loop handling.
        /// Only allocated when the function uses for-in loops.
        /// </summary>
        private sealed class ForInState
        {
            public ForInDriverState? CurrentDriverState;
        }
    }
}
