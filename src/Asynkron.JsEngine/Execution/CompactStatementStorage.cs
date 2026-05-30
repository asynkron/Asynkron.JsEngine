using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution;

internal enum CompactStatementPayloadGroup : byte
{
    ControlFlowNoPayload,
    CompletionControl,
    ExpressionProgram,
    AwaitExpressionWithState,
    CompletionValueWithOptionalAwait,
    AssignmentSlot,
    SimpleVariableDeclaration,
    BindingVariableDeclaration,
    EnvironmentTransitionNormalized,
    ResumeValueWithTarget,
    FunctionDeclarationDescriptor,
    ClassDeclarationDescriptor,
    DeferredAssignmentAndMutation,
    DeferredDeclarationAndScope,
    DeferredSuspendAndExceptionFlow,
    DeferredIteratorAndEnumeration,
    DeferredBranching,
    DeferredWithAndDestructuring
}

internal readonly record struct CompactStatementKindClassification(
    InstructionKind Kind,
    CompactStatementPayloadGroup PayloadGroup,
    bool IsSupported);

internal static class CompactStatementInstructionTaxonomy
{
    public static CompactStatementKindClassification Classify(InstructionKind kind)
    {
        return kind switch
        {
            InstructionKind.Jump => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.ControlFlowNoPayload, true),
            InstructionKind.Break => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.ControlFlowNoPayload, true),
            InstructionKind.Continue => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.ControlFlowNoPayload, true),
            InstructionKind.SetCompletionValue => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.CompletionControl, true),
            InstructionKind.BreakableExit => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.CompletionControl, true),
            InstructionKind.EvaluateAndDiscard => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.ExpressionProgram, true),
            InstructionKind.AwaitAndDiscard => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.AwaitExpressionWithState, true),
            InstructionKind.Throw => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.CompletionValueWithOptionalAwait, true),
            InstructionKind.Return => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.CompletionValueWithOptionalAwait, true),
            InstructionKind.AssignmentSlot => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.AssignmentSlot, true),
            InstructionKind.SimpleVariableDeclaration => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.SimpleVariableDeclaration, true),
            InstructionKind.BindingVariableDeclaration => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.BindingVariableDeclaration, true),
            InstructionKind.IncrementSlot => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredAssignmentAndMutation, false),
            InstructionKind.LogicalCompoundAssignmentSlot => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredAssignmentAndMutation, false),
            InstructionKind.CompoundAssignmentSlot => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredAssignmentAndMutation, false),
            InstructionKind.FunctionDeclaration => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.FunctionDeclarationDescriptor, true),
            InstructionKind.ClassDeclaration => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.ClassDeclarationDescriptor, true),
            InstructionKind.PushEnvironment => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.EnvironmentTransitionNormalized, true),
            InstructionKind.PopEnvironment => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.EnvironmentTransitionNormalized, true),
            InstructionKind.EnterTry => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.EnterCatch => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.LeaveTry => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.BreakableEnter => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.EndFinally => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.Yield => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.YieldStar => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredSuspendAndExceptionFlow, false),
            InstructionKind.StoreResumeValue => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.ResumeValueWithTarget, true),
            InstructionKind.IteratorInit => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredIteratorAndEnumeration, false),
            InstructionKind.IteratorMoveNext => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredIteratorAndEnumeration, false),
            InstructionKind.IteratorClose => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredIteratorAndEnumeration, false),
            InstructionKind.ForInInit => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredIteratorAndEnumeration, false),
            InstructionKind.ForInMoveNext => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredIteratorAndEnumeration, false),
            InstructionKind.Branch => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredBranching, false),
            InstructionKind.EnterWith => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.LeaveWith => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ArrayDestructuringInit => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ArrayDestructuringElement => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ArrayDestructuringRest => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ArrayDestructuringClose => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ObjectDestructuringInit => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ObjectDestructuringProperty => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ObjectDestructuringRest => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            InstructionKind.ObjectDestructuringClose => new CompactStatementKindClassification(kind, CompactStatementPayloadGroup.DeferredWithAndDestructuring, false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown InstructionKind")
        };
    }

    public static bool IsSupported(InstructionKind kind) => Classify(kind).IsSupported;

    public static ImmutableArray<CompactStatementKindClassification> ClassifyAllInstructionKinds()
    {
        var allKinds = Enum.GetValues<InstructionKind>();
        var classifications = ImmutableArray.CreateBuilder<CompactStatementKindClassification>(allKinds.Length);
        foreach (var kind in allKinds)
        {
            classifications.Add(Classify(kind));
        }

        return classifications.MoveToImmutable();
    }
}

internal readonly record struct CompactStatementStorageInstructionReferences(
    int PrimaryExpressionIndex,
    int SecondaryExpressionIndex,
    int PrimarySymbolIndex,
    int SecondarySymbolIndex,
    int BindingTargetIndex,
    int FunctionDeclarationDescriptorIndex,
    int ClassDeclarationDescriptorIndex,
    int PushEnvironmentPayloadIndex,
    int ScopeId,
    int FlatSlotId,
    bool HasAssignmentMetadata);

internal sealed record CompactStatementStorageReferenceTables(
    ImmutableArray<ExpressionProgram?> ExpressionPrograms,
    ImmutableArray<Symbol?> Symbols,
    ImmutableArray<BindingTargetProgram?> BindingTargets,
    ImmutableArray<FunctionDeclarationDescriptor?> FunctionDeclarationDescriptors,
    ImmutableArray<ClassDeclarationDescriptor?> ClassDeclarationDescriptors,
    ImmutableArray<CompactPushEnvironmentPayload?> PushEnvironmentPayloads);

internal sealed record CompactStatementStorageBoundary(
    CompactStatementStorage Storage,
    ImmutableArray<CompactStatementKindClassification> SupportedKindClassifications,
    ImmutableArray<CompactStatementKindClassification> DeferredKindClassifications);

internal enum CompactStatementBoundaryMode
{
    DiagnosticCoverage,
    PureControlFlow
}

internal sealed class CompactStatementStorage
{
    private readonly ImmutableArray<CompactStatementStorageInstructionReferences> _instructionReferences;

    private CompactStatementStorage(
        ImmutableArray<EncodedStatementOpcode> opcodeStream,
        ImmutableArray<int> nextOrTargetOperands,
        ImmutableArray<int> operandTable,
        ImmutableArray<int> extraOperandTable,
        CompactStatementStorageReferenceTables referenceTables,
        ImmutableArray<CompactStatementStorageInstructionReferences> instructionReferences,
        long estimatedCompactByteSize)
    {
        OpcodeStream = opcodeStream;
        NextOrTargetOperands = nextOrTargetOperands;
        OperandTable = operandTable;
        ExtraOperandTable = extraOperandTable;
        ReferenceTables = referenceTables;
        _instructionReferences = instructionReferences;
        EstimatedCompactByteSize = estimatedCompactByteSize;
    }

    public ImmutableArray<EncodedStatementOpcode> OpcodeStream { get; }

    public ImmutableArray<int> NextOrTargetOperands { get; }

    public ImmutableArray<int> OperandTable { get; }

    public ImmutableArray<int> ExtraOperandTable { get; }

    public CompactStatementStorageReferenceTables ReferenceTables { get; }

    public long EstimatedCompactByteSize { get; }

    public int InstructionCount => OpcodeStream.Length;

    public static CompactStatementStorageBoundary CreateBoundary(
        IReadOnlyList<ExecutionInstruction> instructions,
        CompactStatementBoundaryMode mode = CompactStatementBoundaryMode.DiagnosticCoverage)
    {
        var opcodeStream = ImmutableArray.CreateBuilder<EncodedStatementOpcode>();
        var nextOrTargetOperands = ImmutableArray.CreateBuilder<int>();
        var operandTable = ImmutableArray.CreateBuilder<int>();
        var extraOperandTable = ImmutableArray.CreateBuilder<int>();
        var expressionRefs = ImmutableArray.CreateBuilder<ExpressionProgram?>();
        var symbolRefs = ImmutableArray.CreateBuilder<Symbol?>();
        var bindingTargetRefs = ImmutableArray.CreateBuilder<BindingTargetProgram?>();
        var functionDeclarationDescriptorRefs = ImmutableArray.CreateBuilder<FunctionDeclarationDescriptor?>();
        var classDeclarationDescriptorRefs = ImmutableArray.CreateBuilder<ClassDeclarationDescriptor?>();
        var pushEnvironmentPayloadRefs = ImmutableArray.CreateBuilder<CompactPushEnvironmentPayload?>();
        var instructionReferences = ImmutableArray.CreateBuilder<CompactStatementStorageInstructionReferences>();
        var supportedClassifications = new Dictionary<InstructionKind, CompactStatementKindClassification>();
        var deferredClassifications = new Dictionary<InstructionKind, CompactStatementKindClassification>();
        long estimatedCompactByteSize = 0;

        foreach (var instruction in instructions)
        {
            var classification = CompactStatementInstructionTaxonomy.Classify(instruction.Kind);
            if (!IsClassificationIncluded(classification, mode))
            {
                deferredClassifications.TryAdd(classification.Kind, classification with { IsSupported = false });
                continue;
            }

            if (!classification.IsSupported)
            {
                deferredClassifications.TryAdd(classification.Kind, classification);
                continue;
            }

            if (!StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded))
            {
                deferredClassifications.TryAdd(classification.Kind, classification with { IsSupported = false });
                continue;
            }

            supportedClassifications.TryAdd(classification.Kind, classification);
            opcodeStream.Add(encoded.Header.Opcode);
            nextOrTargetOperands.Add(encoded.Header.NextOrTarget);
            operandTable.Add(encoded.Header.Operand);
            extraOperandTable.Add(encoded.Header.Extra);
            instructionReferences.Add(new CompactStatementStorageInstructionReferences(
                PrimaryExpressionIndex: AddReference(expressionRefs, encoded.Payload.PrimaryExpressionProgram),
                SecondaryExpressionIndex: AddReference(expressionRefs, encoded.Payload.SecondaryExpressionProgram),
                PrimarySymbolIndex: AddReference(symbolRefs, encoded.Payload.PrimarySymbol),
                SecondarySymbolIndex: AddReference(symbolRefs, encoded.Payload.SecondarySymbol),
                BindingTargetIndex: AddReference(bindingTargetRefs, encoded.Payload.BindingTargetProgram),
                FunctionDeclarationDescriptorIndex: AddReference(functionDeclarationDescriptorRefs, encoded.Payload.FunctionDeclarationDescriptor),
                ClassDeclarationDescriptorIndex: AddReference(classDeclarationDescriptorRefs, encoded.Payload.ClassDeclarationDescriptor),
                PushEnvironmentPayloadIndex: AddReference(pushEnvironmentPayloadRefs, encoded.Payload.PushEnvironmentPayload),
                ScopeId: encoded.Payload.ScopeId,
                FlatSlotId: encoded.Payload.FlatSlotId,
                HasAssignmentMetadata: encoded.Payload.HasAssignmentMetadata));
            estimatedCompactByteSize += encoded.EstimatedCompactByteSize;
        }

        var storage = new CompactStatementStorage(
            opcodeStream.ToImmutable(),
            nextOrTargetOperands.ToImmutable(),
            operandTable.ToImmutable(),
            extraOperandTable.ToImmutable(),
            new CompactStatementStorageReferenceTables(
                expressionRefs.ToImmutable(),
                symbolRefs.ToImmutable(),
                bindingTargetRefs.ToImmutable(),
                functionDeclarationDescriptorRefs.ToImmutable(),
                classDeclarationDescriptorRefs.ToImmutable(),
                pushEnvironmentPayloadRefs.ToImmutable()),
            instructionReferences.ToImmutable(),
            estimatedCompactByteSize);

        return new CompactStatementStorageBoundary(
            storage,
            supportedClassifications.Values.OrderBy(static classification => classification.Kind).ToImmutableArray(),
            deferredClassifications.Values.OrderBy(static classification => classification.Kind).ToImmutableArray());
    }

    private static bool IsClassificationIncluded(
        CompactStatementKindClassification classification,
        CompactStatementBoundaryMode mode)
    {
        return mode switch
        {
            CompactStatementBoundaryMode.DiagnosticCoverage => true,
            CompactStatementBoundaryMode.PureControlFlow => classification.PayloadGroup is
                CompactStatementPayloadGroup.ControlFlowNoPayload or
                CompactStatementPayloadGroup.CompletionControl,
            _ => true
        };
    }

    public static bool TryEncodeSupportedInstruction(ExecutionInstruction instruction, out CompactStatementInstruction encoded)
    {
        encoded = default;
        if (!CompactStatementInstructionTaxonomy.IsSupported(instruction.Kind))
        {
            return false;
        }

        return StatementInstructionDiagnosticsCodec.TryEncode(instruction, out encoded);
    }

    public ImmutableArray<ExecutionInstruction> DecodeSemanticView()
    {
        var decoded = ImmutableArray.CreateBuilder<ExecutionInstruction>(InstructionCount);
        for (var i = 0; i < InstructionCount; i++)
        {
            decoded.Add(StatementInstructionDiagnosticsCodec.Decode(ToEncodedInstruction(i), ReferenceTables.ExpressionPrograms));
        }

        return decoded.MoveToImmutable();
    }

    private static int AddReference<T>(ImmutableArray<T>.Builder builder, T value)
    {
        if (value is null)
        {
            return -1;
        }

        var existing = builder.IndexOf(value);
        if (existing >= 0)
        {
            return existing;
        }

        builder.Add(value);
        return builder.Count - 1;
    }

    private static T? GetReference<T>(ImmutableArray<T> references, int index)
    {
        if (index < 0)
        {
            return default;
        }

        return references[index];
    }

    private CompactStatementInstruction ToEncodedInstruction(int index)
    {
        var references = _instructionReferences[index];
        return new CompactStatementInstruction(
            Header: new CompactStatementHeader(
                OpcodeStream[index],
                NextOrTargetOperands[index],
                OperandTable[index],
                ExtraOperandTable[index]),
            Payload: new CompactStatementPayload(
                PrimaryExpressionProgramReferenceId: references.PrimaryExpressionIndex,
                SecondaryExpressionProgramReferenceId: references.SecondaryExpressionIndex,
                BindingTargetProgramReferenceId: references.BindingTargetIndex,
                PrimarySymbol: GetReference(ReferenceTables.Symbols, references.PrimarySymbolIndex),
                SecondarySymbol: GetReference(ReferenceTables.Symbols, references.SecondarySymbolIndex),
                BindingTargetProgram: GetReference(ReferenceTables.BindingTargets, references.BindingTargetIndex),
                FunctionDeclarationDescriptorReferenceId: references.FunctionDeclarationDescriptorIndex,
                ClassDeclarationDescriptorReferenceId: references.ClassDeclarationDescriptorIndex,
                FunctionDeclarationDescriptor: GetReference(ReferenceTables.FunctionDeclarationDescriptors, references.FunctionDeclarationDescriptorIndex),
                ClassDeclarationDescriptor: GetReference(ReferenceTables.ClassDeclarationDescriptors, references.ClassDeclarationDescriptorIndex),
                ScopeId: references.ScopeId,
                FlatSlotId: references.FlatSlotId,
                HasAssignmentMetadata: references.HasAssignmentMetadata,
                PushEnvironmentPayload: GetReference(ReferenceTables.PushEnvironmentPayloads, references.PushEnvironmentPayloadIndex)));
    }
}
