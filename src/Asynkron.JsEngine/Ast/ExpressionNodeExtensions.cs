using System.Diagnostics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ExpressionNode? extendsExpression)
    {
        private (IJsEnvironmentAwareCallable? Constructor, IJsPropertyAccessor? Prototype) ResolveSuperclass(
            JsEnvironment environment, EvaluationContext context)
        {
            if (extendsExpression is null)
            {
                return (null, null);
            }

            var baseJsValue = EvaluateExpression(extendsExpression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (null, null);
            }

            if (baseJsValue.IsNullOrUndefined)
            {
                return (null, null);
            }

            var baseValue = baseJsValue.ToObject();

            if (!JsOps.IsConstructor(baseValue))
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "Class extends value is not a constructor or null", context, context.RealmState));
            }

            // Proxy cannot be subclassed because its prototype is undefined.
            if (baseValue is IJsPropertyAccessor accessorWithMarker &&
                TryGetPropertyValue(accessorWithMarker, "__proxyHasNoPrototype__", out var marker, context) &&
                JsOps.ToBoolean(marker))
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "Class extends value does not have a valid prototype",
                    context,
                    context.RealmState));
            }

            if (baseValue is not IJsEnvironmentAwareCallable callable ||
                baseValue is not IJsPropertyAccessor accessor)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "Class extends value is not a constructor or null", context, context.RealmState));
            }

            var hasPrototype = TryGetPropertyValue(baseValue, "prototype", out var prototypeValue, context);
            if (context.ShouldStopEvaluation)
            {
                return (null, null);
            }

            if (!hasPrototype)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "Class extends value does not have a valid prototype",
                    context,
                    context.RealmState));
            }

            if (prototypeValue is null)
            {
                return (callable, null);
            }

            if (prototypeValue is IJsPropertyAccessor prototype)
            {
                return (callable, prototype);
            }

            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState));
        }
    }

    extension(ExpressionNode expression)
    {
        private JsValue EvaluateExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            context.SourceReference = expression.Source;
            using var expressionActivity = Activity.Current?
                .StartEvaluatorActivity($"Expression:{expression.GetType().Name}", context, expression.Source);

            return expression switch
            {
                // Converted to native JsValue
                LiteralExpression literal => EvaluateLiteral(literal, context),
                IdentifierExpression identifier => EvaluateIdentifier(identifier, environment, context),
                BinaryExpression binary => EvaluateBinary(binary, environment, context),
                UnaryExpression unary => EvaluateUnary(unary, environment, context),

                // Converted to native JsValue (Batch A)
                ConditionalExpression conditional => EvaluateConditional(conditional, environment, context),
                CallExpression call => EvaluateCall(call, environment, context),
                FunctionExpression functionExpression => JsValue.FromObject(CreateFunctionValue(functionExpression, environment, context,
                    createFunctionNameEnvironment: true)),
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
                    ? JsValue.FromObject(newTarget)
                    : JsValue.Undefined,
                ImportMetaExpression => EvaluateImportMeta(environment, context),
                ArrayExpression array => EvaluateArray(array, environment, context),
                ObjectExpression obj => EvaluateObject(obj, environment, context),
                ClassExpression classExpression => EvaluateClassExpression(classExpression, environment, context),
                DecoratorExpression => throw new NotSupportedException("Decorators are not supported."),
                TemplateLiteralExpression template => EvaluateTemplateLiteral(template, environment, context),
                TaggedTemplateExpression taggedTemplate => EvaluateTaggedTemplate(taggedTemplate, environment, context),
                AwaitExpression awaitExpression => EvaluateAwait(awaitExpression, environment, context),
                YieldExpression yieldExpression => EvaluateYield(yieldExpression, environment, context),
                ThisExpression => ResolveThisValue(environment, context),
                SuperExpression => throw new InvalidOperationException(
                    $"Super is not available in this context.{GetSourceInfo(context, expression.Source)}"),
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
            };
        }

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

        private bool IsAnonymousFunctionDefinition() =>
            IsAnonymousFunctionDefinitionNode(expression);

        internal static bool IsAnonymousFunctionDefinitionNode(ExpressionNode node)
        {
            // Per ES spec, sequence expressions (comma operator) do not qualify for name inference
            // e.g., `const x = (0, function() {})` should not infer name
            if (node is SequenceExpression)
            {
                return false;
            }

            return node switch
            {
                FunctionExpression func => func.Name is null,
                ClassExpression classExpression => classExpression.Name is null,
                _ => false
            };
        }

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
                        return ContainsDirectEvalCall(cond.Test) || ContainsDirectEvalCall(cond.Consequent) ||
                               ContainsDirectEvalCall(cond.Alternate);
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
                        if (ContainsDirectEvalCall(tagged.Tag) || ContainsDirectEvalCall(tagged.StringsArray) ||
                            ContainsDirectEvalCall(tagged.RawStringsArray))
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

    extension(ExpressionNode callee)
    {
        private (object? Callee, JsValue thisValue, bool SkippedOptional) EvaluateCallTarget(JsEnvironment environment,
            EvaluationContext context)
        {
            if (callee is SuperExpression superExpression)
            {
                var logger = environment.RealmState?.Logger;
                logger?.LogInformation(
                    "Super call target env={Env} hasThisInit={HasThisInit} hasSuper={HasSuper}",
                    environment.GetHashCode(),
                    environment.HasBinding(Symbol.ThisInitialized),
                    environment.HasBinding(Symbol.Super));
                var binding = ExpectSuperBinding(environment, context);

                // Per ES spec 12.3.5.1 SuperCall, the super constructor should be looked up
                // dynamically via GetSuperConstructor() which gets activeFunction.[[Prototype]].
                // For a constructor, the active function is available via NewTarget when it's
                // a constructor being invoked via 'new'.
                object? dynamicSuperConstructor = binding.Constructor;
                if (dynamicSuperConstructor is null &&
                    environment.TryGet(Symbol.NewTarget, out var newTarget) &&
                    newTarget is IJsObjectLike activeFunction)
                {
                    // Get the current [[Prototype]] of the active function (constructor)
                    // This respects Object.setPrototypeOf changes made after class definition
                    // Use PrototypeAccessor to handle non-JsObject prototypes (e.g., HostFunction)
                    dynamicSuperConstructor = (activeFunction as IPrototypeAccessorProvider)?.PrototypeAccessor
                                              ?? activeFunction.Prototype;
                    logger?.LogInformation(
                        "Super call: dynamic lookup newTargetType={NewTargetType} protoType={ProtoType}",
                        newTarget.GetType().Name,
                        dynamicSuperConstructor?.GetType().Name ?? "null");
                }

                if (dynamicSuperConstructor is null)
                {
                    throw new InvalidOperationException(
                        $"Super constructor is not available in this context.{GetSourceInfo(context, superExpression.Source)}");
                }

                var superThis = ReferenceEquals(binding.thisValue, JsEnvironment.Uninitialized)
                    ? JsValue.FromObject(Symbol.Undefined)
                    : binding.thisValue;
                return (dynamicSuperConstructor, superThis, false);
            }

            if (callee is MemberExpression member)
            {
                if (member.Target is SuperExpression)
                {
                    var (memberValue, binding) = ResolveSuperMember(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (Symbol.Undefined, binding.thisValue, true);
                    }

                    return (memberValue, binding.thisValue, false);
                }

                var targetJs = EvaluateExpression(member.Target, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, JsValue.Undefined, true);
                }

                var target = targetJs.ToObject();
                if (member.IsOptional && IsNullish(target))
                {
                    return (null, JsValue.Undefined, true);
                }

                if (IsNullish(target) && HasOptionalChaining(member.Target))
                {
                    return (Symbol.Undefined, JsValue.Undefined, true);
                }

                if (IsNullish(target))
                {
                    var error = StandardLibrary.CreateTypeError(
                        "Cannot read properties of null or undefined",
                        context,
                        context.RealmState);
                    context.SetThrow(error);
                    return (Symbol.Undefined, JsValue.Undefined, true);
                }

                string propertyName;
                if (member.IsComputed)
                {
                    var propertyJs = EvaluateExpression(member.Property, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (Symbol.Undefined, null, true);
                    }

                    propertyName = JsOps.GetRequiredPropertyName(propertyJs.ToObject(), context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (Symbol.Undefined, null, true);
                    }
                }
                else
                {
                    propertyName = member.Property switch
                    {
                        IdentifierExpression id => id.Name.Name,
                        LiteralExpression { Value: string s } => s,
                        _ => JsOps.GetRequiredPropertyName(EvaluateExpression(member.Property, environment, context).ToObject(),
                            context)
                    };
                }

                if (member.IsComputed || !propertyName.IsPrivateName())
                {
                    if (JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context))
                    {
                        if (context.ShouldStopEvaluation)
                        {
                            return (Symbol.Undefined, null, true);
                        }

                        return (directValue, target, false);
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        return (Symbol.Undefined, null, true);
                    }

                    return (Symbol.Undefined, target, false);
                }

                var handle = PropertyHandle.Resolve(
                    target,
                    propertyName,
                    context,
                    context.CurrentScope.IsStrict,
                    allowPrivate: !member.IsComputed);
                var value = handle.GetValue();
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, null, true);
                }

                return (value, target, false);
            }

            if (callee is IdentifierExpression identifier)
            {
                if (environment.TryResolveWithBinding(identifier.Name, context, out var withBinding))
                {
                    var withValue = JsEnvironment.GetWithBindingValue(withBinding);
                    return (withValue, withBinding.BindingObject, false);
                }

                var reference = environment.ResolveIdentifierAssignmentReference(identifier.Name, context);
                var calleeValue = AssignmentReferenceResolver.ReadIdentifierValue(reference.GetValue, context);
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, null, true);
                }

                return (calleeValue, Symbol.Undefined, false);
            }

            var directCallee = EvaluateExpression(callee, environment, context);
            return (directCallee.ToObject(), Symbol.Undefined, false);
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
                    if (member.Target is SuperExpression)
                    {
                        throw StandardLibrary.ThrowReferenceError(
                            "Cannot delete property on super reference",
                            context,
                            context.RealmState);
                    }

                    var targetJs = EvaluateExpression(member.Target, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var propertyValueJs = EvaluateExpression(member.Property, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var handle = PropertyHandle.Resolve(
                        targetJs.ToObject(),
                        propertyValueJs.ToObject(),
                        context,
                        context.CurrentScope.IsStrict,
                        allowPrivate: !member.IsComputed);
                    return handle.Delete();
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

    private static JsValue ResolveThisValue(JsEnvironment environment, EvaluationContext context)
    {
        try
        {
            // Check if we're in an arrow function that has a lexical this environment.
            // If so, read `this` from the original owning environment, not the arrow's local copy.
            // This ensures that after super() updates the constructor's `this`, subsequent
            // reads of `this` inside the arrow function see the updated value.
            if (environment.TryFindBinding(Symbol.LexicalThisEnvironment, allowUninitialized: true, out _, out var lexicalEnvValue) &&
                lexicalEnvValue is JsEnvironment lexicalThisEnv)
            {
                return JsValue.FromObject(lexicalThisEnv.Get(Symbol.This));
            }
            return JsValue.FromObject(environment.Get(Symbol.This));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                     StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
            throw new ThrowSignal(errorObject);
        }
    }

    /// <summary>
    ///     Evaluates an import.meta expression. Returns the import.meta object for the current module.
    /// </summary>
    private static JsValue EvaluateImportMeta(JsEnvironment environment, EvaluationContext context)
    {
        // Try to get the import.meta object from the environment
        // If running in module context, this should be set by the module loader
        if (environment.TryGet(Symbol.ImportMeta, out var importMeta))
        {
            return JsValue.FromObject(importMeta);
        }

        // Return a basic import.meta object with a url property
        var metaObject = new JsObject();
        metaObject.RealmState = context.RealmState;
        metaObject.SetPrototype(null);
        // Set a default URL if we can determine it from the environment
        metaObject.SetProperty("url", string.Empty);
        return JsValue.FromObject(metaObject);
    }
}
