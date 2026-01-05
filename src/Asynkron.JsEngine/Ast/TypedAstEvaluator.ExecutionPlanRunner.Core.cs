#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        public ExecutionPlanRunner(
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
            ImmutableArray<PrivateNameScope> capturedPrivateNameScopes,
            JsValue newTarget = default,
            JsEnvironment? lexicalThisEnvironment = null)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _newTarget = newTarget;
            _callable = callable;
            _realmState = realmState;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _homeObject = homeObject;
            _privateNameScope = privateNameScope;
            _capturedPrivateNameScopes = capturedPrivateNameScopes;
            _lexicalThisEnvironment = lexicalThisEnvironment;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            _isAsync = function.IsAsync;
            _isGenerator = function.IsGenerator;
            _allowIdentifierCache = AllowsIdentifierCaching(function) && !closure.HasWithObjectInChain();

            var planCache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
            if (!planCache.Succeeded || planCache.Plan is null)
            {
                var reason = planCache.FailureReason ?? "Generator contains unsupported construct for IR.";
                throw new NotSupportedException($"IR plan generation failed for function: {reason}");
            }

            _plan = planCache.Plan;
            _programCounter = _plan.EntryPoint;
        }

        /// <summary>
        /// Private constructor for script execution mode.
        /// Used by RunScript() to create a minimal runner without function context.
        /// </summary>
        private ExecutionPlanRunner(
            ExecutionPlan plan,
            JsEnvironment environment,
            EvaluationContext context)
        {
            _plan = plan;
            _programCounter = plan.EntryPoint;
            _executionEnvironment = environment;
            _closure = environment;
            _context = context;
            _realmState = context.RealmState;
            _arguments = [];
            _callable = null!;
            _function = null!;
            _thisValue = context.RealmState.Engine?.GlobalObject is { } go
                ? new JsValue(go)
                : JsValue.Undefined;
            _isStrict = environment.IsStrict;
            _isAsync = false; // Scripts run via RunScript are synchronous
            _isGenerator = false; // Scripts are not generators
            _allowIdentifierCache = context.AllowIdentifierCache;
            _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
            _isScriptMode = true;
        }

        /// <summary>
        /// Runs an execution plan for script-level code.
        /// This is a lightweight path that skips generator/async machinery setup.
        /// The environment is already configured with hoisted declarations.
        /// </summary>
        /// <param name="plan">The execution plan to run.</param>
        /// <param name="environment">The pre-configured script environment with hoisted bindings.</param>
        /// <param name="context">The evaluation context.</param>
        /// <returns>The completion value of the script.</returns>
        public static JsValue RunScript(
            ExecutionPlan plan,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var runner = new ExecutionPlanRunner(plan, environment, context);
            return runner.RunScriptInternal();
        }

        /// <summary>
        /// Internal method to run script without generator overhead.
        /// Returns the raw completion value, not an iterator result.
        /// </summary>
        private JsValue RunScriptInternal()
        {
            // Run the plan - for scripts this completes immediately (no yield/async)
            // The completion value is tracked in _scriptCompletionValue during execution
            var result = ExecutePlan(ResumeMode.Next, JsValue.Undefined);

            // ExecutePlan returns an iterator result {value, done} for generators.
            // For script execution with explicit return statements, extract the return value.
            if (result.TryGetObject<IteratorResultObject>(out var iteratorResult))
            {
                iteratorResult.TryGetProperty("value", out var returnValue);
                // If there was an explicit return, use that value
                // Otherwise fall back to tracked completion value
                return returnValue.IsUndefined ? GetFinalCompletionValue() : returnValue;
            }

            if (result.TryGetObject<JsObject>(out var jsObject) &&
                jsObject.TryGetProperty("value", out var jsValue))
            {
                return jsValue.IsUndefined ? GetFinalCompletionValue() : jsValue;
            }

            // No iterator result wrapper - use tracked completion value
            return GetFinalCompletionValue();
        }

        /// <summary>
        /// Gets the final completion value, converting Unit sentinel to undefined.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue GetFinalCompletionValue()
        {
            return _scriptCompletionValue.IsUnit ? JsValue.Undefined : _scriptCompletionValue;
        }

        public JsObject CreateGeneratorObject()
        {
            var prototype = ResolveGeneratorPrototype();
            var iterator = CreateGeneratorIteratorObject(
                args => Next(args.GetArgument(0)),
                args => Return(args.Count > 0 ? args[0] : JsValue.Undefined),
                args => Throw(args.Count > 0 ? args[0] : JsValue.Undefined),
                prototype);
            iterator.SetProperty(IteratorSymbolPropertyName,
                (JsValue)new HostFunction((_, _) => new JsValue(iterator)));
            iterator.SetProperty(GeneratorBrandPropertyName, JsValue.FromObjectUnsafe(GeneratorBrandMarker));
            return iterator;
        }

        public void Initialize()
        {
            EnsureExecutionEnvironment();
        }

        /// <summary>
        /// Runs the execution plan synchronously to completion.
        /// Unlike generator iteration, this returns the raw completion value
        /// rather than an iterator result object.
        /// Used for sync function execution via IR.
        /// </summary>
        public JsValue RunSync()
        {
            // Run the plan - for sync functions this completes immediately
            var result = ExecutePlan(ResumeMode.Next, JsValue.Undefined);

            // ExecutePlan returns an iterator result {value, done} for generators.
            // For sync execution, extract the raw value.
            // Handle both IteratorResultObject (lightweight) and JsObject (full) cases.
            if (result.TryGetObject<IteratorResultObject>(out var iteratorResult))
            {
                iteratorResult.TryGetProperty("value", out var value);
                return value;
            }

            if (result.TryGetObject<JsObject>(out var jsObject) &&
                jsObject.TryGetProperty("value", out var jsValue))
            {
                return jsValue;
            }

            // If no iterator result (shouldn't happen), return as-is
            return result;
        }

        private JsValue Next(JsValue value)
        {
            return ExecutePlan(ResumeMode.Next, value);
        }

        private JsValue Return(JsValue value)
        {
            return ExecutePlan(ResumeMode.Return, value);
        }

        private JsValue Throw(JsValue error)
        {
            return ExecutePlan(ResumeMode.Throw, error);
        }

        private JsObject? ResolveGeneratorPrototype()
        {
            // Per spec: OrdinaryCreateFromConstructor with intrinsicDefaultProto = "%GeneratorPrototype%"
            // 1. Try to get the generator function's .prototype property
            // 2. If it's an object, use it
            // 3. Otherwise, fall back to %GeneratorPrototype% (the intrinsic default)
            if (_callable is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoValue))
            {
                // protoValue is already a JsValue from TryGetProperty
                if (protoValue.TryGetObject<JsObject>(out var prototypeObject))
                {
                    return prototypeObject;
                }
            }

            // Fall back to %GeneratorPrototype% if the function's .prototype is not an object
            return _realmState.GeneratorPrototype ?? _realmState.ObjectPrototype;
        }

        internal AsyncGeneratorStepResult ExecuteAsyncStep(ResumeMode mode, JsValue resumeValue)
        {
            // Reuse the existing ExecutePlan logic but translate its iterator
            // result / exceptions into a structured step result that async
            // generators can consume without throwing. This entrypoint also
            // marks the executor as async-aware so future steps can surface
            // pending Promises instead of blocking.
            var previousAsyncStepMode = AsyncStateRef.AsyncStepMode;
            AsyncStateRef.AsyncStepMode = true;
            AsyncStateRef.PendingPromise = JsValue.Undefined;

            try
            {
                var result = ExecutePlan(mode, resumeValue);

                if (AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsValue.Undefined, false,
                        AsyncStateRef.PendingPromise);
                }

                if (result.TryGetObject<IJsPropertyAccessor>(out var obj) &&
                    obj.TryGetProperty("done", out var doneRaw) &&
                    obj.TryGetProperty("value", out var value))
                {
                    // doneRaw and value are already JsValue from TryGetProperty
                    var done = doneRaw.IsTruthy;
                    return done
                        ? new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, value, true, JsValue.Undefined)
                        : new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Yield, value, false, JsValue.Undefined);
                }

                // If the plan completed without producing a well-formed iterator
                // result, treat it as a completed step with undefined.
                return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, JsValue.Undefined, true,
                    JsValue.Undefined);
            }
            finally
            {
                AsyncStateRef.AsyncStepMode = previousAsyncStepMode;
                AsyncStateRef.PendingPromise = JsValue.Undefined;
            }
        }
    }
}
