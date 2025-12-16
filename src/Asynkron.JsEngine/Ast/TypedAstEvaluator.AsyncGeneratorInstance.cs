using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

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
                    return JsValue.FromObject(CreateStepPromise(TypedGeneratorInstance.ResumeMode.Next, argValue));
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return JsValue.FromObject(CreateStepPromise(TypedGeneratorInstance.ResumeMode.Return, argValue));
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return JsValue.FromObject(CreateStepPromise(TypedGeneratorInstance.ResumeMode.Throw, argValue));
                },
                prototype);

            // asyncIterator[Symbol.asyncIterator] returns itself.
            var asyncKey = SymbolKeys.AsyncIterator;
            asyncIterator.SetProperty(asyncKey, JsValue.FromObject(new HostFunction((thisValue, _) => thisValue)));

            return asyncIterator;
        }

        private object? CreateStepPromise(TypedGeneratorInstance.ResumeMode mode, JsValue argument)
        {
            // Look up the global Promise constructor from the closure environment.
            IJsCallable? promiseCtor = null;
            if (closure.TryGet(Symbol.PromiseIdentifier, out var promiseCtorObj) &&
                promiseCtorObj is IJsCallable promiseFromEnv)
            {
                promiseCtor = promiseFromEnv;
            }
            else if (realmState.PromiseConstructor is IJsCallable promiseFromRealm)
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
                        resolve.Invoke([JsValue.FromObject(iteratorResult)], JsValue.Undefined);
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
                return hostCtor.InvokeWithContext([JsValue.FromObject(executor)], JsValue.Undefined, null, JsValue.FromObject(hostCtor)).ObjectValue;
            }

            return promiseCtor.Invoke([JsValue.FromObject(executor)], JsValue.Undefined).ObjectValue;
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
                    resolve.Invoke([JsValue.FromObject(iteratorResult)], JsValue.Undefined);
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
        }

        private void HandlePendingStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            if (!step.PendingPromise.TryGetObject<JsObject>(out var pendingPromise))
            {
                reject.Invoke([JsValue.FromObject("Awaited value is not a promise")], JsValue.Undefined);
                return;
            }

            if (!pendingPromise.TryGetProperty("then", out var thenValue))
            {
                reject.Invoke([JsValue.FromObject("Awaited value has no 'then' method")], JsValue.Undefined);
                return;
            }

            // thenValue is already a JsValue from TryGetProperty
            if (!thenValue.TryUnwrap(out IJsCallable? thenCallable))
            {
                reject.Invoke([JsValue.FromObject("'then' is not callable")], JsValue.Undefined);
                return;
            }

            var onFulfilled = new HostFunction((_, args) =>
            {
                var value = args.GetArgument(0);
                var resumed = _inner.ExecuteAsyncStep(TypedGeneratorInstance.ResumeMode.Next, value);
                ResolveFromStep(resumed, resolve, reject);
                return JsValue.Undefined;
            });

            var onRejected = new HostFunction((_, args) =>
            {
                var reason = args.GetArgument(0);
                var resumed = _inner.ExecuteAsyncStep(TypedGeneratorInstance.ResumeMode.Throw, reason);
                ResolveFromStep(resumed, resolve, reject);
                return JsValue.Undefined;
            });

            if (thenCallable is not null)
            {
                thenCallable.Invoke([JsValue.FromObject(onFulfilled), JsValue.FromObject(onRejected)], JsValue.FromObject(pendingPromise));
            }
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
    }
}
