#region

using System.Text;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Pretty printer for IR execution plans. Outputs a human-readable representation
/// of the instruction list for debugging and reasoning about generated IR code.
/// </summary>
internal static class ExecutionPlanPrinter
{
    /// <summary>
    /// Pretty prints the execution plan to a string.
    /// </summary>
    public static string Print(IReadOnlyList<ExecutionInstruction> instructions, int entryIndex = 0)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Execution Plan ({instructions.Count} instructions, entry: {entryIndex}) ===");
        sb.AppendLine();

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            var prefix = i == entryIndex ? "→ " : "  ";
            sb.AppendLine($"{prefix}[{i,3}] {FormatInstruction(instruction)}");
        }

        sb.AppendLine();
        sb.AppendLine("=== End ===");
        return sb.ToString();
    }

    /// <summary>
    /// Pretty prints a single instruction.
    /// </summary>
    public static string FormatInstruction(ExecutionInstruction instruction)
    {
        return instruction switch
        {
            JumpInstruction jump =>
                $"JUMP → [{jump.TargetIndex}]",

            BranchInstruction branch =>
                $"BRANCH ({FormatExpression(branch.Condition)}) ? [{branch.ConsequentIndex}] : [{branch.AlternateIndex}]",

            ExpressionInstruction expr =>
                $"EVAL {FormatExpression(expr.Expression)} → [{expr.Next}]",

            EvaluateAndDiscardInstruction discard =>
                $"EVAL_DISCARD {FormatExpression(discard.Expression)} → [{discard.Next}]",

            StatementInstruction stmt =>
                $"STMT {FormatStatement(stmt.Statement)} → [{stmt.Next}]",

            SimpleVariableDeclarationInstruction varDecl =>
                $"VAR {varDecl.Kind} {varDecl.TargetSymbol.Name}" +
                (varDecl.Initializer != null ? $" = {FormatExpression(varDecl.Initializer)}" : "") +
                $" → [{varDecl.Next}]",

            PushEnvironmentInstruction pushEnv =>
                $"PUSH_ENV (bindings: [{string.Join(", ", pushEnv.PerIterationBindings.Select(s => s.Name))}], " +
                $"scopeId: {pushEnv.ScopeId}, slots: {pushEnv.SlotCount}, pool: {pushEnv.AllowPooling}) → [{pushEnv.Next}]",

            PopEnvironmentInstruction popEnv =>
                $"POP_ENV (scopeId: {popEnv.ScopeId}, pool: {popEnv.AllowPooling}) → [{popEnv.Next}]",

            ReturnInstruction ret =>
                $"RETURN" + (ret.ReturnExpression != null ? $" {FormatExpression(ret.ReturnExpression)}" : ""),

            ThrowInstruction thr =>
                $"THROW {FormatExpression(thr.Expression)}",

            BreakInstruction brk =>
                $"BREAK (popTo: {brk.TargetScopeId}) → [{brk.TargetIndex}]",

            ContinueInstruction cont =>
                $"CONTINUE (popTo: {cont.TargetScopeId}) → [{cont.TargetIndex}]",

            YieldInstruction yield =>
                $"YIELD" + (yield.YieldExpression != null ? $" {FormatExpression(yield.YieldExpression)}" : "") +
                $" → [{yield.Next}]",

            YieldStarInstruction yieldStar =>
                $"YIELD* {FormatExpression(yieldStar.IterableExpression)}" +
                (yieldStar.ResultSlotSymbol != null ? $" (result → {yieldStar.ResultSlotSymbol.Name})" : "") +
                $" → [{yieldStar.Next}]",

            EnterTryInstruction enterTry =>
                $"ENTER_TRY (handler: {(enterTry.HandlerIndex >= 0 ? $"[{enterTry.HandlerIndex}]" : "none")}, " +
                $"finally: {(enterTry.FinallyIndex >= 0 ? $"[{enterTry.FinallyIndex}]" : "none")}) → [{enterTry.Next}]",

            LeaveTryInstruction leaveTry =>
                $"LEAVE_TRY → [{leaveTry.Next}]",

            EndFinallyInstruction endFinally =>
                $"END_FINALLY → [{endFinally.Next}]",

            IteratorInitInstruction iterInit =>
                $"ITER_INIT {FormatExpression(iterInit.IterableExpression)} (slot: {iterInit.IteratorSlot.Name}, kind: {iterInit.Kind}) → [{iterInit.Next}]",

            IteratorMoveNextInstruction moveNext =>
                $"ITER_MOVE_NEXT (iter: {moveNext.IteratorSlot.Name}, value: {moveNext.ValueSlot.Name}) body: [{moveNext.Next}], done: [{moveNext.BreakIndex}]",

            IteratorCloseInstruction iterClose =>
                $"ITER_CLOSE (iter: {iterClose.IteratorSlot.Name}) → [{iterClose.Next}]",

            EnterWithInstruction enterWith =>
                $"ENTER_WITH {FormatExpression(enterWith.ObjectExpression)} → [{enterWith.Next}]",

            LeaveWithInstruction leaveWith =>
                $"LEAVE_WITH → [{leaveWith.Next}]",

            FunctionDeclarationInstruction funcDecl =>
                $"FUNC_DECL → [{funcDecl.Next}]",

            ClassDeclarationInstruction classDecl =>
                $"CLASS_DECL {classDecl.Declaration.Name.Name} → [{classDecl.Next}]",

            StoreResumeValueInstruction storeResume =>
                $"STORE_RESUME" + (storeResume.TargetSymbol != null ? $" → {storeResume.TargetSymbol.Name}" : "") +
                $" → [{storeResume.Next}]",

            _ => instruction.ToString() ?? "<?>"
        };
    }

    private static string FormatExpression(ExpressionNode expr)
    {
        return expr switch
        {
            IdentifierExpression id => id.Name.Name,
            LiteralExpression lit => lit.Value.ToString() ?? "null",
            BinaryExpression bin => $"({FormatExpression(bin.Left)} {bin.Operator} {FormatExpression(bin.Right)})",
            UnaryExpression unary => $"{unary.Operator}{FormatExpression(unary.Operand)}",
            AssignmentExpression assign => $"{assign.Target.Name} = {FormatExpression(assign.Value)}",
            CallExpression call => $"{FormatExpression(call.Callee)}({call.Arguments.Length} args)",
            MemberExpression member => $"{FormatExpression(member.Target)}.{FormatExpression(member.Property)}",
            ConditionalExpression cond => $"({FormatExpression(cond.Test)} ? ... : ...)",
            ArrayExpression arr => $"[{arr.Elements.Length} elements]",
            ObjectExpression => "{...}",
            FunctionExpression func => func.Name != null ? $"function {func.Name.Name}" : "function()",
            AwaitExpression awaitExpr => $"await {FormatExpression(awaitExpr.Expression)}",
            YieldExpression yield => yield.Expression != null ? $"yield {FormatExpression(yield.Expression)}" : "yield",
            NewExpression newExpr => $"new {FormatExpression(newExpr.Constructor)}()",
            ThisExpression => "this",
            SequenceExpression seq => $"({FormatExpression(seq.Left)}, {FormatExpression(seq.Right)})",
            TemplateLiteralExpression => "`template`",
            TaggedTemplateExpression tagged => $"{FormatExpression(tagged.Tag)}`...`",
            ClassExpression classExpr => classExpr.Name != null ? $"class {classExpr.Name.Name}" : "class",
            _ => expr.GetType().Name
        };
    }

    private static string FormatStatement(StatementNode stmt)
    {
        return stmt switch
        {
            BlockStatement block => $"{{ {block.Statements.Length} stmts }}",
            IfStatement => "if (...)",
            ForStatement => "for (...)",
            WhileStatement => "while (...)",
            DoWhileStatement => "do ... while (...)",
            ForEachStatement forEach => forEach.Kind switch
            {
                ForEachKind.Of => "for ... of",
                ForEachKind.In => "for ... in",
                ForEachKind.AwaitOf => "for await ... of",
                _ => "for-each"
            },
            TryStatement => "try { ... }",
            SwitchStatement => "switch (...)",
            LabeledStatement labeled => $"{labeled.Label.Name}: ...",
            ExpressionStatement exprStmt => FormatExpression(exprStmt.Expression),
            VariableDeclaration varDecl => $"{varDecl.Kind} ({varDecl.Declarators.Length} decls)",
            ReturnStatement => "return",
            ThrowStatement => "throw",
            BreakStatement brk => brk.Label != null ? $"break {brk.Label.Name}" : "break",
            ContinueStatement cont => cont.Label != null ? $"continue {cont.Label.Name}" : "continue",
            EmptyStatement => ";",
            WithStatement => "with (...)",
            _ => stmt.GetType().Name
        };
    }
}
