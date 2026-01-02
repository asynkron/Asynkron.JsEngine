#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for exception handling constructs (try/catch/finally, throw).
/// </summary>
internal static class TryEmitter
{
    /// <summary>
    /// Emit IR for a try statement with optional catch and finally blocks.
    /// </summary>
    public static bool TryEmitTry(
        EmitContext ctx,
        TryStatement statement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        var hasCatch = statement.Catch is not null;
        var hasFinally = statement.Finally is not null;

        if (!hasCatch && !hasFinally)
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = ctx.InstructionCount;

        // Build finally block first (bottom-up)
        var finallyEntry = -1;
        var endFinallyIndex = -1;
        if (hasFinally && statement.Finally is not null)
        {
            endFinallyIndex = ctx.Append(new EndFinallyInstruction(nextIndex));

            var builtFinally = ctx.TryBuildStatement(statement.Finally, endFinallyIndex, out finallyEntry, activeLabel);
            if (!builtFinally)
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // LeaveTry marks normal exit from try block
        var leaveTryIndex = ctx.Append(new LeaveTryInstruction(nextIndex));

        // Build catch block using pure IR (no AST delegation)
        var catchEntry = -1;
        if (hasCatch && statement.Catch is not null)
        {
            if (!TryEmitCatchBlock(ctx, statement.Catch, leaveTryIndex, out catchEntry))
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // Build try block
        if (!ctx.TryBuildStatement(statement.TryBlock, leaveTryIndex, out var tryEntry, activeLabel))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Emit EnterTry instruction as the entry point
        // Note: CatchSlotSymbol is null because we use EnterCatchInstruction now
        entryIndex = ctx.Append(new EnterTryInstruction(tryEntry, catchEntry, null, finallyEntry, endFinallyIndex));
        return true;
    }

    /// <summary>
    /// Emit IR for a catch block with pure IR instructions.
    /// Creates EnterCatchInstruction → body instructions → PopEnvironmentInstruction.
    /// </summary>
    private static bool TryEmitCatchBlock(
        EmitContext ctx,
        CatchClause catchClause,
        int leaveTryIndex,
        out int catchEntry)
    {
        // Allocate a scope ID for the catch environment
        var catchScopeId = ctx.AllocateScopeId();

        // Build slot map and determine instruction type based on binding pattern
        ImmutableDictionary<Symbol, int> slotMap;
        int slotCount;
        Symbol? catchParamSymbol = null;
        BindingTarget? destructuringPattern = null;

        if (catchClause.Binding is IdentifierBinding identifierBinding)
        {
            // Simple identifier binding: catch (e) { }
            catchParamSymbol = identifierBinding.Name;
            slotMap = ImmutableDictionary<Symbol, int>.Empty.Add(catchParamSymbol, 0);
            slotCount = 1;
        }
        else if (catchClause.Binding is not null)
        {
            // Destructuring binding: catch ([a, b]) { } or catch ({x, y}) { }
            destructuringPattern = catchClause.Binding;
            slotMap = BuildSlotMapFromBindingTarget(catchClause.Binding, out slotCount);
        }
        else
        {
            // ES2019 optional catch binding: catch { }
            slotMap = ImmutableDictionary<Symbol, int>.Empty;
            slotCount = 0;
        }

        // Build instructions bottom-up:
        // 1. PopEnvironmentInstruction → leaveTryIndex
        // 2. Body statements (possibly with its own block environment) → PopEnvironment
        // 3. EnterCatchInstruction or EnterCatchWithDestructuringInstruction → body entry

        // 1. Pop catch environment at the end
        var popCatchEnv = ctx.Append(new PopEnvironmentInstruction(catchScopeId, false, leaveTryIndex));

        // 2. Emit catch body - use TryEmitBlock to properly handle block-scoped declarations
        // Per ECMAScript, the catch block body gets its own lexical environment for let/const,
        // separate from the catch parameter environment.
        if (!BlockEmitter.TryEmitBlock(ctx, catchClause.Body, popCatchEnv, out var bodyEntry))
        {
            catchEntry = -1;
            return false;
        }

        // 3. Emit the appropriate catch instruction
        if (destructuringPattern is not null)
        {
            // Destructuring catch parameter
            catchEntry = ctx.Append(new EnterCatchWithDestructuringInstruction(
                bodyEntry,
                destructuringPattern,
                catchScopeId,
                slotCount,
                slotMap));
        }
        else
        {
            // Simple identifier or no binding
            catchEntry = ctx.Append(new EnterCatchInstruction(
                bodyEntry,
                catchParamSymbol,
                catchScopeId,
                slotCount,
                slotMap));
        }

        return true;
    }

    /// <summary>
    /// Builds a slot map from a binding target by collecting all identifiers.
    /// </summary>
    private static ImmutableDictionary<Symbol, int> BuildSlotMapFromBindingTarget(
        BindingTarget target,
        out int slotCount)
    {
        var builder = ImmutableDictionary.CreateBuilder<Symbol, int>();
        var index = 0;
        CollectBindingIdentifiers(target, builder, ref index);
        slotCount = index;
        return builder.ToImmutable();
    }

    /// <summary>
    /// Recursively collects all identifier symbols from a binding target.
    /// </summary>
    private static void CollectBindingIdentifiers(BindingTarget target, ImmutableDictionary<Symbol, int>.Builder builder, ref int index)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!builder.ContainsKey(id.Name))
                    {
                        builder.Add(id.Name, index++);
                    }

                    break;

                case ArrayBinding array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingIdentifiers(element.Target, builder, ref index);
                        }
                    }

                    if (array.RestElement is not null)
                    {
                        target = array.RestElement;
                        continue;
                    }

                    break;

                case ObjectBinding obj:
                    foreach (var prop in obj.Properties)
                    {
                        CollectBindingIdentifiers(prop.Target, builder, ref index);
                    }

                    if (obj.RestElement is not null)
                    {
                        target = obj.RestElement;
                        continue;
                    }

                    break;

                case AssignmentTargetBinding:
                    // AssignmentTargetBinding is used for assignment patterns like [x.y] = ...
                    // These don't create new bindings in the catch scope
                    break;
            }

            break;
        }
    }
}
