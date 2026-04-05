#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Caches lowered expression bytecode for class header evaluation.
/// This keeps extends and computed element names off runtime AST compilation.
/// </summary>
internal sealed class ClassDefinitionProgramCache
{
    private ClassDefinitionProgramCache(
        bool succeeded,
        string? failureReason,
        ExpressionProgram? extendsProgram,
        ImmutableArray<ExpressionProgram?> memberNamePrograms,
        ImmutableArray<ExpressionProgram?> fieldNamePrograms)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        ExtendsProgram = extendsProgram;
        MemberNamePrograms = memberNamePrograms;
        FieldNamePrograms = fieldNamePrograms;
    }

    public bool Succeeded { get; }

    public string? FailureReason { get; }

    public ExpressionProgram? ExtendsProgram { get; }

    public ImmutableArray<ExpressionProgram?> MemberNamePrograms { get; }

    public ImmutableArray<ExpressionProgram?> FieldNamePrograms { get; }

    public static ClassDefinitionProgramCache Build(ClassDefinition definition)
    {
        ExpressionProgram? extendsProgram = null;
        if (definition.Extends is { } extendsExpression)
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    extendsExpression,
                    out var compiledExtendsProgram,
                    out var extendsFailure))
            {
                return Failure(
                    $"Class extends expression could not lower to expression bytecode: {extendsFailure ?? "unknown failure"}");
            }

            extendsProgram = compiledExtendsProgram;
        }

        var memberPrograms = ImmutableArray.CreateBuilder<ExpressionProgram?>(definition.Members.Length);
        foreach (var member in definition.Members)
        {
            ExpressionProgram? memberProgram = null;
            if (member is { IsComputed: true, ComputedName: not null })
            {
                if (!ExpressionProgramCompiler.TryCompile(
                        member.ComputedName,
                        out var compiledMemberProgram,
                        out var memberFailure))
                {
                    return Failure(
                        $"Computed class member name '{member.Name}' could not lower to expression bytecode: {memberFailure ?? "unknown failure"}");
                }

                memberProgram = compiledMemberProgram;
            }

            memberPrograms.Add(memberProgram);
        }

        var fieldPrograms = ImmutableArray.CreateBuilder<ExpressionProgram?>(definition.Fields.Length);
        foreach (var field in definition.Fields)
        {
            ExpressionProgram? fieldProgram = null;
            if (field is { IsComputed: true, ComputedName: not null })
            {
                if (!ExpressionProgramCompiler.TryCompile(
                        field.ComputedName,
                        out var compiledFieldProgram,
                        out var fieldFailure))
                {
                    return Failure(
                        $"Computed class field name '{field.Name}' could not lower to expression bytecode: {fieldFailure ?? "unknown failure"}");
                }

                fieldProgram = compiledFieldProgram;
            }

            fieldPrograms.Add(fieldProgram);
        }

        return new ClassDefinitionProgramCache(
            succeeded: true,
            failureReason: null,
            extendsProgram,
            memberPrograms.ToImmutable(),
            fieldPrograms.ToImmutable());
    }

    private static ClassDefinitionProgramCache Failure(string failureReason)
    {
        return new ClassDefinitionProgramCache(
            succeeded: false,
            failureReason,
            extendsProgram: null,
            memberNamePrograms: ImmutableArray<ExpressionProgram?>.Empty,
            fieldNamePrograms: ImmutableArray<ExpressionProgram?>.Empty);
    }
}
