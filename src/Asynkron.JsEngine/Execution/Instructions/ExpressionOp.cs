#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
///     Represents a bytecode operation for expressions.
///     This is a placeholder type for the bytecode migration effort.
///     In the future, this will be replaced with actual bytecode operations.
/// </summary>
/// <remarks>
///     During the migration from AST interpretation to bytecode execution,
///     instructions will have both AST fields (nullable) and bytecode operation
///     fields (nullable). This allows incremental migration where either representation
///     can be used. Once migration is complete, AST fields will be removed.
/// </remarks>
internal abstract record ExpressionOp
{
    /// <summary>
    ///     Placeholder for future bytecode data.
    ///     The actual implementation will be added during Phase 1 of the bytecode migration.
    /// </summary>
    private ExpressionOp()
    {
    }

    /// <summary>
    ///     A no-op placeholder for bytecode operations.
    ///     This allows code to compile during the migration phase.
    /// </summary>
    internal sealed record PlaceholderOp : ExpressionOp;
}
