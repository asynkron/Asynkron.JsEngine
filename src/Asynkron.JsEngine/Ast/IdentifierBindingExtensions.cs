#region

using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static void ApplyIdentifierBinding(this IdentifierBinding identifier, JsValue value,
        JsEnvironment environment,
        EvaluationContext context,
        BindingMode mode,
        bool hasInitializer,
        bool allowNameInference,
        bool skipBlockedBindingLookup = false)
    {
        environment.AssertOwnership(nameof(ApplyIdentifierBinding));
        context.AssertOwnership(nameof(ApplyIdentifierBinding));
        if (mode == BindingMode.Assign && environment.HasLexicalBinding(identifier.Name))
        {
            environment.AssertHasBinding(identifier.Name, nameof(ApplyIdentifierBinding));
        }
        if (allowNameInference && value is
            { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
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
                environment.AssignJsValue(identifier.Name, value);
                break;
            case BindingMode.DefineLet:
                environment.DefineJsValue(identifier.Name, value, isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                break;
            case BindingMode.DefineConst:
                environment.DefineJsValue(identifier.Name, value, true, blocksFunctionScopeOverride: true);
                break;
            case BindingMode.DefineVar:
                {
                    environment.AssertVarBindingScope(identifier.Name, nameof(ApplyIdentifierBinding));
                    if (!hasInitializer)
                    {
                        // Runtime evaluation of `var` without an initializer is a no-op;
                        // bindings are created during declaration instantiation. If the
                        // binding was removed (e.g., deletable eval var), do not recreate it.
                        // if (environment.HasFunctionScopedBinding(identifier.Name))
                        // {
                        //     return;
                        // }

                        return;
                    }

                    if (skipBlockedBindingLookup)
                    {
                        environment.EnsureFunctionScopedVarBinding(identifier.Name, context);
                        environment.GetVarEnvironment().DefineFunctionScoped(
                            identifier.Name,
                            value,
                            hasInitializer: true,
                            context: context);

                        break;
                    }

                    var assignedBlockedBinding = !skipBlockedBindingLookup && environment.TryAssignBlockedBinding(identifier.Name, value);

                    environment.EnsureFunctionScopedVarBinding(identifier.Name, context);

                    if (!assignedBlockedBinding)
                    {
                        environment.AssignJsValue(identifier.Name, value);
                    }

                    break;
                }
            case BindingMode.DefineParameter:
                // Parameters are created before defaults run (see the pre-pass in BindFunctionParameters),
                // so by the time we bind the value, the slot should already exist and still be
                // uninitialized. Assign into it to preserve the TDZ throw on reads during
                // initializer evaluation and fall back to Define only if the slot was not
                // created (defensive).
                if (environment.HasBinding(identifier.Name))
                {
                    environment.AssignJsValue(identifier.Name, value);
                }
                else
                {
                    environment.DefineJsValue(identifier.Name, value, isLexicalBinding: false);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
