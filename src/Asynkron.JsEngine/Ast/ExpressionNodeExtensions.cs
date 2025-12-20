using System.Runtime.CompilerServices;
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

            var baseJsValue = extendsExpression.EvaluateExpression(environment, context);
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
                throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateTypeError(
                    "Class extends value is not a constructor or null", context, context.RealmState)));
            }

            // Proxy cannot be subclassed because its prototype is undefined.
            if (baseValue is IJsPropertyAccessor accessorWithMarker &&
                TryGetPropertyValue(accessorWithMarker, "__proxyHasNoPrototype__", out var marker, context) &&
                JsOps.ToBoolean(marker))
            {
                throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateTypeError(
                    "Class extends value does not have a valid prototype",
                    context,
                    context.RealmState)));
            }

            if (baseValue is not IJsEnvironmentAwareCallable callable ||
                baseValue is not IJsPropertyAccessor accessor)
            {
                throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateTypeError(
                    "Class extends value is not a constructor or null", context, context.RealmState)));
            }

            var hasPrototype = TryGetPropertyValue(baseValue, "prototype", out var prototypeValue, context);
            if (context.ShouldStopEvaluation)
            {
                return (null, null);
            }

            if (!hasPrototype)
            {
                throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateTypeError(
                    "Class extends value does not have a valid prototype",
                    context,
                    context.RealmState)));
            }

            if (prototypeValue is null)
            {
                return (callable, null);
            }

            if (prototypeValue is IJsPropertyAccessor prototype)
            {
                return (callable, prototype);
            }

            throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState)));
        }
    }

    extension(ExpressionNode expression)
    {
        /// <summary>
        /// Ultra-thin hot path for expression evaluation - designed to be inlined.
        /// Uses explicit if statements instead of switch for minimal IL size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            return expression switch
            {
                // Explicit if statements generate less IL than switch expressions
                LiteralExpression literal => literal.Value,
                IdentifierExpression identifier => identifier.EvaluateIdentifier(environment, context),
                BinaryExpression binary => binary.EvaluateBinary(environment, context),
                AssignmentExpression assignment => assignment.EvaluateAssignment(environment, context),
                UnaryExpression unary => unary.EvaluateUnary(environment, context),
                CallExpression call => call.EvaluateCall(environment, context),
                _ => expression.EvaluateExpressionSlow(environment, context)
            };
        }

        /// <summary>
        /// Slow path for less common expression types. Marked NoInlining to keep hot path small.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue EvaluateExpressionSlow(JsEnvironment environment,
            EvaluationContext context)
        {
            // Second tier of common expressions
            switch (expression)
            {
                case UnaryExpression unary:
                    return unary.EvaluateUnary(environment, context);
                case AssignmentExpression assignment:
                    return assignment.EvaluateAssignment(environment, context);
                case MemberExpression member:
                    return member.EvaluateMember(environment, context);
                case CallExpression call:
                    return call.EvaluateCall(environment, context);
            }

            // Slowest path - set source reference and optionally trace
            context.SourceReference = expression.Source;

            return expression switch
            {
                RegexLiteralExpression regex => regex.EvaluateRegexLiteral(context),
                ConditionalExpression conditional => conditional.EvaluateConditional(environment, context),
                FunctionExpression functionExpression => JsValue.FromObjectUnsafe(functionExpression.CreateFunctionValue(environment, context,
                    createFunctionNameEnvironment: true)),
                DestructuringAssignmentExpression destructuringAssignment => destructuringAssignment.EvaluateDestructuringAssignment(environment, context),
                PropertyAssignmentExpression propertyAssignment => propertyAssignment.EvaluatePropertyAssignment(environment, context),
                IndexAssignmentExpression indexAssignment => indexAssignment.EvaluateIndexAssignment(environment, context),
                SequenceExpression sequence => sequence.EvaluateSequence(environment, context),
                NewExpression newExpression => newExpression.EvaluateNew(environment, context),
                NewTargetExpression => environment.TryGet(Symbol.NewTarget, out var newTarget)
                    ? JsValue.FromObjectUnsafe(newTarget)
                    : JsValue.Undefined,
                ImportMetaExpression => EvaluateImportMeta(environment, context),
                ArrayExpression array => array.EvaluateArray(environment, context),
                ObjectExpression obj => obj.EvaluateObject(environment, context),
                ClassExpression classExpression => classExpression.EvaluateClassExpression(environment, context),
                DecoratorExpression => throw new NotSupportedException("Decorators are not supported."),
                TemplateLiteralExpression template => template.EvaluateTemplateLiteral(environment, context),
                TaggedTemplateExpression taggedTemplate => taggedTemplate.EvaluateTaggedTemplate(environment, context),
                AwaitExpression awaitExpression => awaitExpression.EvaluateAwait(environment, context),
                YieldExpression yieldExpression => yieldExpression.EvaluateYield(environment, context),
                ThisExpression => ResolveThisValue(environment, context),
                SuperExpression => throw new InvalidOperationException(
                    $"Super is not available in this context.{context.GetSourceInfo(expression.Source)}"),
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
            };
        }

        private string DescribeCallee()
        {
            return expression switch
            {
                IdentifierExpression id => id.Name.Name,
                MemberExpression member => $"{member.Target.DescribeCallee()}.{member.Property.DescribeMemberName()}",
                CallExpression call => $"{call.Callee.DescribeCallee()}(...)",
                _ => expression.GetType().Name
            };
        }

        private bool IsAnonymousFunctionDefinition() => ExpressionNode.IsAnonymousFunctionDefinitionNode(expression);

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
                        if (call.Callee.ContainsDirectEvalCall())
                        {
                            return true;
                        }

                        foreach (var arg in call.Arguments)
                        {
                            if (arg.Expression.ContainsDirectEvalCall())
                            {
                                return true;
                            }
                        }

                        return false;
                    case BinaryExpression binary:
                        return binary.Left.ContainsDirectEvalCall() || binary.Right.ContainsDirectEvalCall();
                    case ConditionalExpression cond:
                        return cond.Test.ContainsDirectEvalCall() || cond.Consequent.ContainsDirectEvalCall() || cond.Alternate.ContainsDirectEvalCall();
                    case MemberExpression member:
                        return member.Target.ContainsDirectEvalCall() || member.Property.ContainsDirectEvalCall();
                    case UnaryExpression unary:
                        expression = unary.Operand;
                        continue;
                    case SequenceExpression seq:
                        return seq.Left.ContainsDirectEvalCall() || seq.Right.ContainsDirectEvalCall();
                    case ArrayExpression array:
                        foreach (var element in array.Elements)
                        {
                            if (element.Expression is not null && element.Expression.ContainsDirectEvalCall())
                            {
                                return true;
                            }
                        }

                        return false;
                    case ObjectExpression obj:
                        foreach (var member in obj.Members)
                        {
                            if (member.Value is not null && member.Value.ContainsDirectEvalCall())
                            {
                                return true;
                            }

                            if (member.Function is not null && member.Function.ContainsDirectEvalCall())
                            {
                                return true;
                            }
                        }

                        return false;
                    case TemplateLiteralExpression template:
                        foreach (var part in template.Parts)
                        {
                            if (part.Expression is not null && part.Expression.ContainsDirectEvalCall())
                            {
                                return true;
                            }
                        }

                        return false;
                    case TaggedTemplateExpression tagged:
                        if (tagged.Tag.ContainsDirectEvalCall() || tagged.StringsArray.ContainsDirectEvalCall() || tagged.RawStringsArray.ContainsDirectEvalCall())
                        {
                            return true;
                        }

                        foreach (var expr in tagged.Expressions)
                        {
                            if (expr.ContainsDirectEvalCall())
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
        private (JsValue Callee, JsValue thisValue, bool SkippedOptional) EvaluateCallTarget(JsEnvironment environment,
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
                var binding = environment.ExpectSuperBinding(context);

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
                        $"Super constructor is not available in this context.{context.GetSourceInfo(superExpression.Source)}");
                }

                var superThis = ReferenceEquals(binding.thisValue, JsEnvironment.Uninitialized)
                    ? JsValue.Undefined
                    : binding.thisValue;
                return (JsValue.FromObjectUnsafe(dynamicSuperConstructor), superThis, false);
            }

            if (callee is MemberExpression member)
            {
                if (member.Target is SuperExpression)
                {
                    var (memberValue, binding) = member.ResolveSuperMember(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, binding.thisValue, true);
                    }

                    return (memberValue, binding.thisValue, false);
                }

                var targetJs = member.Target.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return (JsValue.Undefined, JsValue.Undefined, true);
                }

                if (member.IsOptional && targetJs.IsNullOrUndefined)
                {
                    return (JsValue.Undefined, JsValue.Undefined, true);
                }

                if (targetJs.IsNullOrUndefined && HasOptionalChaining(member.Target))
                {
                    return (JsValue.Undefined, JsValue.Undefined, true);
                }

                if (targetJs.IsNullOrUndefined)
                {
                    var error = StandardLibrary.CreateTypeError(
                        "Cannot read properties of null or undefined",
                        context,
                        context.RealmState);
                    context.SetThrow(JsValue.FromObjectUnsafe(error));
                    return (JsValue.Undefined, JsValue.Undefined, true);
                }

                var target = targetJs.ToObject();
                string propertyName;
                if (member.IsComputed)
                {
                    var propertyJs = member.Property.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    propertyName = JsOps.GetRequiredPropertyName(propertyJs.ToObject(), context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }
                }
                else
                {
                    propertyName = member.Property switch
                    {
                        IdentifierExpression id => id.Name.Name,
                        LiteralExpression { Value.IsString: true } lit => lit.Value.AsString()!,
                        _ => JsOps.GetRequiredPropertyName(member.Property.EvaluateExpression(environment, context).ToObject(),
                            context)
                    };
                }

                if (member.IsComputed || !propertyName.IsPrivateName())
                {
                    if (targetJs.TryGetObject<IJsPropertyAccessor>(out var accessor))
                    {
                        try
                        {
                            if (accessor.TryGetProperty(propertyName, targetJs, out var directJsValue))
                            {
                                return (directJsValue, targetJs, false);
                            }
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }
                    }

                    if (JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context))
                    {
                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }

                        return (JsValue.FromObjectUnsafe(directValue), targetJs, false);
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    return (JsValue.Undefined, targetJs, false);
                }

                var handle = PropertyHandle.Resolve(
                    target,
                    propertyName,
                    context,
                    context.CurrentScope.IsStrict,
                    allowPrivate: !member.IsComputed);
                var value = handle.GetJsValue();
                if (context.ShouldStopEvaluation)
                {
                    return (JsValue.Undefined, JsValue.Undefined, true);
                }

                return (value, targetJs, false);
            }

            if (callee is IdentifierExpression identifier)
            {
                if (environment.TryResolveWithBinding(identifier.Name, context, out var withBinding))
                {
                    try
                    {
                        var withValue = JsEnvironment.GetWithBindingValue(withBinding);
                        return (JsValue.FromObjectUnsafe(withValue), JsValue.FromObjectUnsafe(withBinding.BindingObject), false);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
                    {
                        // Convert to JavaScript ReferenceError so it can be caught by JavaScript try-catch
                        var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
                        throw new ThrowSignal(JsValue.FromObjectUnsafe(errorObject));
                    }
                }

                // Fast path: use slot-based lookup when available
                if (identifier is { SlotIndex: >= 0, ScopeId: >= 0 })
                {
                    if (environment.TryReadIdentifierWithSlot(
                            identifier.Name,
                            identifier.ScopeId,
                            identifier.SlotIndex,
                            context,
                            out var slotCallee))
                    {
                        return (slotCallee, JsValue.Undefined, false);
                    }
                }

                // Fallback: dictionary-based lookup
                var reference = environment.ResolveIdentifierAssignmentReference(identifier.Name, context);
                var calleeValue = AssignmentReferenceResolver.ReadIdentifierValue(reference.GetJsValue, context);
                if (context.ShouldStopEvaluation)
                {
                    return (JsValue.Undefined, JsValue.Undefined, true);
                }

                return (calleeValue, JsValue.Undefined, false);
            }

            var directCallee = callee.EvaluateExpression(environment, context);
            return (directCallee, JsValue.Undefined, false);
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

                    var targetJs = member.Target.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var propertyValueJs = member.Property.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var handle = PropertyHandle.Resolve(
                        targetJs.ToObject(),
                        propertyValueJs,
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
                    _ = operand.EvaluateExpression(environment, context);
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
                LiteralExpression { Value.IsString: true } lit => lit.Value.AsString()!,
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
                return JsValue.FromObjectUnsafe(lexicalThisEnv.Get(Symbol.This));
            }
            return JsValue.FromObjectUnsafe(environment.Get(Symbol.This));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                     StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
            throw new ThrowSignal(JsValue.FromObjectUnsafe(errorObject));
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
            return JsValue.FromObjectUnsafe(importMeta);
        }

        // Return a basic import.meta object with a url property
        var metaObject = new JsObject();
        metaObject.RealmState = context.RealmState;
        metaObject.SetPrototype(null);
        // Set a default URL if we can determine it from the environment
        metaObject.SetProperty("url", string.Empty);
        return (JsValue)metaObject;
    }
}
