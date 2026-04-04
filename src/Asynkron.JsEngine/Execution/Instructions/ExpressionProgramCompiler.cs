using System.Globalization;
using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Instructions;

internal static class ExpressionProgramCompiler
{
    public static ExpressionProgram PrependSuperReferenceCheck(ExpressionProgram program)
    {
        if (program.IsEmpty)
        {
            return new ExpressionProgram(
                [PackedExpressionOp.EnsureSuperReference],
                program.StringConstants,
                program.ObjectConstants,
                program.SpreadMaskConstants);
        }

        var builder = ImmutableArray.CreateBuilder<PackedExpressionOp>(program.Operations.Length + 1);
        builder.Add(PackedExpressionOp.EnsureSuperReference);
        builder.AddRange(program.Operations);
        return new ExpressionProgram(
            builder.MoveToImmutable(),
            program.StringConstants,
            program.ObjectConstants,
            program.SpreadMaskConstants);
    }

    public static bool TryCompile(
        ExpressionNode expression,
        out ExpressionProgram program,
        out string? failureReason)
    {
        var builder = new ExpressionProgramBuilder();
        if (!TryCompileExpression(expression, builder, out failureReason))
        {
            program = default;
            return false;
        }

        program = builder.Build();
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
            "Expression bytecode only supports static literal object property names."
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
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                builder.Add(PackedExpressionOp.LoadLiteral(literal.Value));
                failureReason = null;
                return true;

            case AssignmentExpression assignment:
                return TryCompileAssignmentExpression(assignment, builder, out failureReason);

            case DestructuringAssignmentExpression destructuringAssignment:
                return TryCompileDestructuringAssignmentExpression(destructuringAssignment, builder, out failureReason);

            case IdentifierExpression identifier:
                builder.Add(PackedExpressionOp.LoadIdentifier(
                    identifier.Name,
                    identifier.ScopeId,
                    identifier.SlotIndex,
                    identifier.FlatSlotId,
                    ReferenceEquals(identifier.Name, Symbol.Arguments)));
                failureReason = null;
                return true;

            case ThisExpression:
                builder.Add(PackedExpressionOp.LoadThis);
                failureReason = null;
                return true;

            case NewTargetExpression:
                builder.Add(PackedExpressionOp.LoadNewTarget);
                failureReason = null;
                return true;

            case RegexLiteralExpression regex:
                builder.Add(PackedExpressionOp.LoadRegexLiteral(builder.InternString(regex.Pattern), regex.Flags));
                failureReason = null;
                return true;

            case FunctionExpression function:
                builder.Add(PackedExpressionOp.LoadFunctionLiteral(builder.InternObject(function)));
                failureReason = null;
                return true;

            case ClassExpression classExpression:
                builder.Add(PackedExpressionOp.LoadClassLiteral(builder.InternObject(classExpression)));
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
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        // Special case: #privateField in obj — the LHS is a PrivateIdentifierExpression
        // which can't be compiled as a regular expression. Handle it as a dedicated op.
        if (expression is { Operator: BinaryOperator.In, Left: PrivateIdentifierExpression privateId })
        {
            if (!TryCompileExpression(expression.Right, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.PrivateFieldIn(builder.InternString(privateId.Name)));
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Left, builder, out failureReason))
        {
            return false;
        }

        switch (expression.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfFalse(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(expression.Right, builder, out failureReason))
                {
                    return false;
                }

                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfFalse(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfTrue(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(expression.Right, builder, out failureReason))
                {
                    return false;
                }

                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfTrue(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfNotNullish(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(expression.Right, builder, out failureReason))
                {
                    return false;
                }

                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfNotNullish(builder.Count);
                failureReason = null;
                return true;
            }
        }

        if (!TryCompileExpression(expression.Right, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.Binary(expression.Operator));
        failureReason = null;
        return true;
    }

    private static bool TryCompileUnaryExpression(
        UnaryExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        switch (expression.Operator)
        {
            case UnaryOperator.LogicalNot:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.UnaryLogicalNot);
                failureReason = null;
                return true;

            case UnaryOperator.TypeOf:
                if (expression.Operand is IdentifierExpression identifierForTypeOf)
                {
                    builder.Add(PackedExpressionOp.TypeOfIdentifier(
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

                builder.Add(PackedExpressionOp.TypeOf);
                failureReason = null;
                return true;

            case UnaryOperator.Delete:
                switch (expression.Operand)
                {
                    case IdentifierExpression identifierForDelete:
                        builder.Add(PackedExpressionOp.DeleteIdentifier(identifierForDelete.Name));
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

                        builder.Add(PackedExpressionOp.DeleteNamedProperty(
                            builder.InternString(propertyLiteral.Value.AsString())));
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

                        builder.Add(PackedExpressionOp.DeleteComputedProperty);
                        failureReason = null;
                        return true;

                    case MemberExpression { Target: SuperExpression } superDelete:
                        // Per ES spec 13.5.1.2: delete on a super reference always throws ReferenceError.
                        // Evaluate the property expression for side effects, then throw.
                        if (superDelete.IsComputed)
                        {
                            if (!TryCompileExpression(superDelete.Property, builder, out failureReason))
                            {
                                return false;
                            }

                            builder.Add(PackedExpressionOp.Pop);
                        }

                        builder.Add(PackedExpressionOp.ThrowReferenceError(
                            builder.InternString("Unsupported reference to 'super'")));
                        failureReason = null;
                        return true;

                    case MemberExpression { IsOptional: true }:
                        failureReason = "Expression bytecode does not yet support delete on optional member expressions.";
                        return false;
                }

                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.Pop);
                builder.Add(PackedExpressionOp.LoadLiteral(JsValue.True));
                failureReason = null;
                return true;

            case UnaryOperator.Plus:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.UnaryPlus);
                failureReason = null;
                return true;

            case UnaryOperator.Minus:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.UnaryMinus);
                failureReason = null;
                return true;

            case UnaryOperator.BitwiseNot:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.UnaryBitwiseNot);
                failureReason = null;
                return true;

            case UnaryOperator.Void:
                if (!TryCompileExpression(expression.Operand, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.UnaryVoid);
                failureReason = null;
                return true;

            case UnaryOperator.Increment:
            case UnaryOperator.Decrement:
                if (expression.Operand is IdentifierExpression identifier)
                {
                    builder.Add(PackedExpressionOp.UpdateIdentifier(
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

    private static bool TryCompileDestructuringAssignmentExpression(
        DestructuringAssignmentExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (!TryCompileExpression(expression.Value, builder, out failureReason))
        {
            return false;
        }

        if (!BindingTargetProgramCompiler.TryCompile(expression.Target, out var targetProgram, out var bindingFailure))
        {
            failureReason =
                $"Expression bytecode could not lower destructuring assignment target '{expression.Target.GetType().Name}': {bindingFailure ?? "unknown reason"}.";
            return false;
        }

        builder.Add(PackedExpressionOp.DuplicateTop);
        builder.Add(PackedExpressionOp.ApplyBindingTarget(builder.InternObject(targetProgram)));
        failureReason = null;
        return true;
    }

    private static bool TryCompileCallExpression(
        CallExpression expression,
        ExpressionProgramBuilder builder,
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
                break;

            case MemberExpression { Target: SuperExpression, IsComputed: false } member:
                if (member.Property is not LiteralExpression { Value.IsString: true } superPropertyLiteral)
                {
                    failureReason = "Expression bytecode only supports literal property names for direct member calls.";
                    return false;
                }

                builder.Add(PackedExpressionOp.LoadNamedSuperCallTarget(
                    builder.InternString(superPropertyLiteral.Value.AsString())));
                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
                }

                hasExplicitThis = true;
                break;

            case MemberExpression { Target: SuperExpression } member:
                builder.Add(PackedExpressionOp.EnsureSuperReference);
                if (!TryCompileExpression(member.Property, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.LoadComputedSuperCallTarget);
                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
                }

                hasExplicitThis = true;
                break;

            case IdentifierExpression identifier:
                if (!TryCompileExpression(identifier, builder, out failureReason))
                {
                    return false;
                }

                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
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
                builder.Add(PackedExpressionOp.LoadNamedCallTarget(builder.InternString(propertyName)));
                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
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

                builder.Add(PackedExpressionOp.LoadComputedCallTarget);
                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
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
                    builder.Add(PackedExpressionOp.JumpIfShortCircuited(-1));
                }

                if (expression.IsOptional)
                {
                    callNullishJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
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

        if (expression.Callee is SuperExpression)
        {
            builder.Add(PackedExpressionOp.SuperConstruct(
                expression.Arguments.Length,
                spreadMaskBuilder is not null
                    ? builder.InternSpreadMask(ImmutableArray.CreateRange(spreadMaskBuilder))
                    : -1));
        }
        else
        {
            builder.Add(PackedExpressionOp.Call(
                expression.Arguments.Length,
                HasExplicitThis: hasExplicitThis,
                IsDirectEval: isDirectEval,
                SpreadMaskConstantIndex: spreadMaskBuilder is not null
                    ? builder.InternSpreadMask(ImmutableArray.CreateRange(spreadMaskBuilder))
                    : -1));
        }

        if (callNullishJumpIndex >= 0)
        {
            if (hasExplicitThis)
            {
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var cleanupIndex = builder.Count;
                builder[callNullishJumpIndex] = PackedExpressionOp.JumpIfNullish(cleanupIndex, ReplaceWithUndefined: true);
                builder.Add(PackedExpressionOp.SwapTopTwo);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
            }
            else
            {
                builder[callNullishJumpIndex] = PackedExpressionOp.JumpIfNullish(builder.Count, ReplaceWithUndefined: true);
            }
        }

        if (targetNullishJumpIndex >= 0)
        {
            builder[targetNullishJumpIndex] = PackedExpressionOp.JumpIfNullish(builder.Count, ReplaceWithUndefined: true);
        }

        if (targetShortCircuitJumpIndex >= 0)
        {
            builder[targetShortCircuitJumpIndex] = PackedExpressionOp.JumpIfShortCircuited(builder.Count);
        }

        if (calleeShortCircuitJumpIndex >= 0)
        {
            builder[calleeShortCircuitJumpIndex] = PackedExpressionOp.JumpIfShortCircuited(builder.Count);
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileAssignmentExpression(
        AssignmentExpression expression,
        ExpressionProgramBuilder builder,
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

        builder.Add(PackedExpressionOp.StoreIdentifier(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            AllowNameInference: ShouldAllowAssignmentNameInference(expression)));
        failureReason = null;
        return true;
    }

    private static bool TryCompileCompoundAssignmentExpression(
        AssignmentExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (expression.Value is not BinaryExpression binary)
        {
            failureReason = "Expression bytecode only supports lowered binary compound assignments.";
            return false;
        }

        var storeOp = PackedExpressionOp.StoreIdentifier(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            AllowNameInference: ShouldAllowAssignmentNameInference(expression));
        var loadOp = PackedExpressionOp.LoadIdentifier(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            ReferenceEquals(expression.Target, Symbol.Arguments));

        // For logical compound assignments (&&=, ||=, ??=), the assignment value
        // is lowered to BinaryExpression(op, lhs, rhs). NamedEvaluation applies to
        // the actual RHS (binary.Right), not the whole BinaryExpression. Create a
        // separate store op that checks binary.Right for anonymous function defs.
        var logicalStoreOp = PackedExpressionOp.StoreIdentifier(
            expression.Target,
            expression.ScopeId,
            expression.SlotIndex,
            expression.FlatSlotId,
            AllowNameInference: IsAnonymousFunctionDefinitionForNameInference(binary.Right) &&
                                !IsParenthesizedIdentifierAssignment(expression));

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                builder.Add(loadOp);
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfFalse(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(logicalStoreOp);
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfFalse(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                builder.Add(loadOp);
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfTrue(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(logicalStoreOp);
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfTrue(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                builder.Add(loadOp);
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfNotNullish(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(logicalStoreOp);
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfNotNullish(builder.Count);
                failureReason = null;
                return true;
            }

            default:
                builder.Add(loadOp);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.Binary(binary.Operator));
                builder.Add(storeOp);
                failureReason = null;
                return true;
        }
    }

    private static bool TryCompileNewExpression(
        NewExpression expression,
        ExpressionProgramBuilder builder,
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

        builder.Add(PackedExpressionOp.Construct(
            expression.Arguments.Length,
            SpreadMaskConstantIndex: spreadMaskBuilder is not null
                ? builder.InternSpreadMask(ImmutableArray.CreateRange(spreadMaskBuilder))
                : -1));
        failureReason = null;
        return true;
    }

    private static bool TryCompileMemberUpdateExpression(
        MemberExpression expression,
        bool isIncrement,
        bool isPrefix,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (HasOptionalChaining(expression.Target) || expression.IsOptional)
        {
            failureReason = "Expression bytecode does not yet support super or optional member update expressions.";
            return false;
        }

        if (expression.Target is SuperExpression)
        {
            if (!expression.IsComputed)
            {
                if (expression.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
                {
                    failureReason = "Expression bytecode only supports literal property names for member updates.";
                    return false;
                }

                builder.Add(PackedExpressionOp.UpdateNamedSuperProperty(
                    builder.InternString(propertyLiteral.Value.AsString()),
                    IsIncrement: isIncrement,
                    IsPrefix: isPrefix));
                failureReason = null;
                return true;
            }

            builder.Add(PackedExpressionOp.EnsureSuperReference);
            builder.Add(PackedExpressionOp.EnsureSuperReference);
            if (!TryCompileExpression(expression.Property, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.UpdateComputedSuperProperty(
                IsIncrement: isIncrement,
                IsPrefix: isPrefix));
            failureReason = null;
            return true;
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

            builder.Add(PackedExpressionOp.UpdateNamedProperty(
                builder.InternString(propertyLiteral.Value.AsString()),
                IsIncrement: isIncrement,
                IsPrefix: isPrefix));
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Property, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.UpdateComputedProperty(
            IsIncrement: isIncrement,
            IsPrefix: isPrefix));
        failureReason = null;
        return true;
    }

    private static bool TryCompileSequenceExpression(
        SequenceExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (!TryCompileExpression(expression.Left, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.Pop);

        if (!TryCompileExpression(expression.Right, builder, out failureReason))
        {
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileTaggedTemplateExpression(
        TaggedTemplateExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        var hasExplicitThis = false;
        var targetNullishJumpIndex = -1;
        var targetShortCircuitJumpIndex = -1;
        var calleeShortCircuitJumpIndex = -1;

        switch (expression.Tag)
        {
            case SuperExpression:
                failureReason = "Expression bytecode does not yet support super tagged templates.";
                return false;

            case MemberExpression { Target: SuperExpression, IsComputed: false } member:
                if (member.Property is not LiteralExpression { Value.IsString: true } superPropertyLiteral)
                {
                    failureReason =
                        "Expression bytecode only supports literal property names for tagged template member access.";
                    return false;
                }

                builder.Add(PackedExpressionOp.LoadNamedSuperCallTarget(
                    builder.InternString(superPropertyLiteral.Value.AsString())));
                hasExplicitThis = true;
                break;

            case MemberExpression { Target: SuperExpression } member:
                builder.Add(PackedExpressionOp.EnsureSuperReference);
                if (!TryCompileExpression(member.Property, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.LoadComputedSuperCallTarget);
                hasExplicitThis = true;
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
                    failureReason = "Expression bytecode only supports literal property names for tagged template member access.";
                    return false;
                }

                builder.Add(PackedExpressionOp.LoadNamedCallTarget(
                    builder.InternString(propertyLiteral.Value.AsString())));
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

                builder.Add(PackedExpressionOp.LoadComputedCallTarget);
                hasExplicitThis = true;
                break;

            default:
                if (!TryCompileExpression(expression.Tag, builder, out failureReason))
                {
                    return false;
                }

                if (HasOptionalChaining(expression.Tag))
                {
                    calleeShortCircuitJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfShortCircuited(-1));
                }
                break;
        }

        if (!TryCreateTaggedTemplateDescriptor(expression, out var descriptor, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.LoadTemplateObject(builder.InternObject(descriptor)));

        foreach (var templateExpression in expression.Expressions)
        {
            if (!TryCompileExpression(templateExpression, builder, out failureReason))
            {
                return false;
            }
        }

        builder.Add(PackedExpressionOp.Call(
            expression.Expressions.Length + 1,
            HasExplicitThis: hasExplicitThis));

        if (targetNullishJumpIndex >= 0)
        {
            builder[targetNullishJumpIndex] = PackedExpressionOp.JumpIfNullish(builder.Count, ReplaceWithUndefined: true);
        }

        if (targetShortCircuitJumpIndex >= 0)
        {
            builder[targetShortCircuitJumpIndex] = PackedExpressionOp.JumpIfShortCircuited(builder.Count);
        }

        if (calleeShortCircuitJumpIndex >= 0)
        {
            builder[calleeShortCircuitJumpIndex] = PackedExpressionOp.JumpIfShortCircuited(builder.Count);
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileTemplateLiteralExpression(
        TemplateLiteralExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        builder.Add(PackedExpressionOp.LoadLiteral(new JsValue(string.Empty)));

        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
            {
                builder.Add(PackedExpressionOp.LoadLiteral(new JsValue(part.Text)));
                builder.Add(PackedExpressionOp.Binary(BinaryOperator.Add));
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

            builder.Add(PackedExpressionOp.ToStringValue);
            builder.Add(PackedExpressionOp.Binary(BinaryOperator.Add));
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
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (expression.IsCompoundAssignment)
        {
            return TryCompileCompoundPropertyAssignmentExpression(expression, builder, out failureReason);
        }

        if (HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional property assignments.";
            return false;
        }

        if (expression.Target is SuperExpression)
        {
            if (expression.IsComputed)
            {
                builder.Add(PackedExpressionOp.EnsureSuperReference);
                if (!TryCompileExpression(expression.Property, builder, out failureReason))
                {
                    return false;
                }

                if (!TryCompileExpression(expression.Value, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetComputedSuperProperty());
                failureReason = null;
                return true;
            }

            if (expression.Property is not LiteralExpression { Value.IsString: true } superPropertyLiteral)
            {
                failureReason = "Expression bytecode only supports literal property names for direct property assignment.";
                return false;
            }

            if (!TryCompileExpression(expression.Value, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.SetNamedSuperProperty(
                builder.InternString(superPropertyLiteral.Value.AsString())));
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        if (expression.IsComputed)
        {
            builder.Add(PackedExpressionOp.EnsureSuperReference);
            if (!TryCompileExpression(expression.Property, builder, out failureReason))
            {
                return false;
            }

            if (!TryCompileExpression(expression.Value, builder, out failureReason))
            {
                return false;
            }

            // Per ES spec, assignment to MemberExpression does NOT trigger NamedEvaluation.
            builder.Add(PackedExpressionOp.SetComputedProperty(AllowNameInference: false));
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

        // Per ES spec, assignment to MemberExpression does NOT trigger NamedEvaluation.
        // Only assignments to IdentifierRef get name inference (handled by AssignmentExpression).
        builder.Add(PackedExpressionOp.SetNamedProperty(
            builder.InternString(propertyLiteral.Value.AsString()),
            AllowNameInference: false));
        failureReason = null;
        return true;
    }

    private static bool TryCompileIndexAssignmentExpression(
        IndexAssignmentExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (expression.IsCompoundAssignment)
        {
            return TryCompileCompoundIndexAssignmentExpression(expression, builder, out failureReason);
        }

        if (HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional index assignments.";
            return false;
        }

        if (expression.Target is SuperExpression)
        {
            builder.Add(PackedExpressionOp.EnsureSuperReference);
            if (!TryCompileExpression(expression.Index, builder, out failureReason))
            {
                return false;
            }

            if (!TryCompileExpression(expression.Value, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.SetComputedSuperProperty());
            failureReason = null;
            return true;
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

        builder.Add(PackedExpressionOp.SetComputedProperty());
        failureReason = null;
        return true;
    }

    private static bool TryCompileCompoundPropertyAssignmentExpression(
        PropertyAssignmentExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (HasOptionalChaining(expression.Target))
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

        var propertyName = propertyLiteral.Value.AsString();
        var propertyNameIndex = builder.InternString(propertyName);
        if (expression.Target is SuperExpression)
        {
            builder.Add(PackedExpressionOp.GetNamedSuperProperty(propertyNameIndex));

            switch (binary.Operator)
            {
                case BinaryOperator.LogicalAnd:
                {
                    var shortCircuitIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfFalse(-1));
                    builder.Add(PackedExpressionOp.Pop);

                    if (!TryCompileExpression(binary.Right, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(PackedExpressionOp.SetNamedSuperProperty(propertyNameIndex));
                    builder[shortCircuitIndex] = PackedExpressionOp.JumpIfFalse(builder.Count);
                    failureReason = null;
                    return true;
                }

                case BinaryOperator.LogicalOr:
                {
                    var shortCircuitIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfTrue(-1));
                    builder.Add(PackedExpressionOp.Pop);

                    if (!TryCompileExpression(binary.Right, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(PackedExpressionOp.SetNamedSuperProperty(propertyNameIndex));
                    builder[shortCircuitIndex] = PackedExpressionOp.JumpIfTrue(builder.Count);
                    failureReason = null;
                    return true;
                }

                case BinaryOperator.NullishCoalescing:
                {
                    var shortCircuitIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNotNullish(-1));
                    builder.Add(PackedExpressionOp.Pop);

                    if (!TryCompileExpression(binary.Right, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(PackedExpressionOp.SetNamedSuperProperty(propertyNameIndex));
                    builder[shortCircuitIndex] = PackedExpressionOp.JumpIfNotNullish(builder.Count);
                    failureReason = null;
                    return true;
                }
            }

            if (!TryCompileExpression(binary.Right, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.Binary(binary.Operator));
            builder.Add(PackedExpressionOp.SetNamedSuperProperty(
                propertyNameIndex,
                AllowNameInference: false));
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.DuplicateTop);
        builder.Add(PackedExpressionOp.GetNamedProperty(propertyNameIndex));

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfFalse(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetNamedProperty(propertyNameIndex));
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfFalse(shortCircuitStart);
                builder.Add(PackedExpressionOp.SwapTopTwo);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfTrue(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetNamedProperty(propertyNameIndex));
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfTrue(shortCircuitStart);
                builder.Add(PackedExpressionOp.SwapTopTwo);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfNotNullish(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetNamedProperty(propertyNameIndex));
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfNotNullish(shortCircuitStart);
                builder.Add(PackedExpressionOp.SwapTopTwo);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                failureReason = null;
                return true;
            }
        }

        if (!TryCompileExpression(binary.Right, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.Binary(binary.Operator));
        builder.Add(PackedExpressionOp.SetNamedProperty(
            propertyNameIndex,
            AllowNameInference: false));
        failureReason = null;
        return true;
    }

    private static bool TryCompileCompoundIndexAssignmentExpression(
        IndexAssignmentExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (HasOptionalChaining(expression.Target))
        {
            failureReason = "Expression bytecode does not yet support super or optional index assignments.";
            return false;
        }

        if (expression.Value is not BinaryExpression binary)
        {
            failureReason = "Expression bytecode only supports lowered binary index compound assignments.";
            return false;
        }

        if (expression.Target is SuperExpression)
        {
            builder.Add(PackedExpressionOp.EnsureSuperReference);
            if (!TryCompileExpression(expression.Index, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.DuplicateTop);
            builder.Add(PackedExpressionOp.GetComputedSuperProperty);

            switch (binary.Operator)
            {
                case BinaryOperator.LogicalAnd:
                {
                    var shortCircuitIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfFalse(-1));
                    builder.Add(PackedExpressionOp.Pop);

                    if (!TryCompileExpression(binary.Right, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(PackedExpressionOp.SetComputedSuperProperty());
                    var endJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.Jump(-1));

                    var shortCircuitStart = builder.Count;
                    builder[shortCircuitIndex] = PackedExpressionOp.JumpIfFalse(shortCircuitStart);
                    builder.Add(PackedExpressionOp.SwapTopTwo);
                    builder.Add(PackedExpressionOp.Pop);
                    builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                    failureReason = null;
                    return true;
                }

                case BinaryOperator.LogicalOr:
                {
                    var shortCircuitIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfTrue(-1));
                    builder.Add(PackedExpressionOp.Pop);

                    if (!TryCompileExpression(binary.Right, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(PackedExpressionOp.SetComputedSuperProperty());
                    var endJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.Jump(-1));

                    var shortCircuitStart = builder.Count;
                    builder[shortCircuitIndex] = PackedExpressionOp.JumpIfTrue(shortCircuitStart);
                    builder.Add(PackedExpressionOp.SwapTopTwo);
                    builder.Add(PackedExpressionOp.Pop);
                    builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                    failureReason = null;
                    return true;
                }

                case BinaryOperator.NullishCoalescing:
                {
                    var shortCircuitIndex = builder.Count;
                    builder.Add(PackedExpressionOp.JumpIfNotNullish(-1));
                    builder.Add(PackedExpressionOp.Pop);

                    if (!TryCompileExpression(binary.Right, builder, out failureReason))
                    {
                        return false;
                    }

                    builder.Add(PackedExpressionOp.SetComputedSuperProperty());
                    var endJumpIndex = builder.Count;
                    builder.Add(PackedExpressionOp.Jump(-1));

                    var shortCircuitStart = builder.Count;
                    builder[shortCircuitIndex] = PackedExpressionOp.JumpIfNotNullish(shortCircuitStart);
                    builder.Add(PackedExpressionOp.SwapTopTwo);
                    builder.Add(PackedExpressionOp.Pop);
                    builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                    failureReason = null;
                    return true;
                }
            }

            if (!TryCompileExpression(binary.Right, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.Binary(binary.Operator));
            builder.Add(PackedExpressionOp.SetComputedSuperProperty(AllowNameInference: false));
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Target, builder, out failureReason))
        {
            return false;
        }

        if (!TryCompileExpression(expression.Index, builder, out failureReason))
        {
            return false;
        }

        // Per ES spec 13.15.2 step 1.e: RequireObjectCoercible(base) BEFORE ToPropertyKey(index).
        // Stack is [target, index]. Check target (depth 1) is not null/undefined before resolving key.
        builder.Add(PackedExpressionOp.RequireObjectCoercible(Depth: 1));

        // Resolve the property key once before duplicating, so both Get and Set
        // use the already-resolved string. This ensures ToPropertyKey is called
        // exactly once per the ECMAScript spec (e.g. S11.13.2_A7.1_T4).
        builder.Add(PackedExpressionOp.ResolvePropertyKey);
        builder.Add(PackedExpressionOp.DuplicateTopTwo);
        builder.Add(PackedExpressionOp.GetComputedProperty());

        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfFalse(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetComputedProperty());
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfFalse(shortCircuitStart);
                builder.Add(PackedExpressionOp.RotateTopThreeRight);
                builder.Add(PackedExpressionOp.Pop);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.LogicalOr:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfTrue(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetComputedProperty());
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfTrue(shortCircuitStart);
                builder.Add(PackedExpressionOp.RotateTopThreeRight);
                builder.Add(PackedExpressionOp.Pop);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                failureReason = null;
                return true;
            }

            case BinaryOperator.NullishCoalescing:
            {
                var shortCircuitIndex = builder.Count;
                builder.Add(PackedExpressionOp.JumpIfNotNullish(-1));
                builder.Add(PackedExpressionOp.Pop);

                if (!TryCompileExpression(binary.Right, builder, out failureReason))
                {
                    return false;
                }

                builder.Add(PackedExpressionOp.SetComputedProperty());
                var endJumpIndex = builder.Count;
                builder.Add(PackedExpressionOp.Jump(-1));

                var shortCircuitStart = builder.Count;
                builder[shortCircuitIndex] = PackedExpressionOp.JumpIfNotNullish(shortCircuitStart);
                builder.Add(PackedExpressionOp.RotateTopThreeRight);
                builder.Add(PackedExpressionOp.Pop);
                builder.Add(PackedExpressionOp.Pop);
                builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
                failureReason = null;
                return true;
            }
        }

        if (!TryCompileExpression(binary.Right, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.Binary(binary.Operator));
        builder.Add(PackedExpressionOp.SetComputedProperty(AllowNameInference: false));
        failureReason = null;
        return true;
    }

    private static bool TryCompileConditionalExpression(
        ConditionalExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (!TryCompileExpression(expression.Test, builder, out failureReason))
        {
            return false;
        }

        var falseBranchJumpIndex = builder.Count;
        builder.Add(PackedExpressionOp.JumpIfFalse(-1));
        builder.Add(PackedExpressionOp.Pop);

        if (!TryCompileExpression(expression.Consequent, builder, out failureReason))
        {
            return false;
        }

        var endJumpIndex = builder.Count;
        builder.Add(PackedExpressionOp.Jump(-1));

        var alternateStartIndex = builder.Count;
        builder[falseBranchJumpIndex] = PackedExpressionOp.JumpIfFalse(alternateStartIndex);
        builder.Add(PackedExpressionOp.Pop);

        if (!TryCompileExpression(expression.Alternate, builder, out failureReason))
        {
            return false;
        }

        builder[endJumpIndex] = PackedExpressionOp.Jump(builder.Count);
        failureReason = null;
        return true;
    }

    private static bool TryCompileMemberExpression(
        MemberExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        if (expression.Target is SuperExpression)
        {
            if (!expression.IsComputed)
            {
                if (expression.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
                {
                    failureReason = "Expression bytecode only supports literal property names for dot access.";
                    return false;
                }

                builder.Add(PackedExpressionOp.GetNamedSuperProperty(
                    builder.InternString(propertyLiteral.Value.AsString())));
                failureReason = null;
                return true;
            }

            builder.Add(PackedExpressionOp.EnsureSuperReference);
            if (!TryCompileExpression(expression.Property, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.GetComputedSuperProperty);
            failureReason = null;
            return true;
        }

        if (expression is { IsComputed: false, Target: IdentifierExpression { Name.Name: "Symbol" }, Property: LiteralExpression { Value.IsString: true } symbolProp })
        {
            switch (symbolProp.Value.AsString())
            {
                case "iterator":
                    builder.Add(PackedExpressionOp.LoadLiteral((JsValue)Symbols.Iterator));
                    failureReason = null;
                    return true;
                case "asyncIterator":
                    builder.Add(PackedExpressionOp.LoadLiteral((JsValue)Symbols.AsyncIterator));
                    failureReason = null;
                    return true;
                case "toStringTag":
                    builder.Add(PackedExpressionOp.LoadLiteral((JsValue)Symbols.ToStringTag));
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

            builder.Add(PackedExpressionOp.GetNamedProperty(
                builder.InternString(propertyLiteral.Value.AsString()),
                IsOptional: expression.IsOptional,
                ShortCircuitOnNullishTarget: shortCircuitOnNullishTarget));
            failureReason = null;
            return true;
        }

        if (expression.IsOptional)
        {
            var endIndex = builder.Count;
            builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));

            if (!TryCompileExpression(expression.Property, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.GetComputedProperty(shortCircuitOnNullishTarget));
            builder[endIndex] = PackedExpressionOp.JumpIfNullish(builder.Count, ReplaceWithUndefined: true);
            failureReason = null;
            return true;
        }

        if (!TryCompileExpression(expression.Property, builder, out failureReason))
        {
            return false;
        }

        builder.Add(PackedExpressionOp.GetComputedProperty(shortCircuitOnNullishTarget));
        failureReason = null;
        return true;
    }

    private static bool TryCompileArrayExpression(
        ArrayExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        builder.Add(PackedExpressionOp.CreateArray);

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

                builder.Add(PackedExpressionOp.ArraySpread);
                continue;
            }

            if (element.Expression is null)
            {
                builder.Add(PackedExpressionOp.ArrayPushHole);
                continue;
            }

            if (!TryCompileExpression(element.Expression, builder, out failureReason))
            {
                return false;
            }

            builder.Add(PackedExpressionOp.ArrayPush);
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileObjectExpression(
        ObjectExpression expression,
        ExpressionProgramBuilder builder,
        out string? failureReason)
    {
        builder.Add(PackedExpressionOp.CreateObject);

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
                            builder.Add(PackedExpressionOp.LoadLiteral(JsValue.Undefined));
                        }

                        if (!TryGetStaticObjectPropertyName(member.Key, out var propertyName))
                        {
                            failureReason = "Expression bytecode only supports static literal object property names.";
                            return false;
                        }

                        builder.Add(PackedExpressionOp.DefineObjectProperty(
                            builder.InternString(propertyName),
                            IsPrototypeMutation: member.Kind == ObjectMemberKind.Property &&
                                                 member.Parameter is null &&
                                                 string.Equals(propertyName, "__proto__", StringComparison.Ordinal),
                            AllowNameInference: IsAnonymousFunctionDefinitionForNameInference(member.Value)));
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

                    builder.Add(PackedExpressionOp.ResolvePropertyKey);

                    if (member.Value is not null)
                    {
                        if (!TryCompileExpression(member.Value, builder, out failureReason))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        builder.Add(PackedExpressionOp.LoadLiteral(JsValue.Undefined));
                    }

                    builder.Add(PackedExpressionOp.DefineComputedObjectProperty(
                        AllowNameInference: IsAnonymousFunctionDefinitionForNameInference(member.Value)));
                    break;

                case ObjectMemberKind.Method:
                    if (member.Function is null)
                    {
                        failureReason = "Object methods must carry a function payload.";
                        return false;
                    }

                    if (!member.IsComputed)
                    {
                        if (!TryGetStaticObjectPropertyName(member.Key, out var methodName))
                        {
                            failureReason = "Expression bytecode only supports static literal object property names.";
                            return false;
                        }

                        builder.Add(PackedExpressionOp.LoadFunctionLiteral(
                            builder.InternObject(member.Function),
                            IsConstructorFunction: false));
                        builder.Add(PackedExpressionOp.DefineObjectMethod(builder.InternString(methodName)));
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

                    builder.Add(PackedExpressionOp.ResolvePropertyKey);
                    builder.Add(PackedExpressionOp.LoadFunctionLiteral(
                        builder.InternObject(member.Function),
                        IsConstructorFunction: false));
                    builder.Add(PackedExpressionOp.DefineComputedObjectMethod);
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
                        if (!TryGetStaticObjectPropertyName(member.Key, out var accessorName))
                        {
                            failureReason = "Expression bytecode only supports static literal object property names.";
                            return false;
                        }

                        builder.Add(PackedExpressionOp.LoadFunctionLiteral(
                            builder.InternObject(member.Function),
                            IsConstructorFunction: false));
                        builder.Add(PackedExpressionOp.DefineObjectAccessor(
                            builder.InternString(accessorName),
                            accessorKind));
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

                    builder.Add(PackedExpressionOp.ResolvePropertyKey);
                    builder.Add(PackedExpressionOp.LoadFunctionLiteral(
                        builder.InternObject(member.Function),
                        IsConstructorFunction: false));
                    builder.Add(PackedExpressionOp.DefineComputedObjectAccessor(accessorKind));
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

                    builder.Add(PackedExpressionOp.ObjectSpread);
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
        ExpressionProgramBuilder builder,
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
            builder.Add(PackedExpressionOp.JumpIfShortCircuited(-1));
        }

        if (member.IsOptional)
        {
            targetNullishJumpIndex = builder.Count;
            builder.Add(PackedExpressionOp.JumpIfNullish(-1, ReplaceWithUndefined: true));
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

    private static bool ShouldAllowAssignmentNameInference(AssignmentExpression expression)
    {
        return IsAnonymousFunctionDefinitionForNameInference(expression.Value);
    }

    private static bool TryGetStaticObjectPropertyName(object key, out string propertyName)
    {
        switch (key)
        {
            case string value:
                propertyName = value;
                return true;
            case double number:
                propertyName = number.ToString(CultureInfo.InvariantCulture);
                return true;
            case JsBigInt bigInt:
                propertyName = bigInt.Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case bool boolean:
                propertyName = boolean ? "true" : "false";
                return true;
            case null:
                propertyName = "null";
                return true;
            case IFormattable formattable:
                propertyName = formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            default:
                propertyName = key.ToString() ?? string.Empty;
                return key is not ExpressionNode;
        }
    }

    private static bool IsAnonymousFunctionDefinitionForNameInference(ExpressionNode? expression)
    {
        return expression switch
        {
            SequenceExpression => false,
            FunctionExpression { Name: null } => true,
            ClassExpression { Name: null } => true,
            _ => false
        };
    }

    private sealed class ExpressionProgramBuilder
    {
        private readonly List<PackedExpressionOp> _operations = [];
        private readonly List<string> _stringConstants = [];
        private readonly Dictionary<string, int> _stringConstantMap = new(StringComparer.Ordinal);
        private readonly List<object> _objectConstants = [];
        private readonly Dictionary<object, int> _objectConstantMap = new(ReferenceEqualityComparer<object>.Instance);
        private readonly List<ImmutableArray<bool>> _spreadMaskConstants = [];
        private readonly Dictionary<ImmutableArray<bool>, int> _spreadMaskConstantMap = [];

        public int Count => _operations.Count;

        public PackedExpressionOp this[int index]
        {
            get => _operations[index];
            set => _operations[index] = value;
        }

        public void Add(PackedExpressionOp operation)
        {
            _operations.Add(operation);
        }

        public int InternString(string value)
        {
            if (_stringConstantMap.TryGetValue(value, out var existingIndex))
            {
                return existingIndex;
            }

            var index = _stringConstants.Count;
            _stringConstants.Add(value);
            _stringConstantMap[value] = index;
            return index;
        }

        public int InternObject<T>(T value)
            where T : class
        {
            if (_objectConstantMap.TryGetValue(value, out var existingIndex))
            {
                return existingIndex;
            }

            var index = _objectConstants.Count;
            _objectConstants.Add(value);
            _objectConstantMap[value] = index;
            return index;
        }

        public int InternSpreadMask(ImmutableArray<bool> value)
        {
            if (_spreadMaskConstantMap.TryGetValue(value, out var existingIndex))
            {
                return existingIndex;
            }

            var index = _spreadMaskConstants.Count;
            _spreadMaskConstants.Add(value);
            _spreadMaskConstantMap[value] = index;
            return index;
        }

        public ExpressionProgram Build()
        {
            return new ExpressionProgram(
                [.. _operations],
                _stringConstants.Count == 0 ? ImmutableArray<string>.Empty : [.. _stringConstants],
                _objectConstants.Count == 0 ? ImmutableArray<object>.Empty : [.. _objectConstants],
                _spreadMaskConstants.Count == 0 ? ImmutableArray<ImmutableArray<bool>>.Empty : [.. _spreadMaskConstants]);
        }
    }
}
