#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for switch statements.
/// Switch statements are lowered to if-else chains for evaluation.
/// </summary>
internal static class SwitchEmitter
{
    /// <summary>
    /// Emit IR for a switch statement.
    /// </summary>
    public static bool TryEmitSwitch(
        EmitContext ctx,
        SwitchStatement statement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        // For now we support switch statements whose discriminant and case
        // tests are yield-free, and whose case bodies only contain at most a
        // single trailing unlabeled `break;` at top level. More complex break
        // shapes (including non-trailing `break`) continue to be rejected.
        if (AstShapeAnalyzer.ContainsYield(statement.Discriminant))
        {
            entryIndex = -1;
            return false;
        }

        foreach (var switchCase in statement.Cases)
        {
            if (switchCase.Test is not null && AstShapeAnalyzer.ContainsYield(switchCase.Test))
            {
                entryIndex = -1;
                return false;
            }
        }

        // Enforce at most a single default clause. JavaScript evaluates switch
        // by first selecting the matching case clause (preferring explicit case
        // tests and only using default if no case matches) and then executing
        // the case body with fallthrough. The default clause can appear in any
        // position; execution begins at the selected clause and falls through
        // to later clauses until a break is hit or the switch ends.
        var defaultIndex = -1;
        for (var i = 0; i < statement.Cases.Length; i++)
        {
            if (statement.Cases[i].Test is null)
            {
                if (defaultIndex != -1)
                {
                    ctx.SetFailureReason("Switch statement contains multiple default clauses.");
                    entryIndex = -1;
                    return false;
                }

                defaultIndex = i;
            }
        }

        var instructionStart = ctx.InstructionCount;
        var discriminantSymbol = Symbol.Intern($"__switch_disc_{instructionStart}");
        var matchIndexSymbol = Symbol.Intern($"__switch_match_{instructionStart}");
        var doneSymbol = Symbol.Intern($"__switch_done_{instructionStart}");

        // Outer block statements (discriminant, match vars - these capture outer scope)
        var outerStatements = ImmutableArray.CreateBuilder<StatementNode>();

        // const __discN = <discriminant>;
        var discBinding = new IdentifierBinding(statement.Source, discriminantSymbol);
        var discDeclarator = new VariableDeclarator(statement.Source, discBinding, statement.Discriminant);
        var discDeclaration = new VariableDeclaration(statement.Source, VariableKind.Const, [discDeclarator]);
        outerStatements.Add(discDeclaration);

        // let __matchN = -1;
        var matchBinding = new IdentifierBinding(statement.Source, matchIndexSymbol);
        var matchInitializer = new LiteralExpression(statement.Source, -1);
        var matchDeclarator = new VariableDeclarator(statement.Source, matchBinding, matchInitializer);
        var matchDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [matchDeclarator]);
        outerStatements.Add(matchDeclaration);

        // let __doneN = false;
        var doneBinding = new IdentifierBinding(statement.Source, doneSymbol);
        var doneInitializer = new LiteralExpression(statement.Source, false);
        var doneDeclarator = new VariableDeclarator(statement.Source, doneBinding, doneInitializer);
        var doneDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [doneDeclarator]);
        outerStatements.Add(doneDeclaration);

        // Inner block statements (switch scope - all case bodies share this)
        var innerStatements = ImmutableArray.CreateBuilder<StatementNode>();

        // Per ES spec 13.12.9: All case bodies share ONE lexical scope.
        // Collect and hoist all let/const declarations from ALL case bodies.
        var hoistedDeclarators = ImmutableArray.CreateBuilder<VariableDeclarator>();
        var hoistedSymbols = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var switchCase in statement.Cases)
        {
            foreach (var stmt in switchCase.Body.Statements)
            {
                if (stmt is VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } varDecl)
                {
                    foreach (var declarator in varDecl.Declarators)
                    {
                        CollectBindingSymbols(declarator.Target, hoistedSymbols, hoistedDeclarators, statement.Source);
                    }
                }
            }
        }

        // Add hoisted declarations (without initializers) at start of inner block
        if (hoistedDeclarators.Count > 0)
        {
            var hoistedDecl =
                new VariableDeclaration(statement.Source, VariableKind.Let, hoistedDeclarators.ToImmutable());
            innerStatements.Add(hoistedDecl);
        }

        // Matching phase: set __matchN to the first matching case index.
        for (var i = 0; i < statement.Cases.Length; i++)
        {
            var switchCase = statement.Cases[i];
            if (switchCase.Test is null)
            {
                continue;
            }

            var matchUnset = new BinaryExpression(statement.Source, BinaryOperator.StrictEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var discIdentifier = new IdentifierExpression(statement.Source, discriminantSymbol);
            var equalTest = new BinaryExpression(statement.Source, BinaryOperator.StrictEqual,
                discIdentifier, switchCase.Test);
            var combinedTest = new BinaryExpression(statement.Source, BinaryOperator.LogicalAnd, matchUnset, equalTest);

            var setMatch = new AssignmentExpression(statement.Source, matchIndexSymbol,
                new LiteralExpression(statement.Source, i));
            // SuppressCompletionValue: matching phase assignments shouldn't affect switch completion
            var setMatchStatement = new ExpressionStatement(statement.Source, setMatch, true);
            innerStatements.Add(new IfStatement(statement.Source, combinedTest,
                new BlockStatement(statement.Source, [setMatchStatement], statement.Cases[0].Body.IsStrict),
                null));
        }

        // If still unmatched, fall back to default (if any).
        if (defaultIndex != -1)
        {
            var stillUnmatched = new BinaryExpression(statement.Source, BinaryOperator.StrictEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var setDefaultMatch = new AssignmentExpression(statement.Source, matchIndexSymbol,
                new LiteralExpression(statement.Source, defaultIndex));
            // SuppressCompletionValue: matching phase assignments shouldn't affect switch completion
            var setDefaultStatement = new ExpressionStatement(statement.Source, setDefaultMatch, true);
            innerStatements.Add(new IfStatement(statement.Source, stillUnmatched,
                new BlockStatement(statement.Source, [setDefaultStatement], statement.Cases[0].Body.IsStrict),
                null));
        }

        // Per ES spec 13.12.9 (CaseBlockEvaluation), step 1: "Let V = undefined."
        // We need to set the initial completion value to undefined before executing case bodies.
        // This ensures that if no case body produces a value (all empty), the switch completes with undefined.
        // We use an expression statement that evaluates to undefined.
        var initCompletionExpr = new IdentifierExpression(statement.Source, Symbol.Undefined);
        innerStatements.Add(new ExpressionStatement(statement.Source, initCompletionExpr));

        for (var caseIndex = 0; caseIndex < statement.Cases.Length; caseIndex++)
        {
            var switchCase = statement.Cases[caseIndex];
            var body = switchCase.Body;
            var bodyStatements = body.Statements;

            var breakIndex = -1;
            for (var i = 0; i < bodyStatements.Length; i++)
            {
                if (bodyStatements[i] is BreakStatement breakStatement)
                {
                    if (breakStatement.Label is not null &&
                        (activeLabel is null || !ReferenceEquals(activeLabel, breakStatement.Label)))
                    {
                        ctx.Rollback(instructionStart);
                        entryIndex = -1;
                        return false;
                    }

                    breakIndex = breakIndex == -1 ? i : breakIndex;
                }
            }

            // Execution guard: if (!__done && __matchN != -1 && __matchN <= caseIndex) { ...body... }
            var notDoneExec = new UnaryExpression(statement.Source, UnaryOperator.LogicalNot,
                new IdentifierExpression(statement.Source, doneSymbol), true);
            var matchSet = new BinaryExpression(statement.Source, BinaryOperator.StrictNotEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var matchReached = new BinaryExpression(statement.Source, BinaryOperator.LessThanOrEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, caseIndex));
            var matchGuard = new BinaryExpression(statement.Source, BinaryOperator.LogicalAnd, matchSet, matchReached);
            var execCondition =
                new BinaryExpression(statement.Source, BinaryOperator.LogicalAnd, notDoneExec, matchGuard);

            var copyCount = breakIndex == -1 ? bodyStatements.Length : breakIndex;

            // Per ES spec 13.12.9 (CaseBlockEvaluation), empty case clauses should NOT
            // produce a completion value that overwrites the previous completion value.
            // Skip generating the if statement for truly empty case bodies.
            if (copyCount == 0 && breakIndex == -1)
            {
                continue;
            }

            var execBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            for (var i = 0; i < copyCount; i++)
            {
                var stmt = bodyStatements[i];
                // Transform let/const declarations with initializers into assignments
                // (the declaration is hoisted to start of switch scope)
                if (stmt is VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } varDecl)
                {
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null && declarator.Target is IdentifierBinding id)
                        {
                            var assign = new AssignmentExpression(stmt.Source, id.Name, declarator.Initializer);
                            execBuilder.Add(new ExpressionStatement(stmt.Source, assign));
                        }
                    }
                }
                else
                {
                    execBuilder.Add(stmt);
                }
            }

            if (breakIndex != -1)
            {
                // Mark as SuppressCompletionValue because __done is a synthetic variable -
                // its assignment should never affect the script's completion value.
                var setDoneAssignment = new AssignmentExpression(statement.Source, doneSymbol,
                    new LiteralExpression(statement.Source, true));
                execBuilder.Add(new ExpressionStatement(statement.Source, setDoneAssignment, true));
            }

            // Case bodies do NOT get their own block scope - but we need to wrap ALL statements
            // from this case in a SINGLE guard to preserve completion values during fallthrough.
            // We cannot use IfStatement here because TryEmitIf adds SetCompletionValue instructions
            // that reset the completion value, breaking fallthrough behavior.
            // Instead, we lower to a synthetic if-like structure ourselves.
            if (execBuilder.Count > 0)
            {
                var caseBlock = new BlockStatement(body.Source, execBuilder.ToImmutable(), body.IsStrict);
                innerStatements.Add(new IfStatement(statement.Source, execCondition, caseBlock, null));
            }
        }

        // Create inner block (switch scope) containing all case body statements
        var isStrict = statement.Cases.Length > 0 && statement.Cases[0].Body.IsStrict;
        var innerBlock = new BlockStatement(statement.Source, innerStatements.ToImmutable(), isStrict);
        outerStatements.Add(innerBlock);

        // Outer block contains discriminant setup + inner block
        var lowered = new BlockStatement(statement.Source, outerStatements.ToImmutable(), isStrict);

        // Create BreakableExitInstruction first (we build bottom-up)
        // This pops the breakable stack when exiting the switch (normal exit or break)
        var breakableExitIndex = ctx.Append(new BreakableExitInstruction(nextIndex));

        // Push a loop scope so break statements in nested blocks (e.g., try/catch inside
        // switch cases) can resolve their targets during IR building. ContinueTarget is -1
        // because switch statements do not support continue.
        ctx.PushLoopScope(activeLabel, -1, breakableExitIndex, -1);
        var bodyBuilt = ctx.TryBuildStatement(lowered, breakableExitIndex, out var switchBodyEntry, activeLabel);
        ctx.PopLoopScope();

        if (!bodyBuilt)
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Wrap entry with BreakableEnterInstruction to push context at runtime.
        // This enables break statements from AST-evaluated code (via StatementInstruction)
        // to resolve their jump targets using the runtime breakable stack.
        // ContinueTarget is -1 because switch statements do not support continue.
        // Use HandlesCompletionInternally because switch handles it with explicit undefined statement above.
        entryIndex = ctx.Append(new BreakableEnterInstruction(
            switchBodyEntry,
            activeLabel,
            breakableExitIndex,
            -1,
            BreakableKind.HandlesCompletionInternally));

        return true;
    }

    /// <summary>
    /// Collects binding symbols from a binding target for switch scope hoisting.
    /// Creates uninitialized declarators for each identifier.
    /// </summary>
    private static void CollectBindingSymbols(BindingTarget target, HashSet<Symbol> seenSymbols, ImmutableArray<VariableDeclarator>.Builder declarators, SourceReference? source)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (seenSymbols.Add(id.Name))
                    {
                        // Create declarator without initializer (TDZ until assignment)
                        declarators.Add(new VariableDeclarator(source, id, null));
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingSymbols(element.Target, seenSymbols, declarators, source);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingSymbols(property.Target, seenSymbols, declarators, source);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
            }

            break;
        }
    }
}
