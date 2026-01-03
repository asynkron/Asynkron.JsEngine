#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(JsEnvironment environment)
    {
        private bool IsGeneratorContext()
        {
            return environment.TryGetObject<YieldResumeContext>(Symbol.YieldResumeContextSymbol, out _);
        }

        private GeneratorPendingCompletion GetGeneratorPendingCompletion()
        {
            if (environment.TryGetObject<GeneratorPendingCompletion>(Symbol.GeneratorPendingCompletionSymbol,
                    out var pending))
            {
                return pending;
            }

            var created = new GeneratorPendingCompletion();
            environment.DefineFunctionScoped(Symbol.GeneratorPendingCompletionSymbol, JsValue.FromObjectUnsafe(created),
                true);
            return created;
        }

        private void EnsureFunctionScopedVarBinding(Symbol name,
            EvaluationContext context)
        {
            if (environment.HasFunctionScopedBinding(name))
            {
                // Ensure slot-backed environments also have a named slot populated for IR fast paths.
                // Do not overwrite an existing initialized value (e.g., hoisted function declaration).
                if (environment.TryGetSlotIndex(name, out var existingSlot))
                {
                    ref var slot = ref environment.GetSlotByIndex(existingSlot);
                    if (slot.Name is null)
                    {
                        slot.Name = name;
                    }

                    if (!slot.Value.IsUninitialized && slot.Value.Kind != JsValueKind.Undefined)
                    {
                        return;
                    }

                    environment.SetSlotDirect(existingSlot, JsValue.Undefined);
                }

                return;
            }

            // ES2024 19.2.1.3 EvalDeclarationInstantiation: in sloppy eval, var bindings
            // are created BEFORE the eval code runs (with canDelete=true). If a binding
            // was deleted during eval execution, encountering the 'var x' statement
            // should NOT re-create it. Only non-eval contexts can create new var bindings.
            if (context.ExecutionKind == ExecutionKind.Eval && !context.IsStrictSource)
            {
                // Var bindings in eval were already instantiated. If the binding doesn't
                // exist now, it means it was deleted and should stay deleted.
                return;
            }

            var allowDelete = context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
            if (environment.TryGetSlotIndex(name, out var slotIndex))
            {
                ref var slot = ref environment.GetSlotByIndex(slotIndex);
                if (slot.Name is null)
                {
                    slot.Name = name;
                }

                if (!slot.Value.IsUninitialized && slot.Value.Kind != JsValueKind.Undefined)
                {
                    return;
                }

                environment.SetSlotDirect(slotIndex, JsValue.Undefined);
                return;
            }

            environment.DefineFunctionScoped(name, JsValue.Undefined, false, context: context, canDelete: allowDelete);
        }

        internal SuperBinding ExpectSuperBinding(EvaluationContext context)
        {
            var logger = environment.RealmState?.Logger;
            try
            {
                if (environment.TryGetObject<SuperBinding>(Symbol.Super, out var binding))
                {
                    logger?.LogInformation("SuperBinding: reuse existing protoNull={ProtoNull} thisInit={ThisInit}",
                        binding.Prototype is null,
                        binding.IsThisInitialized);
                    logger?.LogInformation("SuperBinding: env={Env} context thisInit={ContextThisInit}",
                        environment.GetHashCode(),
                        context.IsThisInitialized);
                    return binding;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError: this is not defined",
                                                           StringComparison.Ordinal))
            {
                // Super access before 'this' is initialised (e.g. during synthetic ctor setup).
                var placeholder = new SuperBinding(null, null, JsValue.Undefined);
                environment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(placeholder), isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                logger?.LogInformation("SuperBinding: synthesized placeholder after ReferenceError for 'this'");
                return placeholder;
            }

            if (environment.TryCreateSuperBindingFromThis(context, out var synthesized))
            {
                environment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(synthesized), isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                logger?.LogInformation("SuperBinding: synthesized from this protoNull={ProtoNull} thisInit={ThisInit}",
                    synthesized.Prototype is null,
                    synthesized.IsThisInitialized);
                return synthesized;
            }

            // Fall back to a best-effort binding so evaluation order (property/key/value)
            // can proceed before any prototype-based errors are raised.
            var thisValue = JsValue.Undefined;
            try
            {
                environment.TryGetJsValue(Symbol.This, out thisValue);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
            {
                logger?.LogInformation("SuperBinding: fallback with uninitialized 'this'");
            }

            IJsPropertyAccessor? prototypeGuess = null;
            if (thisValue.TryGetObject<IJsObjectLike>(out var thisObject))
            {
                prototypeGuess = thisObject is IJsEnvironmentAwareCallable
                    ? thisObject.Prototype
                    : thisObject.Prototype?.Prototype;
            }

            var fallbackBinding = new SuperBinding(null, prototypeGuess, thisValue, context.IsThisInitialized);
            environment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(fallbackBinding), isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
            logger?.LogInformation("SuperBinding: placeholder created protoNull={ProtoNull} thisInit={ThisInit}",
                fallbackBinding.Prototype is null,
                fallbackBinding.IsThisInitialized);
            return fallbackBinding;
        }

        private bool TryCreateSuperBindingFromThis(
            EvaluationContext context,
            out SuperBinding binding)
        {
            var logger = environment.RealmState?.Logger;
            binding = null!;
            JsValue thisValue;
            try
            {
                if (!environment.TryGetJsValue(Symbol.This, out thisValue))
                {
                    logger?.LogInformation("SuperBinding: no 'this' binding available");
                    return false;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
            {
                logger?.LogInformation("SuperBinding: 'this' binding not yet initialized");
                return false;
            }

            if (!thisValue.TryGetObject<IJsObjectLike>(out var thisObject))
            {
                logger?.LogInformation("SuperBinding: 'this' is not object-like type={Type}",
                    thisValue.Kind);
                return false;
            }

            IJsPropertyAccessor? prototypeForSuper;
            IJsEnvironmentAwareCallable? superConstructor = null;

            if (thisObject is IJsEnvironmentAwareCallable)
            {
                // Static method path: base resolves from constructor prototype (__proto__)
                prototypeForSuper = thisObject.Prototype;
                superConstructor = prototypeForSuper as IJsEnvironmentAwareCallable;
            }
            else
            {
                // Instance method path: base is Object.getPrototypeOf(homeObject)
                prototypeForSuper = thisObject.Prototype?.Prototype;
            }

            binding = new SuperBinding(superConstructor, prototypeForSuper, thisValue, context.IsThisInitialized);
            logger?.LogInformation("SuperBinding: built from 'this' protoNull={ProtoNull} thisInit={ThisInit}",
                prototypeForSuper is null,
                context.IsThisInitialized);
            return true;
        }

        internal Exception CreateSuperReferenceError(EvaluationContext context,
            Exception? inner)
        {
            environment.RealmState?.Logger?.LogInformation("SuperBinding: reference error thisInit? {ThisInit}",
                context.IsThisInitialized);
            var message = $"Super is not available in this context.{context.GetSourceInfo()}";
            return StandardLibrary.ThrowReferenceError(message, context, context.RealmState);
        }

        private void SetThisInitializationStatus(bool initialized)
        {
            var logger = environment.RealmState?.Logger;
            if (environment.HasBinding(Symbol.ThisInitialized))
            {
                environment.AssignJsValue(Symbol.ThisInitialized, initialized);
                if (initialized &&
                    environment.TryGetObject<SuperBinding>(Symbol.Super, out var binding) &&
                    !binding.IsThisInitialized)
                {
                    logger?.LogInformation("SuperBinding: bump thisInit -> true env={Env}",
                        environment.GetHashCode());
                    environment.AssignJsValue(Symbol.Super,
                        JsValue.FromObjectUnsafe(new SuperBinding(binding.Constructor, binding.Prototype,
                            binding.thisValue, true)));
                }

                logger?.LogInformation("ThisInitialized updated to {Initialized} env={Env}",
                    initialized,
                    environment.GetHashCode());
                return;
            }

            environment.DefineJsValue(Symbol.ThisInitialized, initialized ? JsValue.True : JsValue.False,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
            logger?.LogInformation("ThisInitialized defined to {Initialized} env={Env}",
                initialized,
                environment.GetHashCode());
        }
    }
}
