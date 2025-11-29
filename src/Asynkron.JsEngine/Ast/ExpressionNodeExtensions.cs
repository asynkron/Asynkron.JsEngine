using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(ExpressionNode? extendsExpression)
    {
        private (IJsEnvironmentAwareCallable? Constructor, JsObject? Prototype) ResolveSuperclass(JsEnvironment environment, EvaluationContext context)
        {
            if (extendsExpression is null)
            {
                return (null, null);
            }

            var baseValue = EvaluateExpression(extendsExpression, environment, context);
            if (context.ShouldStopEvaluation || baseValue is null)
            {
                return (null, null);
            }

            if (baseValue is not IJsEnvironmentAwareCallable callable ||
                baseValue is not IJsPropertyAccessor accessor)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "Class extends value is not a constructor or null", context, context.RealmState));
            }

            if (TryGetPropertyValue(baseValue, "prototype", out var prototypeValue) &&
                prototypeValue is JsObject prototype)
            {
                return (callable, prototype);
            }

            prototype = new JsObject();
            accessor.SetProperty("prototype", prototype);

            return (callable, prototype);
        }
    }

extension(ExpressionNode expression)
    {
        private object? EvaluateExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            context.SourceReference = expression.Source;

            return expression switch
            {
                LiteralExpression literal => EvaluateLiteral(literal, context),
                IdentifierExpression identifier => EvaluateIdentifier(identifier, environment, context),
                BinaryExpression binary => EvaluateBinary(binary, environment, context),
                UnaryExpression unary => EvaluateUnary(unary, environment, context),
                ConditionalExpression conditional => EvaluateConditional(conditional, environment, context),
                CallExpression call => EvaluateCall(call, environment, context),
                FunctionExpression functionExpression => CreateFunctionValue(functionExpression, environment, context),
                AssignmentExpression assignment => EvaluateAssignment(assignment, environment, context),
                DestructuringAssignmentExpression destructuringAssignment =>
                    EvaluateDestructuringAssignment(destructuringAssignment, environment, context),
                PropertyAssignmentExpression propertyAssignment =>
                    EvaluatePropertyAssignment(propertyAssignment, environment, context),
                IndexAssignmentExpression indexAssignment =>
                    EvaluateIndexAssignment(indexAssignment, environment, context),
                SequenceExpression sequence => EvaluateSequence(sequence, environment, context),
                MemberExpression member => EvaluateMember(member, environment, context),
                NewExpression newExpression => EvaluateNew(newExpression, environment, context),
                NewTargetExpression => environment.TryGet(Symbol.NewTarget, out var newTarget)
                    ? newTarget
                    : Symbol.Undefined,
                ArrayExpression array => EvaluateArray(array, environment, context),
                ObjectExpression obj => EvaluateObject(obj, environment, context),
                ClassExpression classExpression => EvaluateClassExpression(classExpression, environment, context),
                DecoratorExpression => throw new NotSupportedException("Decorators are not supported."),
                TemplateLiteralExpression template => EvaluateTemplateLiteral(template, environment, context),
                TaggedTemplateExpression taggedTemplate =>
                    EvaluateTaggedTemplate(taggedTemplate, environment, context),
                AwaitExpression awaitExpression => EvaluateAwait(awaitExpression, environment, context),
                YieldExpression yieldExpression => EvaluateYield(yieldExpression, environment, context),
                ThisExpression => environment.Get(Symbol.This),
                SuperExpression => throw new InvalidOperationException(
                    $"Super is not available in this context.{GetSourceInfo(context, expression.Source)}"),
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
            };
        }
    }

extension(ExpressionNode callee)
    {
        private (object? Callee, object? ThisValue, bool SkippedOptional) EvaluateCallTarget(JsEnvironment environment, EvaluationContext context)
        {
            if (callee is SuperExpression superExpression)
            {
                var binding = ExpectSuperBinding(environment, context);
                if (binding.Constructor is null)
                {
                    throw new InvalidOperationException(
                        $"Super constructor is not available in this context.{GetSourceInfo(context, superExpression.Source)}");
                }

                return (binding.Constructor, binding.ThisValue, false);
            }

            if (callee is MemberExpression member)
            {
                if (member.Target is SuperExpression)
                {
                    var (memberValue, binding) = ResolveSuperMember(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (Symbol.Undefined, binding.ThisValue, true);
                    }

                    return (memberValue, binding.ThisValue, false);
                }

                var target = EvaluateExpression(member.Target, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, null, true);
                }

                if (member.IsOptional && IsNullish(target))
                {
                    return (null, null, true);
                }

                var property = EvaluateExpression(member.Property, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, null, true);
                }

                var propertyName = JsOps.GetRequiredPropertyName(property, context);
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, null, true);
                }

                if (!TryGetPropertyValue(target, propertyName, out var value, context))
                {
                    return context.ShouldStopEvaluation
                        ? (Symbol.Undefined, null, true)
                        : (Symbol.Undefined, target, false);
                }

                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, null, true);
                }

                return (value, target, false);
            }

            var directCallee = EvaluateExpression(callee, environment, context);
            return (directCallee, null, false);
        }
    }

extension(ExpressionNode operand)
    {
        private bool EvaluateDelete(JsEnvironment environment, EvaluationContext context)
        {
            switch (operand)
            {
                case MemberExpression member:
                {
                    var target = EvaluateExpression(member.Target, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var propertyValue = EvaluateExpression(member.Property, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var deleted = DeletePropertyValue(target, propertyValue, context);
                    if (!deleted && context.CurrentScope.IsStrict)
                    {
                        throw StandardLibrary.ThrowTypeError("Cannot delete property", context, context.RealmState);
                    }

                    return deleted;
                }
                case IdentifierExpression identifier when context.CurrentScope.IsStrict:
                    throw StandardLibrary.ThrowSyntaxError(
                        "Delete of an unqualified identifier is not allowed in strict mode.",
                        context,
                        context.RealmState);
                case IdentifierExpression identifier:
                {
                    var outcome = environment.DeleteBinding(identifier.Name);
                    return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
                }
                default:
                    _ = EvaluateExpression(operand, environment, context);
                    return true;
            }
        }
    }

extension(ExpressionNode expression)
    {
        private string DescribeCallee()
        {
            return expression switch
            {
                IdentifierExpression id => id.Name.Name,
                MemberExpression member => $"{DescribeCallee(member.Target)}.{DescribeMemberName(member.Property)}",
                CallExpression call => $"{DescribeCallee(call.Callee)}(...)",
                _ => expression.GetType().Name
            };
        }
    }

extension(ExpressionNode property)
    {
        private string DescribeMemberName()
        {
            return property switch
            {
                LiteralExpression { Value: string s } => s,
                IdentifierExpression id => id.Name.Name,
                _ => property.GetType().Name
            };
        }
    }

extension(ExpressionNode expression)
    {
        private bool IsAnonymousFunctionDefinition()
        {
            return expression switch
            {
                FunctionExpression func => func.Name is null,
                ClassExpression classExpression => classExpression.Name is null,
                _ => false
            };
        }
    }

extension(ExpressionNode expression)
    {
        private bool ContainsDirectEvalCall()
        {
            while (true)
            {
                switch (expression)
                {
                    case CallExpression { IsOptional: false, Callee: IdentifierExpression { Name.Name: "eval" } }:
                        return true;
                    case CallExpression call:
                        if (ContainsDirectEvalCall(call.Callee))
                        {
                            return true;
                        }

                        foreach (var arg in call.Arguments)
                        {
                            if (ContainsDirectEvalCall(arg.Expression))
                            {
                                return true;
                            }
                        }

                        return false;
                    case BinaryExpression binary:
                        return ContainsDirectEvalCall(binary.Left) || ContainsDirectEvalCall(binary.Right);
                    case ConditionalExpression cond:
                        return ContainsDirectEvalCall(cond.Test) || ContainsDirectEvalCall(cond.Consequent) || ContainsDirectEvalCall(cond.Alternate);
                    case MemberExpression member:
                        return ContainsDirectEvalCall(member.Target) || ContainsDirectEvalCall(member.Property);
                    case UnaryExpression unary:
                        expression = unary.Operand;
                        continue;
                    case SequenceExpression seq:
                        return ContainsDirectEvalCall(seq.Left) || ContainsDirectEvalCall(seq.Right);
                    case ArrayExpression array:
                        foreach (var element in array.Elements)
                        {
                            if (element.Expression is not null && ContainsDirectEvalCall(element.Expression))
                            {
                                return true;
                            }
                        }

                        return false;
                    case ObjectExpression obj:
                        foreach (var member in obj.Members)
                        {
                            if (member.Value is not null && ContainsDirectEvalCall(member.Value))
                            {
                                return true;
                            }

                            if (member.Function is not null && ContainsDirectEvalCall(member.Function))
                            {
                                return true;
                            }
                        }

                        return false;
                    case TemplateLiteralExpression template:
                        foreach (var part in template.Parts)
                        {
                            if (part.Expression is not null && ContainsDirectEvalCall(part.Expression))
                            {
                                return true;
                            }
                        }

                        return false;
                    case TaggedTemplateExpression tagged:
                        if (ContainsDirectEvalCall(tagged.Tag) || ContainsDirectEvalCall(tagged.StringsArray) || ContainsDirectEvalCall(tagged.RawStringsArray))
                        {
                            return true;
                        }

                        foreach (var expr in tagged.Expressions)
                        {
                            if (ContainsDirectEvalCall(expr))
                            {
                                return true;
                            }
                        }

                        return false;
                    case FunctionExpression:
                        // Direct eval inside nested functions does not affect the parameter scope we are validating here.
                        return false;
                    default:
                        return false;
                }
            }
        }
    }

}
