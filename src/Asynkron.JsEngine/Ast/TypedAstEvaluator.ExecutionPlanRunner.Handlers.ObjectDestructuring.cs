using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        /// <summary>
        /// State object for object destructuring - stores the coerced source object and
        /// the property keys consumed so a trailing rest element can exclude them.
        /// </summary>
        private sealed class ObjectDestructuringState : IDisposable
        {
            public IJsObjectLike Source = null!;
            public readonly HashSet<string> UsedKeys = new(StringComparer.Ordinal);
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                UsedKeys.Clear();
                Source = null!;
            }
        }

        private static InstructionResult HandleObjectDestructuringInit(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ObjectDestructuringInitInstruction>(instr);
            var sourceValue = runner.EvaluateExpressionProgram(instruction.SourceProgram, environment, context);

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                return AbortObjectDestructuring(runner, null, thrown, out returnValue);
            }

            if (!TryToObjectForDestructuring(sourceValue, context, out var source))
            {
                var typeError = StandardLibrary.CreateTypeError(
                    "Cannot destructure undefined or null", context, context.RealmState);
                return AbortObjectDestructuring(runner, null, typeError, out returnValue);
            }

            var state = new ObjectDestructuringState
            {
                Source = source
            };

            runner.StoreValueBySlot(environment, instruction.SourceSlot, instruction.SourceSlotIndex,
                JsValue.FromObjectUnsafe(state));

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleObjectDestructuringProperty(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ObjectDestructuringPropertyInstruction>(instr);
            var state = GetObjectDestructuringState(runner, environment, instruction.SourceSlot,
                instruction.SourceSlotIndex);

            state.UsedKeys.Add(instruction.PropertyName);

            JsValue value;
            try
            {
                var hasProperty = JsOps.TryGetPropertyValue(
                    JsValue.FromObjectUnsafe(state.Source),
                    instruction.PropertyName,
                    out value,
                    context);
                if (!hasProperty)
                {
                    value = JsValue.Undefined;
                }
            }
            catch (ThrowSignal signal)
            {
                if (!context.IsThrow)
                {
                    context.SetThrow(signal.ThrownValue);
                }

                var thrown = context.FlowValue;
                context.Clear();
                return AbortObjectDestructuring(runner, state, thrown, out returnValue);
            }

            if (context.ShouldStopEvaluation)
            {
                var thrown = context.FlowValue;
                context.Clear();
                return AbortObjectDestructuring(runner, state, thrown, out returnValue);
            }

            BindDestructuringValue(
                runner,
                environment,
                instruction.TargetSymbol,
                instruction.TargetSlotIndex,
                value,
                instruction.VarKind,
                context);

            if (context.ShouldStopEvaluation)
            {
                var thrown = context.FlowValue;
                context.Clear();
                return AbortObjectDestructuring(runner, state, thrown, out returnValue);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleObjectDestructuringRest(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ObjectDestructuringRestInstruction>(instr);
            var state = GetObjectDestructuringState(runner, environment, instruction.SourceSlot,
                instruction.SourceSlotIndex);

            var restObject = new JsObject();
            if (context.RealmState?.ObjectPrototype is not null)
            {
                restObject.SetPrototype(context.RealmState.ObjectPrototype);
            }

            try
            {
                foreach (var key in state.Source.GetOwnPropertyKeysInOrder())
                {
                    if (state.UsedKeys.Contains(key))
                    {
                        continue;
                    }

                    var descriptor = state.Source.GetOwnPropertyDescriptor(key);
                    if (descriptor is not { Enumerable: true })
                    {
                        continue;
                    }

                    if (JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(state.Source), key, out var restValue,
                            context))
                    {
                        restObject.SetProperty(key, restValue);
                        continue;
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        break;
                    }
                }
            }
            catch (ThrowSignal signal)
            {
                if (!context.IsThrow)
                {
                    context.SetThrow(signal.ThrownValue);
                }
            }

            if (context.ShouldStopEvaluation)
            {
                var thrown = context.FlowValue;
                context.Clear();
                return AbortObjectDestructuring(runner, state, thrown, out returnValue);
            }

            BindDestructuringValue(
                runner,
                environment,
                instruction.RestSymbol,
                instruction.RestSlotIndex,
                JsValue.FromObjectUnsafe(restObject),
                instruction.VarKind,
                context);

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleObjectDestructuringClose(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ObjectDestructuringCloseInstruction>(instr);

            // Object destructuring has no iterator to close; just release the state slot.
            if (runner.TryGetValueBySlot(environment, instruction.SourceSlot, instruction.SourceSlotIndex,
                    out var stateValue) &&
                stateValue.TryGetObject<ObjectDestructuringState>(out var state))
            {
                state.Dispose();
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static ObjectDestructuringState GetObjectDestructuringState(
            ExecutionPlanRunner runner,
            JsEnvironment environment,
            Symbol sourceSlot,
            int sourceSlotIndex)
        {
            if (!runner.TryGetValueBySlot(environment, sourceSlot, sourceSlotIndex, out var stateValue) ||
                !stateValue.TryGetObject<ObjectDestructuringState>(out var state))
            {
                throw new InvalidOperationException("Object destructuring state not found");
            }

            return state;
        }

        private static InstructionResult AbortObjectDestructuring(
            ExecutionPlanRunner runner,
            ObjectDestructuringState? state,
            JsValue thrown,
            out JsValue returnValue)
        {
            state?.Dispose();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrown);
        }
    }
}
