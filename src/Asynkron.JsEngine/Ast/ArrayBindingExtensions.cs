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

extension(ArrayBinding binding)
    {
        private void BindArrayPattern(object? value, JsEnvironment environment,
            EvaluationContext context, BindingMode mode)
        {
            if (!TryGetIteratorForDestructuring(value, context, out var iterator, out var enumerator))
            {
                throw StandardLibrary.ThrowTypeError(
                    $"Cannot destructure non-iterable value.{GetSourceInfo(context)}", context);
            }

            if (iterator is not null && binding.Elements.Length == 0 && binding.RestElement is null)
            {
                IteratorClose(iterator, context);
                return;
            }

            var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);
            var iteratorThrew = false;
            var iteratorDone = false;

            try
            {
                foreach (var element in binding.Elements)
                {
                    AssignmentReference? preResolvedReference = null;
                    if (mode == BindingMode.Assign && element.Target is AssignmentTargetBinding assignmentTarget)
                    {
                        preResolvedReference = AssignmentReferenceResolver.Resolve(
                            assignmentTarget.Expression,
                            environment,
                            context,
                            EvaluateExpression);
                        if (context.ShouldStopEvaluation)
                        {
                            if (iterator is not null)
                            {
                                IteratorClose(iterator, context);
                            }

                            return;
                        }
                    }

                    (object? nextValue, bool done) next;
                    try
                    {
                        next = iteratorRecord.Next(context);
                    }
                    catch (ThrowSignal)
                    {
                        iteratorThrew = true;
                        throw;
                    }

                    var (nextValue, done) = next;
                    iteratorDone = done;
                    if (context.ShouldStopEvaluation)
                    {
                        if (iterator is not null)
                        {
                            IteratorClose(iterator, context);
                        }

                        return;
                    }

                    var elementValue = done ? Symbol.Undefined : nextValue;

                    if (element.Target is null)
                    {
                        continue;
                    }

                    var usedDefault = false;
                    if (element.DefaultValue is not null &&
                        ReferenceEquals(elementValue, Symbol.Undefined))
                    {
                        usedDefault = true;
                        elementValue = EvaluateExpression(element.DefaultValue, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (iterator is not null)
                            {
                                IteratorClose(iterator, context);
                            }

                            return;
                        }
                    }

                    if (usedDefault &&
                        element is { Target: IdentifierBinding identifierTarget, DefaultValue: { } defaultExpression } &&
                        IsAnonymousFunctionDefinition(defaultExpression) &&
                        elementValue is IFunctionNameTarget nameTarget)
                    {
                        nameTarget.EnsureHasName(identifierTarget.Name.Name);
                    }

                    if (preResolvedReference is { } resolvedReference)
                    {
                        resolvedReference.SetValue(elementValue);
                    }
                    else
                    {
                        ApplyBindingTarget(element.Target, elementValue, environment, context, mode, allowNameInference: false);
                    }

                    if (!context.ShouldStopEvaluation)
                    {
                        continue;
                    }

                    if (iterator is not null)
                    {
                        IteratorClose(iterator, context);
                    }

                    return;
                }

                if (binding.RestElement is not null)
                {
                    AssignmentReference? preResolvedRest = null;
                    if (mode == BindingMode.Assign && binding.RestElement is AssignmentTargetBinding restTarget)
                    {
                        preResolvedRest = AssignmentReferenceResolver.Resolve(
                            restTarget.Expression,
                            environment,
                            context,
                            EvaluateExpression);
                        if (context.ShouldStopEvaluation)
                        {
                            if (iterator is not null)
                            {
                                IteratorClose(iterator, context);
                            }

                            return;
                        }
                    }

                    var restArray = new JsArray(context.RealmState);
                    while (true)
                    {
                        (object? restValue, bool done) restNext;
                        try
                        {
                            restNext = iteratorRecord.Next(context);
                        }
                        catch (ThrowSignal)
                        {
                            iteratorThrew = true;
                            throw;
                        }

                        var (restValue, done) = restNext;
                        iteratorDone = done;
                        if (context.ShouldStopEvaluation)
                        {
                            if (iterator is not null)
                            {
                                IteratorClose(iterator, context);
                            }

                            return;
                        }

                        if (done)
                        {
                            break;
                        }

                        restArray.Push(restValue);
                    }

                    if (preResolvedRest is { } resolvedRestReference)
                    {
                        resolvedRestReference.SetValue(restArray);
                    }
                    else
                    {
                        ApplyBindingTarget(binding.RestElement, restArray, environment, context, mode, allowNameInference: false);
                    }
                }
            }
            catch (ThrowSignal)
            {
                if (iterator is not null && !iteratorThrew)
                {
                    IteratorClose(iterator, context, preserveExistingThrow: true);
                }

                throw;
            }
            catch
            {
                if (iterator is not null)
                {
                    IteratorClose(iterator, context);
                }

                throw;
            }

            if (iterator is not null && !iteratorDone)
            {
                IteratorClose(iterator, context);
            }
        }
    }

}
