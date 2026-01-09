#region

using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static InstructionResult HandleForInInit(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ForInInitInstruction>(instr);
            var objectEnv = environment;

            // Create TDZ environment for lexical declarations if needed
            if (!instruction.TdzBindings.IsDefaultOrEmpty)
            {
                objectEnv = new JsEnvironment(environment, false, false,
                    instruction.ObjectExpression.Source, "for-in-head-tdz");
                foreach (var tdzSymbol in instruction.TdzBindings)
                {
                    objectEnv.DefineJsValue(tdzSymbol, JsValue.Uninitialized,
                        instruction.TdzIsConst, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }
            }

            // Evaluate the object expression
            var objectValue = instruction.ObjectExpression.EvaluateExpression(objectEnv, context);
            if (context.IsThrow)
            {
                var initThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, initThrown))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(initThrown);
            }

            // Rent a ForInDriverState and collect property keys
            var forInState = ForInDriverStatePool.Rent();
            CollectEnumerablePropertyKeys(objectValue, forInState.PropertyKeys);

            // Set up the state environment
            var stateEnv = environment;
            var walkCount = 0;
            if (instruction.StateSlotIndex >= 0)
            {
                while (stateEnv is not null &&
                       (stateEnv.ScopeId != runner._plan!.RootScopeId ||
                        !stateEnv.HasSlots ||
                        stateEnv._slots!.Length <= instruction.StateSlotIndex))
                {
                    stateEnv = stateEnv.Enclosing;
                    walkCount++;
                    if (walkCount > 1000)
                    {
                        break;
                    }
                }

                stateEnv ??= environment;
            }

            // Cache the JsVariable for fast slot access (use helper to apply offset for script mode)
            if (instruction.StateSlotIndex >= 0 && stateEnv.HasSlots)
            {
                forInState.StateVariable = runner.CreateSlotVariable(stateEnv, instruction.StateSlotIndex);
            }

            forInState.LoopScopeEnvironment = environment;
            runner.ForInStateRef.CurrentDriverState = forInState;

            // Store the state (use runner instance method to apply slot offset)
            runner.StoreValueBySlot(stateEnv, instruction.StateSlot,
                instruction.StateSlotIndex,
                JsValue.FromObjectUnsafe(forInState));

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static InstructionResult HandleForInMoveNext(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ForInMoveNextInstruction>(instr);

            // Use cached driver state for scope-correct access
            var driverState = runner.ForInStateRef.CurrentDriverState;

            if (driverState is null)
            {
                // Fallback: try to get state from the correct scope
                var slotEnv = environment;
                var slotIdx = instruction.StateSlotIndex;

                if (slotIdx >= 0)
                {
                    var slotWalkCount = 0;
                    while (slotEnv != null &&
                           (slotEnv.ScopeId != runner._plan!.RootScopeId ||
                            !slotEnv.HasSlots ||
                            slotEnv._slots!.Length <= slotIdx))
                    {
                        slotEnv = slotEnv.Enclosing;
                        slotWalkCount++;
                        if (slotWalkCount > 100)
                        {
                            break;
                        }
                    }

                    slotEnv ??= environment;
                }

                if (slotEnv is null || !runner.TryGetValueBySlot(slotEnv,
                        instruction.StateSlot,
                        slotIdx, out var stateValue))
                {
                    runner._programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (!stateValue.TryGetObject(out driverState))
                {
                    runner._programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.ForInStateRef.CurrentDriverState = driverState;
            }

            driverState.AssertOwnership("for-in driver state");

            // Get JsVariables directly from driverState (O(1) access)
            var stateVar = driverState.StateVariable;
            var valueVar = driverState.ValueVariable;

            // Capture value JsVariable on first execution (use helper to apply offset for script mode)
            if (!valueVar.IsValid && instruction.ValueSlotIndex >= 0)
            {
                var loopScopeEnv = stateVar.IsValid ? stateVar.Environment : environment;
                if (loopScopeEnv.HasSlots && loopScopeEnv._slots!.Length > instruction.ValueSlotIndex)
                {
                    valueVar = runner.CreateSlotVariable(loopScopeEnv, instruction.ValueSlotIndex);
                    driverState.ValueVariable = valueVar;
                }
            }

            // Check if we have more property keys
            if (driverState.CurrentIndex >= driverState.PropertyKeys.Count)
            {
                // Iteration complete - clean up and jump to break
                runner.ForInStateRef.CurrentDriverState = null;
                ForInDriverStatePool.Return(driverState);
                runner._programCounter = instruction.BreakIndex;
                returnValue = default;
                return InstructionResult.Continue;
            }

            // Get the current property key
            var currentKey = driverState.PropertyKeys[driverState.CurrentIndex];
            driverState.CurrentIndex++;

            // Store the value in the value slot
            if (valueVar.IsValid)
            {
                valueVar.Write(currentKey);
            }
            else
            {
                var loopScopeEnv = stateVar.IsValid ? stateVar.Environment : environment;
                runner.StoreValueBySlot(loopScopeEnv, instruction.ValueSlot,
                    instruction.ValueSlotIndex, currentKey);
            }

            // Continue to loop body
            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        /// <summary>
        /// Collects all enumerable property keys from an object and its prototype chain.
        /// Per ES spec, for-in enumerates string-keyed enumerable properties.
        /// </summary>
        private static void CollectEnumerablePropertyKeys(JsValue value, List<JsValue> keys)
        {
            // Per ES spec, for-in over null or undefined should not iterate
            if (value.IsNull || value.IsUndefined)
            {
                return;
            }

            switch (value.Kind)
            {
                case JsValueKind.Object when value.ObjectValue is JsArray array:
                    CollectArrayPropertyKeys(array, keys);
                    break;

                case JsValueKind.Object when value.ObjectValue is TypedArrayBase typedArray:
                    CollectTypedArrayPropertyKeys(typedArray, keys);
                    break;

                case JsValueKind.String when value.ObjectValue is string s:
                    CollectStringPropertyKeys(s, keys);
                    break;

                case JsValueKind.Object when value.ObjectValue is IJsObjectLike accessor:
                    CollectObjectPropertyKeys(accessor, keys);
                    break;
            }
        }

        private static void CollectArrayPropertyKeys(JsArray array, List<JsValue> keys)
        {
            // First, enumerate numeric indices (array elements)
            for (var i = 0; i < array.Items.Count; i++)
            {
                keys.Add(JsValue.FromString(i.ToString(CultureInfo.InvariantCulture)));
            }

            // Track seen keys to properly handle shadowing
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            // Add all numeric indices as seen (already enumerated above)
            for (var i = 0; i < array.Items.Count; i++)
            {
                seenKeys.Add(JsValueCache.GetIndexString(i));
            }

            // Now enumerate non-index properties on the array and its prototype chain
            IJsPropertyAccessor? current = array;
            while (current is not null)
            {
                var ownKeys = current.GetOwnPropertyNames().ToList();

                foreach (var key in ownKeys)
                {
                    // Skip if we've already seen this key
                    if (!seenKeys.Add(key))
                    {
                        continue;
                    }

                    // Skip 'length' - it's not enumerable
                    if (string.Equals(key, "length", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var desc = current.GetOwnPropertyDescriptor(key);
                    if (desc is null or { Enumerable: false })
                    {
                        continue;
                    }

                    keys.Add(JsValue.FromString(key));
                }

                // Move to prototype
                current = current switch
                {
                    IJsObjectLike objectLike => objectLike.Prototype,
                    IPrototypeAccessorProvider provider => provider.PrototypeAccessor,
                    _ => null
                };
            }
        }

        private static void CollectTypedArrayPropertyKeys(TypedArrayBase typedArray, List<JsValue> keys)
        {
            // TypedArray for-in only exposes own enumerable properties (indices and custom slots)
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in typedArray.GetOwnPropertyNames().ToList())
            {
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                var desc = typedArray.GetOwnPropertyDescriptor(key);
                if (desc is null or { Enumerable: false })
                {
                    continue;
                }

                keys.Add(JsValue.FromString(key));
            }
        }

        private static void CollectStringPropertyKeys(string s, List<JsValue> keys)
        {
            for (var i = 0; i < s.Length; i++)
            {
                keys.Add(JsValue.FromString(JsValueCache.GetIndexString(i)));
            }
        }

        private static void CollectObjectPropertyKeys(IJsObjectLike accessor, List<JsValue> keys)
        {
            // Track seen keys to properly handle shadowing
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            // Walk prototype chain, starting with the object itself
            IJsPropertyAccessor? current = accessor;
            while (current is not null)
            {
                // Collect keys from this object in the chain
                var ownKeys = current.GetOwnPropertyNames().ToList();

                foreach (var key in ownKeys)
                {
                    // Skip if we've already seen this key (shadowed by own/earlier property)
                    if (!seenKeys.Add(key))
                    {
                        continue;
                    }

                    // Per ECMAScript spec, check that the property still exists
                    var desc = current.GetOwnPropertyDescriptor(key);
                    if (desc is null)
                    {
                        continue;
                    }

                    if (desc is { Enumerable: false })
                    {
                        continue;
                    }

                    keys.Add(JsValue.FromString(key));
                }

                // Move to prototype
                current = current switch
                {
                    IJsObjectLike objectLike => objectLike.Prototype,
                    IPrototypeAccessorProvider provider => provider.PrototypeAccessor,
                    _ => null
                };
            }
        }
    }
}
