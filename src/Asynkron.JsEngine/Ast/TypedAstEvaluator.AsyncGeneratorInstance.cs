#region

using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class AsyncGeneratorInstance(
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
        private readonly TypedGeneratorInstance _inner = new(function, closure, arguments, thisValue, callable,
            realmState, isLexicallyStrict, hasFunctionNameEnvironment, homeObject, privateNameScope,
            capturedPrivateNameScopes);

        // WAITING ON FULL ASYNC GENERATOR IR SUPPORT:
        // For now we reuse the sync generator IR plan and runtime to execute
        // the body. Async semantics are modeled by driving the shared plan
        // through a small step API and wrapping each step in a Promise. Once
        // we have a dedicated async-generator IR executor, this wiring
        // should be revisited so await/yield drive a single non-blocking
        // state machine.
        public void Initialize()
        {
            _inner.Initialize();
        }

        public JsObject CreateAsyncIteratorObject()
        {
            var prototype = ResolveGeneratorPrototype();
            var asyncIterator = CreateGeneratorIteratorObject(
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(TypedGeneratorInstance.ResumeMode.Next, argValue);
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(TypedGeneratorInstance.ResumeMode.Return, argValue);
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(TypedGeneratorInstance.ResumeMode.Throw, argValue);
                },
                prototype);

            // asyncIterator[Symbol.asyncIterator] returns itself.
            var asyncKey = SymbolKeys.AsyncIterator;
            asyncIterator.SetProperty(asyncKey, (JsValue)new HostFunction((thisValue, _) => thisValue));

            return asyncIterator;
        }

        private JsValue CreateStepPromise(TypedGeneratorInstance.ResumeMode mode, JsValue argument)
        {
            // Look up the global Promise constructor from the closure environment.
            IJsCallable? promiseCtor = null;
            if (closure.TryGetObject<IJsCallable>(Symbol.PromiseIdentifier, out var promiseFromEnv))
            {
                promiseCtor = promiseFromEnv;
            }
            else if (realmState.PromiseConstructor is { } promiseFromRealm)
            {
                promiseCtor = promiseFromRealm;
            }

            if (promiseCtor is null)
            {
                throw new InvalidOperationException("Promise constructor is not available in the current environment.");
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

                // Drive the underlying generator plan by a single step and
                // resolve/reject the Promise based on the step outcome.
                var step = _inner.ExecuteAsyncStep(mode, argument);
                switch (step.Kind)
                {
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                    {
                        var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                        resolve.Invoke([(JsValue)iteratorResult], JsValue.Undefined);
                        break;
                    }
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                        // step.Value is already JsValue
                        reject.Invoke([step.Value], JsValue.Undefined);
                        break;
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                        HandlePendingStep(step, resolve, reject);
                        break;
                }

                return JsValue.Undefined;
            });

            if (promiseCtor is HostFunction hostCtor)
            {
                return hostCtor.InvokeWithContext([(JsValue)executor], JsValue.Undefined, null, (JsValue)hostCtor);
            }

            return promiseCtor.Invoke([(JsValue)executor], JsValue.Undefined);
        }

        private static JsObject CreateAsyncIteratorResult(JsValue value, bool done)
        {
            var result = new JsObject();
            // value is already JsValue
            result.SetProperty("value", value);
            result.SetProperty("done", done ? JsValue.True : JsValue.False);
            return result;
        }

        private void ResolveFromStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            switch (step.Kind)
            {
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                {
                    var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                    InvokeWithOneArg(resolve, (JsValue)iteratorResult);
                    break;
                }
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                    // step.Value is already JsValue
                    InvokeWithOneArg(reject, step.Value);
                    break;
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                    HandlePendingStep(step, resolve, reject);
                    break;
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

            // thenValue is already a JsValue from TryGetProperty
            if (!thenValue.TryUnwrap(out IJsCallable? thenCallable))
            {
                InvokeWithOneArg(reject, (JsValue)"'then' is not callable");
                return;
            }

            var onFulfilled = new AsyncResumeCallback(this, resolve, reject, false);
            var onRejected = new AsyncResumeCallback(this, resolve, reject, true);

            InvokeWithTwoArgs(
                thenCallable,
                JsValue.FromObjectUnsafe(onFulfilled),
                JsValue.FromObjectUnsafe(onRejected),
                (JsValue)pendingPromise);
        }

        private JsObject? ResolveGeneratorPrototype()
        {
            if (callable is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoValue) &&
                protoValue.TryGetObject<JsObject>(out var prototypeObject))
            {
                return prototypeObject;
            }

            return realmState.AsyncGeneratorPrototype ?? realmState.ObjectPrototype;
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

        /// <summary>
        ///     Lightweight callback for async generator resume - avoids HostFunction allocation.
        /// </summary>
        private sealed class AsyncResumeCallback(
            AsyncGeneratorInstance executor,
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
                var resumed = executor._inner.ExecuteAsyncStep(mode, value);
                executor.ResolveFromStep(resumed, resolve, reject);
                return JsValue.Undefined;
            }
        }
    }
}
