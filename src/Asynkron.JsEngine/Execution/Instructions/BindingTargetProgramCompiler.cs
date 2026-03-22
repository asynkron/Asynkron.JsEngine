using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution.Instructions;

internal static class BindingTargetProgramCompiler
{
    public static bool TryCompile(
        BindingTarget target,
        out BindingTargetProgram program,
        out string? failureReason)
    {
        switch (target)
        {
            case IdentifierBinding identifier:
                program = new IdentifierBindingTargetProgram(identifier.Name);
                failureReason = null;
                return true;

            case ArrayBinding arrayBinding:
                return TryCompileArrayBinding(arrayBinding, out program, out failureReason);

            case ObjectBinding objectBinding:
                return TryCompileObjectBinding(objectBinding, out program, out failureReason);

            case AssignmentTargetBinding:
                failureReason = "Binding target bytecode does not yet support assignment-style destructuring targets.";
                program = default!;
                return false;

            default:
                failureReason = $"Binding target bytecode does not yet support '{target.GetType().Name}'.";
                program = default!;
                return false;
        }
    }

    private static bool TryCompileArrayBinding(
        ArrayBinding binding,
        out BindingTargetProgram program,
        out string? failureReason)
    {
        var elements = ImmutableArray.CreateBuilder<ArrayBindingElementProgram>(binding.Elements.Length);
        foreach (var element in binding.Elements)
        {
            BindingTargetProgram? elementTarget = null;
            var compiledElementTarget = default(BindingTargetProgram)!;
            if (element.Target is not null &&
                !TryCompile(element.Target, out compiledElementTarget, out failureReason))
            {
                program = default!;
                return false;
            }

            if (element.Target is not null)
            {
                elementTarget = compiledElementTarget;
            }

            ExpressionProgram? defaultProgram = null;
            if (element.DefaultValue is not null)
            {
                if (!ExpressionProgramCompiler.TryCompile(element.DefaultValue, out var compiledDefaultProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                defaultProgram = compiledDefaultProgram;
            }

            elements.Add(new ArrayBindingElementProgram(
                elementTarget,
                defaultProgram,
                element.DefaultValue is not null && IsAnonymousFunctionDefinitionForNameInference(element.DefaultValue)));
        }

        BindingTargetProgram? restElement = null;
        var compiledRestElement = default(BindingTargetProgram)!;
        if (binding.RestElement is not null &&
            !TryCompile(binding.RestElement, out compiledRestElement, out failureReason))
        {
            program = default!;
            return false;
        }

        if (binding.RestElement is not null)
        {
            restElement = compiledRestElement;
        }

        program = new ArrayBindingTargetProgram(elements.ToImmutable(), restElement);
        failureReason = null;
        return true;
    }

    private static bool TryCompileObjectBinding(
        ObjectBinding binding,
        out BindingTargetProgram program,
        out string? failureReason)
    {
        var properties = ImmutableArray.CreateBuilder<ObjectBindingPropertyProgram>(binding.Properties.Length);
        foreach (var property in binding.Properties)
        {
            if (!TryCompile(property.Target, out var targetProgram, out failureReason))
            {
                program = default!;
                return false;
            }

            ExpressionProgram? defaultProgram = null;
            if (property.DefaultValue is not null)
            {
                if (!ExpressionProgramCompiler.TryCompile(property.DefaultValue, out var compiledDefaultProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                defaultProgram = compiledDefaultProgram;
            }

            ExpressionProgram? nameProgram = null;
            if (property.NameExpression is not null)
            {
                if (!ExpressionProgramCompiler.TryCompile(property.NameExpression, out var compiledNameProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                nameProgram = compiledNameProgram;
            }

            properties.Add(new ObjectBindingPropertyProgram(
                property.Name,
                targetProgram,
                defaultProgram,
                nameProgram,
                property.DefaultValue is not null && IsAnonymousFunctionDefinitionForNameInference(property.DefaultValue)));
        }

        BindingTargetProgram? restElement = null;
        var compiledRestElement = default(BindingTargetProgram)!;
        if (binding.RestElement is not null &&
            !TryCompile(binding.RestElement, out compiledRestElement, out failureReason))
        {
            program = default!;
            return false;
        }

        if (binding.RestElement is not null)
        {
            restElement = compiledRestElement;
        }

        program = new ObjectBindingTargetProgram(properties.ToImmutable(), restElement);
        failureReason = null;
        return true;
    }

    private static bool IsAnonymousFunctionDefinitionForNameInference(ExpressionNode expression)
    {
        if (expression is SequenceExpression)
        {
            return false;
        }

        return expression switch
        {
            FunctionExpression function => function.Name is null,
            ClassExpression classExpression => classExpression.Name is null,
            _ => false
        };
    }
}
