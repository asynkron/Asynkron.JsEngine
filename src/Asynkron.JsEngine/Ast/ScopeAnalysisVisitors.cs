using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Visitor that detects 'with' statements or direct eval calls.
/// Does not traverse into nested function bodies (they have their own scope).
/// </summary>
internal sealed class DynamicScopeDetector : AstVisitor
{
    [ThreadStatic] private static DynamicScopeDetector? _instance;

    public bool Found { get; private set; }

    private void Reset()
    {
        Found = false;
        ShouldStop = false;
    }

    /// <summary>
    /// Checks if a block contains 'with' statements or direct eval calls.
    /// </summary>
    public static bool ContainsWithOrDirectEval(BlockStatement block)
    {
        if (block.TryGetContainsDynamicScope(out var cached))
        {
            return cached;
        }

        var detector = _instance ??= new DynamicScopeDetector();
        detector.Reset();
        detector.Visit(block);

        block.CacheContainsDynamicScope(detector.Found);
        return detector.Found;
    }

    /// <summary>
    /// Checks if an expression contains direct eval calls.
    /// </summary>
    public static bool ContainsDirectEval(ExpressionNode expression)
    {
        var detector = _instance ??= new DynamicScopeDetector();
        detector.Reset();
        detector.Visit(expression);
        return detector.Found;
    }

    /// <summary>
    /// Checks if a binding target contains direct eval calls (in default values).
    /// </summary>
    public static bool ContainsDirectEval(BindingTarget target)
    {
        var detector = _instance ??= new DynamicScopeDetector();
        detector.Reset();
        detector.Visit(target);
        return detector.Found;
    }

    /// <summary>
    /// Checks if function parameters contain direct eval calls.
    /// </summary>
    public static bool ContainsDirectEvalInParameters(ImmutableArray<FunctionParameter> parameters)
    {
        var detector = _instance ??= new DynamicScopeDetector();
        detector.Reset();

        foreach (var parameter in parameters)
        {
            if (parameter.DefaultValue is not null)
            {
                detector.Visit(parameter.DefaultValue);
                if (detector.Found) return true;
            }

            if (parameter.Pattern is not null)
            {
                detector.Visit(parameter.Pattern);
                if (detector.Found) return true;
            }

            detector.Reset();
        }

        return false;
    }

    protected override void VisitWithStatement(WithStatement node)
    {
        Found = true;
        ShouldStop = true;
    }

    protected override void VisitCallExpression(CallExpression node)
    {
        // Direct eval: eval(...) where eval is an identifier (not optional chaining)
        if (!node.IsOptional && node.Callee is IdentifierExpression { Name.Name: "eval" })
        {
            Found = true;
            ShouldStop = true;
            return;
        }

        base.VisitCallExpression(node);
    }

    // Don't traverse into nested functions - they have their own scope
    protected override void VisitFunctionExpression(FunctionExpression node) { }
    protected override void VisitFunctionDeclaration(FunctionDeclaration node) { }
}

/// <summary>
/// Visitor that detects references to the 'arguments' identifier.
/// Does not traverse into nested function bodies (they have their own arguments).
/// </summary>
internal sealed class ArgumentsReferenceDetector : AstVisitor
{
    [ThreadStatic] private static ArgumentsReferenceDetector? _instance;

    public bool Found { get; private set; }

    private void Reset()
    {
        Found = false;
        ShouldStop = false;
    }

    /// <summary>
    /// Checks if a block contains references to 'arguments'.
    /// </summary>
    public static bool ContainsArgumentsReference(BlockStatement block)
    {
        var detector = _instance ??= new ArgumentsReferenceDetector();
        detector.Reset();
        detector.Visit(block);
        return detector.Found;
    }

    protected override void VisitIdentifierExpression(IdentifierExpression node)
    {
        if (node.Name.Name == "arguments")
        {
            Found = true;
            ShouldStop = true;
        }
    }

    // Don't traverse into nested functions - they have their own arguments
    protected override void VisitFunctionExpression(FunctionExpression node) { }
    protected override void VisitFunctionDeclaration(FunctionDeclaration node) { }
}

/// <summary>
/// Visitor that detects nested function expressions (not declarations).
/// Used to determine if a function contains closures.
/// </summary>
internal sealed class InnerFunctionDetector : AstVisitor
{
    [ThreadStatic] private static InnerFunctionDetector? _instance;

    private bool _skipFirst;
    public bool Found { get; private set; }

    private void Reset(bool skipFirst = false)
    {
        Found = false;
        ShouldStop = false;
        _skipFirst = skipFirst;
    }

    /// <summary>
    /// Checks if an expression contains nested function expressions.
    /// </summary>
    public static bool ContainsInnerFunctionExpression(ExpressionNode expression)
    {
        var detector = _instance ??= new InnerFunctionDetector();
        // If the expression itself is a function, skip it and look inside
        detector.Reset(skipFirst: expression is FunctionExpression);
        detector.Visit(expression);
        return detector.Found;
    }

    protected override void VisitFunctionExpression(FunctionExpression node)
    {
        if (_skipFirst)
        {
            _skipFirst = false;
            base.VisitFunctionExpression(node);
        }
        else
        {
            // Found a nested function expression
            Found = true;
            ShouldStop = true;
        }
    }

    // Function declarations don't count as inner function expressions
    protected override void VisitFunctionDeclaration(FunctionDeclaration node) { }
}

/// <summary>
/// Visitor that detects inner function expressions in a block (for closure detection).
/// FunctionDeclarations count as inner functions. Does not traverse into nested function bodies.
/// </summary>
internal sealed class InnerFunctionBlockDetector : AstVisitor
{
    [ThreadStatic] private static InnerFunctionBlockDetector? _instance;

    public bool Found { get; private set; }

    private void Reset()
    {
        Found = false;
        ShouldStop = false;
    }

    public static bool ContainsInnerFunction(BlockStatement block)
    {
        var detector = _instance ??= new InnerFunctionBlockDetector();
        detector.Reset();
        detector.Visit(block);
        return detector.Found;
    }

    protected override void VisitFunctionExpression(FunctionExpression node)
    {
        Found = true;
        ShouldStop = true;
    }

    protected override void VisitFunctionDeclaration(FunctionDeclaration node)
    {
        // FunctionDeclaration creates a closure - the function itself captures the environment
        Found = true;
        ShouldStop = true;
    }

    protected override void VisitClassExpression(ClassExpression node)
    {
        Found = true;
        ShouldStop = true;
    }

    // Don't descend into class declarations - they're handled at their level
    protected override void VisitClassDeclaration(ClassDeclaration node) { }

    // Check object literal methods
    protected override void VisitObjectExpression(ObjectExpression node)
    {
        foreach (var member in node.Members)
        {
            if (ShouldStop) break;

            if (member.Function is not null)
            {
                Found = true;
                ShouldStop = true;
                return;
            }

            if (member.Key is ExpressionNode keyExpr)
            {
                Visit(keyExpr);
            }

            if (!ShouldStop && member.Value is not null)
            {
                Visit(member.Value);
            }
        }
    }
}

/// <summary>
/// Visitor that detects inner function expressions in binding targets (destructuring patterns).
/// </summary>
internal sealed class InnerFunctionBindingDetector : AstVisitor
{
    [ThreadStatic] private static InnerFunctionBindingDetector? _instance;

    public bool Found { get; private set; }

    private void Reset()
    {
        Found = false;
        ShouldStop = false;
    }

    public static bool ContainsInnerFunction(BindingTarget target)
    {
        var detector = _instance ??= new InnerFunctionBindingDetector();
        detector.Reset();
        detector.Visit(target);
        return detector.Found;
    }

    protected override void VisitFunctionExpression(FunctionExpression node)
    {
        Found = true;
        ShouldStop = true;
    }

    protected override void VisitClassExpression(ClassExpression node)
    {
        Found = true;
        ShouldStop = true;
    }
}

/// <summary>
/// Visitor that detects calls to non-parameter identifiers (recursive/closure calls).
/// </summary>
internal sealed class NonParameterCalleeDetector : AstVisitor
{
    [ThreadStatic] private static NonParameterCalleeDetector? _instance;

    private HashSet<Symbol>? _parameterNames;
    public bool Found { get; private set; }

    private void Reset(HashSet<Symbol> parameterNames)
    {
        Found = false;
        ShouldStop = false;
        _parameterNames = parameterNames;
    }

    public static bool ContainsNonParameterCallee(BlockStatement block, HashSet<Symbol> parameterNames)
    {
        var detector = _instance ??= new NonParameterCalleeDetector();
        detector.Reset(parameterNames);
        detector.Visit(block);
        return detector.Found;
    }

    protected override void VisitCallExpression(CallExpression node)
    {
        if (node.Callee is IdentifierExpression calleeId &&
            !_parameterNames!.Contains(calleeId.Name))
        {
            // The callee is an identifier that's not a parameter - this is a call
            // to a closure-captured function (like recursive calls or outer variables)
            Found = true;
            ShouldStop = true;
            return;
        }

        base.VisitCallExpression(node);
    }

    // Don't traverse into nested functions - they have their own scope
    protected override void VisitFunctionExpression(FunctionExpression node) { }
    protected override void VisitFunctionDeclaration(FunctionDeclaration node) { }
    protected override void VisitClassExpression(ClassExpression node) { }
    protected override void VisitClassDeclaration(ClassDeclaration node) { }
}

/// <summary>
/// Visitor that collects var-declared names from a block.
/// Does not traverse into nested function bodies (var declarations don't hoist out of functions).
/// </summary>
internal sealed class VarNameCollector : AstVisitor
{
    [ThreadStatic] private static VarNameCollector? _instance;

    private List<Symbol>? _names;

    private void Reset(List<Symbol> names)
    {
        _names = names;
        ShouldStop = false;
    }

    /// <summary>
    /// Collects all var-declared names from a block into the provided list.
    /// </summary>
    public static void CollectVarDeclaredNames(BlockStatement block, List<Symbol> names)
    {
        var collector = _instance ??= new VarNameCollector();
        collector.Reset(names);
        collector.Visit(block);
    }

    protected override void VisitVariableDeclaration(VariableDeclaration node)
    {
        if (node.Kind == VariableKind.Var)
        {
            foreach (var declarator in node.Declarators)
            {
                CollectBindingNames(declarator.Target);
            }
        }

        // Still traverse for nested blocks that might have var declarations
        base.VisitVariableDeclaration(node);
    }

    private void CollectBindingNames(BindingTarget target)
    {
        switch (target)
        {
            case IdentifierBinding id:
                _names!.Add(id.Name);
                break;
            case ArrayBinding array:
                foreach (var element in array.Elements)
                {
                    if (element.Target is not null)
                    {
                        CollectBindingNames(element.Target);
                    }
                }
                if (array.RestElement is not null)
                {
                    CollectBindingNames(array.RestElement);
                }
                break;
            case ObjectBinding obj:
                foreach (var prop in obj.Properties)
                {
                    CollectBindingNames(prop.Target);
                }
                if (obj.RestElement is not null)
                {
                    CollectBindingNames(obj.RestElement);
                }
                break;
        }
    }

    // Don't traverse into nested functions - var doesn't hoist out of functions
    protected override void VisitFunctionExpression(FunctionExpression node) { }
    protected override void VisitFunctionDeclaration(FunctionDeclaration node) { }
}
