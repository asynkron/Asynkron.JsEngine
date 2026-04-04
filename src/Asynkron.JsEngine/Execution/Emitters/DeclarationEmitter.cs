#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

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
        // Hoisted function-scoped declarations don't need the declaration at runtime
        return ctx.Append(new FunctionDeclarationInstruction(nextIndex, null));
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

        // NOTE: Await expressions in class definitions (extends clause, computed property names)
        // are handled by ClassDeclarationInstruction via normal expression evaluation.
        // The IR runner's TryHandlePendingAwait handles async suspension/resumption.

        // Use native ClassDeclarationInstruction - handles both sync and async cases
        entryIndex = ctx.Append(new ClassDeclarationInstruction(
            nextIndex,
            new ClassDeclarationDescriptor(classDeclaration.Name, classDeclaration.Definition)));
        return true;
    }

    /// <summary>
    /// Try to emit IR for a variable declaration.
    /// Lowers each declarator to either a specialized instruction or a generic binding declaration instruction.
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

        entryIndex = -1;
        var currentNext = nextIndex;

        for (var i = declaration.Declarators.Length - 1; i >= 0; i--)
        {
            if (!TryEmitVariableDeclarator(ctx, declaration.Kind, declaration.Declarators[i], currentNext,
                    out var declaratorEntry))
            {
                entryIndex = -1;
                return false;
            }

            currentNext = declaratorEntry;
            if (i == 0)
            {
                entryIndex = declaratorEntry;
            }
        }

        return true;
    }

    /// <summary>
    /// Try to emit IR for a single declarator.
    /// </summary>
    private static bool TryEmitVariableDeclarator(
        EmitContext ctx,
        VariableKind varKind,
        VariableDeclarator declarator,
        int nextIndex,
        out int entryIndex)
    {
        if (TryEmitSimpleVariableDeclarator(ctx, varKind, declarator, nextIndex, out entryIndex))
        {
            return true;
        }

        if (TryEmitArrayDestructuringDeclarator(ctx, varKind, declarator, nextIndex, out entryIndex))
        {
            return true;
        }

        if (!BindingTargetProgramCompiler.TryCompile(
                declarator.Target,
                out var targetProgram,
                out var failureReason))
        {
            ctx.SetFailureReason(
                $"Binding variable declaration target '{declarator.Target.GetType().Name}' could not lower to a binding program: {failureReason ?? "unknown reason"}.",
                ExecutionPlanFailureCode.UnsupportedBindingProgram);
            entryIndex = -1;
            return false;
        }

        if (!TryCompileInitializerProgram(
                ctx,
                "BindingVariableDeclarationInstruction",
                declarator.Initializer,
                out var bindingInitializerProgram))
        {
            entryIndex = -1;
            return false;
        }

        if (declarator.Initializer is null)
        {
            entryIndex = ctx.Append(new BindingVariableDeclarationInstruction(
                nextIndex,
                varKind,
                targetProgram));
            return true;
        }

        if (declarator.Initializer is AwaitExpression bindingAwait)
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    bindingAwait.Expression,
                    out var awaitedProgram,
                    out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure(
                    "BindingVariableDeclarationInstruction",
                    bindingAwait.Expression,
                    awaitFailure);
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.Append(new BindingVariableDeclarationInstruction(
                nextIndex,
                varKind,
                targetProgram,
                AwaitStateKey: ((IAstCacheable<Symbol>)bindingAwait).GetOrCreateCache(),
                AwaitedProgram: awaitedProgram));
            return true;
        }

        if (bindingInitializerProgram is not null)
        {
            entryIndex = ctx.Append(new BindingVariableDeclarationInstruction(
                nextIndex,
                varKind,
                targetProgram,
                bindingInitializerProgram));
            return true;
        }

        if (AstShapeAnalyzer.ContainsAwait(declarator.Initializer) ||
            AstShapeAnalyzer.ContainsYield(declarator.Initializer))
        {
            entryIndex = ctx.Append(new SuspendingBindingVariableDeclarationInstruction(
                nextIndex,
                varKind,
                targetProgram,
                declarator.Initializer));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to emit IR for array destructuring declarations.
    /// Only handles simple array binding with no yields or awaits in the source expression.
    /// </summary>
    private static bool TryEmitArrayDestructuringDeclarator(
        EmitContext ctx,
        VariableKind varKind,
        VariableDeclarator declarator,
        int nextIndex,
        out int entryIndex)
    {
        entryIndex = -1;

        if (declarator.Target is not ArrayBinding arrayBinding)
        {
            return false;
        }

        if (declarator.Initializer is null)
        {
            return false;
        }

        if (varKind is VariableKind.Using or VariableKind.AwaitUsing)
        {
            return false;
        }

        if (AstShapeAnalyzer.ContainsAwait(declarator.Initializer) ||
            AstShapeAnalyzer.ContainsYield(declarator.Initializer))
        {
            return false;
        }

        return DestructuringEmitter.TryEmitArrayDestructuring(
            ctx, arrayBinding, declarator.Initializer, varKind, nextIndex, out entryIndex);
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

        if (!EmitContext.IsLowererTemp(targetSymbol))
        {
            return false;
        }

        return YieldEmitter.TryEmitYieldToSymbol(
            ctx, targetSymbol, yieldInitializer, nextIndex, out entryIndex);
    }

    /// <summary>
    /// Try to emit IR for a simple variable declarator (identifier binding only, no yield/await).
    /// </summary>
    private static bool TryEmitSimpleVariableDeclarator(
        EmitContext ctx,
        VariableKind varKind,
        VariableDeclarator declarator,
        int nextIndex,
        out int entryIndex)
    {
        if (varKind is VariableKind.Using or VariableKind.AwaitUsing)
        {
            entryIndex = -1;
            return false;
        }

        if (declarator.Target is not IdentifierBinding identifierBinding)
        {
            entryIndex = -1;
            return false;
        }

        var isScriptLevel = ctx.IsScriptLevel && varKind == VariableKind.Var;
        var allowNameInference = IsAnonymousFunctionDefinition(declarator.Initializer);
        if (declarator.Initializer is AwaitExpression awaitInitializer)
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    awaitInitializer.Expression,
                    out var awaitedProgram,
                    out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure(
                    "SimpleVariableDeclarationInstruction",
                    awaitInitializer.Expression,
                    awaitFailure);
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.Append(new SimpleVariableDeclarationInstruction(
                nextIndex,
                varKind,
                identifierBinding.Name,
                AwaitStateKey: ((IAstCacheable<Symbol>)awaitInitializer).GetOrCreateCache(),
                AwaitedProgram: awaitedProgram,
                AllowNameInference: allowNameInference,
                IsScriptLevel: isScriptLevel));
            return true;
        }

        if (declarator.Initializer is not null &&
            (AstShapeAnalyzer.ContainsYield(declarator.Initializer) ||
             AstShapeAnalyzer.ContainsAwait(declarator.Initializer)))
        {
            entryIndex = ctx.Append(new SuspendingSimpleVariableDeclarationInstruction(
                nextIndex,
                varKind,
                identifierBinding.Name,
                declarator.Initializer,
                allowNameInference,
                isScriptLevel));
            return true;
        }

        if (!TryCompileInitializerProgram(
                ctx,
                "SimpleVariableDeclarationInstruction",
                declarator.Initializer,
                out var initializerProgram))
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.Append(new SimpleVariableDeclarationInstruction(
            nextIndex,
            varKind,
            identifierBinding.Name,
            InitializerProgram: initializerProgram,
            AllowNameInference: allowNameInference,
            IsScriptLevel: isScriptLevel));
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
        if (throwStatement.Expression is AwaitExpression awaitExpression)
        {
            if (!ExpressionProgramCompiler.TryCompile(awaitExpression.Expression, out var awaitedProgram, out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure("ThrowInstruction", awaitExpression.Expression, awaitFailure);
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.Append(new ThrowInstruction(
                AwaitStateKey: ((IAstCacheable<Symbol>)awaitExpression).GetOrCreateCache(),
                AwaitedProgram: awaitedProgram));
            return true;
        }

        if (AstShapeAnalyzer.ContainsYield(throwStatement.Expression))
        {
            ctx.SetFailureReason("Throw expression contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        if (!ExpressionProgramCompiler.TryCompile(throwStatement.Expression!, out var throwProgram, out var throwFailure))
        {
            ctx.SetExpressionProgramFailure("ThrowInstruction", throwStatement.Expression!, throwFailure);
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.Append(new ThrowInstruction(ThrowProgram: throwProgram));
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

        if (returnStatement.Expression is AwaitExpression awaitExpression)
        {
            if (!ExpressionProgramCompiler.TryCompile(awaitExpression.Expression, out var awaitedProgram, out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure("ReturnInstruction", awaitExpression.Expression, awaitFailure);
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.Append(new ReturnInstruction(
                nextIndex,
                AwaitStateKey: ((IAstCacheable<Symbol>)awaitExpression).GetOrCreateCache(),
                AwaitedProgram: awaitedProgram));
            return true;
        }

        ExpressionProgram? returnProgram = null;
        if (returnStatement.Expression is not null)
        {
            if (!ExpressionProgramCompiler.TryCompile(returnStatement.Expression, out var compiledReturnProgram, out var returnFailure))
            {
                ctx.SetExpressionProgramFailure("ReturnInstruction", returnStatement.Expression, returnFailure);
                entryIndex = -1;
                return false;
            }

            returnProgram = compiledReturnProgram;
        }

        // Pass nextIndex so that if return is inside try/finally, we can
        // continue to EndFinallyInstruction after updating pending completion.
        entryIndex = ctx.Append(new ReturnInstruction(nextIndex, returnProgram));
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────────────────

    private static bool ClassDefinitionContainsYield(ClassDefinition definition)
    {
        return ClassDefinitionContains(definition, static e => AstShapeAnalyzer.ContainsYield(e));
    }

    private static bool ClassDefinitionContains(ClassDefinition definition, Func<ExpressionNode, bool> predicate)
    {
        // Check extends clause
        if (definition.Extends is not null && predicate(definition.Extends))
        {
            return true;
        }

        // Check computed property names in members (methods, getters, setters)
        foreach (var member in definition.Members)
        {
            if (member is { IsComputed: true, ComputedName: not null } &&
                predicate(member.ComputedName))
            {
                return true;
            }
        }

        // Check computed property names in fields
        foreach (var field in definition.Fields)
        {
            if (field is { IsComputed: true, ComputedName: not null } &&
                predicate(field.ComputedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAnonymousFunctionDefinition(ExpressionNode? expression)
    {
        if (expression is null or SequenceExpression)
        {
            return false;
        }

        return expression is FunctionExpression { Name: null } or ClassExpression { Name: null };
    }

    private static bool TryCompileInitializerProgram(
        EmitContext ctx,
        string instructionName,
        ExpressionNode? initializer,
        out ExpressionProgram? initializerProgram)
    {
        initializerProgram = null;
        if (initializer is null)
        {
            return true;
        }

        if (AstShapeAnalyzer.ContainsAwait(initializer) || AstShapeAnalyzer.ContainsYield(initializer))
        {
            return true;
        }

        if (!ExpressionProgramCompiler.TryCompile(initializer, out var compiledProgram, out var failureReason))
        {
            ctx.SetExpressionProgramFailure(instructionName, initializer, failureReason);
            return false;
        }

        initializerProgram = compiledProgram;
        return true;
    }
}
