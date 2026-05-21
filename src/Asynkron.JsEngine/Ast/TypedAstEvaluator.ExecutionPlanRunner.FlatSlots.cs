#region

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#endregion

#pragma warning disable CS0618 // Compatibility overloads remain for dynamic/resume seams; not proof of direct runner AST fallback.

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        /// <summary>
        /// Eagerly populates flat slots for all variables in the given scope.
        /// Called when entering a new scope via PushEnvironment.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void PopulateFlatSlotsForScope(int scopeId, JsEnvironment environment)
        {
            if (_flatSlots is null || _plan?.FlatSlotMappings is null)
            {
                return;
            }

            if (!_plan.FlatSlotMappings.TryGetValue(scopeId, out var mappings))
            {
                return;
            }

            AssertFlatSlotMappings(scopeId, mappings, environment);
            foreach (var (slotIndex, flatSlotId) in mappings)
            {
                _flatSlots[flatSlotId] = new JsVariable(environment, slotIndex);
            }
        }

        [Conditional("DEBUG")]
        private void AssertFlatSlotsInitialized()
        {
            if (_plan is null || _flatSlots is null)
            {
                return;
            }

            if (_flatSlots.Length != _plan.FlatSlotCount)
            {
                throw new InvalidOperationException(
                    $"Flat slot array size mismatch. expected={_plan.FlatSlotCount} actual={_flatSlots.Length}");
            }
        }

        [Conditional("DEBUG")]
        private void AssertFlatSlotMappings(
            int scopeId,
            ImmutableArray<(int SlotIndex, int FlatSlotId)> mappings,
            JsEnvironment environment)
        {
            if (_flatSlots is null || _plan is null)
            {
                return;
            }

            if (scopeId >= 0 && (_plan.FlatSlotMappings?.ContainsKey(scopeId) != true))
            {
                throw new InvalidOperationException(
                    $"Flat slot mapping missing for scope. scopeId={scopeId}");
            }

            for (var i = 0; i < mappings.Length; i++)
            {
                var (slotIndex, flatSlotId) = mappings[i];
                if (flatSlotId < 0 || flatSlotId >= _flatSlots.Length)
                {
                    throw new InvalidOperationException(
                        $"Flat slot id out of range. scopeId={scopeId} flatSlotId={flatSlotId} length={_flatSlots.Length}");
                }

                environment.AssertSlotIndexValid(slotIndex, nameof(AssertFlatSlotMappings));
            }
        }
    }
}
