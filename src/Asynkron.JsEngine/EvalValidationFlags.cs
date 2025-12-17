namespace Asynkron.JsEngine;

/// <summary>
///     Flags collected during a single-pass AST scan for eval validation.
/// </summary>
[Flags]
internal enum EvalValidationFlags
{
    None = 0,
    ContainsNewTarget = 1 << 0,
    ContainsSuperReference = 1 << 1,
    ContainsSuperCall = 1 << 2,
    ContainsArguments = 1 << 3,
    ContainsIllegalReturn = 1 << 4,
    ContainsIllegalBreakOrContinue = 1 << 5,
    // Flags for includeFunctionBodies=true variants
    ContainsNewTargetInFunctions = 1 << 6,
    ContainsSuperReferenceInFunctions = 1 << 7,
    ContainsSuperCallInFunctions = 1 << 8,
    ContainsArgumentsInFunctions = 1 << 9
}
