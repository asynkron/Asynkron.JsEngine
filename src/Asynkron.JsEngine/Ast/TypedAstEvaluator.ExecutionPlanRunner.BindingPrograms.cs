using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private void ApplyBindingTargetProgram(
            BindingTargetProgram target,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer = true,
            bool allowNameInference = true,
            bool skipBlockedBindingLookup = false) =>
            ApplyBindingTargetProgram(
                runner: this,
                target,
                value,
                environment,
                context,
                mode,
                hasInitializer,
                allowNameInference,
                skipBlockedBindingLookup);

        internal static void ApplyLoweredBindingTargetProgram(
            BindingTargetProgram target,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer = true,
            bool allowNameInference = true,
            bool skipBlockedBindingLookup = false)
        {
            var previousContext = EvaluationContext.Current;
            var previousEnvironment = JsEnvironment.Current;
            EvaluationContext.Current = context;
            JsEnvironment.Current = environment;
            try
            {
                ApplyBindingTargetProgram(
                    runner: null,
                    target,
                    value,
                    environment,
                    context,
                    mode,
                    hasInitializer,
                    allowNameInference,
                    skipBlockedBindingLookup);
            }
            finally
            {
                JsEnvironment.Current = previousEnvironment;
                EvaluationContext.Current = previousContext;
            }
        }

        private static void ApplyBindingTargetProgram(
            ExecutionPlanRunner? runner,
            BindingTargetProgram target,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer = true,
            bool allowNameInference = true,
            bool skipBlockedBindingLookup = false)
        {
            switch (target)
            {
                case IdentifierBindingTargetProgram identifier:
                    ApplyIdentifierBindingProgram(
                        runner,
                        identifier,
                        value,
                        environment,
                        context,
                        mode,
                        hasInitializer,
                        allowNameInference,
                        skipBlockedBindingLookup);
                    return;

                case ArrayBindingTargetProgram arrayBinding:
                    BindArrayPatternProgram(runner, arrayBinding, value, environment, context, mode);
                    return;

                case ObjectBindingTargetProgram objectBinding:
                    BindObjectPatternProgram(runner, objectBinding, value, environment, context, mode);
                    return;

                case NamedPropertyAssignmentBindingTargetProgram namedPropertyAssignment:
                    ApplyNamedPropertyAssignmentTargetProgram(
                        runner,
                        namedPropertyAssignment,
                        value,
                        environment,
                        context);
                    return;

                case ComputedPropertyAssignmentBindingTargetProgram computedPropertyAssignment:
                    ApplyComputedPropertyAssignmentTargetProgram(
                        runner,
                        computedPropertyAssignment,
                        value,
                        environment,
                        context);
                    return;

                case NamedSuperPropertyAssignmentBindingTargetProgram namedSuperPropertyAssignment:
                    ApplyProgramNamedSuperPropertyAssignment(
                        namedSuperPropertyAssignment.PropertyName,
                        allowNameInference: false,
                        value,
                        environment,
                        context);
                    return;

                case ComputedSuperPropertyAssignmentBindingTargetProgram computedSuperPropertyAssignment:
                    ApplyComputedSuperPropertyAssignmentTargetProgram(
                        runner,
                        computedSuperPropertyAssignment,
                        value,
                        environment,
                        context);
                    return;

                default:
                    throw new NotSupportedException(
                        $"Binding target program '{target.GetType().Name}' is not supported.");
            }
        }

        private static void ApplyIdentifierBindingProgram(
            ExecutionPlanRunner? runner,
            IdentifierBindingTargetProgram target,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer,
            bool allowNameInference,
            bool skipBlockedBindingLookup)
        {
            environment.AssertOwnership(nameof(ApplyIdentifierBindingProgram));
            context.AssertOwnership(nameof(ApplyIdentifierBindingProgram));
            if (TryApplySlotProvenIdentifierBindingProgram(
                    runner,
                    target,
                    value,
                    environment,
                    context,
                    mode,
                    allowNameInference))
            {
                return;
            }

            ApplyIdentifierBindingProgram(
                target.Name,
                value,
                environment,
                context,
                mode,
                hasInitializer,
                allowNameInference,
                skipBlockedBindingLookup);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static bool TryApplySlotProvenIdentifierBindingProgram(
            ExecutionPlanRunner? runner,
            IdentifierBindingTargetProgram target,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool allowNameInference)
        {
            if (target.FlatSlotId < 0 ||
                runner?._flatSlots is null ||
                (uint)target.FlatSlotId >= (uint)runner._flatSlots.Length)
            {
                return false;
            }

            ref var variable = ref runner._flatSlots[target.FlatSlotId];
            if (!variable.IsValid)
            {
                return false;
            }

            var targetEnvironment = variable.Environment;
            if (targetEnvironment.ScopeId != target.ScopeId)
            {
                return false;
            }

            ref var slot = ref targetEnvironment.GetSlotByIndex(variable.SlotIndex);
            if (!ReferenceEquals(slot.Name, target.Name))
            {
                return false;
            }

            if (allowNameInference && value is
                { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(target.Name.Name);
            }

            switch (mode)
            {
                case BindingMode.Assign:
                    if (!context.AllowIdentifierCache || environment.HasWithObjectInChain())
                    {
                        return false;
                    }

                    if (slot.IsUninitialized && slot.IsLexical)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                            $"Cannot access '{target.Name.Name}' before initialization",
                            context,
                            context.RealmState));
                    }

                    if (slot.IsConst)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            $"Cannot reassign constant '{target.Name.Name}'.",
                            context,
                            context.RealmState));
                    }

                    if (slot.IsImmutableBinding ||
                        slot.IsGlobalConstant ||
                        slot.HasSpecialBinding)
                    {
                        return false;
                    }

                    variable.Write(value);
                    return true;

                case BindingMode.DefineLet:
                case BindingMode.DefineConst:
                    if (!slot.IsLexical ||
                        !slot.IsUninitialized ||
                        slot.IsGlobalConstant ||
                        slot.HasSpecialBinding)
                    {
                        return false;
                    }

                    slot.Flags |= SlotFlags.BlocksFunctionScopeOverride;
                    if (mode == BindingMode.DefineConst)
                    {
                        slot.Flags |= SlotFlags.Const;
                    }

                    variable.Write(value);
                    return true;

                default:
                    return false;
            }
        }

        private static void ApplyIdentifierBindingProgram(
            Symbol name,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer,
            bool allowNameInference,
            bool skipBlockedBindingLookup)
        {
            environment.AssertOwnership(nameof(ApplyIdentifierBindingProgram));
            context.AssertOwnership(nameof(ApplyIdentifierBindingProgram));
            if (mode == BindingMode.Assign && environment.HasLexicalBinding(name))
            {
                environment.AssertHasBinding(name, nameof(ApplyIdentifierBindingProgram));
            }

            if (allowNameInference && value is
                { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(name.Name);
            }

            if (mode == BindingMode.Assign && environment.IsConstBinding(name))
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Cannot reassign constant '{name.Name}'.",
                    context,
                    context.RealmState));
            }

            switch (mode)
            {
                case BindingMode.Assign:
                    environment.AssignJsValue(name, value);
                    return;

                case BindingMode.DefineLet:
                    environment.DefineJsValue(name, value, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                    return;

                case BindingMode.DefineConst:
                    environment.DefineJsValue(name, value, true, blocksFunctionScopeOverride: true);
                    return;

                case BindingMode.DefineVar:
                {
                    environment.AssertVarBindingScope(name, nameof(ApplyIdentifierBindingProgram));
                    if (!hasInitializer)
                    {
                        return;
                    }

                    if (skipBlockedBindingLookup)
                    {
                        environment.AssignFunctionScopedVarBinding(name, value, context);
                        return;
                    }

                    var assignedBlockedBinding = environment.TryAssignBlockedBinding(name, value);
                    environment.EnsureFunctionScopedVarBinding(name, context);
                    if (!assignedBlockedBinding)
                    {
                        environment.AssignJsValue(name, value);
                    }

                    return;
                }

                case BindingMode.DefineParameter:
                    if (environment.HasBinding(name))
                    {
                        environment.AssignJsValue(name, value);
                    }
                    else
                    {
                        environment.DefineJsValue(name, value, isLexicalBinding: false);
                    }

                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static void BindObjectPatternProgram(
            ExecutionPlanRunner? runner,
            ObjectBindingTargetProgram binding,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode)
        {
            var obj = ToObjectForDestructuringJsValue(value, context);
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in binding.Properties)
            {
                var propertyName = property.Name;
                if (property.NameProgram is { } nameProgram)
                {
                    var propertyKeyValue = EvaluateBindingExpressionProgram(runner, nameProgram, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    propertyName = JsOps.GetRequiredPropertyName(propertyKeyValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                AssignmentReference? preResolvedReference = null;
                if (mode == BindingMode.Assign)
                {
                    preResolvedReference = TryPreResolveAssignmentTargetProgram(
                        runner,
                        property.Target,
                        environment,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                AssignmentReference? preResolvedVarBindingReference = null;
                var skipBlockedBindingLookup = false;
                if (property.Target is IdentifierBindingTargetProgram identifierForSideEffects)
                {
                    if (mode == BindingMode.DefineVar)
                    {
                        if (environment.TryResolveWithBinding(
                                identifierForSideEffects.Name,
                                context,
                                out var withBinding))
                        {
                            preResolvedVarBindingReference = AssignmentReference.ForWithBinding(
                                withBinding,
                                environment,
                                identifierForSideEffects.Name,
                                context,
                                context.CurrentScope.IsStrict || context.IsStrictSource);
                        }

                        skipBlockedBindingLookup = true;
                    }
                    else
                    {
                        _ = environment.HasBinding(identifierForSideEffects.Name);
                    }
                }

                usedKeys.Add(propertyName);
                var hasProperty = JsOps.TryGetPropertyValue(
                    JsValue.FromObjectUnsafe(obj),
                    propertyName,
                    out var propertyValue,
                    context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }

                if (!hasProperty)
                {
                    propertyValue = JsValue.Undefined;
                }

                var usedDefault = false;
                if (propertyValue.IsUndefined && property.DefaultProgram is { } defaultProgram)
                {
                    usedDefault = true;
                    propertyValue = EvaluateBindingExpressionProgram(runner, defaultProgram, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (usedDefault &&
                    property.DefaultInfersName &&
                    property.Target is IdentifierBindingTargetProgram identifierTarget &&
                    propertyValue.TryGetObject<IFunctionNameTarget>(out var nameTarget))
                {
                    nameTarget.EnsureHasName(identifierTarget.Name.Name);
                }

                var hasBindingInitializer = property.DefaultProgram is not null || !propertyValue.IsUndefined;
                if (preResolvedReference is { } resolvedReference)
                {
                    resolvedReference.SetValue(propertyValue);
                }
                else if (preResolvedVarBindingReference is { } resolvedVarBindingReference)
                {
                    if (hasBindingInitializer)
                    {
                        resolvedVarBindingReference.SetValue(propertyValue);
                    }
                }
                else
                {
                    ApplyBindingTargetProgram(
                        runner,
                        property.Target,
                        propertyValue,
                        environment,
                        context,
                        mode,
                        hasInitializer: hasBindingInitializer,
                        allowNameInference: false,
                        skipBlockedBindingLookup: skipBlockedBindingLookup);
                }
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            if (binding.RestElement is null)
            {
                return;
            }

            var restObject = new JsObject();
            if (context.RealmState?.ObjectPrototype is not null)
            {
                restObject.SetPrototype(context.RealmState.ObjectPrototype);
            }

            foreach (var key in obj.GetOwnPropertyKeysInOrder())
            {
                if (usedKeys.Contains(key))
                {
                    continue;
                }

                var descriptor = obj.GetOwnPropertyDescriptor(key);
                if (descriptor is not { Enumerable: true })
                {
                    continue;
                }

                if (JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(obj), key, out var restValue, context))
                {
                    restObject.SetProperty(key, restValue);
                    continue;
                }

                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            ApplyBindingTargetProgram(
                runner,
                binding.RestElement,
                JsValue.FromJsObject(restObject),
                environment,
                context,
                mode,
                allowNameInference: false);
        }

        private static void BindArrayPatternProgram(
            ExecutionPlanRunner? runner,
            ArrayBindingTargetProgram binding,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode)
        {
            if (TryBindDenseArrayPatternProgram(runner, binding, value, environment, context, mode))
            {
                return;
            }

            if (!TryGetIteratorForDestructuring(value, context, out var iterator, out var enumerator))
            {
                if (context.ShouldStopEvaluation)
                {
                    throw new ThrowSignal(context.FlowValue);
                }

                throw StandardLibrary.ThrowTypeError(
                    $"Cannot destructure non-iterable value.{context.GetSourceInfo()}",
                    context);
            }

            var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);
            var activeIteratorState = context.InGeneratorContext && iterator is not null
                ? new ActiveArrayPatternIteratorState(iterator)
                : null;
            var activeIteratorSymbol = activeIteratorState is not null
                ? Symbol.Synthetic("[[arrayPatternIterator]]")
                : null;
            var iteratorDone = false;
            var iteratorThrew = false;

            try
            {
                if (activeIteratorState is not null)
                {
                    environment.DefineJsValue(
                        activeIteratorSymbol!,
                        JsValue.FromObjectUnsafe(activeIteratorState),
                        isLexicalBinding: false,
                        canDelete: true);
                }

                foreach (var element in binding.Elements)
                {
                    AssignmentReference? preResolvedReference = null;
                    if (mode == BindingMode.Assign && element.Target is not null)
                    {
                        preResolvedReference = TryPreResolveAssignmentTargetProgram(
                            runner,
                            element.Target,
                            environment,
                            context);
                        if (context.ShouldStopEvaluation)
                        {
                            CloseIteratorOnAbrupt();
                            return;
                        }
                    }

                    (JsValue Value, bool Done) next;
                    try
                    {
                        next = iteratorRecord.Next(context);
                    }
                    catch (ThrowSignal)
                    {
                        iteratorThrew = true;
                        throw;
                    }

                    SetIteratorDone(next.Done);
                    if (context.ShouldStopEvaluation)
                    {
                        CloseIteratorOnAbrupt();
                        return;
                    }

                    if (element.Target is null)
                    {
                        continue;
                    }

                    var elementValue = next.Done ? JsValue.Undefined : next.Value;
                    var usedDefault = false;
                    if (elementValue.IsUndefined && element.DefaultProgram is { } defaultProgram)
                    {
                        usedDefault = true;
                        elementValue = EvaluateBindingExpressionProgram(runner, defaultProgram, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            CloseIteratorOnAbrupt();
                            return;
                        }
                    }

                    if (usedDefault &&
                        element.DefaultInfersName &&
                        element.Target is IdentifierBindingTargetProgram identifierTarget &&
                        elementValue.TryGetObject<IFunctionNameTarget>(out var nameTarget))
                    {
                        nameTarget.EnsureHasName(identifierTarget.Name.Name);
                    }

                    if (preResolvedReference is { } resolvedReference)
                    {
                        resolvedReference.SetValue(elementValue);
                    }
                    else
                    {
                        ApplyBindingTargetProgram(
                            runner,
                            element.Target,
                            elementValue,
                            environment,
                            context,
                            mode,
                            hasInitializer: element.DefaultProgram is not null || !next.Done,
                            allowNameInference: false);
                    }
                    if (context.ShouldStopEvaluation)
                    {
                        CloseIteratorOnAbrupt();
                        return;
                    }
                }

                if (binding.RestElement is not null)
                {
                    AssignmentReference? preResolvedReference = null;
                    if (mode == BindingMode.Assign)
                    {
                        preResolvedReference = TryPreResolveAssignmentTargetProgram(
                            runner,
                            binding.RestElement,
                            environment,
                            context);
                        if (context.ShouldStopEvaluation)
                        {
                            CloseIteratorOnAbrupt();
                            return;
                        }
                    }

                    var restArray = new JsArray(context.RealmState);
                    while (true)
                    {
                        (JsValue Value, bool Done) restNext;
                        try
                        {
                            restNext = iteratorRecord.Next(context);
                        }
                        catch (ThrowSignal)
                        {
                            iteratorThrew = true;
                            throw;
                        }

                        SetIteratorDone(restNext.Done);
                        if (context.ShouldStopEvaluation)
                        {
                            CloseIteratorOnAbrupt();
                            return;
                        }

                        if (restNext.Done)
                        {
                            break;
                        }

                        restArray.Push(restNext.Value);
                    }

                    if (preResolvedReference is { } resolvedReference)
                    {
                        resolvedReference.SetValue(JsValue.FromJsArray(restArray));
                    }
                    else
                    {
                        ApplyBindingTargetProgram(
                            runner,
                            binding.RestElement,
                            JsValue.FromJsArray(restArray),
                            environment,
                            context,
                            mode,
                            allowNameInference: false);
                    }
                    if (context.ShouldStopEvaluation)
                    {
                        CloseIteratorOnAbrupt();
                        return;
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
                    iterator.IteratorClose(context, preserveExistingThrow: true);
                }

                throw;
            }
            catch
            {
                if (iterator is not null && !iteratorDone)
                {
                    iterator.IteratorClose(context, preserveExistingThrow: context.IsThrow);
                    if (context.IsThrow)
                    {
                        return;
                    }
                }

                throw;
            }
            finally
            {
                if (activeIteratorSymbol is not null && !IsSuspendedForResume())
                {
                    environment.DeleteBinding(activeIteratorSymbol);
                }

                enumerator?.Dispose();
            }

            if (iterator is not null && !iteratorDone)
            {
                iterator.IteratorClose(context, preserveExistingThrow: context.IsThrow);
                activeIteratorState?.MarkIteratorClosed();
            }

            void SetIteratorDone(bool done)
            {
                iteratorDone = done;
                if (done)
                {
                    activeIteratorState?.MarkIteratorClosed();
                }
            }

            bool IsSuspendedForResume()
            {
                return context.IsYield || context.IsPendingAwait;
            }

            void CloseIteratorOnAbrupt()
            {
                if (IsSuspendedForResume())
                {
                    return;
                }

                if (iterator is not null && !iteratorThrew && !iteratorDone)
                {
                    iterator.IteratorClose(context, preserveExistingThrow: context.IsThrow);
                    activeIteratorState?.MarkIteratorClosed();
                }
            }
        }

        private static bool TryBindDenseArrayPatternProgram(
            ExecutionPlanRunner? runner,
            ArrayBindingTargetProgram binding,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode)
        {
            if (binding.RestElement is not null ||
                context.InGeneratorContext ||
                !value.TryGetObject<JsArray>(out var array) ||
                array.HasCustomIndexedProperties ||
                !array.HasDefaultValuesIteratorForFastDestructuring())
            {
                return false;
            }

            var elements = binding.Elements;
            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                if (element is not { Target: IdentifierBindingTargetProgram, DefaultProgram: null } &&
                    element.Target is not null)
                {
                    return false;
                }

                if (!array.HasOwnIndex((uint)i))
                {
                    return false;
                }
            }

            for (var i = 0; i < elements.Length; i++)
            {
                if (elements[i].Target is not IdentifierBindingTargetProgram identifier)
                {
                    continue;
                }

                ApplyIdentifierBindingProgram(
                    runner,
                    identifier,
                    array.GetElement((uint)i),
                    environment,
                    context,
                    mode,
                    hasInitializer: true,
                    allowNameInference: false,
                    skipBlockedBindingLookup: false);

                if (context.ShouldStopEvaluation)
                {
                    return true;
                }
            }

            return true;
        }

        private sealed class ActiveArrayPatternIteratorState(IJsObjectLike iterator) : IActiveIteratorState
        {
            private bool _closed;

            public bool TryGetActiveIterator(out IJsObjectLike activeIterator)
            {
                if (!_closed)
                {
                    activeIterator = iterator;
                    return true;
                }

                activeIterator = null!;
                return false;
            }

            public void MarkIteratorClosed()
            {
                _closed = true;
            }
        }

        private static AssignmentReference? TryPreResolveAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            BindingTargetProgram target,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return target switch
            {
                NamedPropertyAssignmentBindingTargetProgram namedProperty =>
                    PreResolveNamedPropertyAssignmentTargetProgram(runner, namedProperty, environment, context),
                ComputedPropertyAssignmentBindingTargetProgram computedProperty =>
                    PreResolveComputedPropertyAssignmentTargetProgram(runner, computedProperty, environment, context),
                NamedSuperPropertyAssignmentBindingTargetProgram namedSuperProperty =>
                    PreResolveNamedSuperPropertyAssignmentTargetProgram(namedSuperProperty, environment, context),
                ComputedSuperPropertyAssignmentBindingTargetProgram computedSuperProperty =>
                    PreResolveComputedSuperPropertyAssignmentTargetProgram(
                        runner,
                        computedSuperProperty,
                        environment,
                        context),
                _ => null
            };
        }

        private static AssignmentReference? PreResolveNamedPropertyAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            NamedPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateBindingExpressionProgram(runner, targetProgram.TargetProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            return AssignmentReference.ForDelegate(
                static () => JsValue.Undefined,
                value => ApplyProgramNamedPropertyAssignment(
                    target,
                    targetProgram.PropertyName,
                    allowNameInference: false,
                    value,
                    context));
        }

        private static AssignmentReference? PreResolveComputedPropertyAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            ComputedPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateBindingExpressionProgram(runner, targetProgram.TargetProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            var propertyKey = EvaluateBindingExpressionProgram(runner, targetProgram.PropertyProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            return AssignmentReference.ForDelegate(
                static () => JsValue.Undefined,
                value => ApplyProgramComputedPropertyAssignment(
                    target,
                    propertyKey,
                    allowNameInference: false,
                    value,
                    context));
        }

        private static AssignmentReference? PreResolveNamedSuperPropertyAssignmentTargetProgram(
            NamedSuperPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            EnsureProgramSuperReference(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            return AssignmentReference.ForDelegate(
                static () => JsValue.Undefined,
                value => ApplyProgramNamedSuperPropertyAssignment(
                    targetProgram.PropertyName,
                    allowNameInference: false,
                    value,
                    environment,
                    context));
        }

        private static AssignmentReference? PreResolveComputedSuperPropertyAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            ComputedSuperPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            EnsureProgramSuperReference(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            var propertyKey = EvaluateBindingExpressionProgram(runner, targetProgram.PropertyProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            return AssignmentReference.ForDelegate(
                static () => JsValue.Undefined,
                value => ApplyProgramComputedSuperPropertyAssignment(
                    propertyKey,
                    allowNameInference: false,
                    value,
                    environment,
                    context));
        }

        private static void ApplyNamedPropertyAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            NamedPropertyAssignmentBindingTargetProgram targetProgram,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateBindingExpressionProgram(runner, targetProgram.TargetProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            ApplyProgramNamedPropertyAssignment(
                target,
                targetProgram.PropertyName,
                allowNameInference: false,
                value,
                context);
        }

        private static void ApplyComputedPropertyAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            ComputedPropertyAssignmentBindingTargetProgram targetProgram,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateBindingExpressionProgram(runner, targetProgram.TargetProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            var propertyKey = EvaluateBindingExpressionProgram(runner, targetProgram.PropertyProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            ApplyProgramComputedPropertyAssignment(
                target,
                propertyKey,
                allowNameInference: false,
                value,
                context);
        }

        private static void ApplyComputedSuperPropertyAssignmentTargetProgram(
            ExecutionPlanRunner? runner,
            ComputedSuperPropertyAssignmentBindingTargetProgram targetProgram,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var propertyKey = EvaluateBindingExpressionProgram(runner, targetProgram.PropertyProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            ApplyProgramComputedSuperPropertyAssignment(
                propertyKey,
                allowNameInference: false,
                value,
                environment,
                context);
        }

        private static JsValue EvaluateBindingExpressionProgram(
            ExecutionPlanRunner? runner,
            ExpressionProgram program,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return runner is null
                ? UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(program, environment, context)
                : runner.EvaluateExpressionProgram(program, environment, context);
        }
    }
}
