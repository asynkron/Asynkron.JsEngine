using System;
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
}
