#region

using System.Globalization;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static Symbol GetArrayPatternStateKey(ArrayBinding binding)
    {
        if (binding.Source is null)
        {
            return Symbol.Intern($"__array_pattern_state_{binding.GetHashCode()}");
        }

        return Symbol.Intern(
            $"__array_pattern_state_{binding.Source.StartPosition.ToString(CultureInfo.InvariantCulture)}_{binding.Source.EndPosition.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void SaveArrayPatternState(Symbol stateKey, JsEnvironment environment,
        IJsObjectLike? iterator,
        IEnumerator<JsValue>? enumerator,
        bool iteratorDone,
        int nextElementIndex,
        object? pendingValue,
        bool pendingDone,
        bool consumingRest,
        JsArray? restArray,
        bool hasPendingElement)
    {
        var state = environment.TryGetObject<ArrayPatternState>(stateKey, out var existingState)
            ? existingState
            : new ArrayPatternState();

        state.Iterator = iterator;
        state.Enumerator = enumerator;
        state.IteratorDone = iteratorDone;
        state.NextElementIndex = nextElementIndex;
        state.HasPendingElement = hasPendingElement;
        state.PendingValue = pendingValue;
        state.PendingDone = pendingDone;
        state.RestArray = restArray;
        state.ConsumingRest = consumingRest;

        if (environment.HasOwnBinding(stateKey))
        {
            environment.AssignJsValue(stateKey, JsValue.FromObjectUnsafe(state));
        }
        else
        {
            environment.DefineJsValue(stateKey, JsValue.FromObjectUnsafe(state), isLexicalBinding: true,
                canDelete: true);
        }
    }

    private static void ClearArrayPatternState(Symbol stateKey, JsEnvironment environment)
    {
        environment.DeleteBinding(stateKey);
    }

    private static void BindArrayPattern(this ArrayBinding binding, JsValue value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        var stateKey = GetArrayPatternStateKey(binding);
        ArrayPatternState? resumeState = null;
        if (stateKey is not null && environment.TryGetObject<ArrayPatternState>(stateKey, out var savedState))
        {
            resumeState = savedState;
        }

        var iterator = resumeState?.Iterator;
        var enumerator = resumeState?.Enumerator;
        if (iterator is null && enumerator is null &&
            !TryGetIteratorForDestructuring(value, context, out iterator, out enumerator))
        {
            if (context.ShouldStopEvaluation)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            throw StandardLibrary.ThrowTypeError(
                $"Cannot destructure non-iterable value.{context.GetSourceInfo()}", context);
        }

        if (iterator is not null && binding.Elements.Length == 0 && binding.RestElement is null)
        {
            CloseIterator(context.IsThrow);
            return;
        }

        var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);
        var iteratorThrew = false;
        var iteratorDone = resumeState?.IteratorDone ?? false;
        var startIndex = resumeState?.NextElementIndex ?? 0;
        var hasPendingElement = resumeState?.HasPendingElement == true;
        var pendingValue = resumeState?.PendingValue;
        var pendingDone = resumeState?.PendingDone ?? false;

        // If we're resuming with an existing completion (e.g., generator return/throw),
        // don't advance the iterator again. Just close it and propagate the completion.
        if (context.ShouldStopEvaluation)
        {
            if (iterator is not null && !iteratorDone)
            {
                CloseIterator(preserveExistingThrow: true);
            }

            return;
        }

        try
        {
            for (var elementIndex = startIndex; elementIndex < binding.Elements.Length; elementIndex++)
            {
                var element = binding.Elements[elementIndex];
                AssignmentReference? preResolvedReference = null;
                if (mode == BindingMode.Assign && element.Target is AssignmentTargetBinding assignmentTarget)
                {
                    preResolvedReference = AssignmentReferenceResolver.ResolveForDestructuring(
                        assignmentTarget.Expression,
                        environment,
                        context,
                        (e, env, ctx) => e.EvaluateExpression(env, ctx));
                    if (context.ShouldStopEvaluation)
                    {
                        if (context.IsYield && stateKey is not null)
                        {
                            SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                                elementIndex, pendingValue, pendingDone, false, null, false);
                        }
                        else if (iterator is not null)
                        {
                            CloseIterator(context.IsThrow);
                        }

                        return;
                    }
                }

                var usePending = hasPendingElement && elementIndex == startIndex;
                if (usePending)
                {
                    hasPendingElement = false;
                }

                (object? nextValue, bool done) next;
                if (usePending)
                {
                    next = (pendingValue, pendingDone);
                }
                else
                {
                    var throwBeforeNext = context.IsThrow;
                    try
                    {
                        next = iteratorRecord.Next(context);
                    }
                    catch (ThrowSignal)
                    {
                        iteratorThrew = true;
                        throw;
                    }

                    if (!throwBeforeNext && context.IsThrow)
                    {
                        iteratorThrew = true;
                    }

                    if (context.IsYield && stateKey is not null)
                    {
                        SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                            elementIndex, next.Item1, next.Item2, false, null, false);
                        return;
                    }
                }

                var (nextValue, done) = next;
                iteratorDone = done;
                if (context.ShouldStopEvaluation)
                {
                    if (context.IsYield && stateKey is not null)
                    {
                        SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                            elementIndex, nextValue, done, false, null, false);
                    }
                    else if (iterator is not null && !iteratorThrew)
                    {
                        CloseIterator(context.IsThrow);
                    }

                    return;
                }

                var elementValue = done ? Symbol.Undefined : nextValue;

                if (element.Target is null)
                {
                    continue;
                }

                var usedDefault = false;
                // Check for undefined: could be Symbol.Undefined, JsValue.Undefined, or null
                var isUndefined = ReferenceEquals(elementValue, Symbol.Undefined) ||
                                  elementValue is JsValue { IsUndefined: true } ||
                                  elementValue is null;
                if (element.DefaultValue is not null && isUndefined)
                {
                    usedDefault = true;
                    // Boxing JsValue is preferred over ToObject() - existing code handles boxed JsValue
                    elementValue = element.DefaultValue.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (context.IsYield && stateKey is not null)
                        {
                            SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                                elementIndex, elementValue, done, false, null, true);
                        }
                        else if (iterator is not null)
                        {
                            CloseIterator(context.IsThrow);
                        }

                        return;
                    }
                }

                // Handle name inference for anonymous functions - check both boxed JsValue and direct object
                if (usedDefault &&
                    element is
                    {
                        Target: IdentifierBinding identifierTarget, DefaultValue: { } defaultExpression
                    } && defaultExpression.IsAnonymousFunctionDefinition())
                {
                    var nameTarget = elementValue switch
                    {
                        JsValue jsv when jsv.TryGetObject<IFunctionNameTarget>(out var fn) => fn,
                        IFunctionNameTarget fn => fn,
                        _ => null
                    };
                    nameTarget?.EnsureHasName(identifierTarget.Name.Name);
                }

                if (preResolvedReference is { } resolvedReference)
                {
                    // FromObjectUnsafe handles boxed JsValue correctly
                    resolvedReference.SetValue(JsValue.FromObjectUnsafe(elementValue));
                }
                else
                {
                    // FromObjectUnsafe handles boxed JsValue correctly
                    element.Target.ApplyBindingTarget(JsValue.FromObjectUnsafe(elementValue), environment, context,
                        mode,
                        allowNameInference: false);
                }

                if (!context.ShouldStopEvaluation)
                {
                    continue;
                }

                if (context.IsYield && stateKey is not null)
                {
                    SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                        elementIndex, elementValue, iteratorDone, false, null, true);
                }
                else if (iterator is not null)
                {
                    CloseIterator(context.IsThrow);
                }

                return;
            }

            if (binding.RestElement is not null)
            {
                AssignmentReference? preResolvedRest = null;
                if (mode == BindingMode.Assign && binding.RestElement is AssignmentTargetBinding restTarget)
                {
                    preResolvedRest = AssignmentReferenceResolver.ResolveForDestructuring(
                        restTarget.Expression,
                        environment,
                        context,
                        (e, env, ctx) => e.EvaluateExpression(env, ctx));
                    if (context.ShouldStopEvaluation)
                    {
                        if (context.IsYield && stateKey is not null)
                        {
                            SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                                binding.Elements.Length, null, iteratorDone, true, null, false);
                        }
                        else if (iterator is not null)
                        {
                            CloseIterator(context.IsThrow);
                        }

                        return;
                    }
                }

                var restArray = resumeState?.RestArray ?? new JsArray(context.RealmState);
                var consumePendingRest = resumeState is { ConsumingRest: true, HasPendingElement: true };
                var pendingRestValue = consumePendingRest ? resumeState!.PendingValue : null;
                var pendingRestDone = consumePendingRest && resumeState!.PendingDone;
                while (true)
                {
                    (object? restValue, bool done) restNext;
                    if (consumePendingRest)
                    {
                        restNext = (pendingRestValue, pendingRestDone);
                        consumePendingRest = false;
                    }
                    else
                    {
                        var throwBeforeNext = context.IsThrow;
                        try
                        {
                            restNext = iteratorRecord.Next(context);
                        }
                        catch (ThrowSignal)
                        {
                            iteratorThrew = true;
                            throw;
                        }

                        if (!throwBeforeNext && context.IsThrow)
                        {
                            iteratorThrew = true;
                        }

                        if (context.IsYield && stateKey is not null)
                        {
                            SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                                binding.Elements.Length, restNext.Item1, restNext.Item2, true, restArray, true);
                            return;
                        }
                    }

                    var (restValue, done) = restNext;
                    iteratorDone = done;
                    if (context.ShouldStopEvaluation)
                    {
                        if (context.IsYield && stateKey is not null)
                        {
                            SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                                binding.Elements.Length, restValue, done, true, restArray, true);
                        }
                        else if (iterator is not null && !iteratorThrew)
                        {
                            CloseIterator(context.IsThrow);
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
                    resolvedRestReference.SetValue(JsValue.FromJsArray(restArray));
                }
                else
                {
                    binding.RestElement.ApplyBindingTarget(JsValue.FromJsArray(restArray), environment,
                        context, mode,
                        allowNameInference: false);
                }
            }
        }
        catch (ThrowSignal signal)
        {
            if (!context.IsThrow)
            {
                context.SetThrow(signal.ThrownValue);
            }

            if (iterator is not null && !iteratorThrew && !iteratorDone)
            {
                CloseIterator(true);
            }

            throw;
        }
        catch
        {
            if (iterator is not null && !iteratorDone)
            {
                CloseIterator(context.IsThrow);
                if (context.IsThrow)
                {
                    return;
                }
            }

            throw;
        }

        if (iterator is not null && !iteratorDone)
        {
            // When inside a generator context, don't close the iterator immediately.
            // Instead, save the state so CloseActiveIterators can find it
            // when the generator completes/returns.
            if (context.InGeneratorContext && stateKey is not null)
            {
                SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                    binding.Elements.Length, null, iteratorDone, false, null, false);
                return;
            }

            CloseIterator(context.IsThrow);
        }

        if (stateKey is not null)
        {
            ClearArrayPatternState(stateKey, environment);
        }

        return;

        void CloseIterator(bool preserveExistingThrow)
        {
            if (iterator is null)
            {
                return;
            }

            try
            {
                iterator.IteratorClose(context, preserveExistingThrow);
            }
            catch (ThrowSignal)
            {
                iteratorThrew = true;
                if (!preserveExistingThrow)
                {
                    throw;
                }
            }
            finally
            {
                // Mark the iterator as closed so CloseActiveIterators doesn't try again
                if (stateKey is not null && environment.TryGetObject<ArrayPatternState>(stateKey, out var state))
                {
                    state.IteratorDone = true;
                    state.Iterator = null;
                }
            }
        }
    }

    private sealed class ArrayPatternState : IActiveIteratorState
    {
        public bool ConsumingRest { get; set; }

        public IEnumerator<JsValue>? Enumerator { get; set; }

        public bool HasPendingElement { get; set; }

        public IJsObjectLike? Iterator { get; set; }

        public bool IteratorDone { get; set; }

        public int NextElementIndex { get; set; }

        public bool PendingDone { get; set; }

        public object? PendingValue { get; set; }

        public JsArray? RestArray { get; set; }

        public bool TryGetActiveIterator(out IJsObjectLike iterator)
        {
            if (Iterator is not null && !IteratorDone)
            {
                iterator = Iterator;
                return true;
            }

            iterator = null!;
            return false;
        }

        public void MarkIteratorClosed()
        {
            IteratorDone = true;
            Iterator = null;
        }
    }
}
