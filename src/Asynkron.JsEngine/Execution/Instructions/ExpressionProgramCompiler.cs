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

            case ConditionalExpression conditional:
                return TryCompileConditionalExpression(conditional, builder, out failureReason);

            case MemberExpression member:
                return TryCompileMemberExpression(member, builder, out failureReason);

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
