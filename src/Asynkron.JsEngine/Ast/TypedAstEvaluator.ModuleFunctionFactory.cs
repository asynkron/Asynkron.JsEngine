#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Creates a function value for module-level function hoisting.
    /// This is used during module instantiation to hoist function declarations.
    /// For module-level function declarations, the function name binding is created
    /// in the module environment (not inside the function), so we set hasFunctionNameEnvironment=true
    /// to prevent the function from creating its own internal const binding for the name.
    /// </summary>
    internal static IJsCallable CreateModuleFunction(
        FunctionExpression funcExpr,
        JsEnvironment moduleEnv,
        RealmState realmState,
        bool isStrict,
        string? functionName = null)
    {
        // Module-level function declarations have their name binding in the module environment,
        // which is mutable. Setting hasFunctionNameEnvironment=true prevents the function
        // from creating an internal immutable binding that would shadow the module binding.
        // This allows code like `export default function fn() { fn = 2; }` to work correctly.
        var hasNameInEnvironment = funcExpr.Name is not null;

        IJsCallable result;
        if (funcExpr.IsGenerator)
        {
            if (funcExpr.IsAsync)
            {
                var asyncGen =
                    new AsyncGeneratorFunctionCallable(funcExpr, moduleEnv, realmState, isStrict, hasNameInEnvironment);
                if (functionName != null)
                {
                    asyncGen.EnsureHasName(functionName, true);
                }

                result = asyncGen;
            }
            else
            {
                var gen = new GeneratorFunctionCallable(funcExpr, moduleEnv, realmState, isStrict, hasNameInEnvironment);
                if (functionName != null)
                {
                    gen.EnsureHasName(functionName, true);
                }

                result = gen;
            }
        }
        else
        {
            var fn = new TypedFunction(funcExpr, moduleEnv, realmState, isStrict, hasNameInEnvironment);
            if (functionName != null)
            {
                fn.EnsureHasName(functionName, true);
            }

            result = fn;
        }

        return result;
    }
}
