#region

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Microsoft.Extensions.Logging;

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
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"=== Execution Plan ({instructions.Count} instructions, entry: {entryIndex}) ===");
        sb.AppendLine();

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            var prefix = i == entryIndex ? "→ " : "  ";
            sb.AppendLine(CultureInfo.InvariantCulture, $"{prefix}[{i,3}] {FormatInstruction(instruction)}");
        }

        sb.AppendLine();
        sb.AppendLine("=== End ===");
        return sb.ToString();
    }

    /// <summary>
    /// Prints a single instruction during execution trace with environment depth indentation.
    /// Only compiled when TRACE_IR_EXECUTION is defined.
    /// </summary>
    [Conditional("TRACE_IR_EXECUTION")]
    public static void TraceInstruction(
        ILogger? logger,
        int instructionIndex,
        ExecutionInstruction instruction,
        int envDepth,
        int envScopeId,
        int envHashCode,
        string? extraInfo = null
    )
    {
        if (logger is null)
        {
            return;
        }

        var indent = new string(' ', envDepth * 2);

        var envInfo = $"[env:{envHashCode:X8} scope:{envScopeId,2} d:{envDepth}]";

        var formatted = FormatInstruction(instruction);
        var extra = extraInfo != null ? $" // {extraInfo}" : "";

        logger.LogDebug("{Indent}[{InstructionIndex}] {Formatted} {EnvInfo}{Extra}", indent, instructionIndex,
            formatted, envInfo, extra);
    }

    /// <summary>
    /// Traces a variable definition with environment info.
    /// Only compiled when TRACE_IR_EXECUTION is defined.
    /// </summary>
    [Conditional("TRACE_IR_EXECUTION")]
    public static void TraceDefine(ILogger? logger, string kind, string name, string value, int envDepth, int envScopeId,
        int envHashCode)
    {
        if (logger is null)
        {
            return;
        }

        var indent = new string(' ', envDepth * 2);
        var depthMarker = envDepth > 0 ? $"│{"".PadLeft(envDepth, '·')}" : "";
        logger.LogDebug(
            "{Indent}{DepthMarker}     DEFINE {Kind} '{Name}' = {Value} [env:{EnvHashCode:X8} scope:{EnvScopeId}]",
            indent, depthMarker, kind, name, value, envHashCode, envScopeId);
    }

    /// <summary>
    /// Traces a variable lookup with environment info.
    /// Only compiled when TRACE_IR_EXECUTION is defined.
    /// </summary>
    [Conditional("TRACE_IR_EXECUTION")]
    public static void TraceLookup(ILogger? logger, string name, bool found, int envDepth, int envScopeId,
        int envHashCode, string? foundIn = null)
    {
        if (logger is null)
        {
            return;
        }

        var indent = new string(' ', envDepth * 2);
        var status = found ? $"FOUND in {foundIn}" : "NOT FOUND";
        logger.LogDebug("{Indent}     LOOKUP '{Name}' -> {Status} [env:{EnvHashCode:X8} scope:{EnvScopeId}]", indent,
            name, status, envHashCode, envScopeId);
    }

    /// <summary>
    /// Traces environment push/pop operations.
    /// Only compiled when TRACE_IR_EXECUTION is defined.
    /// </summary>
    [Conditional("TRACE_IR_EXECUTION")]
    public static void TraceEnvChange(string operation, int oldDepth, int newDepth, int oldScopeId, int newScopeId,
        int oldHash, int newHash)
    {
        var indent = new string(' ', Math.Min(oldDepth, newDepth) * 2);
        Console.WriteLine(
            $"{indent}>>> {operation}: depth {oldDepth}->{newDepth}, scope {oldScopeId}->{newScopeId}, env {oldHash:X8}->{newHash:X8}");
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
                $"BRANCH ({FormatExpression(null, branch.ConditionProgram)}) ? [{branch.ConsequentIndex}] : [{branch.AlternateIndex}]",

            EvaluateAndDiscardInstruction discard =>
                $"EVAL_DISCARD {FormatExpression(discard.Expression, discard.ExpressionProgram)} → [{discard.Next}]",

            AwaitAndDiscardInstruction awaitDiscard =>
                $"AWAIT_DISCARD {FormatExpression(null, awaitDiscard.AwaitedProgram)} → [{awaitDiscard.Next}]",

            AssignmentSlotInstruction assign =>
                $"ASSIGN {assign.TargetSymbol.Name} = {FormatExpression(assign.ValueExpression, assign.ValueProgram)} → [{assign.Next}]",

            LogicalCompoundAssignmentSlotInstruction logicalCompound =>
                $"ASSIGN_LOGICAL {logicalCompound.TargetSymbol.Name} {FormatBinaryOperator(logicalCompound.Operator)}= {FormatExpression(logicalCompound.RhsExpression, logicalCompound.RhsProgram)} → [{logicalCompound.Next}]",

            SimpleVariableDeclarationInstruction varDecl =>
                $"VAR {varDecl.Kind} {varDecl.TargetSymbol.Name}" +
                (varDecl.Initializer != null || varDecl.InitializerProgram is not null
                    ? $" = {FormatExpression(varDecl.Initializer, varDecl.InitializerProgram)}"
                    : "") +
                $" → [{varDecl.Next}]",

            BindingVariableDeclarationInstruction bindingDecl =>
                $"VAR_BIND {bindingDecl.VarKind} {FormatBindingTarget(bindingDecl.TargetProgram)}" +
                (bindingDecl.Initializer != null || bindingDecl.InitializerProgram is not null
                    ? $" = {FormatExpression(bindingDecl.Initializer, bindingDecl.InitializerProgram)}"
                    : "") +
                $" → [{bindingDecl.Next}]",

            PushEnvironmentInstruction pushEnv =>
                $"PUSH_ENV (bindings: [{string.Join(", ", pushEnv.PerIterationBindings.Select(s => s.Name))}], " +
                $"scopeId: {pushEnv.ScopeId}, slots: {pushEnv.SlotCount}, pool: {pushEnv.AllowPooling}) → [{pushEnv.Next}]",

            PopEnvironmentInstruction popEnv =>
                $"POP_ENV (scopeId: {popEnv.ScopeId}, pool: {popEnv.AllowPooling}) → [{popEnv.Next}]",

            ReturnInstruction ret =>
                "RETURN" + (ret.AwaitedProgram is not null
                    ? $" await {FormatExpression(null, ret.AwaitedProgram)}"
                    : ret.ReturnProgram is not null
                        ? $" {FormatExpression(null, ret.ReturnProgram)}"
                        : "") +
                (ret.Next >= 0 ? $" → [{ret.Next}]" : ""),

            ThrowInstruction thr =>
                $"THROW {FormatExpression(null, thr.ThrowProgram)}",

            BreakInstruction brk =>
                $"BREAK (popTo: {brk.TargetScopeId}) → [{brk.TargetIndex}]",

            ContinueInstruction cont =>
                $"CONTINUE (popTo: {cont.TargetScopeId}) → [{cont.TargetIndex}]",

            YieldInstruction yield =>
                "YIELD" + (yield.AwaitedProgram is not null
                    ? $" await {FormatExpression(null, yield.AwaitedProgram)}"
                    : yield.YieldExpression != null || yield.YieldProgram is not null
                        ? $" {FormatExpression(yield.YieldExpression, yield.YieldProgram)}"
                        : "")
                + $" → [{yield.Next}]",

            YieldStarInstruction yieldStar =>
                "YIELD* " + (yieldStar.AwaitedProgram is not null
                    ? $"await {FormatExpression(null, yieldStar.AwaitedProgram)}"
                    : FormatExpression(null, yieldStar.IterableProgram))
                + (yieldStar.ResultSlotSymbol != null ? $" (result → {yieldStar.ResultSlotSymbol.Name})" : "")
                + $" → [{yieldStar.Next}]",

            SuspendingYieldStarInstruction suspendingYieldStar =>
                "YIELD* " + FormatExpression(suspendingYieldStar.IterableExpression, null)
                + (suspendingYieldStar.ResultSlotSymbol != null ? $" (result → {suspendingYieldStar.ResultSlotSymbol.Name})" : "")
                + $" → [{suspendingYieldStar.Next}]",

            EnterTryInstruction enterTry =>
                $"ENTER_TRY (handler: {(enterTry.HandlerIndex >= 0 ? $"[{enterTry.HandlerIndex}]" : "none")}, " +
                $"finally: {(enterTry.FinallyIndex >= 0 ? $"[{enterTry.FinallyIndex}]" : "none")}) → [{enterTry.Next}]",

            LeaveTryInstruction leaveTry =>
                $"LEAVE_TRY → [{leaveTry.Next}]",

            EndFinallyInstruction endFinally =>
                $"END_FINALLY → [{endFinally.Next}]",

            IteratorInitInstruction iterInit =>
                $"ITER_INIT {FormatExpression(null, iterInit.IterableProgram)} (slot: {iterInit.IteratorSlot.Name}, kind: {iterInit.Kind}) → [{iterInit.Next}]",

            SuspendingIteratorInitInstruction suspendingIterInit =>
                $"ITER_INIT {FormatExpression(suspendingIterInit.IterableExpression, null)} (slot: {suspendingIterInit.IteratorSlot.Name}, kind: {suspendingIterInit.Kind}) → [{suspendingIterInit.Next}]",

            IteratorMoveNextInstruction moveNext =>
                $"ITER_MOVE_NEXT (iter: {moveNext.IteratorSlot.Name}, value: {moveNext.ValueSlot.Name}) body: [{moveNext.Next}], done: [{moveNext.BreakIndex}]",

            IteratorCloseInstruction iterClose =>
                $"ITER_CLOSE (iter: {iterClose.IteratorSlot.Name}) → [{iterClose.Next}]",

            ForInInitInstruction forInInit =>
                $"FORIN_INIT {FormatExpression(null, forInInit.ObjectProgram)} (state: {forInInit.StateSlot.Name}, value: {forInInit.ValueSlot.Name}) → [{forInInit.Next}]",

            SuspendingForInInitInstruction suspendingForInInit =>
                $"FORIN_INIT {FormatExpression(suspendingForInInit.ObjectExpression, null)} (state: {suspendingForInInit.StateSlot.Name}, value: {suspendingForInInit.ValueSlot.Name}) → [{suspendingForInInit.Next}]",

            ForInMoveNextInstruction forInMoveNext =>
                $"FORIN_MOVE_NEXT (state: {forInMoveNext.StateSlot.Name}, value: {forInMoveNext.ValueSlot.Name}) body: [{forInMoveNext.Next}], done: [{forInMoveNext.BreakIndex}]",

            ArrayDestructuringInitInstruction arrayDestructuringInit =>
                $"ARRAY_DESTRUCT_INIT {FormatExpression(arrayDestructuringInit.SourceExpression, arrayDestructuringInit.SourceProgram)} (iter: {arrayDestructuringInit.IteratorSlot.Name}) → [{arrayDestructuringInit.Next}]",

            EnterWithInstruction enterWith =>
                $"ENTER_WITH {FormatExpression(null, enterWith.ObjectProgram)} → [{enterWith.Next}]",

            SuspendingEnterWithInstruction suspendingEnterWith =>
                $"ENTER_WITH {FormatExpression(suspendingEnterWith.ObjectExpression, null)} → [{suspendingEnterWith.Next}]",

            LeaveWithInstruction leaveWith =>
                $"LEAVE_WITH → [{leaveWith.Next}]",

            FunctionDeclarationInstruction funcDecl =>
                funcDecl.Descriptor is null
                    ? $"FUNC_DECL (hoisted noop) → [{funcDecl.Next}]"
                    : $"FUNC_DECL {funcDecl.Descriptor.Value.Name.Name} → [{funcDecl.Next}]",

            ClassDeclarationInstruction classDecl =>
                $"CLASS_DECL {classDecl.Descriptor.Name.Name} → [{classDecl.Next}]",

            StoreResumeValueInstruction storeResume =>
                "STORE_RESUME" + (storeResume.TargetSymbol != null ? $" → {storeResume.TargetSymbol.Name}" : "") +
                $" → [{storeResume.Next}]",

            IncrementSlotInstruction inc =>
                $"{(inc.IsPrefix ? inc.IsIncrement ? "++" : "--" : "")}{inc.TargetSymbol.Name}" +
                $"{(inc.IsPrefix ? "" : inc.IsIncrement ? "++" : "--")}" +
                $" → [{inc.Next}]",

            CompoundAssignmentSlotInstruction compound =>
                $"COMPOUND {compound.TargetSymbol.Name} {compound.Operator}= {FormatExpression(compound.RhsExpression, compound.RhsProgram)} → [{compound.Next}]",

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
            SuperExpression => "super",
            _ => expr.GetType().Name
        };
    }

    private static string FormatExpression(ExpressionNode? expr, ExpressionProgram? program)
    {
        if (expr is not null)
        {
            return FormatExpression(expr);
        }

        return program is { } expressionProgram
            ? FormatExpressionProgram(expressionProgram)
            : "<undefined>";
    }

    private static string FormatBindingTarget(BindingTargetProgram program) => program.ToString();

    private static string FormatExpressionProgram(ExpressionProgram program)
    {
        if (program.IsEmpty)
        {
            return "<empty>";
        }

        return string.Join(" ", program.Operations.Select(FormatExpressionOp));
    }

    private static string FormatExpressionOp(ExpressionOp op)
    {
        return op switch
        {
            LoadLiteralExpressionOp literal => literal.Value.ToString() ?? "null",
            LoadRegexLiteralExpressionOp loadRegex => $"/{loadRegex.Pattern}/{loadRegex.Flags}",
            LoadFunctionLiteralExpressionOp loadFunction =>
                loadFunction.Function.Name is { } functionName ? $"fn:{functionName.Name}" : "fn",
            LoadClassLiteralExpressionOp loadClass =>
                loadClass.Class.Name is { } className ? $"class:{className.Name}" : "class",
            LoadTemplateObjectExpressionOp => "template",
            LoadIdentifierExpressionOp identifier => identifier.Name.Name,
            StoreIdentifierExpressionOp identifier => $"store.{identifier.Name.Name}",
            ApplyBindingTargetExpressionOp bindingTarget => $"bind.{bindingTarget.TargetProgram}",
            DuplicateTopExpressionOp => "dup",
            DuplicateTopTwoExpressionOp => "dup2",
            SwapTopTwoExpressionOp => "swap",
            RotateTopThreeRightExpressionOp => "rot3r",
            LoadThisExpressionOp => "this",
            LoadNewTargetExpressionOp => "new.target",
            LoadNamedCallTargetExpressionOp callTarget => $"call.{callTarget.PropertyName}",
            LoadComputedCallTargetExpressionOp => "call[]",
            LoadNamedSuperCallTargetExpressionOp callTarget => $"super.call.{callTarget.PropertyName}",
            LoadComputedSuperCallTargetExpressionOp => "super.call[]",
            EnsureSuperReferenceExpressionOp => "super.this",
            CreateArrayExpressionOp => "arr[]",
            ArrayPushExpressionOp => "arr.push",
            ArrayPushHoleExpressionOp => "arr.hole",
            ArraySpreadExpressionOp => "arr.spread",
            CreateObjectExpressionOp => "obj{}",
            RequireObjectCoercibleExpressionOp req => $"require_obj[{req.Depth}]",
            ResolvePropertyKeyExpressionOp => "propkey",
            DefineObjectPropertyExpressionOp property => $"obj.{property.PropertyName}",
            DefineComputedObjectPropertyExpressionOp => "obj[]",
            DefineObjectMethodExpressionOp method => $"obj.method:{method.PropertyName}",
            DefineComputedObjectMethodExpressionOp => "obj.method[]",
            DefineObjectAccessorExpressionOp accessor =>
                accessor.AccessorKind == ObjectAccessorKind.Getter
                    ? $"obj.get:{accessor.PropertyName}"
                    : $"obj.set:{accessor.PropertyName}",
            DefineComputedObjectAccessorExpressionOp accessor =>
                accessor.AccessorKind == ObjectAccessorKind.Getter
                    ? "obj.get[]"
                    : "obj.set[]",
            ObjectSpreadExpressionOp => "obj.spread",
            GetNamedPropertyExpressionOp property => $".{property.PropertyName}",
            GetComputedPropertyExpressionOp => "[]",
            GetNamedSuperPropertyExpressionOp property => $"super.{property.PropertyName}",
            GetComputedSuperPropertyExpressionOp => "super[]",
            SetNamedPropertyExpressionOp property => $"set.{property.PropertyName}",
            SetComputedPropertyExpressionOp => "set[]",
            SetNamedSuperPropertyExpressionOp property => $"super.set.{property.PropertyName}",
            SetComputedSuperPropertyExpressionOp => "super.set[]",
            UpdateIdentifierExpressionOp update =>
                update.IsPrefix
                    ? $"{(update.IsIncrement ? "++" : "--")}{update.Name.Name}"
                    : $"{update.Name.Name}{(update.IsIncrement ? "++" : "--")}",
            UpdateNamedPropertyExpressionOp update =>
                update.IsPrefix
                    ? $"{(update.IsIncrement ? "++" : "--")}.{update.PropertyName}"
                    : $".{update.PropertyName}{(update.IsIncrement ? "++" : "--")}",
            UpdateComputedPropertyExpressionOp update =>
                update.IsPrefix
                    ? $"{(update.IsIncrement ? "++" : "--")}[]"
                    : $"[]{(update.IsIncrement ? "++" : "--")}",
            UpdateNamedSuperPropertyExpressionOp update =>
                update.IsPrefix
                    ? $"{(update.IsIncrement ? "++" : "--")}super.{update.PropertyName}"
                    : $"super.{update.PropertyName}{(update.IsIncrement ? "++" : "--")}",
            UpdateComputedSuperPropertyExpressionOp update =>
                update.IsPrefix
                    ? $"{(update.IsIncrement ? "++" : "--")}super[]"
                    : $"super[]{(update.IsIncrement ? "++" : "--")}",
            TypeOfExpressionOp => "typeof",
            TypeOfIdentifierExpressionOp typeofIdentifier => $"typeof {typeofIdentifier.Name.Name}",
            DeleteIdentifierExpressionOp deleteIdentifier => $"delete {deleteIdentifier.Name.Name}",
            DeleteNamedPropertyExpressionOp deleteNamedProperty => $"delete .{deleteNamedProperty.PropertyName}",
            DeleteComputedPropertyExpressionOp => "delete []",
            UnaryPlusExpressionOp => "+",
            UnaryMinusExpressionOp => "-",
            UnaryBitwiseNotExpressionOp => "~",
            UnaryVoidExpressionOp => "void",
            ToStringExpressionOp => "str",
            UnaryLogicalNotExpressionOp => "!",
            BinaryExpressionOp binary => FormatBinaryOperator(binary.Operator),
            PrivateFieldInExpressionOp privateIn => $"#{privateIn.PrivateName} in",
            ThrowReferenceErrorExpressionOp throwRef => $"throw.ref:{throwRef.Message}",
            PopExpressionOp => "pop",
            JumpExpressionOp jump => $"jmp:{jump.Target}",
            JumpIfNullishExpressionOp jumpIfNullish => $"jmpN:{jumpIfNullish.Target}",
            JumpIfShortCircuitedExpressionOp jumpIfShortCircuited => $"jmpS:{jumpIfShortCircuited.Target}",
            JumpIfTrueExpressionOp jumpIfTrue => $"jmpT:{jumpIfTrue.Target}",
            JumpIfFalseExpressionOp jumpIfFalse => $"jmpF:{jumpIfFalse.Target}",
            JumpIfNotNullishExpressionOp jumpIfNotNullish => $"jmpNN:{jumpIfNotNullish.Target}",
            SuperConstructExpressionOp superConstruct => superConstruct.SpreadMask.IsDefaultOrEmpty
                ? $"super/{superConstruct.ArgumentCount}"
                : $"super*/{superConstruct.ArgumentCount}",
            CallExpressionOp call => call.SpreadMask.IsDefaultOrEmpty
                ? $"call/{call.ArgumentCount}"
                : $"call*/{call.ArgumentCount}",
            ConstructExpressionOp construct => construct.SpreadMask.IsDefaultOrEmpty
                ? $"new/{construct.ArgumentCount}"
                : $"new*/{construct.ArgumentCount}",
            _ => op.GetType().Name
        };
    }

    private static string FormatBinaryOperator(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.LogicalAnd => "&&",
            BinaryOperator.LogicalOr => "||",
            BinaryOperator.NullishCoalescing => "??",
            _ => op.ToString()
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
