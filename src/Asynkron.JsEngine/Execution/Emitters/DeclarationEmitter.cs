using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for declaration statements (var/let/const, function, class).
/// </summary>
internal static class DeclarationEmitter
{
    /// <summary>
    /// Emit IR for a function declaration (hoisted - no-op at runtime).
    /// </summary>
    public static int EmitFunctionDeclaration(EmitContext ctx, int nextIndex)
    {
        return ctx.Append(new FunctionDeclarationInstruction(nextIndex));
    }

    /// <summary>
    /// Emit IR for a class declaration.
    /// </summary>
    public static bool TryEmitClassDeclaration(
        EmitContext ctx,
        ClassDeclaration classDeclaration,
        int nextIndex,
        out int entryIndex)
    {
        // Check for yields in places that are evaluated in the generator context:
        // - extends expression
        // - computed property names of members and fields
        if (ClassDefinitionContainsYield(classDeclaration.Definition))
        {
            ctx.SetFailureReason("Class declaration contains yield in computed property names or extends clause.");
            entryIndex = -1;
            return false;
        }

        // For async generators, awaits need proper handling via StatementInstruction
        if (ClassDefinitionContainsAwait(classDeclaration.Definition))
        {
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, classDeclaration));
            return true;
        }

        // Use native ClassDeclarationInstruction for clean cases
        entryIndex = ctx.Append(new ClassDeclarationInstruction(nextIndex, classDeclaration));
        return true;
    }

    /// <summary>
    /// Try to emit IR for a variable declaration.
    /// Handles yield initializers, binding target defaults, and falls back to StatementInstruction when needed.
    /// </summary>
    public static bool TryEmitVariableDeclaration(
        EmitContext ctx,
        VariableDeclaration declaration,
        int nextIndex,
        out int entryIndex)
    {
        // First try yield initializer handling (lowerer temps)
        if (TryEmitYieldInitializer(ctx, declaration, nextIndex, out entryIndex))
        {
            return true;
        }

        // Check for variable declarations with yields in binding target default values.
        // These cannot be safely lowered because defaults are only evaluated when
        // the value is undefined. Wrap them as StatementInstruction.
        if (DeclarationContainsYieldInBindingTargetDefaults(declaration))
        {
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, declaration));
            return true;
        }

        if (DeclarationContainsYield(declaration))
        {
            ctx.SetFailureReason("Variable declaration contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        // Try to use native SimpleVariableDeclarationInstruction for simple cases
        if (TryEmitSimpleVariableDeclaration(ctx, declaration, nextIndex, out entryIndex))
        {
            return true;
        }

        // Fall back to StatementInstruction for complex declarations
        entryIndex = ctx.Append(new StatementInstruction(nextIndex, declaration));
        return true;
    }

    private static bool TryEmitYieldInitializer(
        EmitContext ctx,
        VariableDeclaration declaration,
        int nextIndex,
        out int entryIndex)
    {
        entryIndex = -1;

        if (declaration.Declarators.Length != 1 ||
            declaration.Declarators[0] is not { } declarator ||
            declarator.Target is not IdentifierBinding { Name: { } targetSymbol } ||
            declarator.Initializer is not YieldExpression yieldInitializer)
        {
            return false;
        }

        if (!IsLowererTemp(targetSymbol))
        {
            return false;
        }

        return YieldEmitter.TryEmitVariableWithYieldInitializer(
            ctx, targetSymbol, yieldInitializer, nextIndex, out entryIndex);
    }

    private static bool IsLowererTemp(Symbol symbol)
    {
        return symbol.Name?.StartsWith("__yield_lower_", StringComparison.Ordinal) == true;
    }

    private static bool DeclarationContainsYield(VariableDeclaration declaration)
    {
        return declaration.Declarators.Any(static d =>
            d.Initializer is not null &&
            AstShapeAnalyzer.ContainsYield(d.Initializer) &&
            !IsLowererTemp(d.Target));
    }

    private static bool IsLowererTemp(BindingTarget target)
    {
        return target is IdentifierBinding { Name.Name: not null } identifier &&
               identifier.Name.Name.StartsWith("__yield_lower_", StringComparison.Ordinal);
    }

    private static bool DeclarationContainsYieldInBindingTargetDefaults(VariableDeclaration declaration)
    {
        return declaration.Declarators.Any(static d =>
            BindingTargetContainsYieldInDefaultValue(d.Target) ||
            (d.Initializer is not null && ExpressionContainsDestructuringWithYieldAnywhere(d.Initializer)));
    }

    private static bool BindingTargetContainsYieldInDefaultValue(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                        return true;
                    if (element.Target is not null && BindingTargetContainsYieldInDefaultValue(element.Target))
                        return true;
                }
                if (arrayBinding.RestElement is not null &&
                    BindingTargetContainsYieldInDefaultValue(arrayBinding.RestElement))
                    return true;
                return false;

            case ObjectBinding objectBinding:
                foreach (var prop in objectBinding.Properties)
                {
                    if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                        return true;
                    if (BindingTargetContainsYieldInDefaultValue(prop.Target))
                        return true;
                }
                if (objectBinding.RestElement is not null &&
                    BindingTargetContainsYieldInDefaultValue(objectBinding.RestElement))
                    return true;
                return false;

            case AssignmentTargetBinding assignmentTarget:
                return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

            default:
                return false;
        }
    }

    private static bool ExpressionContainsDestructuringWithYieldAnywhere(ExpressionNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case DestructuringAssignmentExpression destructuringExpr:
                    if (BindingTargetContainsYieldAnywhere(destructuringExpr.Target))
                        return true;
                    expression = destructuringExpr.Value;
                    continue;
                case AssignmentExpression assignmentExpr:
                    expression = assignmentExpr.Value;
                    continue;
                case PropertyAssignmentExpression propAssignExpr:
                    expression = propAssignExpr.Value;
                    continue;
                case IndexAssignmentExpression indexAssignExpr:
                    expression = indexAssignExpr.Value;
                    continue;
                case ConditionalExpression conditionalExpr:
                    return ExpressionContainsDestructuringWithYieldAnywhere(conditionalExpr.Consequent) ||
                           ExpressionContainsDestructuringWithYieldAnywhere(conditionalExpr.Alternate);
                case SequenceExpression seqExpr:
                    return ExpressionContainsDestructuringWithYieldAnywhere(seqExpr.Left) ||
                           ExpressionContainsDestructuringWithYieldAnywhere(seqExpr.Right);
                default:
                    return false;
            }
        }
    }

    private static bool BindingTargetContainsYieldAnywhere(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                        return true;
                    if (element.Target is not null && BindingTargetContainsYieldAnywhere(element.Target))
                        return true;
                }
                if (arrayBinding.RestElement is not null &&
                    BindingTargetContainsYieldAnywhere(arrayBinding.RestElement))
                    return true;
                return false;

            case ObjectBinding objectBinding:
                foreach (var prop in objectBinding.Properties)
                {
                    if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                        return true;
                    if (prop.NameExpression is not null && AstShapeAnalyzer.ContainsYield(prop.NameExpression))
                        return true;
                    if (BindingTargetContainsYieldAnywhere(prop.Target))
                        return true;
                }
                if (objectBinding.RestElement is not null &&
                    BindingTargetContainsYieldAnywhere(objectBinding.RestElement))
                    return true;
                return false;

            case AssignmentTargetBinding assignmentTarget:
                return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

            default:
                return false;
        }
    }

    /// <summary>
    /// Try to emit IR for a simple variable declaration (identifier bindings only, no destructuring).
    /// </summary>
    private static bool TryEmitSimpleVariableDeclaration(
        EmitContext ctx,
        VariableDeclaration declaration,
        int nextIndex,
        out int entryIndex)
    {
        // Don't handle using/await using for now - they have complex disposal semantics
        if (declaration.Kind is VariableKind.Using or VariableKind.AwaitUsing)
        {
            entryIndex = -1;
            return false;
        }

        // First, verify ALL declarators are simple (identifier binding, no yields/awaits)
        foreach (var declarator in declaration.Declarators)
        {
            // Only handle simple identifier binding (no destructuring)
            if (declarator.Target is not IdentifierBinding)
            {
                entryIndex = -1;
                return false;
            }

            // Ensure no yields or awaits in initializer
            if (declarator.Initializer is not null &&
                (AstShapeAnalyzer.ContainsYield(declarator.Initializer) ||
                 AstShapeAnalyzer.ContainsAwait(declarator.Initializer)))
            {
                entryIndex = -1;
                return false;
            }
        }

        // All declarators are simple - build a chain of instructions
        // Work backwards from the last declarator to properly chain next pointers
        var currentNext = nextIndex;
        entryIndex = -1;

        for (var i = declaration.Declarators.Length - 1; i >= 0; i--)
        {
            var declarator = declaration.Declarators[i];
            var targetSymbol = ((IdentifierBinding)declarator.Target).Name;

            var instructionIndex = ctx.Append(new SimpleVariableDeclarationInstruction(
                currentNext,
                declaration.Kind,
                targetSymbol!,
                declarator.Initializer));

            currentNext = instructionIndex;
            if (i == 0)
            {
                entryIndex = instructionIndex;
            }
        }

        return true;
    }

    /// <summary>
    /// Emit IR for a throw statement.
    /// </summary>
    public static bool TryEmitThrow(
        EmitContext ctx,
        ThrowStatement throwStatement,
        out int entryIndex)
    {
        if (throwStatement.Expression is not null &&
            AstShapeAnalyzer.ContainsYield(throwStatement.Expression))
        {
            ctx.SetFailureReason("Throw expression contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        // Use native ThrowInstruction - it evaluates the expression and throws
        entryIndex = ctx.Append(new ThrowInstruction(throwStatement.Expression!));
        return true;
    }

    /// <summary>
    /// Emit IR for a return statement.
    /// </summary>
    public static bool TryEmitReturn(
        EmitContext ctx,
        ReturnStatement returnStatement,
        int nextIndex,
        out int entryIndex)
    {
        if (returnStatement.Expression is not null &&
            AstShapeAnalyzer.ContainsYield(returnStatement.Expression))
        {
            ctx.SetFailureReason("Return expression contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        // Pass nextIndex so that if return is inside try/finally, we can
        // continue to EndFinallyInstruction after updating pending completion.
        entryIndex = ctx.Append(new ReturnInstruction(nextIndex, returnStatement.Expression));
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────────────────

    private static bool ClassDefinitionContainsYield(ClassDefinition definition)
    {
        // Check extends clause
        if (definition.Extends is not null && AstShapeAnalyzer.ContainsYield(definition.Extends))
        {
            return true;
        }

        // Check computed property names in members (methods, getters, setters)
        foreach (var member in definition.Members)
        {
            if (member is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsYield(member.ComputedName))
            {
                return true;
            }
        }

        // Check computed property names in fields
        foreach (var field in definition.Fields)
        {
            if (field is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsYield(field.ComputedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ClassDefinitionContainsAwait(ClassDefinition definition)
    {
        // Check extends clause
        if (definition.Extends is not null && AstShapeAnalyzer.ContainsAwait(definition.Extends))
        {
            return true;
        }

        // Check computed property names in members (methods, getters, setters)
        foreach (var member in definition.Members)
        {
            if (member is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsAwait(member.ComputedName))
            {
                return true;
            }
        }

        // Check computed property names in fields
        foreach (var field in definition.Fields)
        {
            if (field is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsAwait(field.ComputedName))
            {
                return true;
            }
        }

        return false;
    }
}
