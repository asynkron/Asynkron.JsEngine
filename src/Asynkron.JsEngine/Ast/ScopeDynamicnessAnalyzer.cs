#region

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Determines whether a function body is free of dynamic scope features
    /// (direct eval / with) that would invalidate identifier binding caches.
    /// Conservative: any direct eval or with anywhere in the function's
    /// evaluated expressions disables caching.
    /// </summary>
    internal static bool AllowsIdentifierCaching(FunctionExpression function)
    {
        if (DynamicScopeDetector.ContainsDirectEvalInParameters(function.Parameters))
        {
            return false;
        }

        return AllowsIdentifierCaching(function.Body);
    }

    /// <summary>
    /// Determines whether a program is free of dynamic scope features that would
    /// invalidate identifier binding caches.
    /// </summary>
    internal static bool AllowsIdentifierCaching(ProgramNode program)
    {
        var block = new BlockStatement(program.Source, program.Body, program.IsStrict);
        return AllowsIdentifierCaching(block);
    }

    /// <summary>
    /// Determines whether a block is free of dynamic scope features (with/direct eval).
    /// </summary>
    internal static bool AllowsIdentifierCaching(BlockStatement block)
    {
        return !DynamicScopeDetector.ContainsWithOrDirectEval(block);
    }

    /// <summary>
    /// Determines whether a function body references the 'arguments' identifier.
    /// Used to skip creating the arguments object when it's not needed.
    /// Note: This doesn't cross function boundaries - nested functions have their own arguments.
    /// </summary>
    internal static bool UsesArgumentsIdentifier(FunctionExpression function)
    {
        return ArgumentsReferenceDetector.ContainsArgumentsReference(function.Body);
    }

    /// <summary>
    /// Determines whether a function body contains inner function expressions (including arrows).
    /// Used to detect if the function creates closures that would capture the invocation environment.
    /// If no inner functions exist, the invocation environment can be pooled for reuse.
    /// </summary>
    internal static bool ContainsInnerFunctionExpression(FunctionExpression function)
    {
        return ContainsInnerFunctionExpression(function.Body);
    }

    internal static bool ContainsInnerFunctionExpression(BlockStatement block)
    {
        if (block.TryGetContainsInnerFunction(out var cached))
        {
            return cached;
        }

        var result = InnerFunctionBlockDetector.ContainsInnerFunction(block);
        block.CacheContainsInnerFunction(result);
        return result;
    }

    internal static bool ContainsInnerFunctionExpression(ExpressionNode expression)
    {
        return InnerFunctionDetector.ContainsInnerFunctionExpression(expression);
    }

    internal static bool ContainsInnerFunctionExpression(BindingTarget target)
    {
        return InnerFunctionBindingDetector.ContainsInnerFunction(target);
    }

    /// <summary>
    /// Checks if a function body contains any CallExpression where the callee is an identifier
    /// that is not one of the function's parameters. This detects recursive calls and calls
    /// to closure-captured functions which cannot use environment reuse optimization.
    /// </summary>
    internal static bool ContainsNonParameterCalleeIdentifier(FunctionExpression function,
        HashSet<Symbol> parameterNames)
    {
        return NonParameterCalleeDetector.ContainsNonParameterCallee(function.Body, parameterNames);
    }
}
