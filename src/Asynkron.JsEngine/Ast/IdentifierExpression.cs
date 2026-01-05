#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a reference to an identifier.
/// </summary>
/// <param name="Source">Source location in the original code.</param>
/// <param name="Name">The identifier name as a Symbol.</param>
/// <param name="ScopeDepth">How many scopes up to find this variable (0 = local, 1 = parent, etc.). -1 means unresolved (use dictionary lookup).</param>
/// <param name="SlotIndex">Index into the scope's slots array. -1 means unresolved.</param>
/// <param name="ScopeId">Unique ID of the scope where this variable is declared. -1 means unresolved.</param>
/// <param name="FlatSlotId">Index into the flat slots array for O(1) access. -1 means unresolved or requires scope chain lookup.</param>
public sealed record IdentifierExpression(
    SourceReference? Source,
    Symbol Name,
    int ScopeDepth = -1,
    int SlotIndex = -1,
    int ScopeId = -1,
    int FlatSlotId = -1) : ExpressionNode(Source);
