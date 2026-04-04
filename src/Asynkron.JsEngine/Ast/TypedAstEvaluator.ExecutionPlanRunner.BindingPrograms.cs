#region

using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

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
            bool skipBlockedBindingLookup = false)
        {
            switch (target)
            {
                case IdentifierBindingTargetProgram identifier:
                    ApplyIdentifierBindingProgram(
                        identifier.Name,
                        value,
                        environment,
                        context,
                        mode,
                        hasInitializer,
                        allowNameInference,
                        skipBlockedBindingLookup);
                    return;

                case ArrayBindingTargetProgram arrayBinding:
                    BindArrayPatternProgram(arrayBinding, value, environment, context, mode);
                    return;

                case ObjectBindingTargetProgram objectBinding:
                    BindObjectPatternProgram(objectBinding, value, environment, context, mode);
                    return;

                case NamedPropertyAssignmentBindingTargetProgram namedPropertyAssignment:
                    ApplyNamedPropertyAssignmentTargetProgram(
                        namedPropertyAssignment,
                        value,
                        environment,
                        context);
                    return;

                case ComputedPropertyAssignmentBindingTargetProgram computedPropertyAssignment:
                    ApplyComputedPropertyAssignmentTargetProgram(
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

        private void ApplyIdentifierBindingProgram(
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
                        environment.EnsureFunctionScopedVarBinding(name, context);
                        environment.GetVarEnvironment().AssignJsValue(name, value);
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

        private void BindObjectPatternProgram(
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
                    var propertyKeyValue = EvaluateExpressionProgram(nameProgram, environment, context);
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
                        property.Target,
                        environment,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (property.Target is IdentifierBindingTargetProgram identifierForSideEffects)
                {
                    _ = environment.HasBinding(identifierForSideEffects.Name);
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
                    propertyValue = EvaluateExpressionProgram(defaultProgram, environment, context);
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

                if (preResolvedReference is { } resolvedReference)
                {
                    resolvedReference.SetValue(propertyValue);
                }
                else
                {
                    ApplyBindingTargetProgram(
                        property.Target,
                        propertyValue,
                        environment,
                        context,
                        mode,
                        hasInitializer: property.DefaultProgram is not null || !propertyValue.IsUndefined,
                        allowNameInference: false,
                        skipBlockedBindingLookup: mode == BindingMode.DefineVar &&
                                                  property.Target is IdentifierBindingTargetProgram);
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
                binding.RestElement,
                JsValue.FromJsObject(restObject),
                environment,
                context,
                mode,
                allowNameInference: false);
        }

        private void BindArrayPatternProgram(
            ArrayBindingTargetProgram binding,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode)
        {
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
            var iteratorDone = false;
            var iteratorThrew = false;

            try
            {
                foreach (var element in binding.Elements)
                {
                    AssignmentReference? preResolvedReference = null;
                    if (mode == BindingMode.Assign && element.Target is not null)
                    {
                        preResolvedReference = TryPreResolveAssignmentTargetProgram(
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

                    iteratorDone = next.Done;
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
                        elementValue = EvaluateExpressionProgram(defaultProgram, environment, context);
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

                        iteratorDone = restNext.Done;
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
                enumerator?.Dispose();
            }

            if (iterator is not null && !iteratorDone)
            {
                iterator.IteratorClose(context, preserveExistingThrow: context.IsThrow);
            }

            void CloseIteratorOnAbrupt()
            {
                if (iterator is not null && !iteratorThrew && !iteratorDone)
                {
                    iterator.IteratorClose(context, preserveExistingThrow: context.IsThrow);
                }
            }
        }

        private AssignmentReference? TryPreResolveAssignmentTargetProgram(
            BindingTargetProgram target,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return target switch
            {
                NamedPropertyAssignmentBindingTargetProgram namedProperty =>
                    PreResolveNamedPropertyAssignmentTargetProgram(namedProperty, environment, context),
                ComputedPropertyAssignmentBindingTargetProgram computedProperty =>
                    PreResolveComputedPropertyAssignmentTargetProgram(computedProperty, environment, context),
                NamedSuperPropertyAssignmentBindingTargetProgram namedSuperProperty =>
                    PreResolveNamedSuperPropertyAssignmentTargetProgram(namedSuperProperty, environment, context),
                ComputedSuperPropertyAssignmentBindingTargetProgram computedSuperProperty =>
                    PreResolveComputedSuperPropertyAssignmentTargetProgram(computedSuperProperty, environment, context),
                _ => null
            };
        }

        private AssignmentReference? PreResolveNamedPropertyAssignmentTargetProgram(
            NamedPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateExpressionProgram(targetProgram.TargetProgram, environment, context);
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

        private AssignmentReference? PreResolveComputedPropertyAssignmentTargetProgram(
            ComputedPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateExpressionProgram(targetProgram.TargetProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            var propertyKey = EvaluateExpressionProgram(targetProgram.PropertyProgram, environment, context);
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

        private AssignmentReference? PreResolveComputedSuperPropertyAssignmentTargetProgram(
            ComputedSuperPropertyAssignmentBindingTargetProgram targetProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            EnsureProgramSuperReference(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return null;
            }

            var propertyKey = EvaluateExpressionProgram(targetProgram.PropertyProgram, environment, context);
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

        private void ApplyNamedPropertyAssignmentTargetProgram(
            NamedPropertyAssignmentBindingTargetProgram targetProgram,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateExpressionProgram(targetProgram.TargetProgram, environment, context);
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

        private void ApplyComputedPropertyAssignmentTargetProgram(
            ComputedPropertyAssignmentBindingTargetProgram targetProgram,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var target = EvaluateExpressionProgram(targetProgram.TargetProgram, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            var propertyKey = EvaluateExpressionProgram(targetProgram.PropertyProgram, environment, context);
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

        private void ApplyComputedSuperPropertyAssignmentTargetProgram(
            ComputedSuperPropertyAssignmentBindingTargetProgram targetProgram,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var propertyKey = EvaluateExpressionProgram(targetProgram.PropertyProgram, environment, context);
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
    }
}
