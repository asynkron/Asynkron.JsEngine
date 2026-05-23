using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
public sealed class StatementInstructionStorageDiagnosticsTests : IAsyncLifetime
{
    private JsEngine _engine = null!;

    public Task InitializeAsync()
    {
        _engine = new JsEngine();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _engine.DisposeAsync();
    }

    [Fact]
    public async Task Collect_ForRepresentativeLoweredProgram_ReportsHistogramAndCompactEstimate()
    {
        var parsedProgram = _engine.ParseProgram("""
            function walk(limit) {
                let total = 0;
                for (let i = 0; i < limit; i++) {
                    if (i > 5) break;
                    total = total + i;
                }
                return total;
            }

            walk(8);
            """);
        await _engine.Evaluate(parsedProgram);

        var snapshot = StatementInstructionStorageDiagnostics.Collect(parsedProgram);

        Assert.True(snapshot.PlanCount > 0);
        Assert.True(snapshot.InstructionCount > 0);
        Assert.True(snapshot.SupportedInstructionCount > 0);
        Assert.True(snapshot.UnsupportedInstructionCount > 0);
        Assert.True(snapshot.OwnerBackedEncodedBytes > 0);
        Assert.True(snapshot.OperandTableEntryCount > 0);
        Assert.True(snapshot.ExtraOperandTableEntryCount >= 0);
        Assert.True(snapshot.ExpressionReferenceCount >= 0);
        Assert.True(snapshot.SecondaryExpressionReferenceCount >= 0);
        Assert.True(snapshot.SymbolOperandCount >= 0);
        Assert.True(snapshot.BindingTargetOperandCount >= 0);
        Assert.True(snapshot.ExpressionProgramReferenceTableCount >= 0);
        Assert.True(snapshot.BindingTargetProgramReferenceTableCount >= 0);
        Assert.True(snapshot.EstimatedCompactEncodedBytes > 0);
        Assert.NotEmpty(snapshot.InstructionKindHistogram);
        Assert.NotEmpty(snapshot.SupportedInstructionKindHistogram);
        Assert.NotEmpty(snapshot.UnsupportedInstructionKindHistogram);
        Assert.NotEmpty(snapshot.UnsupportedFamilyReasonHistogram);
        Assert.Contains(
            snapshot.SupportedInstructionKindHistogram,
            entry => entry.Key is InstructionKind.SetCompletionValue or InstructionKind.Break or InstructionKind.BreakableExit);
        Assert.Contains(snapshot.UnsupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.PushEnvironment);
        Assert.Contains(snapshot.UnsupportedFamilyReasonHistogram, entry => entry.Key == "declaration-and-scope");
    }

    [Fact]
    public void Collect_ForManuallyConstructedSimpleFamilyPlan_MatchesExpectedEncodedByteEstimate()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new SetCompletionValueInstruction(2),
            new BreakInstruction(9, 2),
            new ContinueInstruction(12, 2),
            new PopEnvironmentInstruction(ScopeId: 5, AllowPooling: true, Next: 3),
            new LeaveTryInstruction(6),
            new EndFinallyInstruction(7)
        };

        var plan = new ExecutionPlan(instructions.ToImmutableArray(), EntryPoint: 0);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.Equal(1, snapshot.PlanCount);
        Assert.Equal(instructions.Length, snapshot.InstructionCount);
        Assert.Equal(4, snapshot.SupportedInstructionCount);
        Assert.Equal(3, snapshot.UnsupportedInstructionCount);
        Assert.Equal(64, snapshot.OwnerBackedEncodedBytes);
        Assert.Equal(4, snapshot.OperandTableEntryCount);
        Assert.Equal(0, snapshot.ExtraOperandTableEntryCount);
        Assert.Equal(0, snapshot.ExpressionReferenceCount);
        Assert.Equal(0, snapshot.SecondaryExpressionReferenceCount);
        Assert.Equal(0, snapshot.SymbolOperandCount);
        Assert.Equal(0, snapshot.BindingTargetOperandCount);
        Assert.Equal(0, snapshot.ExpressionProgramReferenceTableCount);
        Assert.Equal(0, snapshot.BindingTargetProgramReferenceTableCount);
        Assert.Equal(64, snapshot.EstimatedCompactEncodedBytes);
        Assert.Contains(snapshot.UnsupportedFamilyReasonHistogram, entry => entry.Key == "declaration-and-scope" && entry.Value == 1);
        Assert.Contains(snapshot.UnsupportedFamilyReasonHistogram, entry => entry.Key == "suspend-and-exception-flow" && entry.Value == 2);
    }

    [Fact]
    public void Collect_ForPlanWithPureControlFlowSidecar_UsesFullDiagnosticCoverageBoundary()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new ReturnInstruction(7, ReturnProgram: ExpressionProgram.Empty)
        };

        var pureControlFlowSidecar = CompactStatementStorage.CreateBoundary(instructions, CompactStatementBoundaryMode.PureControlFlow);
        var diagnosticCoverageBoundary = CompactStatementStorage.CreateBoundary(instructions, CompactStatementBoundaryMode.DiagnosticCoverage);
        var plan = new ExecutionPlan(
            instructions.ToImmutableArray(),
            EntryPoint: 0,
            CompactStatementStorageBoundary: pureControlFlowSidecar);

        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.Equal(2, snapshot.InstructionCount);
        Assert.Equal(2, snapshot.SupportedInstructionCount);
        Assert.Equal(0, snapshot.UnsupportedInstructionCount);
        Assert.Equal(32, snapshot.OwnerBackedEncodedBytes);
        Assert.Equal(diagnosticCoverageBoundary.Storage.EstimatedCompactByteSize, snapshot.EstimatedCompactEncodedBytes);
        Assert.Contains(snapshot.SupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.Return);
    }

    [Fact]
    public async Task Collect_PreservesExecutionPlanInstructionRuntimeShape()
    {
        var parsedProgram = _engine.ParseProgram("""
            function sample(value) {
                return value + 1;
            }
            """);
        await _engine.Evaluate(parsedProgram);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(parsedProgram.Body));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        var plan = Assert.IsType<ExecutionPlan>(cache.Plan);
        var baselineInstructionCount = plan.Instructions.Length;

        _ = StatementInstructionStorageDiagnostics.Collect(parsedProgram);

        var planAfter = Assert.IsType<ExecutionPlan>(cache.Plan);
        Assert.Same(plan, planAfter);
        Assert.Equal(baselineInstructionCount, planAfter.Instructions.Length);
    }

    [Fact]
    public async Task Build_PopulatesPureControlFlowCompactSidecar_WithoutMutatingInstructionRuntimeShape()
    {
        var parsedProgram = _engine.ParseProgram("""
            function sample(limit) {
                let total = 0;
                if (limit > 1) {
                    total = total + limit;
                } else {
                    total = total + 1;
                }
                return total;
            }
            """);
        await _engine.Evaluate(parsedProgram);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(parsedProgram.Body));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        var plan = Assert.IsType<ExecutionPlan>(cache.Plan);
        var baselineInstructions = plan.Instructions;
        Assert.NotNull(plan.CompactStatementStorageBoundary);
        var sidecar = plan.CompactStatementStorageBoundary!;
        var decodedSidecar = sidecar.Storage.DecodeSemanticView();
        var expectedPureControlFlowInstructions = baselineInstructions
            .Where(static instruction =>
            {
                var classification = CompactStatementInstructionTaxonomy.Classify(instruction.Kind);
                return classification.PayloadGroup is CompactStatementPayloadGroup.ControlFlowNoPayload or
                    CompactStatementPayloadGroup.CompletionControl;
            })
            .ToImmutableArray();

        Assert.All(
            sidecar.SupportedKindClassifications,
            entry => Assert.True(
                entry.PayloadGroup is CompactStatementPayloadGroup.ControlFlowNoPayload or
                    CompactStatementPayloadGroup.CompletionControl));
        Assert.DoesNotContain(
            sidecar.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.Return);
        Assert.Contains(
            sidecar.DeferredKindClassifications,
            entry => entry.Kind == InstructionKind.Return &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.CompletionValueWithOptionalAwait &&
                     !entry.IsSupported);
        Assert.Equal(expectedPureControlFlowInstructions.Length, decodedSidecar.Length);
        for (var i = 0; i < decodedSidecar.Length; i++)
        {
            Assert.True(
                StatementInstructionDiagnosticsCodec.TryEncode(expectedPureControlFlowInstructions[i], out var expectedEncoded),
                $"Failed to encode expected instruction at index {i}: {expectedPureControlFlowInstructions[i].Kind}");
            Assert.True(
                StatementInstructionDiagnosticsCodec.TryEncode(decodedSidecar[i], out var decodedEncoded),
                $"Failed to encode decoded instruction at index {i}: {decodedSidecar[i].Kind}");
            Assert.Equal(expectedEncoded, decodedEncoded);
        }
        Assert.Equal(baselineInstructions, plan.Instructions);
    }

    [Fact]
    public async Task Collect_FromFunctionPlan_TraversesNestedDeclarationPlans()
    {
        var parsedProgram = _engine.ParseProgram("""
            function outer() {
                function inner() {
                    return 1;
                }

                class Local {
                    method() {
                        return inner();
                    }
                }

                return inner();
            }
            """);
        await _engine.Evaluate(parsedProgram);

        var outerDeclaration = Assert.IsType<FunctionDeclaration>(Assert.Single(parsedProgram.Body));
        var outerCache = ((IAstCacheable<ExecutionPlanCache>)outerDeclaration.Function).GetOrCreateCache();
        var outerPlan = Assert.IsType<ExecutionPlan>(outerCache.Plan);

        var fromPlan = StatementInstructionStorageDiagnostics.Collect(outerPlan);

        Assert.True(fromPlan.PlanCount >= 3);
        Assert.Contains(fromPlan.InstructionKindHistogram, entry => entry.Key == InstructionKind.FunctionDeclaration);
        Assert.Contains(fromPlan.InstructionKindHistogram, entry => entry.Key == InstructionKind.ClassDeclaration);
        Assert.Contains(fromPlan.InstructionKindHistogram, entry => entry.Key == InstructionKind.Return);
        Assert.Contains(fromPlan.SupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.FunctionDeclaration);
        Assert.Contains(fromPlan.SupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.ClassDeclaration);
        Assert.DoesNotContain(fromPlan.UnsupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.FunctionDeclaration);
        Assert.DoesNotContain(fromPlan.UnsupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.ClassDeclaration);
    }

    [Fact]
    public void Collect_ReusedExpressionPrograms_UsesStableReferenceTableIds()
    {
        var sharedProgram = ExpressionProgram.Empty;
        var instructions = new ExecutionInstruction[]
        {
            new EvaluateAndDiscardInstruction(1, sharedProgram, SuppressCompletionValue: false),
            new AwaitAndDiscardInstruction(2, Symbol.Intern("await"), sharedProgram, SuppressCompletionValue: false),
            new ReturnInstruction(3, sharedProgram, Symbol.Intern("await"), sharedProgram)
        };

        var plan = new ExecutionPlan(instructions.ToImmutableArray(), EntryPoint: 0);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.Equal(1, snapshot.ExpressionProgramReferenceTableCount);
        Assert.True(snapshot.ExpressionReferenceCount >= 3);
        Assert.True(snapshot.SecondaryExpressionReferenceCount >= 1);
    }

    [Fact]
    public void CompactStatementStorageBoundary_ReturnThrow_DirectAwaitAndLoweredPayloadsStayDistinct()
    {
        var returnLowered = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(1)));
        var returnAwaited = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(2)));
        var throwLowered = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(3)));
        var throwAwaited = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(4)));
        var returnAwaitState = Symbol.Intern("returnAwaitState");
        var throwAwaitState = Symbol.Intern("throwAwaitState");

        var instructions = new ExecutionInstruction[]
        {
            new ReturnInstruction(1, ReturnProgram: null, returnAwaitState, returnAwaited),
            new ReturnInstruction(2, ReturnProgram: returnLowered, AwaitStateKey: null, AwaitedProgram: null),
            new ThrowInstruction(ThrowProgram: null, throwAwaitState, throwAwaited),
            new ThrowInstruction(ThrowProgram: throwLowered, AwaitStateKey: null, AwaitedProgram: null)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);
        var decoded = boundary.Storage.DecodeSemanticView();

        var directReturn = Assert.IsType<ReturnInstruction>(decoded[0]);
        Assert.Null(directReturn.ReturnProgram);
        Assert.Equal(returnAwaitState, directReturn.AwaitStateKey);
        Assert.Equal(returnAwaited, directReturn.AwaitedProgram);

        var loweredReturn = Assert.IsType<ReturnInstruction>(decoded[1]);
        Assert.Equal(returnLowered, loweredReturn.ReturnProgram);
        Assert.Null(loweredReturn.AwaitStateKey);
        Assert.Null(loweredReturn.AwaitedProgram);

        var directThrow = Assert.IsType<ThrowInstruction>(decoded[2]);
        Assert.Null(directThrow.ThrowProgram);
        Assert.Equal(throwAwaitState, directThrow.AwaitStateKey);
        Assert.Equal(throwAwaited, directThrow.AwaitedProgram);

        var loweredThrow = Assert.IsType<ThrowInstruction>(decoded[3]);
        Assert.Equal(throwLowered, loweredThrow.ThrowProgram);
        Assert.Null(loweredThrow.AwaitStateKey);
        Assert.Null(loweredThrow.AwaitedProgram);
    }

    [Fact]
    public void Collect_ForReturnThrowDirectAwaitAndLoweredShapes_CountsPrimaryAndSecondaryReferences()
    {
        var returnLowered = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(10)));
        var returnAwaited = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(20)));
        var throwLowered = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(30)));
        var throwAwaited = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(40)));

        var plan = new ExecutionPlan(
            ImmutableArray.Create<ExecutionInstruction>(
                new ReturnInstruction(1, ReturnProgram: null, Symbol.Intern("returnAwait"), returnAwaited),
                new ReturnInstruction(2, ReturnProgram: returnLowered, AwaitStateKey: null, AwaitedProgram: null),
                new ThrowInstruction(ThrowProgram: null, Symbol.Intern("throwAwait"), throwAwaited),
                new ThrowInstruction(ThrowProgram: throwLowered, AwaitStateKey: null, AwaitedProgram: null)),
            EntryPoint: 0);

        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.Equal(4, snapshot.InstructionCount);
        Assert.Equal(4, snapshot.SupportedInstructionCount);
        Assert.Equal(0, snapshot.UnsupportedInstructionCount);
        Assert.Equal(2, snapshot.ExpressionReferenceCount);
        Assert.Equal(2, snapshot.SecondaryExpressionReferenceCount);
        Assert.Equal(2, snapshot.SymbolOperandCount);
        Assert.Equal(4, snapshot.ExpressionProgramReferenceTableCount);
    }

    [Fact]
    public void BindingVariableDeclaration_DiagnosticsEncoding_UsesBindingTargetReferenceTableAndRoundTripsNestedShape()
    {
        var bindingTarget = new ObjectBindingTargetProgram(
            ImmutableArray.Create(
                new ObjectBindingPropertyProgram(
                    Name: "plain",
                    Target: new IdentifierBindingTargetProgram(Symbol.Intern("plain")),
                    DefaultProgram: ExpressionProgram.Empty),
                new ObjectBindingPropertyProgram(
                    Name: "computed",
                    Target: new IdentifierBindingTargetProgram(Symbol.Intern("value")),
                    DefaultProgram: ExpressionProgram.Empty,
                    NameProgram: ExpressionProgram.Empty)),
            RestElement: new IdentifierBindingTargetProgram(Symbol.Intern("rest")));

        var instruction = new BindingVariableDeclarationInstruction(
            Next: 4,
            VarKind: VariableKind.Let,
            TargetProgram: bindingTarget,
            InitializerProgram: ExpressionProgram.Empty,
            AwaitStateKey: Symbol.Intern("await_state"),
            AwaitedProgram: ExpressionProgram.Empty);

        var expressionPrograms = new StatementDiagnosticsExpressionProgramTable();
        var bindingTargets = new StatementDiagnosticsBindingTargetProgramTable();
        Assert.True(
            StatementInstructionDiagnosticsCodec.TryEncode(
                instruction,
                expressionPrograms,
                bindingTargets,
                new StatementDiagnosticsFunctionDeclarationDescriptorTable(),
                new StatementDiagnosticsClassDeclarationDescriptorTable(),
                out var encoded));

        Assert.True(encoded.Payload.BindingTargetProgramReferenceId >= 0);
        Assert.Equal(1, bindingTargets.Count);

        var decoded = Assert.IsType<BindingVariableDeclarationInstruction>(
            StatementInstructionDiagnosticsCodec.Decode(
                encoded,
                expressionPrograms,
                bindingTargets,
                new StatementDiagnosticsFunctionDeclarationDescriptorTable(),
                new StatementDiagnosticsClassDeclarationDescriptorTable()));
        Assert.Equal(instruction.TargetProgram, decoded.TargetProgram);

        var plan = new ExecutionPlan(ImmutableArray.Create<ExecutionInstruction>(instruction), EntryPoint: 0);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);
        Assert.Equal(1, snapshot.BindingTargetOperandCount);
        Assert.Equal(1, snapshot.BindingTargetProgramReferenceTableCount);
    }

    [Fact]
    public void FunctionDeclaration_DiagnosticsEncoding_AllowsNullDescriptorHoistedNoOpShape()
    {
        var instruction = new FunctionDeclarationInstruction(Next: 3, Descriptor: null);
        Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded));

        Assert.Equal(EncodedStatementOpcode.FunctionDeclaration, encoded.Header.Opcode);
        Assert.Equal(-1, encoded.Payload.FunctionDeclarationDescriptorReferenceId);
        Assert.Null(encoded.Payload.FunctionDeclarationDescriptor);

        var decoded = Assert.IsType<FunctionDeclarationInstruction>(StatementInstructionDiagnosticsCodec.Decode(encoded));
        Assert.Equal(instruction.Next, decoded.Next);
        Assert.Null(decoded.Descriptor);
    }

    [Fact]
    public void CompactStatementStorageBoundary_ForMixedInstructionKinds_SeparatesSupportedAndDeferredFamilies()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new ReturnInstruction(8, ReturnProgram: ExpressionProgram.Empty),
            new PopEnvironmentInstruction(ScopeId: 2, AllowPooling: false, Next: 9),
            new BranchInstruction(ConditionProgram: ExpressionProgram.Empty, ConsequentIndex: 3, AlternateIndex: 5)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);

        Assert.Equal(2, boundary.Storage.InstructionCount);
        Assert.Equal(2, boundary.Storage.DecodeSemanticView().Length);
        Assert.Contains(
            boundary.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.Jump &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.ControlFlowNoPayload &&
                     entry.IsSupported);
        Assert.Contains(
            boundary.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.Return &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.CompletionValueWithOptionalAwait &&
                     entry.IsSupported);
        Assert.Contains(
            boundary.DeferredKindClassifications,
            entry => entry.Kind == InstructionKind.PopEnvironment &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.DeferredDeclarationAndScope &&
                     !entry.IsSupported);
        Assert.Contains(
            boundary.DeferredKindClassifications,
            entry => entry.Kind == InstructionKind.Branch &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.DeferredBranching &&
                     !entry.IsSupported);
    }

    [Fact]
    public void CompactStatementStorageBoundary_DecodeSemanticView_ForPureControlFlow_PreservesFormatting()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new BreakInstruction(8, 2),
            new ContinueInstruction(10, 2)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);
        var decoded = boundary.Storage.DecodeSemanticView();

        Assert.Equal(instructions.Length, decoded.Length);
        for (var i = 0; i < instructions.Length; i++)
        {
            Assert.Equal(
                ExecutionPlanDiagnostics.FormatInstruction(instructions[i]),
                ExecutionPlanDiagnostics.FormatInstruction(decoded[i]));
        }
    }

    [Fact]
    public void CompactStatementStorageBoundary_StoreResumeValue_UsesSymbolReferenceAndRoundTripsSemanticView()
    {
        var instruction = new StoreResumeValueInstruction(Next: 11, TargetSymbol: Symbol.Intern("resume_target"));
        var boundary = CompactStatementStorage.CreateBoundary(new ExecutionInstruction[] { instruction });
        var decoded = boundary.Storage.DecodeSemanticView();

        Assert.Single(decoded);
        Assert.Equal(instruction, Assert.IsType<StoreResumeValueInstruction>(decoded[0]));
        Assert.Contains(
            boundary.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.StoreResumeValue &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.ResumeValueWithTarget &&
                     entry.IsSupported);
        Assert.Equal(1, boundary.Storage.ReferenceTables.Symbols.Length);
        Assert.Equal(Symbol.Intern("resume_target"), boundary.Storage.ReferenceTables.Symbols[0]);

        var snapshot = StatementInstructionStorageDiagnostics.Collect(
            new ExecutionPlan(ImmutableArray.Create<ExecutionInstruction>(instruction), EntryPoint: 0));
        Assert.Equal(1, snapshot.SupportedInstructionCount);
        Assert.Equal(0, snapshot.UnsupportedInstructionCount);
        Assert.Equal(1, snapshot.SymbolOperandCount);
    }

    [Fact]
    public void CompactStatementStorageBoundary_StoresExpressionProgramsAsReferencesOutsideOpcodeStream()
    {
        var expressionProgram = ExpressionProgram.Empty;
        var instructions = new ExecutionInstruction[]
        {
            new EvaluateAndDiscardInstruction(1, expressionProgram),
            new ReturnInstruction(2, ReturnProgram: expressionProgram)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);
        var storage = boundary.Storage;

        Assert.Equal(2, storage.InstructionCount);
        Assert.Equal(2, storage.OpcodeStream.Length);
        Assert.Equal(1, storage.ReferenceTables.ExpressionPrograms.Length);
        Assert.All(storage.ReferenceTables.ExpressionPrograms, program => Assert.Equal(expressionProgram, program));
    }

    [Fact]
    public void CompactStatementStorageBoundary_AssignmentSlotAndSimpleVariableDeclaration_PreserveSemanticOperandsAndReferencePayloads()
    {
        var assignmentValueProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadThis));
        var assignmentAwaitedProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadNewTarget));
        var declarationInitializerProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadImportMeta));
        var declarationAwaitedProgram = new ExpressionProgram(
            ImmutableArray.Create(PackedExpressionOp.LoadResolvedIdentifierValue));
        var assignmentTarget = Symbol.Intern("target");
        var assignmentAwaitState = Symbol.Intern("assignment-await");
        var declarationTarget = Symbol.Intern("decl");
        var declarationAwaitState = Symbol.Intern("declaration-await");
        var instructions = new ExecutionInstruction[]
        {
            new AssignmentSlotInstruction(
                Next: 5,
                TargetSymbol: assignmentTarget,
                ValueProgram: assignmentValueProgram,
                AwaitStateKey: assignmentAwaitState,
                AwaitedProgram: assignmentAwaitedProgram,
                SuppressCompletionValue: true,
                AllowNameInference: false,
                ScopeId: 17,
                SlotIndex: 9,
                FlatSlotId: 31),
            new SimpleVariableDeclarationInstruction(
                Next: 7,
                VarKind: VariableKind.Const,
                TargetSymbol: declarationTarget,
                InitializerProgram: declarationInitializerProgram,
                AwaitStateKey: declarationAwaitState,
                AwaitedProgram: declarationAwaitedProgram,
                AllowNameInference: true,
                IsScriptLevel: true)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);
        var storage = boundary.Storage;
        var decoded = storage.DecodeSemanticView();

        Assert.Equal(2, storage.InstructionCount);
        Assert.Equal(4, storage.ReferenceTables.ExpressionPrograms.Length);
        Assert.Equal(4, storage.ReferenceTables.Symbols.Length);
        Assert.Equal(assignmentValueProgram, storage.ReferenceTables.ExpressionPrograms[0]);
        Assert.Equal(assignmentAwaitedProgram, storage.ReferenceTables.ExpressionPrograms[1]);
        Assert.Equal(declarationInitializerProgram, storage.ReferenceTables.ExpressionPrograms[2]);
        Assert.Equal(declarationAwaitedProgram, storage.ReferenceTables.ExpressionPrograms[3]);
        Assert.Equal(EncodedStatementOpcode.AssignmentSlot, storage.OpcodeStream[0]);
        Assert.Equal(EncodedStatementOpcode.SimpleVariableDeclaration, storage.OpcodeStream[1]);
        Assert.Equal(9, storage.ExtraOperandTable[0]);
        Assert.Equal((int)VariableKind.Const, storage.OperandTable[1]);
        Assert.Contains(boundary.SupportedKindClassifications, entry => entry.Kind == InstructionKind.AssignmentSlot);
        Assert.Contains(boundary.SupportedKindClassifications, entry => entry.Kind == InstructionKind.SimpleVariableDeclaration);

        var decodedAssignment = Assert.IsType<AssignmentSlotInstruction>(decoded[0]);
        Assert.Equal(assignmentTarget, decodedAssignment.TargetSymbol);
        Assert.Equal(assignmentAwaitState, decodedAssignment.AwaitStateKey);
        Assert.Equal(assignmentValueProgram, decodedAssignment.ValueProgram);
        Assert.Equal(assignmentAwaitedProgram, decodedAssignment.AwaitedProgram);
        Assert.True(decodedAssignment.SuppressCompletionValue);
        Assert.False(decodedAssignment.AllowNameInference);
        Assert.Equal(17, decodedAssignment.ScopeId);
        Assert.Equal(9, decodedAssignment.SlotIndex);
        Assert.Equal(31, decodedAssignment.FlatSlotId);

        var decodedDeclaration = Assert.IsType<SimpleVariableDeclarationInstruction>(decoded[1]);
        Assert.Equal(VariableKind.Const, decodedDeclaration.VarKind);
        Assert.Equal(declarationTarget, decodedDeclaration.TargetSymbol);
        Assert.Equal(declarationAwaitState, decodedDeclaration.AwaitStateKey);
        Assert.Equal(declarationInitializerProgram, decodedDeclaration.InitializerProgram);
        Assert.Equal(declarationAwaitedProgram, decodedDeclaration.AwaitedProgram);
        Assert.True(decodedDeclaration.AllowNameInference);
        Assert.True(decodedDeclaration.IsScriptLevel);
    }

    [Fact]
    public async Task CompactStatementStorageBoundary_FromScriptPlan_PreservesControlFlowSemanticParity()
    {
        var scriptCases = new[]
        {
            """
            for (let i = 0; i < 3; i++) {
                if (i > 1) {
                    break;
                }
            }
            """,
            """
            let total = 0;
            for (let i = 0; i < 4; i++) {
                if ((i % 2) === 0) {
                    continue;
                }

                total = total + i;
            }
            """,
            """
            outer: for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 3; j++) {
                    break outer;
                }
            }
            """,
            """
            let value = 0;
            while (value < 3) {
                value = value + 1;
            }
            value;
            """,
            """
            label: {
                break label;
            }
            """,
            """
            switch (1) {
                case 1:
                    break;
                default:
                    break;
            }
            """
        };

        var expectedFamilyKinds = new HashSet<InstructionKind>
        {
            InstructionKind.Jump,
            InstructionKind.Break,
            InstructionKind.Continue,
            InstructionKind.SetCompletionValue,
            InstructionKind.BreakableExit
        };
        var seenFamilyKinds = new HashSet<InstructionKind>();

        foreach (var source in scriptCases)
        {
            var parsedProgram = _engine.ParseProgram(source);
            await _engine.Evaluate(parsedProgram);

            var scriptCache = ((IAstCacheable<ScriptPlanCache>)parsedProgram).GetOrCreateCache();
            var plan = Assert.IsType<ExecutionPlan>(scriptCache.Plan);
            var boundary = plan.CreateCompactStatementStorageBoundary();

            var expectedSupported = plan.Instructions
                .Where(instruction =>
                    expectedFamilyKinds.Contains(instruction.Kind) &&
                    CompactStatementStorage.TryEncodeSupportedInstruction(instruction, out _))
                .ToArray();
            var actualSupported = boundary.Storage.DecodeSemanticView()
                .Where(instruction => expectedFamilyKinds.Contains(instruction.Kind))
                .ToArray();

            Assert.Equal(expectedSupported.Length, actualSupported.Length);
            Assert.NotEmpty(actualSupported);

            for (var i = 0; i < expectedSupported.Length; i++)
            {
                AssertEquivalentSupportedInstruction(expectedSupported[i], actualSupported[i]);
                seenFamilyKinds.Add(expectedSupported[i].Kind);
            }
        }

        // Keep Jump covered in semantic parity even when script lowering emits loop-specific
        // control-flow instructions instead of a direct Jump for this script corpus.
        var jumpPlan = new ExecutionPlan(ImmutableArray.Create<ExecutionInstruction>(new JumpInstruction(1)), EntryPoint: 0);
        var jumpBoundary = jumpPlan.CreateCompactStatementStorageBoundary();
        var jumpExpected = Assert.Single(jumpPlan.Instructions.Where(instruction =>
            instruction.Kind == InstructionKind.Jump &&
            CompactStatementStorage.TryEncodeSupportedInstruction(instruction, out _)));
        var jumpActual = Assert.Single(jumpBoundary.Storage.DecodeSemanticView().Where(instruction => instruction.Kind == InstructionKind.Jump));
        AssertEquivalentSupportedInstruction(jumpExpected, jumpActual);
        seenFamilyKinds.Add(InstructionKind.Jump);

        Assert.True(
            expectedFamilyKinds.IsSubsetOf(seenFamilyKinds),
            $"Expected to observe supported control-flow kinds: {string.Join(", ", expectedFamilyKinds.OrderBy(kind => kind))}; actual: {string.Join(", ", seenFamilyKinds.OrderBy(kind => kind))}");
    }

    private static void AssertEquivalentSupportedInstruction(ExecutionInstruction expected, ExecutionInstruction actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case JumpInstruction expectedJump:
                var actualJump = Assert.IsType<JumpInstruction>(actual);
                Assert.Equal(expectedJump.Next, actualJump.Next);
                return;
            case BreakInstruction expectedBreak:
                var actualBreak = Assert.IsType<BreakInstruction>(actual);
                Assert.Equal(expectedBreak.TargetIndex, actualBreak.TargetIndex);
                Assert.Equal(expectedBreak.TargetScopeId, actualBreak.TargetScopeId);
                return;
            case ContinueInstruction expectedContinue:
                var actualContinue = Assert.IsType<ContinueInstruction>(actual);
                Assert.Equal(expectedContinue.TargetIndex, actualContinue.TargetIndex);
                Assert.Equal(expectedContinue.TargetScopeId, actualContinue.TargetScopeId);
                return;
            case SetCompletionValueInstruction expectedSetCompletion:
                var actualSetCompletion = Assert.IsType<SetCompletionValueInstruction>(actual);
                Assert.Equal(expectedSetCompletion.Next, actualSetCompletion.Next);
                return;
            case BreakableExitInstruction expectedBreakableExit:
                var actualBreakableExit = Assert.IsType<BreakableExitInstruction>(actual);
                Assert.Equal(expectedBreakableExit.Next, actualBreakableExit.Next);
                return;
        }

        Assert.Fail($"Unsupported parity instruction kind encountered: {expected.Kind}");
    }
}
