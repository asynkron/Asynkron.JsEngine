using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.JsTypes;

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

            // Reject case bodies that have break/continue inside finally blocks.
            // The switch is lowered to if statements, so break/continue in finally
            // would have no valid target. Fall back to StatementInstruction which
            // handles this via AST evaluation.
            if (EmitContext.ContainsUnlabeledAbruptInFinally(switchCase.Body))
            {
                ctx.SetFailureReason("Switch case contains break/continue in finally block.");
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

        var statements = ImmutableArray.CreateBuilder<StatementNode>();

        // const __discN = <discriminant>;
        var discBinding = new IdentifierBinding(statement.Source, discriminantSymbol);
        var discDeclarator = new VariableDeclarator(statement.Source, discBinding, statement.Discriminant);
        var discDeclaration = new VariableDeclaration(statement.Source, VariableKind.Const, [discDeclarator]);
        statements.Add(discDeclaration);

        // let __matchN = -1;
        var matchBinding = new IdentifierBinding(statement.Source, matchIndexSymbol);
        var matchInitializer = new LiteralExpression(statement.Source, -1);
        var matchDeclarator = new VariableDeclarator(statement.Source, matchBinding, matchInitializer);
        var matchDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [matchDeclarator]);
        statements.Add(matchDeclaration);

        // let __doneN = false;
        var doneBinding = new IdentifierBinding(statement.Source, doneSymbol);
        var doneInitializer = new LiteralExpression(statement.Source, false);
        var doneDeclarator = new VariableDeclarator(statement.Source, doneBinding, doneInitializer);
        var doneDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [doneDeclarator]);
        statements.Add(doneDeclaration);

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
            var setMatchStatement = new ExpressionStatement(statement.Source, setMatch);
            statements.Add(new IfStatement(statement.Source, combinedTest,
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
            var setDefaultStatement = new ExpressionStatement(statement.Source, setDefaultMatch);
            statements.Add(new IfStatement(statement.Source, stillUnmatched,
                new BlockStatement(statement.Source, [setDefaultStatement], statement.Cases[0].Body.IsStrict),
                null));
        }

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

            var execBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            var copyCount = breakIndex == -1 ? bodyStatements.Length : breakIndex;
            for (var i = 0; i < copyCount; i++)
            {
                execBuilder.Add(bodyStatements[i]);
            }

            if (breakIndex != -1)
            {
                var setDoneAssignment = new AssignmentExpression(statement.Source, doneSymbol,
                    new LiteralExpression(statement.Source, true));
                execBuilder.Add(new ExpressionStatement(statement.Source, setDoneAssignment));
            }

            var execBlock = new BlockStatement(body.Source, execBuilder.ToImmutable(), body.IsStrict);
            statements.Add(new IfStatement(statement.Source, execCondition, execBlock, null));
        }

        var isStrict = statement.Cases.Length > 0 && statement.Cases[0].Body.IsStrict;
        var lowered = new BlockStatement(statement.Source, statements.ToImmutable(), isStrict);

        // Create LoopExitInstruction first (we build bottom-up)
        // This pops the loop stack when exiting the switch (normal exit or break)
        var loopExitIndex = ctx.Append(new LoopExitInstruction(nextIndex));

        if (!ctx.TryBuildStatement(lowered, loopExitIndex, out var switchBodyEntry, activeLabel))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Wrap entry with LoopEnterInstruction to push loop context at runtime
        // This enables break statements from AST-evaluated code (via StatementInstruction)
        // to resolve their jump targets using the runtime loop stack.
        // ContinueTarget is -1 because switch statements do not support continue.
        entryIndex = ctx.Append(new LoopEnterInstruction(
            switchBodyEntry,
            activeLabel,
            loopExitIndex,
            -1));

        return true;
    }
}
