using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(Symbol? key)
    {
        private DelegatedYieldState? GetDelegatedState(JsEnvironment environment)
        {
            if (key is null)
            {
                return null;
            }

            if (environment.TryGetObject<DelegatedYieldState>(key, out var state))
            {
                return state;
            }

            return null;
        }

        private void StoreDelegatedState(JsEnvironment environment, DelegatedYieldState state)
        {
            if (key is null)
            {
                return;
            }

            if (environment.HasBinding(key))
            {
                environment.Assign(key, state);
            }
            else
            {
                environment.DefineJsValue(key, JsValue.FromObjectUnsafe(state));
            }
        }

        private void ClearDelegatedState(JsEnvironment environment)
        {
            if (key is null)
            {
                return;
            }

            if (environment.HasBinding(key))
            {
                environment.Assign(key, null);
            }
        }
    }
}
