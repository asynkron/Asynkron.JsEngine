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
        bool isStrict,
        string? functionName = null)
    {
        IJsCallable result;
        if (funcExpr.IsGenerator)
        {
            if (funcExpr.IsAsync)
            {
                var asyncGen = new AsyncGeneratorFactory(funcExpr, moduleEnv, realmState, isStrict, true);
                if (functionName != null)
                {
                    asyncGen.EnsureHasName(functionName, overwriteExisting: true);
                }
                result = asyncGen;
            }
            else
            {
                var gen = new TypedGeneratorFactory(funcExpr, moduleEnv, realmState, isStrict, true);
                if (functionName != null)
                {
                    gen.EnsureHasName(functionName, overwriteExisting: true);
                }
                result = gen;
            }
        }
        else
        {
            var fn = new TypedFunction(funcExpr, moduleEnv, realmState, isStrict, false, true);
            if (functionName != null)
            {
                fn.EnsureHasName(functionName, overwriteExisting: true);
            }
            result = fn;
        }
        return result;
    }
}
