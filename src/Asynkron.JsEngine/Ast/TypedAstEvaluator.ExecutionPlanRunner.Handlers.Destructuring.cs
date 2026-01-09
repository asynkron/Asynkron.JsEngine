#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        /// <summary>
        /// State object for array destructuring - stores iterator and enumerator.
        /// </summary>
        private sealed class ArrayDestructuringState : IDisposable
        {
            public IJsObjectLike? Iterator;
            public IEnumerator<JsValue>? Enumerator;
            public IJsCallable? NextMethod;
            public bool Done;
            private bool _disposed;

            public (JsValue Value, bool Done) Next(EvaluationContext context)
            {
                if (Done)
                {
                    return (JsValue.Undefined, true);
                }

                if (Iterator is null)
                {
                    if (Enumerator?.MoveNext() != true)
                    {
                        Done = true;
                        return (JsValue.Undefined, true);
                    }
                    return (Enumerator.Current, false);
                }

                NextMethod ??= Iterator.GetIteratorNextCallable(context);
                var candidate = Iterator.InvokeIteratorNext(NextMethod, context: context);
                if (!candidate.TryGetObject<IJsObjectLike>(out var result))
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                }

                var done =
                    JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(result), "done", out var doneValue, context) &&
                    JsOps.ToBoolean(doneValue);

                if (done)
                {
                    Done = true;
                    return (JsValue.Undefined, true);
                }

                var value = JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(result), "value", out var yieldedValue,
                    context)
                    ? yieldedValue
                    : JsValue.Undefined;

                return (value, false);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Enumerator?.Dispose();
                Enumerator = null;
            }
        }

        private static InstructionResult HandleArrayDestructuringInit(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ArrayDestructuringInitInstruction>(instr);

            // Evaluate the source expression
            var sourceValue = instruction.SourceExpression.EvaluateExpression(environment, context);

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }
                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrown);
            }

            // Get iterator from the source value
            if (!TryGetIteratorForDestructuring(sourceValue, context, out var iterator, out var enumerator))
            {
                if (context.ShouldStopEvaluation)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }
                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                var typeError = StandardLibrary.CreateTypeError($"Cannot destructure non-iterable value.{context.GetSourceInfo()}", context, context.RealmState);
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, typeError))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }
                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(typeError);
            }

            // Create state object
            var state = new ArrayDestructuringState
            {
                Iterator = iterator,
                Enumerator = enumerator
            };

            // Store state in slot (use runner instance method to apply slot offset)
            runner.StoreValueBySlot(environment, instruction.IteratorSlot, instruction.IteratorSlotIndex,
                JsValue.FromObjectUnsafe(state));

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleArrayDestructuringElement(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ArrayDestructuringElementInstruction>(instr);

            // Get state from slot (use runner instance method to apply slot offset)
            if (!runner.TryGetValueBySlot(environment, instruction.IteratorSlot, instruction.IteratorSlotIndex, out var stateValue) ||
                !stateValue.TryGetObject<ArrayDestructuringState>(out var state))
            {
                throw new InvalidOperationException("Array destructuring state not found");
            }

            // Get next value from iterator
            var (value, _) = state.Next(context);

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                state.Dispose();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }
                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrown);
            }

            // Bind value to target (skip for holes where TargetSymbol is null)
            if (instruction.TargetSymbol is not null)
            {
                BindDestructuringValue(environment, instruction.TargetSymbol, value, instruction.VarKind);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleArrayDestructuringRest(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ArrayDestructuringRestInstruction>(instr);

            // Get state from slot (use runner instance method to apply slot offset)
            if (!runner.TryGetValueBySlot(environment, instruction.IteratorSlot, instruction.IteratorSlotIndex, out var stateValue) ||
                !stateValue.TryGetObject<ArrayDestructuringState>(out var state))
            {
                throw new InvalidOperationException("Array destructuring state not found");
            }

            // Collect remaining values into an array
            var restArray = new JsArray(context.RealmState);

            while (true)
            {
                var (value, done) = state.Next(context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    state.Dispose();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }
                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (done)
                {
                    break;
                }

                restArray.Push(value);
            }

            // Bind rest array to target
            BindDestructuringValue(environment, instruction.RestSymbol,
                JsValue.FromObjectUnsafe(restArray), instruction.VarKind);

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleArrayDestructuringClose(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ArrayDestructuringCloseInstruction>(instr);

            // Get state from slot and close/dispose (use runner instance method to apply slot offset)
            if (runner.TryGetValueBySlot(environment, instruction.IteratorSlot, instruction.IteratorSlotIndex, out var stateValue) &&
                stateValue.TryGetObject<ArrayDestructuringState>(out var state))
            {
                // If we have a JavaScript iterator, call IteratorClose to invoke return() method
                // This is required per ES spec to properly close generators and other iterators
                if (state.Iterator is not null && !state.Done)
                {
                    state.Iterator.IteratorClose(context);
                }

                state.Dispose();
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        /// <summary>
        /// Bind a destructured value to a symbol based on variable kind.
        /// </summary>
        private static void BindDestructuringValue(
            JsEnvironment environment,
            Symbol targetSymbol,
            JsValue value,
            VariableKind varKind)
        {
            switch (varKind)
            {
                case VariableKind.Var:
                    // For var, assign to existing (hoisted) binding
                    environment.AssignJsValue(targetSymbol, value);
                    break;

                case VariableKind.Let:
                    environment.DefineJsValue(targetSymbol, value, isConst: false, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                    break;

                case VariableKind.Const:
                    environment.DefineJsValue(targetSymbol, value, isConst: true, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported variable kind: {varKind}");
            }
        }
    }
}
