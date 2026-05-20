#region

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
                $"EVAL_DISCARD {FormatExpression(null, discard.ExpressionProgram)} → [{discard.Next}]",

            AwaitAndDiscardInstruction awaitDiscard =>
                $"AWAIT_DISCARD {FormatExpression(null, awaitDiscard.AwaitedProgram)} → [{awaitDiscard.Next}]",

            AssignmentSlotInstruction assign =>
                $"ASSIGN {assign.TargetSymbol.Name} = {FormatExpression(null, assign.AwaitedProgram ?? assign.ValueProgram)} → [{assign.Next}]",

            LogicalCompoundAssignmentSlotInstruction logicalCompound =>
                $"ASSIGN_LOGICAL {logicalCompound.TargetSymbol.Name} {FormatBinaryOperator(logicalCompound.Operator)}= {FormatExpression(null, logicalCompound.AwaitedProgram ?? logicalCompound.RhsProgram)} → [{logicalCompound.Next}]",

            SimpleVariableDeclarationInstruction varDecl =>
                $"VAR {varDecl.VarKind} {varDecl.TargetSymbol.Name}" +
                ((varDecl.AwaitedProgram ?? varDecl.InitializerProgram) is not null
                    ? $" = {FormatExpression(null, varDecl.AwaitedProgram ?? varDecl.InitializerProgram)}"
                    : "") +
                $" → [{varDecl.Next}]",

            BindingVariableDeclarationInstruction bindingDecl =>
                $"VAR_BIND {bindingDecl.VarKind} {FormatBindingTarget(bindingDecl.TargetProgram)}" +
                ((bindingDecl.AwaitedProgram ?? bindingDecl.InitializerProgram) is not null
                    ? $" = {FormatExpression(null, bindingDecl.AwaitedProgram ?? bindingDecl.InitializerProgram)}"
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
                $"THROW {FormatExpression(null, thr.AwaitedProgram ?? thr.ThrowProgram)}",

            BreakInstruction brk =>
                $"BREAK (popTo: {brk.TargetScopeId}) → [{brk.TargetIndex}]",

            ContinueInstruction cont =>
                $"CONTINUE (popTo: {cont.TargetScopeId}) → [{cont.TargetIndex}]",

            YieldInstruction yield =>
                "YIELD" + (yield.AwaitedProgram is not null
                    ? $" await {FormatExpression(null, yield.AwaitedProgram)}"
                    : yield.YieldProgram is not null
                        ? $" {FormatExpression(null, yield.YieldProgram)}"
                        : "")
                + $" → [{yield.Next}]",

            YieldStarInstruction yieldStar =>
                "YIELD* " + (yieldStar.AwaitedProgram is not null
                    ? $"await {FormatExpression(null, yieldStar.AwaitedProgram)}"
                    : FormatExpression(null, yieldStar.IterableProgram))
                + (yieldStar.ResultSlotSymbol != null ? $" (result → {yieldStar.ResultSlotSymbol.Name})" : "")
                + $" → [{yieldStar.Next}]",

            EnterTryInstruction enterTry =>
                $"ENTER_TRY (handler: {(enterTry.HandlerIndex >= 0 ? $"[{enterTry.HandlerIndex}]" : "none")}, " +
                $"finally: {(enterTry.FinallyIndex >= 0 ? $"[{enterTry.FinallyIndex}]" : "none")}) → [{enterTry.Next}]",

            LeaveTryInstruction leaveTry =>
                $"LEAVE_TRY → [{leaveTry.Next}]",

            EndFinallyInstruction endFinally =>
                $"END_FINALLY → [{endFinally.Next}]",

            IteratorInitInstruction iterInit =>
                $"ITER_INIT {FormatExpression(null, iterInit.AwaitedProgram ?? iterInit.IterableProgram)} (slot: {iterInit.IteratorSlot.Name}, kind: {iterInit.Kind}) → [{iterInit.Next}]",

            IteratorMoveNextInstruction moveNext =>
                $"ITER_MOVE_NEXT (iter: {moveNext.IteratorSlot.Name}, value: {moveNext.ValueSlot.Name}) body: [{moveNext.Next}], done: [{moveNext.BreakIndex}]",

            IteratorCloseInstruction iterClose =>
                $"ITER_CLOSE (iter: {iterClose.IteratorSlot.Name}) → [{iterClose.Next}]",

            ForInInitInstruction forInInit =>
                $"FORIN_INIT {FormatExpression(null, forInInit.AwaitedProgram ?? forInInit.ObjectProgram)} (state: {forInInit.StateSlot.Name}, value: {forInInit.ValueSlot.Name}) → [{forInInit.Next}]",

            ForInMoveNextInstruction forInMoveNext =>
                $"FORIN_MOVE_NEXT (state: {forInMoveNext.StateSlot.Name}, value: {forInMoveNext.ValueSlot.Name}) body: [{forInMoveNext.Next}], done: [{forInMoveNext.BreakIndex}]",

            ArrayDestructuringInitInstruction arrayDestructuringInit =>
                $"ARRAY_DESTRUCT_INIT {FormatExpression(null, arrayDestructuringInit.SourceProgram)} (iter: {arrayDestructuringInit.IteratorSlot.Name}) → [{arrayDestructuringInit.Next}]",

            EnterWithInstruction enterWith =>
                $"ENTER_WITH {FormatExpression(null, enterWith.AwaitedProgram ?? enterWith.ObjectProgram)} → [{enterWith.Next}]",

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
                $"COMPOUND {compound.TargetSymbol.Name} {compound.Operator}= {FormatExpression(null, compound.AwaitedProgram ?? compound.RhsProgram)} → [{compound.Next}]",

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

        return string.Join(
            " ",
            program.Operations.Select(op => FormatExpressionOp(
                op,
                program.LiteralConstants,
                program.StringConstants,
                program.ObjectConstants,
                program.IdentifierConstants,
                program.SpreadMaskConstants)));
    }

    private static string FormatExpressionOp(
        PackedExpressionOp op,
        ImmutableArray<JsValue> literalConstants,
        ImmutableArray<string> stringConstants,
        ImmutableArray<object> objectConstants,
        ImmutableArray<IdentifierOperand> identifierConstants,
        ImmutableArray<ImmutableArray<int>> spreadMaskConstants)
    {
        var literalConstantSpan = literalConstants.AsSpan();
        var stringConstantSpan = stringConstants.AsSpan();
        var objectConstantSpan = objectConstants.AsSpan();
        var identifierConstantSpan = identifierConstants.AsSpan();
        var spreadMaskConstantSpan = spreadMaskConstants.AsSpan();
        return op.Kind switch
        {
            ExpressionOpKind.LoadLiteral => op.GetLiteral(literalConstantSpan).ToString() ?? "null",
            ExpressionOpKind.LoadRegexLiteral => $"/{op.GetString(stringConstantSpan)}/{op.RegexFlags}",
            ExpressionOpKind.LoadFunctionLiteral => op.GetObject<FunctionLiteralDescriptor>(objectConstantSpan).Function.Name is { } functionName ? $"fn:{functionName.Name}" : "fn",
            ExpressionOpKind.LoadClassLiteral => op.GetObject<ClassExpression>(objectConstantSpan).Name is { } className ? $"class:{className.Name}" : "class",
            ExpressionOpKind.LoadTemplateObject => "template",
            ExpressionOpKind.LoadIdentifier => op.GetIdentifier(identifierConstantSpan).Name.Name,
            ExpressionOpKind.LoadIdentifierCallTarget => $"call.{op.GetIdentifier(identifierConstantSpan).Name.Name}",
            ExpressionOpKind.ResolveIdentifierReference => $"ref.{op.GetIdentifier(identifierConstantSpan).Name.Name}",
            ExpressionOpKind.LoadResolvedIdentifierValue => "ref.load",
            ExpressionOpKind.PopResolvedIdentifierReference => "ref.pop",
            ExpressionOpKind.StoreResolvedIdentifier => $"ref.store.{op.GetIdentifier(identifierConstantSpan).Name.Name}",
            ExpressionOpKind.StoreIdentifier => $"store.{op.GetIdentifier(identifierConstantSpan).Name.Name}",
            ExpressionOpKind.ApplyBindingTarget => $"bind.{op.GetObject<BindingTargetProgram>(objectConstantSpan)}",
            ExpressionOpKind.DuplicateTop => "dup",
            ExpressionOpKind.DuplicateTopTwo => "dup2",
            ExpressionOpKind.SwapTopTwo => "swap",
            ExpressionOpKind.RotateTopThreeRight => "rot3r",
            ExpressionOpKind.LoadThis => "this",
            ExpressionOpKind.LoadNewTarget => "new.target",
            ExpressionOpKind.LoadImportMeta => "import.meta",
            ExpressionOpKind.LoadNamedCallTarget => $"call.{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.LoadComputedCallTarget => "call[]",
            ExpressionOpKind.LoadNamedSuperCallTarget => $"super.call.{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.LoadComputedSuperCallTarget => "super.call[]",
            ExpressionOpKind.EnsureSuperReference => "super.this",
            ExpressionOpKind.CreateArray => "arr[]",
            ExpressionOpKind.ArrayPush => "arr.push",
            ExpressionOpKind.ArrayPushHole => "arr.hole",
            ExpressionOpKind.ArraySpread => "arr.spread",
            ExpressionOpKind.CreateObject => "obj{}",
            ExpressionOpKind.RequireObjectCoercible => $"require_obj[{op.Depth}]",
            ExpressionOpKind.ResolvePropertyKey => "propkey",
            ExpressionOpKind.DefineObjectProperty => $"obj.{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.DefineComputedObjectProperty => "obj[]",
            ExpressionOpKind.DefineObjectMethod => $"obj.method:{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.DefineComputedObjectMethod => "obj.method[]",
            ExpressionOpKind.DefineObjectAccessor => op.AccessorKind == ObjectAccessorKind.Getter
                ? $"obj.get:{op.GetString(stringConstantSpan)}"
                : $"obj.set:{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.DefineComputedObjectAccessor => op.AccessorKind == ObjectAccessorKind.Getter
                ? "obj.get[]"
                : "obj.set[]",
            ExpressionOpKind.ObjectSpread => "obj.spread",
            ExpressionOpKind.GetNamedProperty => $".{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.GetComputedProperty => "[]",
            ExpressionOpKind.GetNamedSuperProperty => $"super.{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.GetComputedSuperProperty => "super[]",
            ExpressionOpKind.SetNamedProperty => $"set.{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.SetComputedProperty => "set[]",
            ExpressionOpKind.SetNamedSuperProperty => $"super.set.{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.SetComputedSuperProperty => "super.set[]",
            ExpressionOpKind.UpdateIdentifier => op.IsPrefix
                ? $"{(op.IsIncrement ? "++" : "--")}{op.GetIdentifier(identifierConstantSpan).Name.Name}"
                : $"{op.GetIdentifier(identifierConstantSpan).Name.Name}{(op.IsIncrement ? "++" : "--")}",
            ExpressionOpKind.UpdateNamedProperty => op.IsPrefix
                ? $"{(op.IsIncrement ? "++" : "--")}.{op.GetString(stringConstantSpan)}"
                : $".{op.GetString(stringConstantSpan)}{(op.IsIncrement ? "++" : "--")}",
            ExpressionOpKind.UpdateComputedProperty => op.IsPrefix
                ? $"{(op.IsIncrement ? "++" : "--")}[]"
                : $"[]{(op.IsIncrement ? "++" : "--")}",
            ExpressionOpKind.UpdateNamedSuperProperty => op.IsPrefix
                ? $"{(op.IsIncrement ? "++" : "--")}super.{op.GetString(stringConstantSpan)}"
                : $"super.{op.GetString(stringConstantSpan)}{(op.IsIncrement ? "++" : "--")}",
            ExpressionOpKind.UpdateComputedSuperProperty => op.IsPrefix
                ? $"{(op.IsIncrement ? "++" : "--")}super[]"
                : $"super[]{(op.IsIncrement ? "++" : "--")}",
            ExpressionOpKind.TypeOf => "typeof",
            ExpressionOpKind.TypeOfIdentifier => $"typeof {op.GetIdentifier(identifierConstantSpan).Name.Name}",
            ExpressionOpKind.DeleteIdentifier => $"delete {op.GetIdentifier(identifierConstantSpan).Name.Name}",
            ExpressionOpKind.DeleteNamedProperty => $"delete .{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.DeleteComputedProperty => "delete []",
            ExpressionOpKind.UnaryPlus => "+",
            ExpressionOpKind.UnaryMinus => "-",
            ExpressionOpKind.UnaryBitwiseNot => "~",
            ExpressionOpKind.UnaryVoid => "void",
            ExpressionOpKind.ToString => "str",
            ExpressionOpKind.UnaryLogicalNot => "!",
            ExpressionOpKind.Binary => FormatBinaryOperator(op.Operator),
            ExpressionOpKind.PrivateFieldIn => $"#{op.GetString(stringConstantSpan)} in",
            ExpressionOpKind.ThrowReferenceError => $"throw.ref:{op.GetString(stringConstantSpan)}",
            ExpressionOpKind.Pop => "pop",
            ExpressionOpKind.Jump => $"jmp:{op.Target}",
            ExpressionOpKind.JumpIfNullish => $"jmpN:{op.Target}",
            ExpressionOpKind.JumpIfShortCircuited => $"jmpS:{op.Target}",
            ExpressionOpKind.JumpIfTrue => $"jmpT:{op.Target}",
            ExpressionOpKind.JumpIfFalse => $"jmpF:{op.Target}",
            ExpressionOpKind.JumpIfNotNullish => $"jmpNN:{op.Target}",
            ExpressionOpKind.SuperConstruct => op.GetSpreadIndices(spreadMaskConstantSpan).IsDefaultOrEmpty
                ? $"super/{op.ArgumentCount}"
                : $"super*/{op.ArgumentCount}",
            ExpressionOpKind.Call => op.GetSpreadIndices(spreadMaskConstantSpan).IsDefaultOrEmpty
                ? $"call/{op.ArgumentCount}"
                : $"call*/{op.ArgumentCount}",
            ExpressionOpKind.Construct => op.GetSpreadIndices(spreadMaskConstantSpan).IsDefaultOrEmpty
                ? $"new/{op.ArgumentCount}"
                : $"new*/{op.ArgumentCount}",
            _ => op.Kind.ToString()
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

}
