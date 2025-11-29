using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Runtime;

/// <summary>
///     Holds per-engine realm state such as intrinsic prototypes and constructors,
///     so we do not rely on mutable StandardLibrary statics across realms.
/// </summary>
public sealed class RealmState
{
    public IJsEngineOptions Options { get; internal set; } = JsEngineOptions.Default;
    public JsObject? ObjectPrototype { get; set; }
    public JsObject? FunctionPrototype { get; set; }
    public JsObject? ArrayPrototype { get; set; }
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
    public HostFunction? ArrayConstructor { get; set; }
    public JsObject? TypedArrayPrototype { get; set; }
    public HostFunction? TypedArrayConstructor { get; set; }
    public JsObject? ArrayBufferPrototype { get; set; }
    public HostFunction? ArrayBufferConstructor { get; set; }
    public HostFunction? RegExpConstructor { get; set; }
    public RegExpStatics RegExpStatics { get; } = new();

    // Internal flags to avoid re-attaching built-in surfaces per instance
    public bool StringPrototypeMethodsInitialized { get; set; }

    public EvaluationContext CreateContext(
        ScopeKind kind = ScopeKind.Function,
        ScopeMode mode = ScopeMode.Strict,
        bool skipAnnexBInstantiation = false,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool pushScope = true)
    {
        var context = new EvaluationContext(this, cancellationToken, executionKind);
        if (pushScope)
        {
            context.PushScope(kind, mode, skipAnnexBInstantiation);
        }

        return context;
    }

    public EvaluationContext CreateStrictContext(
        ScopeKind kind = ScopeKind.Function,
        bool skipAnnexBInstantiation = false,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool pushScope = true)
    {
        return CreateContext(
            kind,
            ScopeMode.Strict,
            skipAnnexBInstantiation,
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
