using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IdentifierBinding identifier)
    {
        private void ApplyIdentifierBinding(object? value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer,
            bool allowNameInference)
        {
            if (allowNameInference && value is IFunctionNameTarget nameTarget)
            {
                nameTarget.EnsureHasName(identifier.Name.Name);
            }

            if (mode == BindingMode.Assign && environment.IsConstBinding(identifier.Name))
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Cannot reassign constant '{identifier.Name.Name}'.", context, context.RealmState));
            }

            switch (mode)
            {
                case BindingMode.Assign:
                    environment.Assign(identifier.Name, value);
                    break;
                case BindingMode.DefineLet:
                    environment.Define(identifier.Name, value, isLexical: true, blocksFunctionScopeOverride: true);
                    break;
                case BindingMode.DefineConst:
                    environment.Define(identifier.Name, value, true, blocksFunctionScopeOverride: true);
                    break;
                case BindingMode.DefineVar:
                {
                    var assignedBlockedBinding = environment.TryAssignBlockedBinding(identifier.Name, value);

                    EnsureFunctionScopedVarBinding(environment, identifier.Name, context);

                    if (hasInitializer && !assignedBlockedBinding)
                    {
                        environment.Assign(identifier.Name, value);
                    }

                    break;
                }
                case BindingMode.DefineParameter:
                    // Parameters are created before defaults run (see the pre-pass in BindFunctionParameters),
                    // so by the time we bind the value the slot should already exist and still be
                    // uninitialized. Assign into it to preserve the TDZ throw on reads during
                    // initializer evaluation, and fall back to Define only if the slot was not
                    // created (defensive).
                    if (environment.HasBinding(identifier.Name))
                    {
                        environment.Assign(identifier.Name, value);
                    }
                    else
                    {
                        environment.Define(identifier.Name, value, isLexical: false);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}
