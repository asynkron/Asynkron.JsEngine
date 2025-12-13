using System.Collections.Concurrent;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Runtime;

/// <summary>
///     Holds per-engine realm state such as intrinsic prototypes and constructors,
///     so we do not rely on mutable StandardLibrary statics across realms.
/// </summary>
public sealed class RealmState
{
    private const int MaxContextPoolSize = 16;
    private const int MaxEnvironmentPoolSize = 32;
    private readonly ConcurrentStack<EvaluationContext> _contextPool = new();
    private readonly ConcurrentStack<JsEnvironment> _environmentPool = new();

    public IJsEngineOptions Options { get; internal set; } = JsEngineOptions.Default;
    internal JsEngine? Engine { get; set; }
    public ILogger? Logger { get; set; }
    private readonly Dictionary<string, string> _symbolPropertyKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Per ES spec 13.2.8.4, template objects are cached by parse node (source location).
    /// The key is the TaggedTemplateExpression AST node reference.
    /// </summary>
    internal Dictionary<object, object> TemplateObjectCache { get; } = new(ReferenceEqualityComparer.Instance);
    public JsObject? ObjectPrototype { get; set; }
    public IJsObjectLike? FunctionPrototype { get; set; }
    public IJsObjectLike? ArrayPrototype { get; set; }
    public JsObject? DatePrototype { get; set; }
    public JsObject? ErrorPrototype { get; set; }
    public JsObject? TypeErrorPrototype { get; set; }
    public JsObject? SyntaxErrorPrototype { get; set; }
    public JsObject? RegExpPrototype { get; set; }
    public HostFunction? TypeErrorConstructor { get; set; }
    public HostFunction? RangeErrorConstructor { get; set; }
    public HostFunction? SyntaxErrorConstructor { get; set; }
    public HostFunction? ReferenceErrorConstructor { get; set; }
    public JsObject? ReferenceErrorPrototype { get; set; }
    public JsObject? BooleanPrototype { get; set; }
    public JsObject? NumberPrototype { get; set; }
    public JsObject? StringPrototype { get; set; }
    public JsObject? BigIntPrototype { get; set; }
    public JsObject? SymbolPrototype { get; set; }
    public JsObject? MapPrototype { get; set; }
    public JsObject? SetPrototype { get; set; }
    public JsObject? WeakMapPrototype { get; set; }
    public JsObject? WeakSetPrototype { get; set; }
    public HostFunction? ArrayConstructor { get; set; }
    public HostFunction? MapConstructor { get; set; }
    public HostFunction? SetConstructor { get; set; }
    public HostFunction? WeakMapConstructor { get; set; }
    public HostFunction? WeakSetConstructor { get; set; }
    public JsObject? TypedArrayPrototype { get; set; }
    public HostFunction? TypedArrayConstructor { get; set; }
    public JsObject? ArrayBufferPrototype { get; set; }
    public HostFunction? ArrayBufferConstructor { get; set; }
    public JsObject? SharedArrayBufferPrototype { get; set; }
    public HostFunction? SharedArrayBufferConstructor { get; set; }
    public JsObject? DataViewPrototype { get; set; }
    public HostFunction? DataViewConstructor { get; set; }
    public HostFunction? RegExpConstructor { get; set; }
    public RegExpStatics RegExpStatics { get; } = new();
    public JsObject? GeneratorFunctionPrototype { get; set; }
    public JsObject? GeneratorPrototype { get; set; }
    public HostFunction? GeneratorFunctionConstructor { get; set; }
    public JsObject? AsyncGeneratorFunctionPrototype { get; set; }
    public JsObject? AsyncGeneratorPrototype { get; set; }
    public JsObject? AsyncIteratorPrototype { get; set; }
    public HostFunction? AsyncGeneratorFunctionConstructor { get; set; }
    public IJsCallable? PromiseConstructor { get; set; }
    public JsObject? PromisePrototype { get; set; }

    // Internal flags to avoid re-attaching built-in surfaces per instance
    public bool StringPrototypeMethodsInitialized { get; set; }

    internal bool TryGetSymbolPropertyKey(string wellKnownName, out string propertyName)
    {
        return _symbolPropertyKeys.TryGetValue(wellKnownName, out propertyName);
    }

    internal string GetSymbolPropertyKey(string wellKnownName)
    {
        if (_symbolPropertyKeys.TryGetValue(wellKnownName, out var propertyName))
        {
            return propertyName;
        }

        propertyName = Ast.TypedAstSymbol.PropertyKey(wellKnownName, null);
        _symbolPropertyKeys[wellKnownName] = propertyName;
        return propertyName;
    }

    public EvaluationContext CreateContext(
        ScopeKind kind = ScopeKind.Function,
        ScopeMode mode = ScopeMode.Strict,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool pushScope = true)
    {
        var context = new EvaluationContext(this, cancellationToken, executionKind);
        if (pushScope)
        {
            context.PushScope(kind, mode);
        }

        return context;
    }

    /// <summary>
    /// Rents an EvaluationContext from the pool or creates a new one.
    /// Call ReturnContext when done to return it to the pool.
    /// </summary>
    public EvaluationContext RentContext(
        ScopeKind kind = ScopeKind.Function,
        ScopeMode mode = ScopeMode.Strict,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool pushScope = true)
    {
        if (_contextPool.TryPop(out var context))
        {
            context.Reset();
        }
        else
        {
            context = new EvaluationContext(this, cancellationToken, executionKind);
        }

        if (pushScope)
        {
            context.PushScope(kind, mode);
        }

        return context;
    }

    /// <summary>
    /// Returns an EvaluationContext to the pool for reuse.
    /// </summary>
    public void ReturnContext(EvaluationContext context)
    {
        if (_contextPool.Count < MaxContextPoolSize)
        {
            _contextPool.Push(context);
        }
    }

    /// <summary>
    /// Rents a JsEnvironment from the pool or creates a new one.
    /// Call ReturnEnvironment when done to return it to the pool.
    /// </summary>
    public JsEnvironment RentEnvironment(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        Parser.SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        if (_environmentPool.TryPop(out var env))
        {
            env.Reset(enclosing, isFunctionScope, isStrict, creatingSource, description, isParameterEnvironment, isBodyEnvironment);
            return env;
        }

        return new JsEnvironment(enclosing, isFunctionScope, isStrict, creatingSource, description,
            isParameterEnvironment: isParameterEnvironment, isBodyEnvironment: isBodyEnvironment);
    }

    /// <summary>
    /// Returns a JsEnvironment to the pool for reuse.
    /// </summary>
    public void ReturnEnvironment(JsEnvironment env)
    {
        if (_environmentPool.Count < MaxEnvironmentPoolSize)
        {
            _environmentPool.Push(env);
        }
    }

    public EvaluationContext CreateStrictContext(
        ScopeKind kind = ScopeKind.Function,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool pushScope = true)
    {
        return CreateContext(
            kind,
            ScopeMode.Strict,
            cancellationToken,
            executionKind,
            pushScope);
    }
}

public sealed class RegExpStatics
{
    public string Input { get; set; } = string.Empty;
    public string LastMatch { get; set; } = string.Empty;
    public string LastParen { get; set; } = string.Empty;
    public string LeftContext { get; set; } = string.Empty;
    public string RightContext { get; set; } = string.Empty;
    public string[] Captures { get; } = new string[9];
}
