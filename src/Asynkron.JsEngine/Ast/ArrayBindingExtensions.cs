using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ArrayBinding binding)
    {
        private void BindArrayPattern(object? /* intentional */ value, JsEnvironment environment,
            EvaluationContext context, BindingMode mode)
        {
            var stateKey = GetArrayPatternStateKey(binding);
            ArrayPatternState? resumeState = null;
            if (stateKey is not null && environment.TryGet(stateKey, out var existing) &&
                existing is ArrayPatternState savedState)
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
                            if (context.IsYield && stateKey is { })
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

                        if (context.IsYield && stateKey is { })
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
                        if (context.IsYield && stateKey is { })
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
                                      (elementValue is JsValue jsVal && jsVal.IsUndefined) ||
                                      elementValue is null;
                    if (element.DefaultValue is not null && isUndefined)
                    {
                        usedDefault = true;
                        // Boxing JsValue is preferred over ToObject() - existing code handles boxed JsValue
                        elementValue = element.DefaultValue.EvaluateExpression(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (context.IsYield && stateKey is { })
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
                        IFunctionNameTarget? nameTarget = elementValue switch
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
                        // ApplyBindingTarget handles boxed JsValue via pattern matching
                        element.Target.ApplyBindingTarget(elementValue, environment, context, mode,
                            allowNameInference: false);
                    }

                    if (!context.ShouldStopEvaluation)
                    {
                        continue;
                    }

                    if (context.IsYield && stateKey is { })
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
                            if (context.IsYield && stateKey is { })
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
                    var consumePendingRest = resumeState?.ConsumingRest == true && resumeState.HasPendingElement;
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

                            if (context.IsYield && stateKey is { })
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
                            if (context.IsYield && stateKey is { })
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
                        resolvedRestReference.SetValue(JsValue.FromObjectUnsafe(restArray));
                    }
                    else
                    {
                        binding.RestElement.ApplyBindingTargetJsValue(JsValue.FromObjectUnsafe(restArray), environment, context, mode,
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
                    CloseIterator(true, signal.ThrownValue);
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
                // Instead, save the state so CloseActiveArrayPatternIterators can find it
                // when the generator completes/returns.
                if (context.InGeneratorContext && stateKey is { })
                {
                    SaveArrayPatternState(stateKey, environment, iterator, enumerator, iteratorDone,
                        binding.Elements.Length, null, iteratorDone, false, null, false);
                    return;
                }

                CloseIterator(context.IsThrow);
            }

            if (stateKey is { })
            {
                ClearArrayPatternState(stateKey, environment);
            }

            return;

            void CloseIterator(bool preserveExistingThrow, JsValue existingThrowOverride = default)
            {
                if (iterator is null)
                {
                    return;
                }

                try
                {
                    iterator.IteratorClose(context, preserveExistingThrow, existingThrowOverride);
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
                    // Mark the iterator as closed so CloseActiveArrayPatternIterators doesn't try again
                    if (stateKey is not null && environment.TryGet(stateKey, out var stateObj) && stateObj is ArrayPatternState state)
                    {
                        state.IteratorDone = true;
                        state.Iterator = null;
                    }
                }
            }
        }
    }

    private static Symbol? GetArrayPatternStateKey(ArrayBinding binding)
    {
        if (binding.Source is null)
        {
            return Symbol.Intern($"__array_pattern_state_{binding.GetHashCode()}");
        }

        return Symbol.Intern(
            $"__array_pattern_state_{binding.Source.StartPosition}_{binding.Source.EndPosition}");
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
        var state = environment.TryGet(stateKey, out var existing) && existing is ArrayPatternState existingState
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
            environment.Assign(stateKey, state);
        }
        else
        {
            environment.DefineJsValue(stateKey, JsValue.FromObjectUnsafe(state), isConst: false, isLexical: true, canDelete: true);
        }
    }

    private static void ClearArrayPatternState(Symbol stateKey, JsEnvironment environment)
    {
        environment.DeleteBinding(stateKey);
    }

    private sealed class ArrayPatternState
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
    }

}
