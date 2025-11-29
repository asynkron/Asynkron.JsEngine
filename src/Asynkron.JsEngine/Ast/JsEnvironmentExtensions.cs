using Asynkron.JsEngine.JsTypes;

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
    }

extension(JsEnvironment environment)
    {
        private YieldTracker GetYieldTracker()
        {
            if (!environment.TryGet(Symbol.YieldTrackerSymbol, out var tracker) || tracker is not YieldTracker yieldTracker)
            {
                throw new InvalidOperationException("'yield' can only be used inside a generator function.");
            }

            return yieldTracker;
        }
    }

extension(JsEnvironment environment)
    {
        private ResumePayload GetResumePayload(int yieldIndex)
        {
            if (!environment.TryGet(Symbol.YieldResumeContextSymbol, out var contextValue) ||
                contextValue is not YieldResumeContext resumeContext)
            {
                return ResumePayload.Empty;
            }

            return resumeContext.TakePayload(yieldIndex);
        }
    }

extension(JsEnvironment environment)
    {
        private bool IsGeneratorContext()
        {
            return environment.TryGet(Symbol.YieldResumeContextSymbol, out var contextValue) &&
                   contextValue is YieldResumeContext;
        }
    }

extension(JsEnvironment environment)
    {
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
    }

extension(JsEnvironment environment)
    {
        private void EnsureFunctionScopedVarBinding(Symbol name,
            EvaluationContext context)
        {
            if (environment.HasFunctionScopedBinding(name))
            {
                return;
            }

            environment.DefineFunctionScoped(name, Symbol.Undefined, hasInitializer: false, context: context);
        }
    }

extension(JsEnvironment environment)
    {
        private SuperBinding ExpectSuperBinding(EvaluationContext context)
        {
            try
            {
                if (environment.Get(Symbol.Super) is SuperBinding binding)
                {
                    return binding;
                }
            }
            catch (InvalidOperationException ex)
            {
                var wrapped = CreateSuperReferenceError(environment, context, ex);
                if (wrapped is ThrowSignal throwSignal)
                {
                    throw throwSignal;
                }

                throw wrapped;
            }

            var fallback = CreateSuperReferenceError(environment, context, null);
            if (fallback is ThrowSignal signal)
            {
                throw signal;
            }

            throw fallback;
        }
    }

extension(JsEnvironment environment)
    {
        private Exception CreateSuperReferenceError(EvaluationContext context,
            Exception? inner)
        {
            var message = $"Super is not available in this context.{GetSourceInfo(context)}";
            if (!environment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctorVal) ||
                ctorVal is not IJsCallable ctor)
            {
                return new InvalidOperationException(message, inner);
            }

            var error = ctor.Invoke([message], Symbol.Undefined);
            return new ThrowSignal(error);

        }
    }

extension(JsEnvironment environment)
    {
        private void SetThisInitializationStatus(bool initialized)
        {
            if (environment.HasBinding(Symbol.ThisInitialized))
            {
                environment.Assign(Symbol.ThisInitialized, initialized);
                if (initialized &&
                    environment.TryGet(Symbol.Super, out var superBinding) &&
                    superBinding is SuperBinding { IsThisInitialized: false } binding)
                {
                    environment.Assign(Symbol.Super,
                        new SuperBinding(binding.Constructor, binding.Prototype, binding.ThisValue, true));
                }
                return;
            }

            environment.Define(Symbol.ThisInitialized, initialized, isConst: false, isLexical: true,
                blocksFunctionScopeOverride: true);
        }
    }

}
