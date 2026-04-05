#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Execution;

internal readonly record struct LoweredClassField(
    string DeclaredName,
    bool IsStatic,
    bool IsPrivate,
    bool IsComputed,
    bool AllowsAnonymousFunctionNameInference);

internal readonly record struct LoweredClassMember(
    ClassMemberKind Kind,
    string Name,
    LoweredClassCallable Callable,
    bool IsStatic,
    bool IsComputed,
    bool IsPrivate);

internal readonly record struct LoweredClassCallable(
    FunctionExpression Function,
    FunctionExecutionPlanSeed PlanSeed);

internal readonly record struct LoweredClassDefinition(
    SourceReference? Source,
    LoweredClassCallable Constructor,
    ImmutableArray<LoweredClassMember> Members,
    ImmutableArray<LoweredClassField> Fields,
    ImmutableArray<ClassStaticElement> StaticElements,
    ImmutableArray<ExecutionPlan> StaticBlockPlans);

/// <summary>
/// Caches lowered expression bytecode for class header evaluation.
/// This keeps extends and computed element names off runtime AST compilation.
/// </summary>
internal sealed class ClassDefinitionProgramCache
{
    private ClassDefinitionProgramCache(
        bool succeeded,
        string? failureReason,
        LoweredClassDefinition definition,
        ExpressionProgram? extendsProgram,
        ImmutableArray<ExpressionProgram?> memberNamePrograms,
        ImmutableArray<ExpressionProgram?> fieldNamePrograms,
        ImmutableArray<ExpressionProgram?> fieldInitializerPrograms)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        Definition = definition;
        ExtendsProgram = extendsProgram;
        MemberNamePrograms = memberNamePrograms;
        FieldNamePrograms = fieldNamePrograms;
        FieldInitializerPrograms = fieldInitializerPrograms;
    }

    public bool Succeeded { get; }

    public string? FailureReason { get; }

    public LoweredClassDefinition Definition { get; }

    public ExpressionProgram? ExtendsProgram { get; }

    public ImmutableArray<ExpressionProgram?> MemberNamePrograms { get; }

    public ImmutableArray<ExpressionProgram?> FieldNamePrograms { get; }

    public ImmutableArray<ExpressionProgram?> FieldInitializerPrograms { get; }

    public static ClassDefinitionProgramCache Build(ClassDefinition definition)
    {
        var staticBlockPlans = ImmutableArray.CreateBuilder<ExecutionPlan>(definition.StaticBlocks.Length);
        foreach (var block in definition.StaticBlocks)
        {
            var blockCache = ((IAstCacheable<StaticBlockPlanCache>)block).GetOrCreateCache();
            if (!blockCache.Succeeded || blockCache.Plan is null)
            {
                return Failure(
                    $"Class static block could not lower to an IR plan: {blockCache.FailureReason ?? "unknown failure"}");
            }

            staticBlockPlans.Add(blockCache.Plan);
        }

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

        var loweredMembers = ImmutableArray.CreateBuilder<LoweredClassMember>(definition.Members.Length);
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

            loweredMembers.Add(new LoweredClassMember(
                member.Kind,
                member.Name,
                LowerCallable(member.Function),
                member.IsStatic,
                member.IsComputed,
                member.IsPrivate));
            memberPrograms.Add(memberProgram);
        }

        var loweredFields = ImmutableArray.CreateBuilder<LoweredClassField>(definition.Fields.Length);
        var fieldPrograms = ImmutableArray.CreateBuilder<ExpressionProgram?>(definition.Fields.Length);
        var fieldInitializerPrograms = ImmutableArray.CreateBuilder<ExpressionProgram?>(definition.Fields.Length);
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

            ExpressionProgram? initializerProgram = null;
            if (field.Initializer is { } initializer)
            {
                if (!ExpressionProgramCompiler.TryCompile(
                        initializer,
                        out var compiledInitializerProgram,
                        out var initializerFailure))
                {
                    return Failure(
                        $"Class field initializer '{field.Name}' could not lower to expression bytecode: {initializerFailure ?? "unknown failure"}");
                }

                initializerProgram = compiledInitializerProgram;
            }

            fieldInitializerPrograms.Add(initializerProgram);
            loweredFields.Add(new LoweredClassField(
                field.Name,
                field.IsStatic,
                field.IsPrivate,
                field.IsComputed,
                IsAnonymousFunctionDefinition(field.Initializer)));
        }

        var loweredDefinition = new LoweredClassDefinition(
            definition.Source,
            LowerConstructor(definition),
            loweredMembers.ToImmutable(),
            loweredFields.ToImmutable(),
            definition.StaticElements,
            staticBlockPlans.ToImmutable());

        return new ClassDefinitionProgramCache(
            succeeded: true,
            failureReason: null,
            loweredDefinition,
            extendsProgram,
            memberPrograms.ToImmutable(),
            fieldPrograms.ToImmutable(),
            fieldInitializerPrograms.ToImmutable());
    }

    private static ClassDefinitionProgramCache Failure(string failureReason)
    {
        return new ClassDefinitionProgramCache(
            succeeded: false,
            failureReason,
            definition: default,
            extendsProgram: null,
            memberNamePrograms: ImmutableArray<ExpressionProgram?>.Empty,
            fieldNamePrograms: ImmutableArray<ExpressionProgram?>.Empty,
            fieldInitializerPrograms: ImmutableArray<ExpressionProgram?>.Empty);
    }

    private static bool IsAnonymousFunctionDefinition(ExpressionNode? initializer)
    {
        return initializer switch
        {
            SequenceExpression => false,
            FunctionExpression { Name: null } => true,
            ClassExpression { Name: null } => true,
            _ => false
        };
    }

    private static LoweredClassCallable LowerConstructor(ClassDefinition definition)
    {
        var constructor = definition.Constructor;
        if (definition.Extends is not null &&
            !constructor.IsDefaultDerivedConstructor &&
            constructor.IsImplicitDefaultDerivedConstructor())
        {
            constructor = constructor with { IsDefaultDerivedConstructor = true };
        }

        return LowerCallable(constructor);
    }

    private static LoweredClassCallable LowerCallable(FunctionExpression function)
    {
        var planResult = ExecutionPlanBuilder.Build(function, reportDiagnostics: false);
        return new LoweredClassCallable(
            function,
            FunctionExecutionPlanSeed.FromResult(planResult));
    }
}
