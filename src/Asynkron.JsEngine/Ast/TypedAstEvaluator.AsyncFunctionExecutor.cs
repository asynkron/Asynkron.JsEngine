using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    ///     Drives an async function to completion using the generator IR executor.
    ///     Unlike AsyncGeneratorInstance which exposes .next()/.return()/.throw() methods,
    ///     this class drives execution automatically and returns a single Promise.
    /// </summary>
    internal sealed class AsyncFunctionExecutor
    {
        private readonly FunctionExpression _function;
        private readonly JsEnvironment _closure;
        private readonly IReadOnlyList<JsValue> _arguments;
        private readonly JsValue _thisValue;
        private readonly IJsCallable _callable;
        private readonly RealmState _realmState;
        private readonly bool _isLexicallyStrict;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly IJsObjectLike? _homeObject;
        private readonly PrivateNameScope? _privateNameScope;
        private readonly ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes;

        private TypedGeneratorInstance? _inner;

        public AsyncFunctionExecutor(
            FunctionExpression function,
            JsEnvironment closure,
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            IJsCallable callable,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment,
            IJsObjectLike? homeObject,
            PrivateNameScope? privateNameScope,
            ImmutableArray<PrivateNameScope> capturedPrivateNameScopes)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _callable = callable;
            _realmState = realmState;
            _isLexicallyStrict = isLexicallyStrict;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _homeObject = homeObject;
            _privateNameScope = privateNameScope;
            _capturedPrivateNameScopes = capturedPrivateNameScopes;
        }

        /// <summary>
        ///     Executes the async function and returns a Promise that resolves/rejects
        ///     with the function's completion value or thrown error.
        /// </summary>
        public JsValue Execute()
        {
            var promiseCtor = _realmState.PromiseConstructor;
            if (promiseCtor is null)
            {
                throw new InvalidOperationException("Promise constructor is not available.");
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                IJsCallable? resolve = null;
                IJsCallable? reject = null;

                if (execArgs.Count >= 1 && execArgs[0].TryUnwrap(out IJsCallable? res))
                {
                    resolve = res;
                }

                if (execArgs.Count >= 2 && execArgs[1].TryUnwrap(out IJsCallable? rej))
                {
                    reject = rej;
                }

                if (resolve is null || reject is null)
                {
                    return JsValue.Undefined;
                }

                try
                {
                    // Initialize generator inside Promise to capture any early errors
                    _inner = new TypedGeneratorInstance(
                        _function,
                        _closure,
                        _arguments,
                        _thisValue,
                        _callable,
                        _realmState,
                        _isLexicallyStrict,
                        _hasFunctionNameEnvironment,
                        _homeObject,
                        _privateNameScope,
                        _capturedPrivateNameScopes);
                    _inner.Initialize();

                    // Start execution - async functions don't receive an argument on first call
                    DriveToCompletion(TypedGeneratorInstance.ResumeMode.Next, JsValue.Undefined, resolve, reject);
                }
                catch (ThrowSignal signal)
                {
                    // Early error during initialization - reject the promise
                    InvokeWithOneArg(reject, signal.ThrownValue);
                }
                catch (Exception ex)
                {
                    // Non-JS exception during initialization - wrap in error message
                    InvokeWithOneArg(reject, (JsValue)ex.Message);
                }

                return JsValue.Undefined;
            });

            if (promiseCtor is HostFunction hostCtor)
            {
                return hostCtor.InvokeWithContext([(JsValue)executor], JsValue.Undefined, null, (JsValue)hostCtor);
            }

            return promiseCtor.Invoke([(JsValue)executor], JsValue.Undefined);
        }

        private void DriveToCompletion(
            TypedGeneratorInstance.ResumeMode mode,
            JsValue argument,
            IJsCallable resolve,
            IJsCallable reject)
        {
            try
            {
                var step = _inner!.ExecuteAsyncStep(mode, argument);

                switch (step.Kind)
                {
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                        // Async function completed - resolve with the return value
                        InvokeWithOneArg(resolve, step.Value);
                        break;

                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                        // Async functions don't yield externally - treat as await
                        // and continue driving to completion
                        DriveToCompletion(TypedGeneratorInstance.ResumeMode.Next, step.Value, resolve, reject);
                        break;

                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                        // Async function threw - reject the promise
                        InvokeWithOneArg(reject, step.Value);
                        break;

                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                        // Await hit a pending promise - attach handlers to resume
                        HandlePendingStep(step, resolve, reject);
                        break;
                }
            }
            catch (ThrowSignal signal)
            {
                // Uncaught exception - reject the promise
                InvokeWithOneArg(reject, signal.ThrownValue);
            }
            catch (Exception ex)
            {
                // Non-JS exception - wrap in error message
                InvokeWithOneArg(reject, (JsValue)ex.Message);
            }
        }

        private void HandlePendingStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            if (!step.PendingPromise.TryGetObject<JsObject>(out var pendingPromise))
            {
                InvokeWithOneArg(reject, (JsValue)"Awaited value is not a promise");
                return;
            }

            if (!pendingPromise.TryGetProperty("then", out var thenValue))
            {
                InvokeWithOneArg(reject, (JsValue)"Awaited value has no 'then' method");
                return;
            }

            if (!thenValue.TryUnwrap(out IJsCallable? thenCallable))
            {
                InvokeWithOneArg(reject, (JsValue)"'then' is not callable");
                return;
            }

            // Use lightweight IJsCallable implementations directly - no HostFunction wrapper needed
            // This avoids allocating HostFunction + its internal JsObject per await point
            var onFulfilled = new AsyncResumeCallback(this, resolve, reject, isRejection: false);
            var onRejected = new AsyncResumeCallback(this, resolve, reject, isRejection: true);

            InvokeWithTwoArgs(
                thenCallable,
                JsValue.FromObjectUnsafe(onFulfilled),
                JsValue.FromObjectUnsafe(onRejected),
                (JsValue)pendingPromise);

            // Don't drain microtasks here - let them drain naturally after synchronous
            // code completes. This ensures proper async semantics where async functions
            // suspend at await and synchronous code continues executing.
            // The engine's DrainMicrotasks() call after ExecuteProgram handles this.
        }

        /// <summary>
        ///     Lightweight callback for async function resume - avoids HostFunction allocation.
        /// </summary>
        private sealed class AsyncResumeCallback(
            AsyncFunctionExecutor executor,
            IJsCallable resolve,
            IJsCallable reject,
            bool isRejection) : IJsCallable
        {
            public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
            {
                var value = args.Count > 0 ? args[0] : JsValue.Undefined;
                var mode = isRejection
                    ? TypedGeneratorInstance.ResumeMode.Throw
                    : TypedGeneratorInstance.ResumeMode.Next;
                executor.DriveToCompletion(mode, value, resolve, reject);
                return JsValue.Undefined;
            }
        }

        private static void InvokeWithOneArg(IJsCallable callable, JsValue arg0)
        {
            var args = JsValueCache.RentJsValueArray(1);
            try
            {
                args[0] = arg0;
                callable.Invoke(args, JsValue.Undefined);
            }
            finally
            {
                JsValueCache.ReturnJsValueArray(args);
            }
        }

        private static void InvokeWithTwoArgs(IJsCallable callable, JsValue arg0, JsValue arg1, JsValue thisValue)
        {
            var args = JsValueCache.RentJsValueArray(2);
            try
            {
                args[0] = arg0;
                args[1] = arg1;
                callable.Invoke(args, thisValue);
            }
            finally
            {
                JsValueCache.ReturnJsValueArray(args);
            }
        }
    }
}
