using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution.Instructions;

internal static class ExpressionProgramCompiler
{
    public static bool TryCompile(
        ExpressionNode expression,
        out ExpressionProgram program,
        out string? failureReason)
    {
        var builder = new List<ExpressionOp>();
        if (!TryCompileExpression(expression, builder, out failureReason))
        {
            program = default;
            return false;
        }

        program = new ExpressionProgram([.. builder]);
        return true;
    }

    private static bool TryCompileExpression(
        ExpressionNode expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                builder.Add(new LoadLiteralExpressionOp(literal.Value));
                failureReason = null;
                return true;

            case IdentifierExpression identifier:
                builder.Add(new LoadIdentifierExpressionOp(
                    identifier.Name,
                    identifier.ScopeId,
                    identifier.SlotIndex,
                    identifier.FlatSlotId,
                    ReferenceEquals(identifier.Name, Symbol.Arguments)));
                failureReason = null;
                return true;

            case ThisExpression:
                builder.Add(new LoadThisExpressionOp());
                failureReason = null;
                return true;

            case NewTargetExpression:
                builder.Add(new LoadNewTargetExpressionOp());
                failureReason = null;
                return true;

            case ArrayExpression array:
                return TryCompileArrayExpression(array, builder, out failureReason);

            case ObjectExpression obj:
                return TryCompileObjectExpression(obj, builder, out failureReason);

            case SequenceExpression sequence:
                return TryCompileSequenceExpression(sequence, builder, out failureReason);

            case TemplateLiteralExpression template:
                return TryCompileTemplateLiteralExpression(template, builder, out failureReason);

            case PropertyAssignmentExpression propertyAssignment:
                return TryCompilePropertyAssignmentExpression(propertyAssignment, builder, out failureReason);

            case IndexAssignmentExpression indexAssignment:
                return TryCompileIndexAssignmentExpression(indexAssignment, builder, out failureReason);

            case ConditionalExpression conditional:
                return TryCompileConditionalExpression(conditional, builder, out failureReason);

            case MemberExpression member:
                return TryCompileMemberExpression(member, builder, out failureReason);

            case CallExpression call:
                return TryCompileCallExpression(call, builder, out failureReason);

            case NewExpression construct:
                return TryCompileNewExpression(construct, builder, out failureReason);

            case UnaryExpression { Operator: UnaryOperator.LogicalNot } unary:
                if (!TryCompileExpression(unary.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new UnaryLogicalNotExpressionOp());
                failureReason = null;
                return true;

            case BinaryExpression binary:
                return TryCompileBinaryExpression(binary, builder, out failureReason);

            default:
                failureReason =
                    $"Expression bytecode does not yet support '{expression.GetType().Name}'.";
                return false;
        }
    }

    private static bool TryCompileBinaryExpression(
        BinaryExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (!TryCompileExpression(expression.Left, builder, out failureReason))
        {
            return false;
        }

        switch (expression.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfFalseExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(expression.Right, builder, out failureReason))
                {
                    return false;
                }

                builder[shortCircuitIndex] = new JumpIfFalseExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfTrueExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(expression.Right, builder, out failureReason))
                {
                    return false;
                }

                builder[shortCircuitIndex] = new JumpIfTrueExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfNotNullishExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(expression.Right, builder, out failureReason))
                {
                    return false;
                }

                builder[shortCircuitIndex] = new JumpIfNotNullishExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }
        }

        if (!TryCompileExpression(expression.Right, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new BinaryExpressionOp(expression.Operator));
        failureReason = null;
        return true;
    }

    private static bool TryCompileCallExpression(
        CallExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.IsOptional || HasOptionalChaining(expression.Callee))
        {
            failureReason = "Expression bytecode does not yet support optional call expressions.";
            return false;
        }

        foreach (var argument in expression.Arguments)
        {
            if (argument.IsSpread)
            {
                failureReason = "Expression bytecode does not yet support spread call arguments.";
                return false;
            }
        }

        var isDirectEval = false;

        var hasExplicitThis = false;

        switch (expression.Callee)
        {
            case SuperExpression:
            case MemberExpression { Target: SuperExpression }:
                failureReason = "Expression bytecode does not yet support super call expressions.";
                return false;

            case IdentifierExpression identifier:
                if (!TryCompileExpression(identifier, builder, out failureReason))
                {
                    return false;
                }

                isDirectEval = identifier.Name.Name == "eval";
                break;

            case MemberExpression { IsOptional: false, IsComputed: false } member:
                if (!TryCompileCallTargetObject(member.Target, builder, out failureReason))
                {
                    return false;
                }

                if (member.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
                {
                    failureReason = "Expression bytecode only supports literal property names for direct member calls.";
                    return false;
                }

                var propertyName = propertyLiteral.Value.AsString();
                if (string.Equals(propertyName, "call", StringComparison.Ordinal) ||
                    string.Equals(propertyName, "apply", StringComparison.Ordinal))
                {
                    failureReason = "Expression bytecode does not yet support .call/.apply call expressions.";
                    return false;
                }

                builder.Add(new LoadNamedCallTargetExpressionOp(propertyName));
                hasExplicitThis = true;
                break;

            case MemberExpression { IsOptional: false, IsComputed: true } member:
                if (!TryCompileCallTargetObject(member.Target, builder, out failureReason))
                {
                    return false;
                }

                if (!TryCompileExpression(member.Property, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new LoadComputedCallTargetExpressionOp());
                hasExplicitThis = true;
                break;

            default:
                if (!TryCompileExpression(expression.Callee, builder, out failureReason))
                {
                    return false;
                }

                break;
        }

        foreach (var argument in expression.Arguments)
        {
            if (!TryCompileExpression(argument.Expression, builder, out failureReason))
            {
                return false;
            }
        }

        builder.Add(new CallExpressionOp(
            expression.Arguments.Length,
            HasExplicitThis: hasExplicitThis,
            IsDirectEval: isDirectEval));
        failureReason = null;
        return true;
    }

    private static bool TryCompileNewExpression(
        NewExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        foreach (var argument in expression.Arguments)
        {
            if (argument.IsSpread)
            {
                failureReason = "Expression bytecode does not yet support spread constructor arguments.";
                return false;
            }
        }

        if (!TryCompileExpression(expression.Constructor, builder, out failureReason))
        {
            return false;
        }

        foreach (var argument in expression.Arguments)
        {
            if (!TryCompileExpression(argument.Expression, builder, out failureReason))
            {
                return false;
            }
        }

        builder.Add(new ConstructExpressionOp(expression.Arguments.Length));
        failureReason = null;
        return true;
    }

    private static bool TryCompileSequenceExpression(
        SequenceExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (!TryCompileExpression(expression.Left, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new PopExpressionOp());

        if (!TryCompileExpression(expression.Right, builder, out failureReason))
        {
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileTemplateLiteralExpression(
        TemplateLiteralExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        builder.Add(new LoadLiteralExpressionOp(new JsValue(string.Empty)));

        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
            {
                builder.Add(new LoadLiteralExpressionOp(new JsValue(part.Text)));
                builder.Add(new BinaryExpressionOp(BinaryOperator.Add));
                continue;
            }

            if (part.Expression is null)
            {
                continue;
            }

            if (!TryCompileExpression(part.Expression, builder, out failureReason))
            {
                return false;
            }

            builder.Add(new ToStringExpressionOp());
            builder.Add(new BinaryExpressionOp(BinaryOperator.Add));
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompilePropertyAssignmentExpression(
        PropertyAssignmentExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.IsCompoundAssignment)
        {
            failureReason = "Expression bytecode does not yet support compound property assignments.";
            return false;
        }

        if (expression.Target is SuperExpression || HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional property assignments.";
            return false;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        if (expression.IsComputed)
        {
            if (!TryCompileExpression(expression.Property, builder, out failureReason))
            {
                return false;
            }

            if (!TryCompileExpression(expression.Value, builder, out failureReason))
            {
                return false;
            }

            builder.Add(new SetComputedPropertyExpressionOp());
            failureReason = null;
            return true;
        }

        if (expression.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
        {
            failureReason = "Expression bytecode only supports literal property names for direct property assignment.";
            return false;
        }

        if (!TryCompileExpression(expression.Value, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new SetNamedPropertyExpressionOp(propertyLiteral.Value.AsString()));
        failureReason = null;
        return true;
    }

    private static bool TryCompileIndexAssignmentExpression(
        IndexAssignmentExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.IsCompoundAssignment)
        {
            failureReason = "Expression bytecode does not yet support compound index assignments.";
            return false;
        }

        if (expression.Target is SuperExpression || HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional index assignments.";
            return false;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        if (!TryCompileExpression(expression.Index, builder, out failureReason))
        {
            return false;
        }

        if (!TryCompileExpression(expression.Value, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new SetComputedPropertyExpressionOp());
        failureReason = null;
        return true;
    }

    private static bool TryCompileConditionalExpression(
        ConditionalExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (!TryCompileExpression(expression.Test, builder, out failureReason))
        {
            return false;
        }

        var falseBranchJumpIndex = builder.Count;
        builder.Add(new JumpIfFalseExpressionOp(-1));
        builder.Add(new PopExpressionOp());

        if (!TryCompileExpression(expression.Consequent, builder, out failureReason))
        {
            return false;
        }

        var endJumpIndex = builder.Count;
        builder.Add(new JumpExpressionOp(-1));

        var alternateStartIndex = builder.Count;
        builder[falseBranchJumpIndex] = new JumpIfFalseExpressionOp(alternateStartIndex);
        builder.Add(new PopExpressionOp());

        if (!TryCompileExpression(expression.Alternate, builder, out failureReason))
        {
            return false;
        }

        builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
        failureReason = null;
        return true;
    }

    private static bool TryCompileMemberExpression(
        MemberExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.Target is SuperExpression)
        {
            failureReason = "Expression bytecode does not yet support super member access.";
            return false;
        }

        if (expression is { IsComputed: false, Target: IdentifierExpression { Name.Name: "Symbol" }, Property: LiteralExpression { Value.IsString: true } symbolProp })
        {
            switch (symbolProp.Value.AsString())
            {
                case "iterator":
                    builder.Add(new LoadLiteralExpressionOp((JsValue)Symbols.Iterator));
                    failureReason = null;
                    return true;
                case "asyncIterator":
                    builder.Add(new LoadLiteralExpressionOp((JsValue)Symbols.AsyncIterator));
                    failureReason = null;
                    return true;
                case "toStringTag":
                    builder.Add(new LoadLiteralExpressionOp((JsValue)Symbols.ToStringTag));
                    failureReason = null;
                    return true;
            }
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        var shortCircuitOnNullishTarget = HasOptionalChaining(expression.Target);

        if (!expression.IsComputed)
        {
            if (expression.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
            {
                failureReason = "Expression bytecode only supports literal property names for dot access.";
                return false;
            }

            builder.Add(new GetNamedPropertyExpressionOp(
                propertyLiteral.Value.AsString(),
                IsOptional: expression.IsOptional,
                ShortCircuitOnNullishTarget: shortCircuitOnNullishTarget));
            failureReason = null;
            return true;
        }

        if (expression.IsOptional)
        {
            var endIndex = builder.Count;
            builder.Add(new JumpIfNullishExpressionOp(-1, ReplaceWithUndefined: true));

            if (!TryCompileExpression(expression.Property, builder, out failureReason))
            {
                return false;
            }

            builder.Add(new GetComputedPropertyExpressionOp(shortCircuitOnNullishTarget));
            builder[endIndex] = new JumpIfNullishExpressionOp(builder.Count, ReplaceWithUndefined: true);
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Property, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new GetComputedPropertyExpressionOp(shortCircuitOnNullishTarget));
        failureReason = null;
        return true;
    }

    private static bool TryCompileArrayExpression(
        ArrayExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        builder.Add(new CreateArrayExpressionOp());

        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                if (element.Expression is null)
                {
                    failureReason = "Array spread elements must have an expression.";
                    return false;
                }

                if (!TryCompileExpression(element.Expression, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new ArraySpreadExpressionOp());
                continue;
            }

            if (element.Expression is null)
            {
                builder.Add(new ArrayPushHoleExpressionOp());
                continue;
            }

            if (!TryCompileExpression(element.Expression, builder, out failureReason))
            {
                return false;
            }

            builder.Add(new ArrayPushExpressionOp());
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileObjectExpression(
        ObjectExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        builder.Add(new CreateObjectExpressionOp());

        foreach (var member in expression.Members)
        {
            switch (member.Kind)
            {
                case ObjectMemberKind.Property or ObjectMemberKind.Field:
                    if (member.Function is not null)
                    {
                        failureReason =
                            $"Expression bytecode does not yet support object member kind '{member.Kind}'.";
                        return false;
                    }

                    if (!member.IsComputed)
                    {
                        if (member.Value is not null)
                        {
                            if (!TryCompileExpression(member.Value, builder, out failureReason))
                            {
                                return false;
                            }
                        }
                        else
                        {
                            builder.Add(new LoadLiteralExpressionOp(JsValue.Undefined));
                        }

                        if (member.Key is not string propertyName)
                        {
                            failureReason = "Expression bytecode only supports static string object property names.";
                            return false;
                        }

                        builder.Add(new DefineObjectPropertyExpressionOp(
                            propertyName,
                            IsPrototypeMutation: member.Kind == ObjectMemberKind.Property &&
                                                 member.Parameter is null &&
                                                 string.Equals(propertyName, "__proto__", StringComparison.Ordinal)));
                        break;
                    }

                    if (member.Key is not ExpressionNode keyExpression)
                    {
                        failureReason = "Computed object property names must use an expression key.";
                        return false;
                    }

                    if (!TryCompileExpression(keyExpression, builder, out failureReason))
                    {
                        return false;
                    }

                    if (member.Value is not null)
                    {
                        if (!TryCompileExpression(member.Value, builder, out failureReason))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        builder.Add(new LoadLiteralExpressionOp(JsValue.Undefined));
                    }

                    builder.Add(new DefineComputedObjectPropertyExpressionOp());
                    break;

                case ObjectMemberKind.Spread:
                    if (member.Value is null)
                    {
                        failureReason = "Object spread members must have a value expression.";
                        return false;
                    }

                    if (!TryCompileExpression(member.Value, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(new ObjectSpreadExpressionOp());
                    break;

                default:
                    failureReason =
                        $"Expression bytecode does not yet support object member kind '{member.Kind}'.";
                    return false;
            }
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileCallTargetObject(
        ExpressionNode target,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (target is SuperExpression || HasOptionalChaining(target))
        {
            failureReason = "Expression bytecode does not yet support optional or super member call targets.";
            return false;
        }

        return TryCompileExpression(target, builder, out failureReason);
    }

    private static bool HasOptionalChaining(ExpressionNode? expression)
    {
        while (expression is not null)
        {
            switch (expression)
            {
                case MemberExpression { IsOptional: true }:
                case CallExpression { IsOptional: true }:
                    return true;
                case MemberExpression member:
                    expression = member.Target;
                    break;
                case CallExpression call:
                    expression = call.Callee;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }
}
