using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Bundles the transformed S-expression with its typed AST counterpart so the
/// runtime can decide between the legacy and typed evaluators without
/// re-parsing.
/// </summary>
/// <param name="SExpression">The transformed S-expression produced by the parser pipeline.</param>
/// <param name="Typed">The typed AST built from the S-expression.</param>
public sealed record ParsedProgram(Cons SExpression, ProgramNode Typed);
