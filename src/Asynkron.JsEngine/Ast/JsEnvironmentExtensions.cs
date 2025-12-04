using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(JsEnvironment environment)
    {
        private bool IsSimpleCatchParameterBinding(Symbol name)
        {
            try
            {
                if (environment.TryFindBinding(name, out var bindingEnvironment, out _) &&
                    !bindingEnvironment.IsFunctionScope &&
                    bindingEnvironment.IsSimpleCatchParameter(name))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore lookup failures such as TDZ reads.
            }

            return false;
        }

        private bool HasBlockingLexicalBeforeFunctionScope(Symbol name)
        {
            var current = environment;
            var skippedOwnBinding = false;
            while (current?.IsFunctionScope == false)
            {
                if (current.HasOwnLexicalBinding(name))
                {
                    if (!skippedOwnBinding)
                    {
                        skippedOwnBinding = true;
                    }
                    else if (!current.IsSimpleCatchParameter(name))
                    {
                        return true;
                    }
                }

                current = current.Enclosing;
            }

            return false;
        }

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

        private SuperBinding ExpectSuperBinding(EvaluationContext context)
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
                var placeholder = new SuperBinding(null, null, JsEnvironment.Uninitialized, false);
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
            var thisValue = JsEnvironment.Uninitialized;
            try
            {
                environment.TryGet(Symbol.This, out thisValue);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                         StringComparison.Ordinal))
            {
                logger?.LogInformation("SuperBinding: fallback with uninitialized 'this'");
            }
            IJsPropertyAccessor? prototypeGuess = null;
            if (thisValue is IJsObjectLike thisObject)
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
            object? thisValue;
            try
            {
                if (!environment.TryGet(Symbol.This, out thisValue))
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

            if (thisValue is not IJsObjectLike thisObject)
            {
                logger?.LogInformation("SuperBinding: 'this' is not object-like type={Type}",
                    thisValue?.GetType().Name ?? "null");
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

        private Exception CreateSuperReferenceError(EvaluationContext context,
            Exception? inner)
        {
            environment.RealmState?.Logger?.LogInformation("SuperBinding: reference error thisInit? {ThisInit}", context.IsThisInitialized);
            var message = $"Super is not available in this context.{GetSourceInfo(context)}";
            if (!environment.TryGet(Symbol.SyntaxErrorIdentifier, out var ctorVal) ||
                ctorVal is not IJsCallable ctor)
            {
                return new InvalidOperationException(message, inner);
            }

            var error = ctor.Invoke([message], Symbol.Undefined);
            return new ThrowSignal(error);
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
                        new SuperBinding(binding.Constructor, binding.Prototype, binding.ThisValue, true));
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
