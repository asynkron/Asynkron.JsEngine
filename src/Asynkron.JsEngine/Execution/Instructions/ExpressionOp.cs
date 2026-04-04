using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution.Instructions;

internal enum ExpressionOpKind : byte
{
    LoadLiteral,
    LoadRegexLiteral,
    LoadFunctionLiteral,
    LoadClassLiteral,
    LoadIdentifier,
    LoadTemplateObject,
    StoreIdentifier,
    ApplyBindingTarget,
    DuplicateTop,
    DuplicateTopTwo,
    SwapTopTwo,
    RotateTopThreeRight,
    LoadThis,
    LoadNewTarget,
    LoadNamedCallTarget,
    LoadComputedCallTarget,
    LoadNamedSuperCallTarget,
    LoadComputedSuperCallTarget,
    EnsureSuperReference,
    CreateArray,
    ArrayPush,
    ArrayPushHole,
    ArraySpread,
    CreateObject,
    RequireObjectCoercible,
    ResolvePropertyKey,
    DefineObjectProperty,
    DefineComputedObjectProperty,
    DefineObjectMethod,
    DefineComputedObjectMethod,
    DefineObjectAccessor,
    DefineComputedObjectAccessor,
    ObjectSpread,
    GetNamedProperty,
    GetComputedProperty,
    GetNamedSuperProperty,
    GetComputedSuperProperty,
    SetNamedProperty,
    SetComputedProperty,
    SetNamedSuperProperty,
    SetComputedSuperProperty,
    UpdateIdentifier,
    UpdateNamedProperty,
    UpdateComputedProperty,
    UpdateNamedSuperProperty,
    UpdateComputedSuperProperty,
    TypeOf,
    TypeOfIdentifier,
    DeleteIdentifier,
    DeleteNamedProperty,
    DeleteComputedProperty,
    UnaryPlus,
    UnaryMinus,
    UnaryBitwiseNot,
    UnaryVoid,
    ToString,
    UnaryLogicalNot,
    Binary,
    Pop,
    Jump,
    JumpIfNullish,
    JumpIfShortCircuited,
    JumpIfTrue,
    JumpIfFalse,
    JumpIfNotNullish,
    SuperConstruct,
    Call,
    Construct,
    PrivateFieldIn,
    ThrowReferenceError
}

internal readonly record struct ExpressionProgram
{
    public ExpressionProgram(ImmutableArray<PackedExpressionOp> operations)
    {
        Operations = operations;
        MaxStackDepth = ComputeMaxStackDepth(operations);
    }

    public static ExpressionProgram Empty { get; } = new(ImmutableArray<PackedExpressionOp>.Empty);

    public ImmutableArray<PackedExpressionOp> Operations { get; init; }

    public int MaxStackDepth { get; init; }

    public bool IsEmpty => Operations.IsDefaultOrEmpty || Operations.Length == 0;

    public override string ToString() => $"{Operations.Length} ops, stack {MaxStackDepth}";

    private static int ComputeMaxStackDepth(ImmutableArray<PackedExpressionOp> operations)
    {
        if (operations.IsDefaultOrEmpty)
        {
            return 0;
        }

        var stackDepth = 0;
        var maxStackDepth = 0;

        foreach (var operation in operations)
        {
            stackDepth += GetStackDelta(operation);
            maxStackDepth = Math.Max(maxStackDepth, stackDepth);
        }

        return Math.Max(maxStackDepth, 1);
    }

    private static int GetStackDelta(PackedExpressionOp operation)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => 1,
            ExpressionOpKind.LoadRegexLiteral => 1,
            ExpressionOpKind.LoadFunctionLiteral => 1,
            ExpressionOpKind.LoadClassLiteral => 1,
            ExpressionOpKind.LoadIdentifier => 1,
            ExpressionOpKind.LoadTemplateObject => 1,
            ExpressionOpKind.StoreIdentifier => 0,
            ExpressionOpKind.ApplyBindingTarget => -1,
            ExpressionOpKind.DuplicateTop => 1,
            ExpressionOpKind.DuplicateTopTwo => 2,
            ExpressionOpKind.SwapTopTwo => 0,
            ExpressionOpKind.RotateTopThreeRight => 0,
            ExpressionOpKind.LoadThis => 1,
            ExpressionOpKind.LoadNewTarget => 1,
            ExpressionOpKind.LoadNamedCallTarget => 1,
            ExpressionOpKind.LoadComputedCallTarget => 0,
            ExpressionOpKind.LoadNamedSuperCallTarget => 2,
            ExpressionOpKind.LoadComputedSuperCallTarget => 1,
            ExpressionOpKind.EnsureSuperReference => 0,
            ExpressionOpKind.CreateArray => 1,
            ExpressionOpKind.ArrayPush => -1,
            ExpressionOpKind.ArrayPushHole => 0,
            ExpressionOpKind.ArraySpread => -1,
            ExpressionOpKind.CreateObject => 1,
            ExpressionOpKind.RequireObjectCoercible => 0,
            ExpressionOpKind.ResolvePropertyKey => 0,
            ExpressionOpKind.DefineObjectProperty => -1,
            ExpressionOpKind.DefineComputedObjectProperty => -2,
            ExpressionOpKind.DefineObjectMethod => -1,
            ExpressionOpKind.DefineComputedObjectMethod => -2,
            ExpressionOpKind.DefineObjectAccessor => -1,
            ExpressionOpKind.DefineComputedObjectAccessor => -2,
            ExpressionOpKind.ObjectSpread => -1,
            ExpressionOpKind.GetNamedProperty => 0,
            ExpressionOpKind.GetComputedProperty => -1,
            ExpressionOpKind.GetNamedSuperProperty => 1,
            ExpressionOpKind.GetComputedSuperProperty => 0,
            ExpressionOpKind.SetNamedProperty => -1,
            ExpressionOpKind.SetComputedProperty => -2,
            ExpressionOpKind.SetNamedSuperProperty => 0,
            ExpressionOpKind.SetComputedSuperProperty => -1,
            ExpressionOpKind.UpdateIdentifier => 1,
            ExpressionOpKind.UpdateNamedProperty => 0,
            ExpressionOpKind.UpdateComputedProperty => -1,
            ExpressionOpKind.UpdateNamedSuperProperty => 1,
            ExpressionOpKind.UpdateComputedSuperProperty => 0,
            ExpressionOpKind.TypeOf => 0,
            ExpressionOpKind.TypeOfIdentifier => 1,
            ExpressionOpKind.DeleteIdentifier => 1,
            ExpressionOpKind.DeleteNamedProperty => 0,
            ExpressionOpKind.DeleteComputedProperty => -1,
            ExpressionOpKind.UnaryPlus => 0,
            ExpressionOpKind.UnaryMinus => 0,
            ExpressionOpKind.UnaryBitwiseNot => 0,
            ExpressionOpKind.UnaryVoid => 0,
            ExpressionOpKind.ToString => 0,
            ExpressionOpKind.UnaryLogicalNot => 0,
            ExpressionOpKind.Binary => -1,
            ExpressionOpKind.Pop => -1,
            ExpressionOpKind.Jump => 0,
            ExpressionOpKind.JumpIfNullish => 0,
            ExpressionOpKind.JumpIfShortCircuited => 0,
            ExpressionOpKind.JumpIfTrue => 0,
            ExpressionOpKind.JumpIfFalse => 0,
            ExpressionOpKind.JumpIfNotNullish => 0,
            ExpressionOpKind.SuperConstruct => 1 - operation.ArgumentCount,
            ExpressionOpKind.Call => -(operation.ArgumentCount + (operation.HasExplicitThis ? 1 : 0)),
            ExpressionOpKind.Construct => -operation.ArgumentCount,
            ExpressionOpKind.PrivateFieldIn => 0,
            ExpressionOpKind.ThrowReferenceError => 0,
            _ => throw new NotSupportedException(
                $"Expression stack analysis does not support '{operation.Kind}'.")
        };
    }
}

internal enum ObjectAccessorKind : byte
{
    Getter,
    Setter
}

internal sealed class TaggedTemplateDescriptor
{
    public TaggedTemplateDescriptor(
        ImmutableArray<JsValue> cookedStrings,
        ImmutableArray<JsValue> rawStrings)
    {
        CookedStrings = cookedStrings;
        RawStrings = rawStrings;
    }

    public ImmutableArray<JsValue> CookedStrings { get; }

    public ImmutableArray<JsValue> RawStrings { get; }
}

internal readonly struct PackedExpressionOp
{
    private const byte Flag0 = 1 << 0;
    private const byte Flag1 = 1 << 1;
    private const byte Flag2 = 1 << 2;
    private const byte RegexFlagHasIndices = 1 << 0;
    private const byte RegexFlagGlobal = 1 << 1;
    private const byte RegexFlagIgnoreCase = 1 << 2;
    private const byte RegexFlagMultiline = 1 << 3;
    private const byte RegexFlagDotAll = 1 << 4;
    private const byte RegexFlagUnicode = 1 << 5;
    private const byte RegexFlagUnicodeSets = 1 << 6;
    private const byte RegexFlagSticky = 1 << 7;

    public static readonly PackedExpressionOp EnsureSuperReference = new(ExpressionOpKind.EnsureSuperReference);
    public static readonly PackedExpressionOp LoadThis = new(ExpressionOpKind.LoadThis);
    public static readonly PackedExpressionOp LoadNewTarget = new(ExpressionOpKind.LoadNewTarget);
    public static readonly PackedExpressionOp DuplicateTop = new(ExpressionOpKind.DuplicateTop);
    public static readonly PackedExpressionOp DuplicateTopTwo = new(ExpressionOpKind.DuplicateTopTwo);
    public static readonly PackedExpressionOp SwapTopTwo = new(ExpressionOpKind.SwapTopTwo);
    public static readonly PackedExpressionOp RotateTopThreeRight = new(ExpressionOpKind.RotateTopThreeRight);
    public static readonly PackedExpressionOp LoadComputedCallTarget = new(ExpressionOpKind.LoadComputedCallTarget);
    public static readonly PackedExpressionOp LoadComputedSuperCallTarget = new(ExpressionOpKind.LoadComputedSuperCallTarget);
    public static readonly PackedExpressionOp CreateArray = new(ExpressionOpKind.CreateArray);
    public static readonly PackedExpressionOp ArrayPush = new(ExpressionOpKind.ArrayPush);
    public static readonly PackedExpressionOp ArrayPushHole = new(ExpressionOpKind.ArrayPushHole);
    public static readonly PackedExpressionOp ArraySpread = new(ExpressionOpKind.ArraySpread);
    public static readonly PackedExpressionOp CreateObject = new(ExpressionOpKind.CreateObject);
    public static readonly PackedExpressionOp ResolvePropertyKey = new(ExpressionOpKind.ResolvePropertyKey);
    public static readonly PackedExpressionOp DefineComputedObjectMethod = new(ExpressionOpKind.DefineComputedObjectMethod);
    public static readonly PackedExpressionOp ObjectSpread = new(ExpressionOpKind.ObjectSpread);
    public static readonly PackedExpressionOp GetComputedSuperProperty = new(ExpressionOpKind.GetComputedSuperProperty);
    public static readonly PackedExpressionOp TypeOf = new(ExpressionOpKind.TypeOf);
    public static readonly PackedExpressionOp DeleteComputedProperty = new(ExpressionOpKind.DeleteComputedProperty);
    public static readonly PackedExpressionOp UnaryPlus = new(ExpressionOpKind.UnaryPlus);
    public static readonly PackedExpressionOp UnaryMinus = new(ExpressionOpKind.UnaryMinus);
    public static readonly PackedExpressionOp UnaryBitwiseNot = new(ExpressionOpKind.UnaryBitwiseNot);
    public static readonly PackedExpressionOp UnaryVoid = new(ExpressionOpKind.UnaryVoid);
    public static readonly PackedExpressionOp ToStringValue = new(ExpressionOpKind.ToString);
    public static readonly PackedExpressionOp UnaryLogicalNot = new(ExpressionOpKind.UnaryLogicalNot);
    public static readonly PackedExpressionOp Pop = new(ExpressionOpKind.Pop);

    private static readonly PackedExpressionOp RequireObjectCoercibleDefault = new(ExpressionOpKind.RequireObjectCoercible);
    private static readonly PackedExpressionOp RequireObjectCoercibleDepthOne = new(ExpressionOpKind.RequireObjectCoercible, int0: 1);
    private static readonly PackedExpressionOp GetComputedPropertyDefault = new(ExpressionOpKind.GetComputedProperty);
    private static readonly PackedExpressionOp GetComputedPropertyShortCircuit = new(ExpressionOpKind.GetComputedProperty, flags: Flag1);
    private static readonly PackedExpressionOp SetComputedPropertyDefault = new(ExpressionOpKind.SetComputedProperty, flags: Flag0);
    private static readonly PackedExpressionOp SetComputedPropertyNoInference = new(ExpressionOpKind.SetComputedProperty);
    private static readonly PackedExpressionOp SetComputedSuperPropertyDefault = new(ExpressionOpKind.SetComputedSuperProperty, flags: Flag0);
    private static readonly PackedExpressionOp SetComputedSuperPropertyNoInference = new(ExpressionOpKind.SetComputedSuperProperty);

    private readonly object? _data;
    private readonly JsValue _value;
    private readonly int _int0;
    private readonly int _int1;
    private readonly int _int2;
    private readonly byte _flags;

    private PackedExpressionOp(
        ExpressionOpKind kind,
        object? data = null,
        JsValue value = default,
        int int0 = 0,
        int int1 = 0,
        int int2 = 0,
        byte flags = 0)
    {
        Kind = kind;
        _data = data;
        _value = value;
        _int0 = int0;
        _int1 = int1;
        _int2 = int2;
        _flags = flags;
    }

    public ExpressionOpKind Kind { get; }

    public JsValue LiteralValue => _value;

    public string Pattern => (string)_data!;

    public string RegexFlags => DecodeRegexFlags(_flags);

    public FunctionExpression Function => (FunctionExpression)_data!;

    public ClassExpression Class => (ClassExpression)_data!;

    public TaggedTemplateDescriptor TemplateDescriptor => (TaggedTemplateDescriptor)_data!;

    public Symbol Name => (Symbol)_data!;

    public BindingTargetProgram TargetProgram => (BindingTargetProgram)_data!;

    public string Text => (string)_data!;

    public int ScopeId => _int0;

    public int SlotIndex => _int1;

    public int FlatSlotId => _int2;

    public int Depth => _int0;

    public int Target => _int0;

    public int ArgumentCount => _int0;

    public ObjectAccessorKind AccessorKind => (ObjectAccessorKind)_int0;

    public BinaryOperator Operator => (BinaryOperator)_int0;

    public bool IsArguments => Kind == ExpressionOpKind.UpdateIdentifier
        ? (_flags & Flag2) != 0
        : (_flags & Flag0) != 0;

    public bool AllowNameInference => Kind == ExpressionOpKind.DefineObjectProperty
        ? (_flags & Flag1) != 0
        : (_flags & Flag0) != 0;

    public bool IsOptional => (_flags & Flag0) != 0;

    public bool IsIncrement => (_flags & Flag0) != 0;

    public bool HasExplicitThis => (_flags & Flag0) != 0;

    public bool IsPrototypeMutation => (_flags & Flag0) != 0;

    public bool IsConstructorFunction => (_flags & Flag0) != 0;

    public bool ShortCircuitOnNullishTarget => (_flags & Flag1) != 0;

    public bool IsPrefix => (_flags & Flag1) != 0;

    public bool IsDirectEval => (_flags & Flag1) != 0;

    public bool ReplaceWithUndefined => (_flags & Flag1) != 0;

    public ImmutableArray<bool> SpreadMask => _data is ImmutableArray<bool> spreadMask
        ? spreadMask
        : default;

    public static PackedExpressionOp LoadLiteral(JsValue Value)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadLiteral, value: Value);
    }

    public static PackedExpressionOp LoadRegexLiteral(string Pattern, string Flags)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadRegexLiteral,
            data: Pattern,
            flags: EncodeRegexFlags(Flags));
    }

    public static PackedExpressionOp LoadFunctionLiteral(
        FunctionExpression Function,
        bool IsConstructorFunction = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadFunctionLiteral,
            data: Function,
            flags: IsConstructorFunction ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp LoadClassLiteral(ClassExpression Class)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadClassLiteral, data: Class);
    }

    public static PackedExpressionOp LoadIdentifier(
        Symbol Name,
        int ScopeId = -1,
        int SlotIndex = -1,
        int FlatSlotId = -1,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadIdentifier,
            data: Name,
            int0: ScopeId,
            int1: SlotIndex,
            int2: FlatSlotId,
            flags: IsArguments ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp LoadTemplateObject(TaggedTemplateDescriptor Descriptor)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadTemplateObject, data: Descriptor);
    }

    public static PackedExpressionOp StoreIdentifier(
        Symbol Name,
        int ScopeId = -1,
        int SlotIndex = -1,
        int FlatSlotId = -1,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.StoreIdentifier,
            data: Name,
            int0: ScopeId,
            int1: SlotIndex,
            int2: FlatSlotId,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp ApplyBindingTarget(BindingTargetProgram TargetProgram)
    {
        return new PackedExpressionOp(ExpressionOpKind.ApplyBindingTarget, data: TargetProgram);
    }

    public static PackedExpressionOp LoadNamedCallTarget(string PropertyName)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadNamedCallTarget, data: PropertyName);
    }

    public static PackedExpressionOp LoadNamedSuperCallTarget(string PropertyName)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadNamedSuperCallTarget, data: PropertyName);
    }

    public static PackedExpressionOp RequireObjectCoercible(int Depth = 0)
    {
        return Depth switch
        {
            0 => RequireObjectCoercibleDefault,
            1 => RequireObjectCoercibleDepthOne,
            _ => new PackedExpressionOp(ExpressionOpKind.RequireObjectCoercible, int0: Depth)
        };
    }

    public static PackedExpressionOp DefineObjectProperty(
        string PropertyName,
        bool IsPrototypeMutation = false,
        bool AllowNameInference = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineObjectProperty,
            data: PropertyName,
            flags: (byte)((IsPrototypeMutation ? Flag0 : 0) |
                          (AllowNameInference ? Flag1 : 0)));
    }

    public static PackedExpressionOp DefineComputedObjectProperty(bool AllowNameInference = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineComputedObjectProperty,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp DefineObjectMethod(string PropertyName)
    {
        return new PackedExpressionOp(ExpressionOpKind.DefineObjectMethod, data: PropertyName);
    }

    public static PackedExpressionOp DefineObjectAccessor(string PropertyName, ObjectAccessorKind AccessorKind)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineObjectAccessor,
            data: PropertyName,
            int0: (int)AccessorKind);
    }

    public static PackedExpressionOp DefineComputedObjectAccessor(ObjectAccessorKind AccessorKind)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineComputedObjectAccessor,
            int0: (int)AccessorKind);
    }

    public static PackedExpressionOp GetNamedProperty(
        string PropertyName,
        bool IsOptional = false,
        bool ShortCircuitOnNullishTarget = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.GetNamedProperty,
            data: PropertyName,
            flags: (byte)((IsOptional ? Flag0 : 0) |
                          (ShortCircuitOnNullishTarget ? Flag1 : 0)));
    }

    public static PackedExpressionOp GetComputedProperty(bool ShortCircuitOnNullishTarget = false)
    {
        return ShortCircuitOnNullishTarget
            ? GetComputedPropertyShortCircuit
            : GetComputedPropertyDefault;
    }

    public static PackedExpressionOp GetNamedSuperProperty(string PropertyName)
    {
        return new PackedExpressionOp(ExpressionOpKind.GetNamedSuperProperty, data: PropertyName);
    }

    public static PackedExpressionOp SetNamedProperty(
        string PropertyName,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.SetNamedProperty,
            data: PropertyName,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp SetComputedProperty(bool AllowNameInference = true)
    {
        return AllowNameInference
            ? SetComputedPropertyDefault
            : SetComputedPropertyNoInference;
    }

    public static PackedExpressionOp SetNamedSuperProperty(
        string PropertyName,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.SetNamedSuperProperty,
            data: PropertyName,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp SetComputedSuperProperty(bool AllowNameInference = true)
    {
        return AllowNameInference
            ? SetComputedSuperPropertyDefault
            : SetComputedSuperPropertyNoInference;
    }

    public static PackedExpressionOp UpdateIdentifier(
        Symbol Name,
        int ScopeId = -1,
        int SlotIndex = -1,
        int FlatSlotId = -1,
        bool IsIncrement = true,
        bool IsPrefix = true,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateIdentifier,
            data: Name,
            int0: ScopeId,
            int1: SlotIndex,
            int2: FlatSlotId,
            flags: (byte)((IsIncrement ? Flag0 : 0) |
                          (IsPrefix ? Flag1 : 0) |
                          (IsArguments ? Flag2 : 0)));
    }

    public static PackedExpressionOp UpdateNamedProperty(
        string PropertyName,
        bool IsIncrement = true,
        bool IsPrefix = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateNamedProperty,
            data: PropertyName,
            flags: (byte)((IsIncrement ? Flag0 : 0) |
                          (IsPrefix ? Flag1 : 0)));
    }

    public static PackedExpressionOp UpdateComputedProperty(
        bool IsIncrement = true,
        bool IsPrefix = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateComputedProperty,
            flags: (byte)((IsIncrement ? Flag0 : 0) |
                          (IsPrefix ? Flag1 : 0)));
    }

    public static PackedExpressionOp UpdateNamedSuperProperty(
        string PropertyName,
        bool IsIncrement = true,
        bool IsPrefix = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateNamedSuperProperty,
            data: PropertyName,
            flags: (byte)((IsIncrement ? Flag0 : 0) |
                          (IsPrefix ? Flag1 : 0)));
    }

    public static PackedExpressionOp UpdateComputedSuperProperty(
        bool IsIncrement = true,
        bool IsPrefix = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateComputedSuperProperty,
            flags: (byte)((IsIncrement ? Flag0 : 0) |
                          (IsPrefix ? Flag1 : 0)));
    }

    public static PackedExpressionOp TypeOfIdentifier(
        Symbol Name,
        int ScopeId = -1,
        int SlotIndex = -1,
        int FlatSlotId = -1,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.TypeOfIdentifier,
            data: Name,
            int0: ScopeId,
            int1: SlotIndex,
            int2: FlatSlotId,
            flags: IsArguments ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp DeleteIdentifier(Symbol Name)
    {
        return new PackedExpressionOp(ExpressionOpKind.DeleteIdentifier, data: Name);
    }

    public static PackedExpressionOp DeleteNamedProperty(string PropertyName)
    {
        return new PackedExpressionOp(ExpressionOpKind.DeleteNamedProperty, data: PropertyName);
    }

    public static PackedExpressionOp Binary(BinaryOperator Operator)
    {
        return new PackedExpressionOp(ExpressionOpKind.Binary, int0: (int)Operator);
    }

    public static PackedExpressionOp PrivateFieldIn(string PrivateName)
    {
        return new PackedExpressionOp(ExpressionOpKind.PrivateFieldIn, data: PrivateName);
    }

    public static PackedExpressionOp ThrowReferenceError(string Message)
    {
        return new PackedExpressionOp(ExpressionOpKind.ThrowReferenceError, data: Message);
    }

    public static PackedExpressionOp Jump(int Target)
    {
        return new PackedExpressionOp(ExpressionOpKind.Jump, int0: Target);
    }

    public static PackedExpressionOp JumpIfNullish(
        int Target,
        bool ReplaceWithUndefined = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.JumpIfNullish,
            int0: Target,
            flags: ReplaceWithUndefined ? Flag1 : (byte)0);
    }

    public static PackedExpressionOp JumpIfShortCircuited(int Target)
    {
        return new PackedExpressionOp(ExpressionOpKind.JumpIfShortCircuited, int0: Target);
    }

    public static PackedExpressionOp JumpIfTrue(int Target)
    {
        return new PackedExpressionOp(ExpressionOpKind.JumpIfTrue, int0: Target);
    }

    public static PackedExpressionOp JumpIfFalse(int Target)
    {
        return new PackedExpressionOp(ExpressionOpKind.JumpIfFalse, int0: Target);
    }

    public static PackedExpressionOp JumpIfNotNullish(int Target)
    {
        return new PackedExpressionOp(ExpressionOpKind.JumpIfNotNullish, int0: Target);
    }

    public static PackedExpressionOp SuperConstruct(
        int ArgumentCount,
        ImmutableArray<bool> SpreadMask = default)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.SuperConstruct,
            data: SpreadMask.IsDefaultOrEmpty ? null : SpreadMask,
            int0: ArgumentCount);
    }

    public static PackedExpressionOp Call(
        int ArgumentCount,
        bool HasExplicitThis = false,
        bool IsDirectEval = false,
        ImmutableArray<bool> SpreadMask = default)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.Call,
            data: SpreadMask.IsDefaultOrEmpty ? null : SpreadMask,
            int0: ArgumentCount,
            flags: (byte)((HasExplicitThis ? Flag0 : 0) |
                          (IsDirectEval ? Flag1 : 0)));
    }

    public static PackedExpressionOp Construct(
        int ArgumentCount,
        ImmutableArray<bool> SpreadMask = default)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.Construct,
            data: SpreadMask.IsDefaultOrEmpty ? null : SpreadMask,
            int0: ArgumentCount);
    }

    public PackedExpressionOp WithIdentifierResolution(int scopeId, int slotIndex, int flatSlotId)
    {
        return new PackedExpressionOp(Kind, _data, _value, scopeId, slotIndex, flatSlotId, _flags);
    }

    public PackedExpressionOp WithBindingTargetProgram(BindingTargetProgram targetProgram)
    {
        return new PackedExpressionOp(Kind, targetProgram, _value, _int0, _int1, _int2, _flags);
    }

    public PackedExpressionOp WithFunction(FunctionExpression function)
    {
        return new PackedExpressionOp(Kind, function, _value, _int0, _int1, _int2, _flags);
    }

    public PackedExpressionOp WithClass(ClassExpression classExpression)
    {
        return new PackedExpressionOp(Kind, classExpression, _value, _int0, _int1, _int2, _flags);
    }

    private static byte EncodeRegexFlags(string flags)
    {
        var encoded = (byte)0;

        foreach (var flag in flags)
        {
            encoded |= flag switch
            {
                'd' => RegexFlagHasIndices,
                'g' => RegexFlagGlobal,
                'i' => RegexFlagIgnoreCase,
                'm' => RegexFlagMultiline,
                's' => RegexFlagDotAll,
                'u' => RegexFlagUnicode,
                'v' => RegexFlagUnicodeSets,
                'y' => RegexFlagSticky,
                _ => throw new NotSupportedException($"Unsupported regex flag '{flag}'.")
            };
        }

        return encoded;
    }

    private static string DecodeRegexFlags(byte encodedFlags)
    {
        var length = 0;
        if ((encodedFlags & RegexFlagHasIndices) != 0) length++;
        if ((encodedFlags & RegexFlagGlobal) != 0) length++;
        if ((encodedFlags & RegexFlagIgnoreCase) != 0) length++;
        if ((encodedFlags & RegexFlagMultiline) != 0) length++;
        if ((encodedFlags & RegexFlagDotAll) != 0) length++;
        if ((encodedFlags & RegexFlagUnicode) != 0) length++;
        if ((encodedFlags & RegexFlagUnicodeSets) != 0) length++;
        if ((encodedFlags & RegexFlagSticky) != 0) length++;

        return string.Create(length, encodedFlags, static (span, flags) =>
        {
            var index = 0;

            if ((flags & RegexFlagHasIndices) != 0)
            {
                span[index++] = 'd';
            }

            if ((flags & RegexFlagGlobal) != 0)
            {
                span[index++] = 'g';
            }

            if ((flags & RegexFlagIgnoreCase) != 0)
            {
                span[index++] = 'i';
            }

            if ((flags & RegexFlagMultiline) != 0)
            {
                span[index++] = 'm';
            }

            if ((flags & RegexFlagDotAll) != 0)
            {
                span[index++] = 's';
            }

            if ((flags & RegexFlagUnicode) != 0)
            {
                span[index++] = 'u';
            }

            if ((flags & RegexFlagUnicodeSets) != 0)
            {
                span[index++] = 'v';
            }

            if ((flags & RegexFlagSticky) != 0)
            {
                span[index++] = 'y';
            }
        });
    }
}
