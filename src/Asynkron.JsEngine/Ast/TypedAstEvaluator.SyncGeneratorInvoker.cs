using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Callable for synchronous generator functions (function*).
    /// Returns a sync iterator when invoked.
    /// </summary>
    private sealed class SyncGeneratorInvoker : GeneratorFunctionBase
    {
        public SyncGeneratorInvoker(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment = false,
            bool isConstructorFunction = true,
            FunctionExecutionPlanSeed planSeed = default)
            : base(function, closure, realmState, isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction, planSeed)
            => Initialize();

        protected override string FunctionTypeName => "GeneratorFunction";

        protected override void ValidateFunctionType()
        {
            if (!_function.IsGenerator)
            {
                throw new ArgumentException("Factory can only wrap generator functions.", nameof(_function));
            }
        }

        public override JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            if (TryCreateUnifiedBytecodeGenerator(arguments, thisValue, out var unifiedIterator))
            {
                return JsValue.FromJsObject(unifiedIterator);
            }

            var runner = CreateClassifiedGeneratorDeclinedBodyRunner(arguments, thisValue);
            runner.Initialize();
            return (JsValue)runner.CreateGeneratorObject();
        }

        private bool TryCreateUnifiedBytecodeGenerator(
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            out JsObject iterator)
        {
            iterator = null!;

            // Non-simple parameter lists (destructuring patterns, defaults, rest) require
            // IteratorBindingInitialization during FunctionDeclarationInstantiation, which runs
            // eagerly at call time before the generator object is produced and can throw (e.g.
            // GetIterator(null) for `*m([[x]]){}` called with `[null]`). The resumable route copies
            // arguments straight into positional slots and cannot model that, so it must decline and
            // fall back to the runner path. See ADR 0283 / gh2675 resumable-route boundary.
            if (!_function.HasOnlySimpleIdentifierParameters())
            {
                return false;
            }

            if (!TryGetExecutionPlan(out var plan))
            {
                return false;
            }

            if (!TryCollectResumableRootHoistedFunctionDeclarations(
                    _function,
                    plan,
                    allowCapturedActivationSlots: true,
                    out var hoistedFunctionDeclarations))
            {
                return false;
            }

            var needsMaterializedBodyEnvironment =
                UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan) ||
                HoistedFunctionDeclarationsNeedMaterializedBodyEnvironment(hoistedFunctionDeclarations);
            var needsNestedFunctionLiteralLexicalThisOrPrivateNameContext =
                UnifiedBytecodeProductionEligibility.PlanNeedsNestedFunctionLiteralLexicalThisOrPrivateNameContext(plan);
            var activation = new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: false,
                IsGenerator: true,
                HasCapturedOrDynamicActivation: !AllowsIdentifierCaching(_function) || _closure.HasWithObjectInChain(),
                HasArgumentsObjectDependency: NeedsArgumentsBinding(_function),
                AllowsRootFunctionDeclarationInstructions: !hoistedFunctionDeclarations.IsEmpty,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: needsMaterializedBodyEnvironment,
                AllowsNestedFunctionLiteralLexicalThisOrPrivateNameContext:
                needsNestedFunctionLiteralLexicalThisOrPrivateNameContext);
            var eligibility = UnifiedBytecodeProductionEligibility.EvaluateResumable(plan, activation);
            if (!eligibility.IsEligible)
            {
                return false;
            }

            var program = eligibility.Program;
            var isStrict = _function.Body.IsStrict || _closure.IsStrict || _isLexicallyStrict;
            var boundThis = isStrict
                ? thisValue
                : SyncFunctionInvoker.CoerceThisValueForNonStrict(thisValue, RealmState);
            var requiresResumableSuperBinding = RequiresResumableSuperEnvironment(program);
            if (needsMaterializedBodyEnvironment && requiresResumableSuperBinding)
            {
                return false;
            }

            var resumableEnvironment = CreateResumableInvocationEnvironment(
                _closure,
                boundThis,
                isStrict,
                _function.Source,
                _homeObject,
                forceFunctionEnvironment: needsNestedFunctionLiteralLexicalThisOrPrivateNameContext);
            var context = RealmState.CreateContext();
            if (!TryInitializeResumableSlots(
                    plan,
                    program,
                    arguments,
                    out var slots))
            {
                return false;
            }

            var callingEnvironment = resumableEnvironment;
            if (needsMaterializedBodyEnvironment)
            {
                if (!TryCreateMaterializedResumableBodyEnvironment(
                        plan,
                        program,
                        slots,
                        resumableEnvironment,
                        isStrict,
                        _function.Source,
                        out callingEnvironment))
                {
                    return false;
                }
            }

            if (!TryPopulateResumableRootHoistedFunctionDeclarations(
                    hoistedFunctionDeclarations,
                    plan,
                    program,
                    slots,
                    callingEnvironment,
                    context))
            {
                return false;
            }

            SuperBinding? resumableSuperBinding = null;
            if (requiresResumableSuperBinding &&
                !TryCreateResumableSuperBinding(_closure, boundThis, _homeObject, out resumableSuperBinding))
            {
                return false;
            }

            // A generator is never a constructor and never an arrow, so its own new.target is undefined.
            var state = new UnifiedBytecodeResumeState(program, slots, boundThis, callingEnvironment, isStrict, JsValue.Undefined)
            {
                HasMaterializedBodyEnvironment = needsMaterializedBodyEnvironment,
                ResumableSuperBinding = resumableSuperBinding,
                // Thread the private-name scopes lexically active where this generator method was defined
                // (captured enclosing scopes plus the class's own brand scope, innermost last) onto the
                // resume state so the resumable VM can re-enter them on each per-step context and resolve
                // `#name in obj` correctly. Empty for generators that close over no private names.
                PrivateNameScopes = UnifiedBytecodeResumeState.CombinePrivateNameScopes(_capturedPrivateNameScopes, PrivateNameScope),
            };
            RealmState.Logger?.LogInformation(
                "unified-bytecode-resumable-generator-fast-path func={Function} argc={ArgumentCount}",
                _function.Name?.Name ?? "<anonymous>",
                arguments.Count);

            var prototype = ResolveInstanceGeneratorPrototype();
            var createdIterator = CreateGeneratorIteratorObject(
                args => ExecuteUnifiedBytecodeGeneratorStep(
                    state,
                    UnifiedBytecodeResumeMode.Next,
                    args.GetArgument(0),
                    context),
                args => ExecuteUnifiedBytecodeGeneratorStep(
                    state,
                    UnifiedBytecodeResumeMode.Return,
                    args.Count > 0 ? args[0] : JsValue.Undefined,
                    context),
                args => ExecuteUnifiedBytecodeGeneratorStep(
                    state,
                    UnifiedBytecodeResumeMode.Throw,
                    args.Count > 0 ? args[0] : JsValue.Undefined,
                    context),
                prototype);
            createdIterator.SetProperty(IteratorSymbolPropertyName,
                JsValue.FromObjectUnsafe(new HostFunction((_, _) => JsValue.FromJsObject(createdIterator))));
            createdIterator.SetProperty(GeneratorBrandPropertyName, JsValue.FromObjectUnsafe(GeneratorBrandMarker));
            iterator = createdIterator;
            return true;
        }

        private bool TryGetExecutionPlan(out ExecutionPlan plan)
        {
            if (_planSeed.Plan is { } seededPlan)
            {
                plan = seededPlan;
                return true;
            }

            var cache = ((IAstCacheable<ExecutionPlanCache>)_function).GetOrCreateCache();
            if (cache.Plan is { } cachedPlan)
            {
                plan = cachedPlan;
                return true;
            }

            plan = null!;
            return false;
        }

        private static JsValue ExecuteUnifiedBytecodeGeneratorStep(
            UnifiedBytecodeResumeState state,
            UnifiedBytecodeResumeMode mode,
            JsValue value,
            EvaluationContext context)
        {
            var step = UnifiedBytecodeVirtualMachine.ExecuteResumable(state, mode, value, context);
            return step.Kind switch
            {
                UnifiedBytecodeStepKind.Yield =>
                    !step.IteratorResult.IsUndefined
                        ? step.IteratorResult
                        : UnifiedBytecodeVirtualMachine.CreateIteratorResult(step.Value, done: false),
                UnifiedBytecodeStepKind.Completed =>
                    UnifiedBytecodeVirtualMachine.CreateIteratorResult(step.Value, done: true),
                UnifiedBytecodeStepKind.Throw => throw new ThrowSignal(step.Value),
                _ => throw new NotSupportedException(
                    $"Unified bytecode generator step '{step.Kind}' is not supported by synchronous generators.")
            };
        }

        protected override void EnsureIntrinsics()
        {
            // Lazily initialize the GeneratorFunction.prototype and Generator.prototype intrinsics
            // if they haven't been created yet.

            // First create %GeneratorPrototype% if needed
            if (RealmState.GeneratorPrototype is null)
            {
                // Use the generated prototype factory to get all required properties
                // (Symbol.toStringTag, next/return/throw methods with proper descriptors, etc.)
                var generatorProto = (JsObject)GeneratorPrototype.CreatePrototype(RealmState);
                // %GeneratorPrototype% inherits from %IteratorPrototype%, which inherits from %Object.prototype%
                var iteratorPrototype = RealmState.IteratorPrototype ??=
                    (JsObject)IteratorPrototype.CreatePrototype(RealmState);
                generatorProto.SetPrototype(iteratorPrototype);

                RealmState.GeneratorPrototype = generatorProto;
            }

            // Then create %GeneratorFunction.prototype% and link it to %GeneratorPrototype%
            if (RealmState.GeneratorFunctionPrototype is null && RealmState.FunctionPrototype is not null)
            {
                var genFuncProto = new JsObject();
                genFuncProto.SetPrototype(RealmState.FunctionPrototype);

                // Add Symbol.toStringTag property per ES spec (non-writable, non-enumerable, configurable)
                genFuncProto.DefineProperty(SymbolKeys.ToStringTag,
                    new PropertyDescriptor { Value = "GeneratorFunction", Writable = false, Enumerable = false, Configurable = true });

                // %GeneratorFunction.prototype% should have a .prototype property pointing to %GeneratorPrototype%
                genFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = RealmState.GeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true
                    });

                RealmState.GeneratorFunctionPrototype = genFuncProto;

                // %GeneratorPrototype%.constructor === %GeneratorFunction.prototype%
                // Per ES spec: non-writable, non-enumerable, configurable.
                if (RealmState.GeneratorPrototype is { } generatorProto &&
                    generatorProto.GetOwnPropertyDescriptor("constructor") is null)
                {
                    generatorProto.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = genFuncProto,
                            Writable = false,
                            Enumerable = false,
                            Configurable = true
                        });
                }

                // Create the GeneratorFunction constructor if we have access to the engine
                if (RealmState is { Engine: { } engine, GeneratorFunctionConstructor: null })
                {
                    var generatorFunctionConstructor = CreateGeneratorFunctionConstructor(engine, RealmState);
                    RealmState.GeneratorFunctionConstructor = generatorFunctionConstructor;

                    // Set GeneratorFunctionPrototype.constructor = GeneratorFunction
                    genFuncProto.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = generatorFunctionConstructor,
                            Writable = false,
                            Enumerable = false,
                            Configurable = true
                        });

                    // GeneratorFunction.__proto__ === Function (inherit from Function)
                    if (RealmState.FunctionPrototype is { } functionPrototype)
                    {
                        generatorFunctionConstructor.Properties.SetPrototype(functionPrototype);
                    }

                    // GeneratorFunction.prototype must be non-writable per ES spec
                    generatorFunctionConstructor.DefineProperty("prototype",
                        new PropertyDescriptor
                        {
                            Value = genFuncProto,
                            Writable = false,
                            Enumerable = false,
                            Configurable = false
                        });
                }
            }
        }

        protected override IJsPropertyAccessor? GetFunctionPrototype()
        {
            return RealmState.GeneratorFunctionPrototype;
        }

        protected override JsObject? GetGeneratorPrototype()
        {
            return RealmState.GeneratorPrototype;
        }

        private static HostFunction CreateGeneratorFunctionConstructor(JsEngine engine, RealmState realm)
        {
            HostFunction generatorFunctionConstructor = null!;

            generatorFunctionConstructor = new HostFunction((_, args) =>
                CreateDynamicGeneratorFunction(args, generatorFunctionConstructor, engine, realm, "function*", realm.GeneratorFunctionConstructor!))
            {
                RealmState = realm
            };

            generatorFunctionConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
            {
                IJsCallable targetCallable = generatorFunctionConstructor;
                if (newTarget.TryUnwrap(out IJsCallable? callable))
                {
                    targetCallable = callable;
                }

                return CreateDynamicGeneratorFunction(args, targetCallable, engine, realm, "function*", realm.GeneratorFunctionConstructor!);
            });

            StandardLibrary.DefineConstantProperty(generatorFunctionConstructor, "length", 1d, true);
            StandardLibrary.DefineConstantProperty(generatorFunctionConstructor, "name", "GeneratorFunction", true);

            return generatorFunctionConstructor;
        }
    }
}
