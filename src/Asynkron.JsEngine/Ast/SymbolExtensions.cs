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

            if (environment.TryGet(key, out var existing) && existing is DelegatedYieldState state)
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

            if (environment.TryGet(key, out _))
            {
                environment.Assign(key, state);
            }
            else
            {
                environment.Define(key, state);
            }
        }

        private void ClearDelegatedState(JsEnvironment environment)
        {
            if (key is null)
            {
                return;
            }

            if (environment.TryGet(key, out _))
            {
                environment.Assign(key, null);
            }
        }
    }
}
