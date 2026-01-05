#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an assignment to an identifier.
/// </summary>
/// <param name="Source">Source location in the original code.</param>
/// <param name="Target">The target identifier name.</param>
/// <param name="Value">The value expression to assign.</param>
/// <param name="IsCompoundAssignment">True if this is a compound assignment (+=, -=, etc.).</param>
/// <param name="ScopeDepth">How many scopes up to find target variable. -1 means unresolved.</param>
/// <param name="SlotIndex">Index into the scope's slots array. -1 means unresolved.</param>
/// <param name="ScopeId">Unique ID of the scope where this variable is declared. -1 means unresolved.</param>
/// <param name="IsImmutableTarget">True if the target is an immutable binding (e.g., named function expression name).
/// In strict mode, assignment throws TypeError. In non-strict mode, assignment is silently ignored.</param>
/// <param name="FlatSlotId">Index into the flat slots array for O(1) access. -1 means unresolved or requires scope chain lookup.</param>
public sealed record AssignmentExpression(
    SourceReference? Source,
    Symbol Target,
    ExpressionNode Value,
    bool IsCompoundAssignment = false,
    int ScopeDepth = -1,
    int SlotIndex = -1,
    int ScopeId = -1,
    bool IsImmutableTarget = false,
    int FlatSlotId = -1)
    : ExpressionNode(Source)
{
    /// <summary>
    /// Cached identifier expression representing the target with scope metadata.
    /// Populated by the scope analyzer to avoid recreating at hot call sites.
    /// </summary>
    public IdentifierExpression? TargetIdentifier { get; init; }
}
