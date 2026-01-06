#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        /// <summary>
        /// Eagerly populates flat slots for all variables in the given scope.
        /// Called when entering a new scope via PushEnvironment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            foreach (var (slotIndex, flatSlotId) in mappings)
            {
                _flatSlots[flatSlotId] = new JsVariable(environment, slotIndex);
            }
        }
    }
}
