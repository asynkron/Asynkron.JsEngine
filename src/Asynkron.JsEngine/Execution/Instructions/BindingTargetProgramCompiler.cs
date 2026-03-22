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

            case AssignmentTargetBinding assignmentTarget:
                return TryCompileAssignmentTargetBinding(assignmentTarget, out program, out failureReason);

            default:
                failureReason = $"Binding target bytecode does not yet support '{target.GetType().Name}'.";
                program = default!;
                return false;
        }
    }

    private static bool TryCompileAssignmentTargetBinding(
        AssignmentTargetBinding binding,
        out BindingTargetProgram program,
        out string? failureReason)
    {
        switch (binding.Expression)
        {
            case IdentifierExpression identifier:
                program = new IdentifierBindingTargetProgram(identifier.Name);
                failureReason = null;
                return true;

            case MemberExpression { Target: SuperExpression, IsComputed: false } member:
                if (member.Property is not LiteralExpression { Value.IsString: true } propertyLiteral)
                {
                    failureReason =
                        "Binding target bytecode only supports literal property names for assignment-style dot targets.";
                    program = default!;
                    return false;
                }

                program = new NamedSuperPropertyAssignmentBindingTargetProgram(propertyLiteral.Value.AsString());
                failureReason = null;
                return true;

            case MemberExpression { Target: SuperExpression } member:
                if (!ExpressionProgramCompiler.TryCompile(member.Property, out var superPropertyProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                program = new ComputedSuperPropertyAssignmentBindingTargetProgram(
                    ExpressionProgramCompiler.PrependSuperReferenceCheck(superPropertyProgram));
                failureReason = null;
                return true;

            case MemberExpression { IsComputed: false } member:
                if (!ExpressionProgramCompiler.TryCompile(member.Target, out var targetProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                if (member.Property is not LiteralExpression { Value.IsString: true } namedPropertyLiteral)
                {
                    failureReason =
                        "Binding target bytecode only supports literal property names for assignment-style dot targets.";
                    program = default!;
                    return false;
                }

                program = new NamedPropertyAssignmentBindingTargetProgram(
                    targetProgram,
                    namedPropertyLiteral.Value.AsString());
                failureReason = null;
                return true;

            case MemberExpression member:
                if (!ExpressionProgramCompiler.TryCompile(member.Target, out var computedTargetProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                if (!ExpressionProgramCompiler.TryCompile(member.Property, out var propertyProgram, out failureReason))
                {
                    program = default!;
                    return false;
                }

                program = new ComputedPropertyAssignmentBindingTargetProgram(
                    computedTargetProgram,
                    propertyProgram);
                failureReason = null;
                return true;

            default:
                failureReason =
                    $"Binding target bytecode does not yet support assignment target '{binding.Expression.GetType().Name}'.";
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
