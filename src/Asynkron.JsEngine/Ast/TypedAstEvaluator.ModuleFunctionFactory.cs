using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Creates a function value for module-level function hoisting.
    /// This is used during module instantiation to hoist function declarations.
    /// </summary>
    internal static IJsCallable CreateModuleFunction(
        FunctionExpression funcExpr,
        JsEnvironment moduleEnv,
        RealmState realmState,
        bool isStrict)
    {
        if (funcExpr.IsGenerator)
        {
            if (funcExpr.IsAsync)
            {
                return new AsyncGeneratorFactory(funcExpr, moduleEnv, realmState, isStrict, true);
            }
            return new TypedGeneratorFactory(funcExpr, moduleEnv, realmState, isStrict, true);
        }
        return new TypedFunction(funcExpr, moduleEnv, realmState, isStrict, false, true);
    }
}
