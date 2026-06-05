using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal enum UnifiedBytecodeOpCode : byte
{
    LoadSlot,
    LoadDynamicIdentifier,
    LoadThis,
    LoadNewTarget,
    LoadImportMeta,
    LoadTemplateObject,
    LoadLiteral,
    LoadRegexLiteral,
    StoreSlot,
    UpdateSlot,
    InitializeSlot,
    RegisterDisposable,
    DeclareDynamicVar,
    DeclareDynamicLexical,
    InitializeDynamicLexical,
    StoreDynamicIdentifier,
    ResolveDynamicIdentifierReference,
    LoadDynamicIdentifierReference,
    StoreDynamicIdentifierReference,
    PopDynamicIdentifierReference,
    Binary,
    RequireObjectCoercible,
    ResolvePropertyKey,
    GetNamedProperty,
    GetNamedPropertyOptional,
    GetComputedProperty,
    GetNamedPropertyForCompoundSet,
    GetComputedPropertyForCompoundSet,
    SetNamedProperty,
    SetComputedProperty,
    EnsureSuperReference,
    GetNamedSuperProperty,
    GetComputedSuperProperty,
    SetNamedSuperProperty,
    SetComputedSuperProperty,
    UpdateNamedSuperProperty,
    UpdateComputedSuperProperty,
    UpdateNamedProperty,
    UpdateComputedProperty,
    UpdateDynamicIdentifier,
    TypeOf,
    TypeOfIdentifier,
    TypeOfDynamicIdentifier,
    DeleteDynamicIdentifier,
    DeleteNamedProperty,
    DeleteComputedProperty,
    UnaryPlus,
    UnaryMinus,
    UnaryLogicalNot,
    UnaryBitwiseNot,
    UnaryVoid,
    PrivateFieldIn,
    ToString,
    Pop,
    DuplicateTop,
    DuplicateTopTwo,
    SwapTopTwo,
    RotateTopThreeRight,
    CreateArray,
    ArrayPush,
    ArrayPushHole,
    ArraySpread,
    CreateObject,
    DefineObjectProperty,
    DefineComputedObjectProperty,
    DefineObjectMethod,
    DefineComputedObjectMethod,
    DefineObjectAccessor,
    DefineComputedObjectAccessor,
    ObjectSpread,
    Jump,
    JumpWithDriverCleanup,
    JumpIfFalse,
    JumpIfShortCircuitFalse,
    JumpIfShortCircuitTrue,
    JumpIfShortCircuitNotNullish,
    JumpIfShortCircuited,
    JumpIfNullishReplaceUndefined,
    Return,
    ReturnUndefined,
    Throw,
    ThrowReferenceError,
    Break,
    Continue,
    PushEnvironment,
    PopEnvironment,
    EnterTry,
    EnterCatch,
    LeaveTry,
    EndFinally,
    EnterWith,
    LeaveWith,
    TdzHeadInit,
    IteratorInit,
    IteratorMoveNext,
    IteratorClose,
    ForInInit,
    ForInMoveNext,
    ArrayDestructuringInit,
    ArrayDestructuringElement,
    ArrayDestructuringRest,
    ArrayDestructuringClose,
    ObjectDestructuringInit,
    ObjectDestructuringProperty,
    ObjectDestructuringRest,
    ObjectDestructuringClose,
    PrepareIdentifierCallTarget,
    PrepareIdentifierOptionalCallTarget,
    PrepareDynamicIdentifierCallTarget,
    PrepareDynamicIdentifierOptionalCallTarget,
    PrepareNamedCallTarget,
    PrepareComputedCallTarget,
    PrepareNamedOptionalCallTarget,
    PrepareComputedOptionalCallTarget,
    PrepareNamedSuperCallTarget,
    PrepareComputedSuperCallTarget,
    CallInvocationBoundary,
    ConstructInvocationBoundary,
    SuperConstructInvocationBoundary,
    DeclareClass,
    DeclareFunction,
    LoadClassLiteral,
    LoadFunctionLiteral,
    Yield,
    StoreResumeValue,
    AwaitAndDiscard,
    AwaitValue,
    AwaitedReturn,
    YieldStar,
    ApplyBindingTarget,
    ApplyDeclarationBindingTarget,
    EnsureHasName
}

internal readonly record struct UnifiedBytecodeInstruction(
    UnifiedBytecodeOpCode OpCode,
    int Operand = 0);

internal enum UnifiedBytecodeCallTargetKind : byte
{
    Identifier,
    NamedMember,
    ComputedMember,
    NamedSuperMember,
    ComputedSuperMember
}

internal readonly record struct UnifiedBytecodeCallTarget(
    UnifiedBytecodeCallTargetKind Kind,
    int SlotIndex = -1,
    int NameConstantIndex = -1,
    bool IsOptionalReceiverCheck = false);

internal readonly record struct UnifiedBytecodeScopeDescriptor(
    int ScopeId,
    ImmutableArray<int> LexicalSlotIndices,
    ImmutableArray<int> ConstSlotIndices = default,
    ImmutableArray<int> PerIterationCopySlotIndices = default);

internal readonly record struct UnifiedBytecodeTryDescriptor(
    int HandlerTarget,
    int FinallyTarget,
    int EndFinallyTarget,
    int LeaveTryTarget,
    int LoopContinueTarget = -1,
    int LoopBreakTarget = -1);

internal readonly record struct UnifiedBytecodeCatchDescriptor(
    int ScopeId,
    ImmutableArray<int> SlotIndices,
    Symbol? BindingName,
    int BindingSlot = -1);

internal readonly record struct UnifiedBytecodeDriverDescriptor(
    int StateSlot,
    int ValueSlot = -1,
    int TargetSlot = -1,
    int BreakTarget = -1,
    int NextTarget = -1,
    int ContinueTarget = -1,
    IteratorDriverKind IteratorKind = IteratorDriverKind.Sync,
    ImmutableArray<int> TdzHeadSlots = default,
    bool TdzHeadIsConst = false,
    int NameConstantIndex = -1,
    int MoveNextTarget = -1,
    int TargetNameConstantIndex = -1);

internal enum UnifiedBytecodeResumeMode : byte
{
    Next,
    Throw,
    Return
}

internal enum UnifiedBytecodeResumePayloadKind : byte
{
    None,
    Value,
    Throw,
    Return
}

internal enum UnifiedBytecodeStepKind : byte
{
    Completed,
    Yield,
    PendingAwait,
    Throw
}

internal enum UnifiedBytecodeAbruptCompletionKind : byte
{
    None,
    Return,
    Throw,
    Break,
    Continue
}

internal readonly record struct UnifiedBytecodePendingAbruptCompletion(
    UnifiedBytecodeAbruptCompletionKind Kind,
    JsTypes.JsValue Value,
    int Target,
    int ResumeTarget,
    bool OriginatedInFinally)
{
    public static UnifiedBytecodePendingAbruptCompletion None { get; } =
        new(
            UnifiedBytecodeAbruptCompletionKind.None,
            JsTypes.JsValue.Undefined,
            -1,
            -1,
            false);
}

internal sealed class UnifiedBytecodeResumableTryFrame(int descriptorIndex)
{
    public int DescriptorIndex { get; } = descriptorIndex;
    public bool CatchUsed { get; set; }
    public bool FinallyScheduled { get; set; }
    public JsTypes.JsValue ThrownValue { get; set; } = JsTypes.JsValue.Undefined;
    public UnifiedBytecodeCatchDescriptor? ActiveCatchDescriptor { get; set; }
    public UnifiedBytecodePendingAbruptCompletion PendingCompletion { get; set; } =
        UnifiedBytecodePendingAbruptCompletion.None;
}

internal readonly record struct UnifiedBytecodeStepResult(
    UnifiedBytecodeStepKind Kind,
    JsTypes.JsValue Value,
    bool Done,
    JsTypes.JsValue PendingPromise,
    JsTypes.JsValue IteratorResult)
{
    public static UnifiedBytecodeStepResult Completed(JsTypes.JsValue value) =>
        new(UnifiedBytecodeStepKind.Completed, value, true, JsTypes.JsValue.Undefined, JsTypes.JsValue.Undefined);

    public static UnifiedBytecodeStepResult Yield(JsTypes.JsValue value) =>
        new(UnifiedBytecodeStepKind.Yield, value, false, JsTypes.JsValue.Undefined, JsTypes.JsValue.Undefined);

    public static UnifiedBytecodeStepResult YieldIteratorResult(JsTypes.JsValue iteratorResult) =>
        new(UnifiedBytecodeStepKind.Yield, JsTypes.JsValue.Undefined, false, JsTypes.JsValue.Undefined, iteratorResult);

    public static UnifiedBytecodeStepResult PendingAwait(JsTypes.JsValue promise) =>
        new(UnifiedBytecodeStepKind.PendingAwait, JsTypes.JsValue.Undefined, false, promise, JsTypes.JsValue.Undefined);

    public static UnifiedBytecodeStepResult Throw(JsTypes.JsValue value) =>
        new(UnifiedBytecodeStepKind.Throw, value, true, JsTypes.JsValue.Undefined, JsTypes.JsValue.Undefined);
}

internal sealed class UnifiedBytecodeResumeState
{
    public UnifiedBytecodeResumeState(
        UnifiedBytecodeProgram program,
        JsTypes.JsValue[] slots,
        JsTypes.JsValue thisValue = default,
        JsEnvironment? callingEnvironment = null,
        bool isStrict = false,
        JsTypes.JsValue newTarget = default)
    {
        Program = program;
        Slots = slots;
        ThisValue = thisValue;
        CallingEnvironment = callingEnvironment;
        IsStrict = isStrict;
        NewTargetValue = newTarget;
        OperandStack = new JsTypes.JsValue[Math.Max(program.MaxStackDepth, 2)];
        OperandStackShortCircuitFlags = program.RequiresShortCircuitStackFlags
            ? new ulong[(OperandStack.Length + 63) >> 6]
            : null;
        _constSlots = CreateConstSlotBitmap(program, slots.Length);
    }

    private bool[]? _constSlots;

    public UnifiedBytecodeProgram Program { get; }
    public JsTypes.JsValue[] Slots { get; }

    /// <summary>
    ///     The enclosing lexical environment of the resumable activation (the generator/async
    ///     function's closure). Captured at construction so it survives suspension/resume and can be
    ///     threaded into synchronous call dispatch (<see cref="UnifiedBytecodeVirtualMachine.ExecutePreparedCall" />)
    ///     as the caller environment a callee inherits caller-context-sensitive behaviour from. The
    ///     environment is stable for the whole frame, so storing it once is sufficient even when a call
    ///     sits between two suspension points.
    /// </summary>
    public JsEnvironment? CallingEnvironment { get; }

    /// <summary>
    ///     VM-owned snapshot of the <c>super</c> binding for resumable method bodies. This lets
    ///     resumable super read/write/update/call-target opcodes preserve the method receiver and
    ///     home-object prototype without constructing an <see cref="TypedAstEvaluator.ExecutionPlanRunner" />
    ///     just to materialize an invocation environment.
    /// </summary>
    public SuperBinding? ResumableSuperBinding { get; set; }

    /// <summary>
    ///     The strict/sloppy-coerced <c>this</c> binding for the resumable activation. Captured at
    ///     construction so it survives suspension/resume across <c>yield</c>/<c>await</c> boundaries.
    /// </summary>
    public JsTypes.JsValue ThisValue { get; }

    /// <summary>
    ///     The <c>new.target</c> binding for the resumable activation, captured at construction so it
    ///     survives suspension. An ordinary generator/async function is never a constructor, so its own
    ///     <c>new.target</c> is <c>undefined</c> (shadowing any enclosing constructor); an async ARROW
    ///     lexically inherits the enclosing function's <c>new.target</c>. The invoker computes the correct
    ///     value per function kind, so the resumable <c>LoadNewTarget</c> handler reads it directly rather
    ///     than walking the closure chain (which would leak an enclosing constructor for ordinary bodies).
    /// </summary>
    public JsTypes.JsValue NewTargetValue { get; }

    /// <summary>
    ///     Whether the resumable activation's function body executes in strict mode. Captured at
    ///     construction (the function/closure/lexical strictness, the same value the sync VM threads into
    ///     <see cref="UnifiedBytecodeVirtualMachine.Execute" />) so strict-only semantics — e.g. a write to
    ///     a non-writable property throwing a <c>TypeError</c> — are decided by the generator/async body's
    ///     own strictness, not by whatever scope the resume call happens to run under. The resumable VM has
    ///     no <c>isStrict</c> parameter, so this property is how the property-write opcodes get it.
    /// </summary>
    public bool IsStrict { get; }
    public JsTypes.JsValue[] OperandStack { get; }

    /// <summary>
    ///     Per-flat-slot const bitmap for the resumable activation. Seeded from the compiler-owned
    ///     <see cref="UnifiedBytecodeProgram.ConstSlotIndices" /> and extended by loop-head TDZ setup for
    ///     per-iteration <c>const</c> bindings. This mirrors the sync VM's const-slot tracking so
    ///     resumable <c>StoreSlot</c> / <c>UpdateSlot</c> can raise the same <c>TypeError</c> for const
    ///     reassignment instead of declining every lexical slot.
    /// </summary>
    public bool IsConstSlot(int slotIndex) =>
        _constSlots is not null &&
        (uint)slotIndex < (uint)_constSlots.Length &&
        _constSlots[slotIndex];

    public void MarkConstSlot(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)Slots.Length)
        {
            return;
        }

        _constSlots ??= new bool[Slots.Length];
        _constSlots[slotIndex] = true;
    }

    /// <summary>
    ///     Per-operand-slot short-circuit flags, index-aligned with <see cref="OperandStack" />. A set
    ///     bit at index <c>i</c> means the value at operand slot <c>i</c> is the synthetic
    ///     <c>undefined</c> produced by an optional-chain short-circuit, so downstream property reads /
    ///     call-target preparations must propagate <c>undefined</c> rather than re-throwing on a nullish
    ///     base. The sync VM keeps the equivalent array as a stack-parallel local
    ///     (<see cref="UnifiedBytecodeVirtualMachine" />.<c>stackShortCircuitFlags</c>); the resumable VM
    ///     stores it on the resume state so it survives <c>yield</c>/<c>await</c> suspension in lockstep
    ///     with the operand stack (both are stable backing arrays referenced by the static loop, so a
    ///     suspend/resume that saves/restores <see cref="StackPointer" /> leaves the flag column aligned).
    ///     Allocated only when <see cref="UnifiedBytecodeProgram.RequiresShortCircuitStackFlags" /> — i.e.
    ///     when the program contains a <c>JumpIfShortCircuited</c> opcode; otherwise <c>null</c> and every
    ///     flag query reads as <c>false</c>.
    /// </summary>
    public ulong[]? OperandStackShortCircuitFlags { get; }

    private static bool[]? CreateConstSlotBitmap(UnifiedBytecodeProgram program, int slotCount)
    {
        if (program.ConstSlotIndices.IsDefaultOrEmpty)
        {
            return null;
        }

        var constSlots = new bool[slotCount];
        var constSlotIndices = program.ConstSlotIndices;
        for (var i = 0; i < constSlotIndices.Length; i++)
        {
            var slotIndex = constSlotIndices[i];
            if ((uint)slotIndex < (uint)constSlots.Length)
            {
                constSlots[slotIndex] = true;
            }
        }

        return constSlots;
    }

    /// <summary>
    ///     The private-name scopes (the class brand scope plus any enclosing captured scopes) that were
    ///     lexically active where the resumable function was DEFINED. Captured at construction from the
    ///     invoker so they survive <c>yield</c>/<c>await</c> suspension, and re-entered onto the live
    ///     <see cref="EvaluationContext" /> at the top of every <see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />
    ///     step. The sync VM gets this for free because the regular function-invocation path enters the
    ///     scopes around the body, but each resumable step runs on a fresh per-step context, so the
    ///     <c>PrivateFieldIn</c> handler (and any future private-name opcode) would otherwise be unable to
    ///     resolve <c>#name</c> -> its mangled key and would wrongly report the field absent. The scopes are
    ///     a read-only lexical fact of the frame, identical on every resume, so storing them once is
    ///     sufficient. Defaults to empty for resumable bodies that reference no private names.
    /// </summary>
    public ImmutableArray<PrivateNameScope> PrivateNameScopes { get; set; } =
        ImmutableArray<PrivateNameScope>.Empty;

    /// <summary>
    ///     Combines the captured (enclosing) private-name scopes with the body's own class brand scope
    ///     (if any) into the order the sync function-invocation path enters them: enclosing scopes first,
    ///     then the own scope innermost. Used by every resumable invoker to populate
    ///     <see cref="PrivateNameScopes" />. Returns empty when the body references no private names.
    /// </summary>
    public static ImmutableArray<PrivateNameScope> CombinePrivateNameScopes(
        ImmutableArray<PrivateNameScope> capturedScopes,
        PrivateNameScope? ownScope)
    {
        var hasCaptured = !capturedScopes.IsDefaultOrEmpty;
        if (!hasCaptured && ownScope is null)
        {
            return ImmutableArray<PrivateNameScope>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PrivateNameScope>();
        if (hasCaptured)
        {
            builder.AddRange(capturedScopes);
        }

        if (ownScope is { } scope)
        {
            builder.Add(scope);
        }

        return builder.ToImmutable();
    }

    public int ProgramCounter { get; set; }
    public int StackPointer { get; set; }
    public int NextActiveDriverOrdinal;
    public Stack<UnifiedBytecodeResumableTryFrame>? ResumableTryFrames;
    public bool[]? ResumableInactiveCatchBindingSlots;
    public AssignmentReference[]? DynamicIdentifierReferences;
    public int DynamicIdentifierReferenceCount;
    public bool IsCompleted { get; set; }
    public UnifiedBytecodeResumePayloadKind ResumePayloadKind { get; set; }
    public JsTypes.JsValue ResumePayload { get; set; } = JsTypes.JsValue.Undefined;
    public UnifiedBytecodePendingAbruptCompletion PendingAbruptCompletion { get; set; } =
        UnifiedBytecodePendingAbruptCompletion.None;
    public JsTypes.JsValue PendingAwaitPromise { get; set; } = JsTypes.JsValue.Undefined;
    public bool HasMaterializedBodyEnvironment { get; init; }
    public bool IsAsyncLike { get; init; }
    public bool IsAsyncGenerator { get; init; }
}

internal sealed record UnifiedBytecodeProgram(
    ImmutableArray<UnifiedBytecodeInstruction> Instructions,
    int MaxStackDepth,
    int SlotCount,
    ImmutableArray<JsTypes.JsValue> LiteralConstants,
    ImmutableArray<string> StringConstants,
    ImmutableArray<string?> SlotNames,
    ImmutableArray<int> ParameterSlotIndices,
    ImmutableArray<int> LexicalSlotIndices,
    ImmutableArray<UnifiedBytecodeCallTarget> CallTargetConstants,
    ImmutableArray<UnifiedBytecodeScopeDescriptor> ScopeDescriptors,
    ImmutableArray<UnifiedBytecodeTryDescriptor> TryDescriptors,
    ImmutableArray<UnifiedBytecodeCatchDescriptor> CatchDescriptors,
    ImmutableArray<UnifiedBytecodeDriverDescriptor> DriverDescriptors,
    ImmutableArray<ImmutableArray<int>> CallSpreadMasks = default,
    ImmutableArray<FunctionLiteralDescriptor> FunctionLiteralConstants = default,
    ImmutableArray<ClassExpression> ClassLiteralConstants = default,
    ImmutableArray<ClassDeclarationDescriptor> ClassDeclarationConstants = default,
    ImmutableArray<BindingTargetProgram> BindingTargetConstants = default,
    ImmutableArray<TaggedTemplateDescriptor> TemplateObjectConstants = default,
    bool RequiresShortCircuitStackFlags = false,
    int ScriptCompletionSlot = -1,
    ImmutableArray<int> ConstSlotIndices = default);
