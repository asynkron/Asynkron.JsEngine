using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a call expression.
/// </summary>
/// <param name="CanReuseCallerEnvironment">True if this call can safely reuse the caller's environment.
/// Set by ScopeAnalyzer when: (1) the containing function has no closures, (2) no eval/with in scope,
/// and (3) no scope variables are referenced after this call's arguments are evaluated.</param>
public sealed record CallExpression(
    SourceReference? Source,
    ExpressionNode Callee,
    ImmutableArray<CallArgument> Arguments,
    bool IsOptional,
    bool CanReuseCallerEnvironment = false) : ExpressionNode(Source);
