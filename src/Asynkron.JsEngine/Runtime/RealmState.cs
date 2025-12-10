using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Runtime;

/// <summary>
///     Holds per-engine realm state such as intrinsic prototypes and constructors,
///     so we do not rely on mutable StandardLibrary statics across realms.
/// </summary>
public sealed class RealmState
{
    public IJsEngineOptions Options { get; internal set; } = JsEngineOptions.Default;
    internal JsEngine? Engine { get; set; }
    public ILogger? Logger { get; set; }

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
    public HostFunction? ArrayConstructor { get; set; }
    public JsObject? TypedArrayPrototype { get; set; }
    public HostFunction? TypedArrayConstructor { get; set; }
    public JsObject? ArrayBufferPrototype { get; set; }
    public HostFunction? ArrayBufferConstructor { get; set; }
    public JsObject? SharedArrayBufferPrototype { get; set; }
    public HostFunction? SharedArrayBufferConstructor { get; set; }
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
