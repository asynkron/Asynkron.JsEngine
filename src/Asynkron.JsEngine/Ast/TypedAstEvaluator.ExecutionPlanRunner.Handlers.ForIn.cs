#region

using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        [MethodImpl(JsEngineConstants.Inlining)]
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
                objectEnv = JsEnvironment.CreateInstance(environment, false, false,
                    instruction.ObjectSource ?? instruction.ObjectExpression?.Source, "for-in-head-tdz");
                foreach (var tdzSymbol in instruction.TdzBindings)
                {
                    objectEnv.DefineJsValue(tdzSymbol, JsValue.Uninitialized,
                        instruction.TdzIsConst, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }
            }

            // Evaluate the object expression
            var objectValue = instruction.ObjectProgram is { } objectProgram
                ? runner.EvaluateExpressionProgram(objectProgram, objectEnv, context)
                : instruction.ObjectExpression!.EvaluateExpression(objectEnv, context);
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
            forInState.SourceObject = objectValue;
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

        [MethodImpl(JsEngineConstants.Inlining)]
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

                if (!runner.TryGetValueBySlot(slotEnv,
                        instruction.StateSlot,
                        slotIdx, out var stateValue) || !stateValue.TryGetObject(out driverState))
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

            // Find the next property key that still exists on the object.
            // Per ES spec, properties deleted during enumeration should be skipped.
            JsValue currentKey;
            while (true)
            {
                if (driverState.CurrentIndex >= driverState.PropertyKeys.Count)
                {
                    // Iteration complete - clean up and jump to break
                    runner.ForInStateRef.CurrentDriverState = null;
                    ForInDriverStatePool.Return(driverState);
                    runner._programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                currentKey = driverState.PropertyKeys[driverState.CurrentIndex];
                driverState.CurrentIndex++;

                // Check if the property still exists on the source object (or its prototype chain)
                if (PropertyStillExists(driverState.SourceObject, currentKey))
                {
                    break;
                }
            }

            // Store the value in the value slot.
            // Always use StoreValueBySlot which updates both the slot AND the dictionary.
            // The dictionary write is needed because the binding statement reads __forIn_value
            // via identifier resolution, which falls back to dictionary lookup when the slot
            // ScopeId doesn't match (e.g., strict-wrapper scripts where the environment's
            // ScopeId is synthetic but identifiers use the analysis root scope ID).
            {
                var loopScopeEnv = valueVar.IsValid ? valueVar.Environment : (stateVar.IsValid ? stateVar.Environment : environment);
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
            // Track seen keys to properly handle shadowing
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            // First, enumerate numeric indices (array elements) - skip non-enumerable ones
            for (var i = 0; i < array.Items.Count; i++)
            {
                var indexKey = i.ToString(CultureInfo.InvariantCulture);
                seenKeys.Add(indexKey);

                // Check if there's an explicit descriptor marking this index as non-enumerable
                var desc = array.GetOwnPropertyDescriptor(indexKey);
                if (desc is { Enumerable: false })
                {
                    continue;
                }

                keys.Add(JsValue.FromString(indexKey));
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

                // Move to prototype - check both Prototype (JsObject) and PrototypeAccessor
                current = current switch
                {
                    IJsObjectLike objectLike when objectLike.Prototype is not null => objectLike.Prototype,
                    IPrototypeAccessorProvider provider when provider.PrototypeAccessor is not null =>
                        provider.PrototypeAccessor,
                    IJsObjectLike objectLike2 when objectLike2 is IPrototypeAccessorProvider prov2 =>
                        prov2.PrototypeAccessor,
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

        /// <summary>
        /// Checks if a property key still exists as an enumerable property on the object
        /// or its prototype chain. Used to skip properties deleted during for-in enumeration.
        /// </summary>
        private static bool PropertyStillExists(JsValue sourceObject, JsValue key)
        {
            if (sourceObject.ObjectValue is not IJsObjectLike obj)
            {
                return true; // can't check, assume exists
            }

            var keyStr = key.IsString && key.ObjectValue is string s ? s : key.ToString();

            // Walk prototype chain
            IJsPropertyAccessor? current = obj;
            while (current is not null)
            {
                var desc = current.GetOwnPropertyDescriptor(keyStr);
                if (desc is not null)
                {
                    return desc is not { Enumerable: false };
                }

                current = current switch
                {
                    IJsObjectLike objectLike when objectLike.Prototype is not null => objectLike.Prototype,
                    IPrototypeAccessorProvider provider when provider.PrototypeAccessor is not null =>
                        provider.PrototypeAccessor,
                    _ => null
                };
            }

            return false; // property no longer exists
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

                // Move to prototype - check both Prototype (JsObject) and PrototypeAccessor
                // (IJsPropertyAccessor) to handle cases where the prototype is a HostFunction
                // (e.g., Function.prototype) rather than a JsObject.
                current = current switch
                {
                    IJsObjectLike objectLike when objectLike.Prototype is not null => objectLike.Prototype,
                    IPrototypeAccessorProvider provider when provider.PrototypeAccessor is not null =>
                        provider.PrototypeAccessor,
                    IJsObjectLike objectLike2 when objectLike2 is IPrototypeAccessorProvider prov2 =>
                        prov2.PrototypeAccessor,
                    _ => null
                };
            }
        }
    }
}
