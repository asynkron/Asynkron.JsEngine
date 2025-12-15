using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(JsEnvironment environment)
    {
        private YieldTracker GetYieldTracker()
        {
            if (!environment.TryGet(Symbol.YieldTrackerSymbol, out var tracker) ||
                tracker is not YieldTracker yieldTracker)
            {
                throw new InvalidOperationException("'yield' can only be used inside a generator function.");
            }

            return yieldTracker;
        }

        private ResumePayload GetResumePayload(int yieldIndex)
        {
            if (!environment.TryGet(Symbol.YieldResumeContextSymbol, out var contextValue) ||
                contextValue is not YieldResumeContext resumeContext)
            {
                return ResumePayload.Empty;
            }

            return resumeContext.TakePayload(yieldIndex);
        }

        private bool IsGeneratorContext()
        {
            return environment.TryGet(Symbol.YieldResumeContextSymbol, out var contextValue) &&
                   contextValue is YieldResumeContext;
        }

        private GeneratorPendingCompletion GetGeneratorPendingCompletion()
        {
            if (environment.TryGet(Symbol.GeneratorPendingCompletionSymbol, out var existing) &&
                existing is GeneratorPendingCompletion pending)
            {
                return pending;
            }

            var created = new GeneratorPendingCompletion();
            environment.DefineFunctionScoped(Symbol.GeneratorPendingCompletionSymbol, created, true);
            return created;
        }

        private void EnsureFunctionScopedVarBinding(Symbol name,
            EvaluationContext context)
        {
            if (environment.HasFunctionScopedBinding(name))
            {
                return;
            }

            var allowDelete = context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
            environment.DefineFunctionScoped(name, Symbol.Undefined, false, context: context, canDelete: allowDelete);
        }

        internal SuperBinding ExpectSuperBinding(EvaluationContext context)
        {
            var logger = environment.RealmState?.Logger;
            try
            {
                if (environment.TryGet(Symbol.Super, out var existing) &&
                    existing is SuperBinding binding)
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
                var placeholder = new SuperBinding(null, null, JsValue.Undefined, false);
                environment.Define(Symbol.Super, placeholder, false, isLexical: true, blocksFunctionScopeOverride: true);
                logger?.LogInformation("SuperBinding: synthesized placeholder after ReferenceError for 'this'");
                return placeholder;
            }

            if (TryCreateSuperBindingFromThis(environment, context, out var synthesized))
            {
                environment.Define(Symbol.Super, synthesized, false, isLexical: true,
                    blocksFunctionScopeOverride: true);
                logger?.LogInformation("SuperBinding: synthesized from this protoNull={ProtoNull} thisInit={ThisInit}",
                    synthesized.Prototype is null,
                    synthesized.IsThisInitialized);
                return synthesized;
            }

            // Fall back to a best-effort binding so evaluation order (property/key/value)
            // can proceed before any prototype-based errors are raised.
            object? thisValueObj = null;
            try
            {
                environment.TryGet(Symbol.This, out thisValueObj);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                         StringComparison.Ordinal))
            {
                logger?.LogInformation("SuperBinding: fallback with uninitialized 'this'");
            }

            var thisValue = JsValue.FromObject(thisValueObj);
            IJsPropertyAccessor? prototypeGuess = null;
            if (thisValue.TryGetObject<IJsObjectLike>(out var thisObject))
            {
                prototypeGuess = thisObject is IJsEnvironmentAwareCallable
                    ? thisObject.Prototype
                    : thisObject.Prototype?.Prototype;
            }

            var fallbackBinding = new SuperBinding(null, prototypeGuess, thisValue, context.IsThisInitialized);
            environment.Define(Symbol.Super, fallbackBinding, false, isLexical: true, blocksFunctionScopeOverride: true);
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
            object? thisValueObj;
            try
            {
                if (!environment.TryGet(Symbol.This, out thisValueObj))
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

            var thisValue = JsValue.FromObject(thisValueObj);
            if (!thisValue.TryGetObject<IJsObjectLike>(out var thisObject))
            {
                logger?.LogInformation("SuperBinding: 'this' is not object-like type={Type}",
                    thisValue.Kind);
                return false;
            }

            IJsPropertyAccessor? prototypeForSuper;
            IJsEnvironmentAwareCallable? superConstructor = null;

            if (thisObject is IJsEnvironmentAwareCallable ctorLike)
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
            environment.RealmState?.Logger?.LogInformation("SuperBinding: reference error thisInit? {ThisInit}", context.IsThisInitialized);
            var message = $"Super is not available in this context.{GetSourceInfo(context)}";
            return StandardLibrary.ThrowReferenceError(message, context, context.RealmState);
        }

        private void SetThisInitializationStatus(bool initialized)
        {
            var logger = environment.RealmState?.Logger;
            if (environment.HasBinding(Symbol.ThisInitialized))
            {
                environment.Assign(Symbol.ThisInitialized, initialized);
                if (initialized &&
                    environment.TryGet(Symbol.Super, out var superBinding) &&
                    superBinding is SuperBinding { IsThisInitialized: false } binding)
                {
                    logger?.LogInformation("SuperBinding: bump thisInit -> true env={Env}",
                        environment.GetHashCode());
                    environment.Assign(Symbol.Super,
                        new SuperBinding(binding.Constructor, binding.Prototype, binding.thisValue, true));
                }

                logger?.LogInformation("ThisInitialized updated to {Initialized} env={Env}",
                    initialized,
                    environment.GetHashCode());
                return;
            }

            environment.Define(Symbol.ThisInitialized, initialized, isLexical: true,
                blocksFunctionScopeOverride: true);
            logger?.LogInformation("ThisInitialized defined to {Initialized} env={Env}",
                initialized,
                environment.GetHashCode());
        }
    }
}
