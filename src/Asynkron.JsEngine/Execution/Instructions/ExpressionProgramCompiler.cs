using System.Collections.Immutable;
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

    public static ExpressionProgramCompileFailure ClassifyFailure(
        ExpressionNode expression,
        string? failureReason)
    {
        var detail = failureReason ?? $"Expression bytecode does not yet support '{expression.GetType().Name}'.";

        var code = detail switch
        {
            "Expression bytecode does not yet support delete on super or optional member expressions."
                => ExpressionProgramFailureCode.UnsupportedDeleteTarget,
            "Expression bytecode does not yet support super call expressions."
                => ExpressionProgramFailureCode.SuperCall,
            "Expression bytecode does not yet support nested optional call expressions."
                => ExpressionProgramFailureCode.NestedOptionalCall,
            "Expression bytecode does not yet support super or optional member update expressions."
                => ExpressionProgramFailureCode.OptionalOrSuperMemberUpdate,
            "Expression bytecode does not yet support super tagged templates."
                => ExpressionProgramFailureCode.SuperTaggedTemplate,
            "Expression bytecode does not yet support optional tagged templates."
                => ExpressionProgramFailureCode.OptionalTaggedTemplate,
            "Expression bytecode does not yet support nested optional tagged templates."
                => ExpressionProgramFailureCode.NestedOptionalTaggedTemplate,
            "Expression bytecode does not yet support super or optional property assignments."
                => ExpressionProgramFailureCode.OptionalOrSuperPropertyAssignment,
            "Expression bytecode does not yet support super or optional index assignments."
                => ExpressionProgramFailureCode.OptionalOrSuperIndexAssignment,
            "Expression bytecode does not yet support super member access."
                => ExpressionProgramFailureCode.SuperMemberAccess,
            "Expression bytecode only supports lowered binary compound assignments."
                => ExpressionProgramFailureCode.UnsupportedCompoundAssignmentShape,
            "Expression bytecode only supports identifier and member update expressions."
                => ExpressionProgramFailureCode.UnsupportedUpdateTarget,
            "Expression bytecode only supports static string object property names."
                => ExpressionProgramFailureCode.UnsupportedStaticObjectPropertyName,
            "Computed object property names must use an expression key."
                => ExpressionProgramFailureCode.InvalidComputedObjectKey,
            "Expression bytecode only supports literal property names for dot access."
                => ExpressionProgramFailureCode.UnsupportedDotAccessPropertyName,
            "Expression bytecode only supports literal property names for direct member calls."
                => ExpressionProgramFailureCode.UnsupportedDirectMemberCallPropertyName,
            "Expression bytecode only supports literal property names for tagged template member access."
                => ExpressionProgramFailureCode.UnsupportedTaggedTemplateMemberAccessName,
            "Expression bytecode does not yet support optional or super member call targets."
                => ExpressionProgramFailureCode.OptionalOrSuperMemberCallTarget,
            _ when detail.StartsWith("Expression bytecode does not yet support object member kind '", StringComparison.Ordinal)
                => ExpressionProgramFailureCode.UnsupportedObjectMemberKind,
            _ when detail.StartsWith("Expression bytecode does not yet support unary operator '", StringComparison.Ordinal)
                => ExpressionProgramFailureCode.UnsupportedUnaryOperator,
            _ => ExpressionProgramFailureCode.UnsupportedExpressionNode
        };

        return new ExpressionProgramCompileFailure(code, detail);
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

            case AssignmentExpression assignment:
                return TryCompileAssignmentExpression(assignment, builder, out failureReason);

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

            case FunctionExpression function:
                builder.Add(new LoadFunctionLiteralExpressionOp(function));
                failureReason = null;
                return true;

            case ClassExpression classExpression:
                builder.Add(new LoadClassLiteralExpressionOp(classExpression));
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

            case TaggedTemplateExpression taggedTemplate:
                return TryCompileTaggedTemplateExpression(taggedTemplate, builder, out failureReason);

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

            case UnaryExpression unary:
                return TryCompileUnaryExpression(unary, builder, out failureReason);

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

    private static bool TryCompileUnaryExpression(
        UnaryExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        switch (expression.Operator)
        {
            case UnaryOperator.LogicalNot:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new UnaryLogicalNotExpressionOp());
                failureReason = null;
                return true;

            case UnaryOperator.TypeOf:
                if (expression.Operand is IdentifierExpression identifierForTypeOf)
                {
                    builder.Add(new TypeOfIdentifierExpressionOp(
                        identifierForTypeOf.Name,
                        identifierForTypeOf.ScopeId,
                        identifierForTypeOf.SlotIndex,
                        identifierForTypeOf.FlatSlotId,
                        IsArguments: ReferenceEquals(identifierForTypeOf.Name, Symbol.Arguments)));
                    failureReason = null;
                    return true;
                }

                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new TypeOfExpressionOp());
                failureReason = null;
                return true;

            case UnaryOperator.Delete:
                switch (expression.Operand)
                {
                    case IdentifierExpression identifierForDelete:
                        builder.Add(new DeleteIdentifierExpressionOp(identifierForDelete.Name));
                        failureReason = null;
                        return true;

                    case MemberExpression
                        {
                            Target: not SuperExpression,
                            IsOptional: false,
                            IsComputed: false,
                            Property: LiteralExpression { Value.IsString: true } propertyLiteral
                        } namedDelete:
                        if (!TryCompileExpression(namedDelete.Target, builder, out failureReason))
                        {
                            return false;
                        }

                        builder.Add(new DeleteNamedPropertyExpressionOp(propertyLiteral.Value.AsString()));
                        failureReason = null;
                        return true;

                    case MemberExpression
                        {
                            Target: not SuperExpression,
                            IsOptional: false,
                            IsComputed: true
                        } computedDelete:
                        if (!TryCompileExpression(computedDelete.Target, builder, out failureReason))
                        {
                            return false;
                        }

                        if (!TryCompileExpression(computedDelete.Property, builder, out failureReason))
                        {
                            return false;
                        }

                        builder.Add(new DeleteComputedPropertyExpressionOp());
                        failureReason = null;
                        return true;

                    case MemberExpression { IsOptional: true }:
                    case MemberExpression { Target: SuperExpression }:
                        failureReason = "Expression bytecode does not yet support delete on super or optional member expressions.";
                        return false;
                }

                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new PopExpressionOp());
                builder.Add(new LoadLiteralExpressionOp(JsValue.True));
                failureReason = null;
                return true;

            case UnaryOperator.Plus:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new UnaryPlusExpressionOp());
                failureReason = null;
                return true;

            case UnaryOperator.Minus:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new UnaryMinusExpressionOp());
                failureReason = null;
                return true;

            case UnaryOperator.BitwiseNot:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new UnaryBitwiseNotExpressionOp());
                failureReason = null;
                return true;

            case UnaryOperator.Void:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new UnaryVoidExpressionOp());
                failureReason = null;
                return true;

            case UnaryOperator.Increment:
            case UnaryOperator.Decrement:
                if (expression.Operand is IdentifierExpression identifier)
                {
                    builder.Add(new UpdateIdentifierExpressionOp(
                        identifier.Name,
                        identifier.ScopeId,
                        identifier.SlotIndex,
                        identifier.FlatSlotId,
                        IsIncrement: expression.Operator == UnaryOperator.Increment,
                        IsPrefix: expression.IsPrefix,
                        IsArguments: ReferenceEquals(identifier.Name, Symbol.Arguments)));
                    failureReason = null;
                    return true;
                }

                if (expression.Operand is MemberExpression member)
                {
                    return TryCompileMemberUpdateExpression(
                        member,
                        expression.Operator == UnaryOperator.Increment,
                        expression.IsPrefix,
                        builder,
                        out failureReason);
                }

                failureReason = "Expression bytecode only supports identifier and member update expressions.";
                return false;

            default:
                failureReason = $"Expression bytecode does not yet support unary operator '{expression.Operator}'.";
                return false;
        }
    }

    private static bool TryCompileCallExpression(
        CallExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        var isDirectEval = false;

        var hasExplicitThis = false;
        var targetNullishJumpIndex = -1;
        var targetShortCircuitJumpIndex = -1;
        var callNullishJumpIndex = -1;
        var calleeShortCircuitJumpIndex = -1;
        List<bool>? spreadMaskBuilder = null;

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

                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(new JumpIfNullishExpressionOp(-1, ReplaceWithUndefined: true));
                }
                else
                {
                    isDirectEval = identifier.Name.Name == "eval";
                }
                break;

            case MemberExpression { IsComputed: false } member:
                if (!TryCompileOptionalMemberCallTarget(
                        member,
                        builder,
                        ref targetNullishJumpIndex,
                        ref targetShortCircuitJumpIndex,
                        out failureReason))
                {
                    return false;
                }

                if (member.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
                {
                    failureReason = "Expression bytecode only supports literal property names for direct member calls.";
                    return false;
                }

                var propertyName = propertyLiteral.Value.AsString();
                builder.Add(new LoadNamedCallTargetExpressionOp(propertyName));
                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(new JumpIfNullishExpressionOp(-1, ReplaceWithUndefined: true));
                }
                hasExplicitThis = true;
                break;

            case MemberExpression member:
                if (!TryCompileOptionalMemberCallTarget(
                        member,
                        builder,
                        ref targetNullishJumpIndex,
                        ref targetShortCircuitJumpIndex,
                        out failureReason))
                {
                    return false;
                }

                if (!TryCompileExpression(member.Property, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new LoadComputedCallTargetExpressionOp());
                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(new JumpIfNullishExpressionOp(-1, ReplaceWithUndefined: true));
                }
                hasExplicitThis = true;
                break;

            default:
                if (!TryCompileExpression(expression.Callee, builder, out failureReason))
                {
                    return false;
                }

                if (HasOptionalChaining(expression.Callee))
                {
                    calleeShortCircuitJumpIndex = builder.Count;
                    builder.Add(new JumpIfShortCircuitedExpressionOp(-1));
                }

                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(new JumpIfNullishExpressionOp(-1, ReplaceWithUndefined: true));
                }
                break;
        }

        for (var argumentIndex = 0; argumentIndex < expression.Arguments.Length; argumentIndex++)
        {
            var argument = expression.Arguments[argumentIndex];
            if (!TryCompileExpression(argument.Expression, builder, out failureReason))
            {
                return false;
            }

            if (argument.IsSpread)
            {
                if (spreadMaskBuilder is null)
                {
                    spreadMaskBuilder = new List<bool>(expression.Arguments.Length);
                    for (var i = 0; i < argumentIndex; i++)
                    {
                        spreadMaskBuilder.Add(false);
                    }
                }
            }

            if (spreadMaskBuilder is not null)
            {
                spreadMaskBuilder.Add(argument.IsSpread);
            }
        }

        builder.Add(new CallExpressionOp(
            expression.Arguments.Length,
            HasExplicitThis: hasExplicitThis,
            IsDirectEval: isDirectEval,
            SpreadMask: spreadMaskBuilder is not null
                ? ImmutableArray.CreateRange(spreadMaskBuilder)
                : default));

        if (callNullishJumpIndex >= 0)
        {
            if (hasExplicitThis)
            {
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var cleanupIndex = builder.Count;
                builder[callNullishJumpIndex] = new JumpIfNullishExpressionOp(cleanupIndex, ReplaceWithUndefined: true);
                builder.Add(new SwapTopTwoExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
            }
            else
            {
                builder[callNullishJumpIndex] = new JumpIfNullishExpressionOp(builder.Count, ReplaceWithUndefined: true);
            }
        }

        if (targetNullishJumpIndex >= 0)
        {
            builder[targetNullishJumpIndex] = new JumpIfNullishExpressionOp(builder.Count, ReplaceWithUndefined: true);
        }

        if (targetShortCircuitJumpIndex >= 0)
        {
            builder[targetShortCircuitJumpIndex] = new JumpIfShortCircuitedExpressionOp(builder.Count);
        }

        if (calleeShortCircuitJumpIndex >= 0)
        {
            builder[calleeShortCircuitJumpIndex] = new JumpIfShortCircuitedExpressionOp(builder.Count);
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileAssignmentExpression(
        AssignmentExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.IsCompoundAssignment)
        {
            return TryCompileCompoundAssignmentExpression(expression, builder, out failureReason);
        }

        if (!TryCompileExpression(expression.Value, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new StoreIdentifierExpressionOp(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            AllowNameInference: !IsParenthesizedIdentifierAssignment(expression)));
        failureReason = null;
        return true;
    }

    private static bool TryCompileCompoundAssignmentExpression(
        AssignmentExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.Value is not BinaryExpression binary)
        {
            failureReason = "Expression bytecode only supports lowered binary compound assignments.";
            return false;
        }

        var storeOp = new StoreIdentifierExpressionOp(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            AllowNameInference: !IsParenthesizedIdentifierAssignment(expression));
        var loadOp = new LoadIdentifierExpressionOp(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            ReferenceEquals(expression.Target, Symbol.Arguments));

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                builder.Add(loadOp);
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfFalseExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(storeOp);
                builder[shortCircuitIndex] = new JumpIfFalseExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                builder.Add(loadOp);
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfTrueExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(storeOp);
                builder[shortCircuitIndex] = new JumpIfTrueExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                builder.Add(loadOp);
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfNotNullishExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(storeOp);
                builder[shortCircuitIndex] = new JumpIfNotNullishExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            default:
                builder.Add(loadOp);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new BinaryExpressionOp(binary.Operator));
                builder.Add(storeOp);
                failureReason = null;
                return true;
        }
    }

    private static bool TryCompileNewExpression(
        NewExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        List<bool>? spreadMaskBuilder = null;

        if (!TryCompileExpression(expression.Constructor, builder, out failureReason))
        {
            return false;
        }

        for (var argumentIndex = 0; argumentIndex < expression.Arguments.Length; argumentIndex++)
        {
            var argument = expression.Arguments[argumentIndex];
            if (!TryCompileExpression(argument.Expression, builder, out failureReason))
            {
                return false;
            }

            if (argument.IsSpread)
            {
                if (spreadMaskBuilder is null)
                {
                    spreadMaskBuilder = new List<bool>(expression.Arguments.Length);
                    for (var i = 0; i < argumentIndex; i++)
                    {
                        spreadMaskBuilder.Add(false);
                    }
                }
            }

            if (spreadMaskBuilder is not null)
            {
                spreadMaskBuilder.Add(argument.IsSpread);
            }
        }

        builder.Add(new ConstructExpressionOp(
            expression.Arguments.Length,
            SpreadMask: spreadMaskBuilder is not null
                ? ImmutableArray.CreateRange(spreadMaskBuilder)
                : default));
        failureReason = null;
        return true;
    }

    private static bool TryCompileMemberUpdateExpression(
        MemberExpression expression,
        bool isIncrement,
        bool isPrefix,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.Target is SuperExpression || HasOptionalChaining(expression.Target) || expression.IsOptional)
        {
            failureReason = "Expression bytecode does not yet support super or optional member update expressions.";
            return false;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        if (!expression.IsComputed)
        {
            if (expression.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
            {
                failureReason = "Expression bytecode only supports literal property names for member updates.";
                return false;
            }

            builder.Add(new UpdateNamedPropertyExpressionOp(
                propertyLiteral.Value.AsString(),
                IsIncrement: isIncrement,
                IsPrefix: isPrefix));
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Property, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new UpdateComputedPropertyExpressionOp(
            IsIncrement: isIncrement,
            IsPrefix: isPrefix));
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

    private static bool TryCompileTaggedTemplateExpression(
        TaggedTemplateExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        var hasExplicitThis = false;

        switch (expression.Tag)
        {
            case SuperExpression:
            case MemberExpression { Target: SuperExpression }:
                failureReason = "Expression bytecode does not yet support super tagged templates.";
                return false;

            case MemberExpression { IsOptional: true }:
                failureReason = "Expression bytecode does not yet support optional tagged templates.";
                return false;

            case MemberExpression { IsComputed: false } member:
                if (HasOptionalChaining(member.Target))
                {
                    failureReason = "Expression bytecode does not yet support nested optional tagged templates.";
                    return false;
                }

                if (!TryCompileExpression(member.Target, builder, out failureReason))
                {
                    return false;
                }

                if (member.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
                {
                    failureReason = "Expression bytecode only supports literal property names for tagged template member access.";
                    return false;
                }

                builder.Add(new LoadNamedCallTargetExpressionOp(propertyLiteral.Value.AsString()));
                hasExplicitThis = true;
                break;

            case MemberExpression member:
                if (HasOptionalChaining(member.Target))
                {
                    failureReason = "Expression bytecode does not yet support nested optional tagged templates.";
                    return false;
                }

                if (!TryCompileExpression(member.Target, builder, out failureReason))
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
                if (HasOptionalChaining(expression.Tag))
                {
                    failureReason = "Expression bytecode does not yet support optional tagged templates.";
                    return false;
                }

                if (!TryCompileExpression(expression.Tag, builder, out failureReason))
                {
                    return false;
                }
                break;
        }

        if (!TryCreateTaggedTemplateDescriptor(expression, out var descriptor, out failureReason))
        {
            return false;
        }

        builder.Add(new LoadTemplateObjectExpressionOp(descriptor));

        foreach (var templateExpression in expression.Expressions)
        {
            if (!TryCompileExpression(templateExpression, builder, out failureReason))
            {
                return false;
            }
        }

        builder.Add(new CallExpressionOp(
            expression.Expressions.Length + 1,
            HasExplicitThis: hasExplicitThis));
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

    private static bool TryCreateTaggedTemplateDescriptor(
        TaggedTemplateExpression expression,
        out TaggedTemplateDescriptor descriptor,
        out string? failureReason)
    {
        if (expression.StringsArray is not ArrayExpression cookedArray)
        {
            descriptor = null!;
            failureReason = "Tagged template cooked strings must lower to an array literal.";
            return false;
        }

        if (expression.RawStringsArray is not ArrayExpression rawArray)
        {
            descriptor = null!;
            failureReason = "Tagged template raw strings must lower to an array literal.";
            return false;
        }

        if (!TryCompileTemplateArray(cookedArray, out var cookedStrings, out failureReason) ||
            !TryCompileTemplateArray(rawArray, out var rawStrings, out failureReason))
        {
            descriptor = null!;
            return false;
        }

        descriptor = new TaggedTemplateDescriptor(cookedStrings, rawStrings);
        failureReason = null;
        return true;
    }

    private static bool TryCompileTemplateArray(
        ArrayExpression expression,
        out ImmutableArray<JsValue> values,
        out string? failureReason)
    {
        var builder = ImmutableArray.CreateBuilder<JsValue>(expression.Elements.Length);
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                values = default;
                failureReason = "Tagged template arrays do not support spread elements.";
                return false;
            }

            if (element.Expression is not LiteralExpression literal)
            {
                values = default;
                failureReason = "Tagged template arrays must contain only literal elements.";
                return false;
            }

            builder.Add(literal.Value);
        }

        values = builder.MoveToImmutable();
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
            return TryCompileCompoundPropertyAssignmentExpression(expression, builder, out failureReason);
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
            return TryCompileCompoundIndexAssignmentExpression(expression, builder, out failureReason);
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

    private static bool TryCompileCompoundPropertyAssignmentExpression(
        PropertyAssignmentExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.Target is SuperExpression || HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional property assignments.";
            return false;
        }

        if (expression.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
        {
            failureReason = "Expression bytecode only supports literal property names for direct property assignment.";
            return false;
        }

        if (expression.Value is not BinaryExpression binary)
        {
            failureReason = "Expression bytecode only supports lowered binary property compound assignments.";
            return false;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        var propertyName = propertyLiteral.Value.AsString();
        builder.Add(new DuplicateTopExpressionOp());
        builder.Add(new GetNamedPropertyExpressionOp(propertyName));

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfFalseExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new SetNamedPropertyExpressionOp(propertyName));
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = new JumpIfFalseExpressionOp(shortCircuitStart);
                builder.Add(new SwapTopTwoExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfTrueExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new SetNamedPropertyExpressionOp(propertyName));
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = new JumpIfTrueExpressionOp(shortCircuitStart);
                builder.Add(new SwapTopTwoExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfNotNullishExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new SetNamedPropertyExpressionOp(propertyName));
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = new JumpIfNotNullishExpressionOp(shortCircuitStart);
                builder.Add(new SwapTopTwoExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }
        }

        if (!TryCompileExpression(binary.Right, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new BinaryExpressionOp(binary.Operator));
        builder.Add(new SetNamedPropertyExpressionOp(propertyName, AllowNameInference: false));
        failureReason = null;
        return true;
    }

    private static bool TryCompileCompoundIndexAssignmentExpression(
        IndexAssignmentExpression expression,
        List<ExpressionOp> builder,
        out string? failureReason)
    {
        if (expression.Target is SuperExpression || HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional index assignments.";
            return false;
        }

        if (expression.Value is not BinaryExpression binary)
        {
            failureReason = "Expression bytecode only supports lowered binary index compound assignments.";
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

        builder.Add(new DuplicateTopTwoExpressionOp());
        builder.Add(new GetComputedPropertyExpressionOp());

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfFalseExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new SetComputedPropertyExpressionOp());
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = new JumpIfFalseExpressionOp(shortCircuitStart);
                builder.Add(new RotateTopThreeRightExpressionOp());
                builder.Add(new PopExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfTrueExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new SetComputedPropertyExpressionOp());
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = new JumpIfTrueExpressionOp(shortCircuitStart);
                builder.Add(new RotateTopThreeRightExpressionOp());
                builder.Add(new PopExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(new JumpIfNotNullishExpressionOp(-1));
                builder.Add(new PopExpressionOp());

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(new SetComputedPropertyExpressionOp());
                var endJumpIndex = builder.Count;
                builder.Add(new JumpExpressionOp(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = new JumpIfNotNullishExpressionOp(shortCircuitStart);
                builder.Add(new RotateTopThreeRightExpressionOp());
                builder.Add(new PopExpressionOp());
                builder.Add(new PopExpressionOp());
                builder[endJumpIndex] = new JumpExpressionOp(builder.Count);
                failureReason = null;
                return true;
            }
        }

        if (!TryCompileExpression(binary.Right, builder, out failureReason))
        {
            return false;
        }

        builder.Add(new BinaryExpressionOp(binary.Operator));
        builder.Add(new SetComputedPropertyExpressionOp(AllowNameInference: false));
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

                case ObjectMemberKind.Method:
                    if (member.Function is null)
                    {
                        failureReason = "Object methods must carry a function payload.";
                        return false;
                    }

                    if (!member.IsComputed)
                    {
                        if (member.Key is not string methodName)
                        {
                            failureReason = "Expression bytecode only supports static string object property names.";
                            return false;
                        }

                        builder.Add(new LoadFunctionLiteralExpressionOp(member.Function, IsConstructorFunction: false));
                        builder.Add(new DefineObjectMethodExpressionOp(methodName));
                        break;
                    }

                    if (member.Key is not ExpressionNode methodKeyExpression)
                    {
                        failureReason = "Computed object property names must use an expression key.";
                        return false;
                    }

                    if (!TryCompileExpression(methodKeyExpression, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(new LoadFunctionLiteralExpressionOp(member.Function, IsConstructorFunction: false));
                    builder.Add(new DefineComputedObjectMethodExpressionOp());
                    break;

                case ObjectMemberKind.Getter:
                case ObjectMemberKind.Setter:
                    if (member.Function is null)
                    {
                        failureReason = "Object accessors must carry a function payload.";
                        return false;
                    }

                    var accessorKind = member.Kind == ObjectMemberKind.Getter
                        ? ObjectAccessorKind.Getter
                        : ObjectAccessorKind.Setter;

                    if (!member.IsComputed)
                    {
                        if (member.Key is not string accessorName)
                        {
                            failureReason = "Expression bytecode only supports static string object property names.";
                            return false;
                        }

                        builder.Add(new LoadFunctionLiteralExpressionOp(member.Function, IsConstructorFunction: false));
                        builder.Add(new DefineObjectAccessorExpressionOp(accessorName, accessorKind));
                        break;
                    }

                    if (member.Key is not ExpressionNode accessorKeyExpression)
                    {
                        failureReason = "Computed object property names must use an expression key.";
                        return false;
                    }

                    if (!TryCompileExpression(accessorKeyExpression, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(new LoadFunctionLiteralExpressionOp(member.Function, IsConstructorFunction: false));
                    builder.Add(new DefineComputedObjectAccessorExpressionOp(accessorKind));
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

    private static bool TryCompileOptionalMemberCallTarget(
        MemberExpression member,
        List<ExpressionOp> builder,
        ref int targetNullishJumpIndex,
        ref int targetShortCircuitJumpIndex,
        out string? failureReason)
    {
        if (member.Target is SuperExpression)
        {
            failureReason = "Expression bytecode does not yet support optional or super member call targets.";
            return false;
        }

        if (!TryCompileExpression(member.Target, builder, out failureReason))
        {
            return false;
        }

        if (HasOptionalChaining(member.Target))
        {
            targetShortCircuitJumpIndex = builder.Count;
            builder.Add(new JumpIfShortCircuitedExpressionOp(-1));
        }

        if (member.IsOptional)
        {
            targetNullishJumpIndex = builder.Count;
            builder.Add(new JumpIfNullishExpressionOp(-1, ReplaceWithUndefined: true));
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

    private static bool IsParenthesizedIdentifierAssignment(AssignmentExpression expression)
    {
        if (expression.Source is null)
        {
            return false;
        }

        var source = expression.Source.Source;
        var index = expression.Source.StartPosition - 1;
        while (index >= 0 && char.IsWhiteSpace(source, index))
        {
            index--;
        }

        return index >= 0 && source[index] == '(';
    }
}
