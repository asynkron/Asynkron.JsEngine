#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Runtime;

/// <summary>
///     Holds per-engine realm state such as intrinsic prototypes and constructors,
///     so we do not rely on mutable StandardLibrary statics across realms.
/// </summary>
public sealed class RealmState
{
    private readonly ObjectPool<EvaluationContext> _contextPool;
    private readonly ObjectPool<JsEnvironment> _environmentPool;

    public RealmState()
    {
        _contextPool = new ObjectPool<EvaluationContext>(16, () => new EvaluationContext(this));
        _environmentPool = new ObjectPool<JsEnvironment>(32, static () => new JsEnvironment(null, false, false));
    }

    public IJsEngineOptions Options { get; internal init; } = JsEngineOptions.Default;
    internal JsEngine? Engine { get; init; }
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Cached from Options.EnableFastPaths for hot path access.
    /// When false, all fast paths are disabled and only base implementations are used.
    /// </summary>
    public bool EnableFastPaths { get; internal init; } = true;

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
    public HostFunction? URIErrorConstructor { get; set; }
    public JsObject? ReferenceErrorPrototype { get; set; }
    public JsObject? BooleanPrototype { get; set; }
    public JsObject? NumberPrototype { get; set; }
    public JsObject? StringPrototype { get; set; }
    public JsObject? BigIntPrototype { get; set; }
    public JsObject? SymbolPrototype { get; set; }
    public JsObject? MapPrototype { get; set; }
    public JsObject? MapIteratorPrototype { get; set; }
    public JsObject? SetPrototype { get; set; }
    public JsObject? SetIteratorPrototype { get; set; }
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
    public JsObject? IteratorPrototype { get; set; }
    public HostFunction? AsyncGeneratorFunctionConstructor { get; set; }
    public IJsCallable? PromiseConstructor { get; set; }
    public JsObject? PromisePrototype { get; set; }

    // Internal flags to avoid re-attaching built-in surfaces per instance
    public bool StringPrototypeMethodsInitialized { get; set; }


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
        bool pushScope = true)
    {
        var context = _contextPool.Rent();
        context.Reset();

        if (pushScope)
        {
            context.PushScope(kind, mode);
        }

        return context;
    }

    /// <summary>
    /// Returns an EvaluationContext to the pool for reuse.
    /// </summary>
    public void ReturnContext(EvaluationContext context) => _contextPool.Return(context);

    /// <summary>
    /// Rents a JsEnvironment from the pool or creates a new one.
    /// Call ReturnEnvironment when done to return it to the pool.
    /// </summary>
    public JsEnvironment RentEnvironment(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        var env = _environmentPool.Rent();
        env.Reset(enclosing, isFunctionScope, isStrict, creatingSource, description, isParameterEnvironment,
            isBodyEnvironment);
        return env;
    }

    /// <summary>
    /// Returns a JsEnvironment to the pool for reuse.
    /// </summary>
    public void ReturnEnvironment(JsEnvironment env) => _environmentPool.Return(env);

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
