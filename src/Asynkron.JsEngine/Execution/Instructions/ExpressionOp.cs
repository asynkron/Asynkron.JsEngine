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
    public ExpressionProgram(ImmutableArray<ExpressionOp> operations)
    {
        Operations = PackOperations(operations);
        MaxStackDepth = ComputeMaxStackDepth(Operations);
    }

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

    private static ImmutableArray<PackedExpressionOp> PackOperations(ImmutableArray<ExpressionOp> operations)
    {
        if (operations.IsDefaultOrEmpty)
        {
            return ImmutableArray<PackedExpressionOp>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PackedExpressionOp>(operations.Length);
        foreach (var operation in operations)
        {
            builder.Add(PackedExpressionOp.Pack(operation));
        }

        return builder.MoveToImmutable();
    }

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

internal abstract record ExpressionOp(ExpressionOpKind Kind);

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

internal sealed class RegexLiteralPayload
{
    public RegexLiteralPayload(string pattern, string flags)
    {
        Pattern = pattern;
        Flags = flags;
    }

    public string Pattern { get; }

    public string Flags { get; }
}

internal sealed class SpreadMaskPayload
{
    public SpreadMaskPayload(ImmutableArray<bool> spreadMask)
    {
        SpreadMask = spreadMask;
    }

    public ImmutableArray<bool> SpreadMask { get; }
}

internal readonly struct PackedExpressionOp
{
    private const byte Flag0 = 1 << 0;
    private const byte Flag1 = 1 << 1;
    private const byte Flag2 = 1 << 2;

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

    public string Pattern => ((RegexLiteralPayload)_data!).Pattern;

    public string RegexFlags => ((RegexLiteralPayload)_data!).Flags;

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

    public ImmutableArray<bool> SpreadMask => _data is SpreadMaskPayload payload
        ? payload.SpreadMask
        : default;

    public static PackedExpressionOp LoadLiteral(JsValue Value)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadLiteral, value: Value);
    }

    public static PackedExpressionOp LoadRegexLiteral(string Pattern, string Flags)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadRegexLiteral,
            data: new RegexLiteralPayload(Pattern, Flags));
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
            data: SpreadMask.IsDefaultOrEmpty ? null : new SpreadMaskPayload(SpreadMask),
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
            data: SpreadMask.IsDefaultOrEmpty ? null : new SpreadMaskPayload(SpreadMask),
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
            data: SpreadMask.IsDefaultOrEmpty ? null : new SpreadMaskPayload(SpreadMask),
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

    public static PackedExpressionOp Pack(ExpressionOp operation)
    {
        return operation switch
        {
            LoadLiteralExpressionOp loadLiteral => new PackedExpressionOp(ExpressionOpKind.LoadLiteral, value: loadLiteral.Value),
            LoadRegexLiteralExpressionOp loadRegex => new PackedExpressionOp(
                ExpressionOpKind.LoadRegexLiteral,
                data: new RegexLiteralPayload(loadRegex.Pattern, loadRegex.Flags)),
            LoadFunctionLiteralExpressionOp loadFunction => new PackedExpressionOp(
                ExpressionOpKind.LoadFunctionLiteral,
                data: loadFunction.Function,
                flags: loadFunction.IsConstructorFunction ? Flag0 : (byte)0),
            LoadClassLiteralExpressionOp loadClass => new PackedExpressionOp(ExpressionOpKind.LoadClassLiteral, data: loadClass.Class),
            LoadTemplateObjectExpressionOp templateObject => new PackedExpressionOp(ExpressionOpKind.LoadTemplateObject, data: templateObject.Descriptor),
            LoadIdentifierExpressionOp loadIdentifier => new PackedExpressionOp(
                ExpressionOpKind.LoadIdentifier,
                data: loadIdentifier.Name,
                int0: loadIdentifier.ScopeId,
                int1: loadIdentifier.SlotIndex,
                int2: loadIdentifier.FlatSlotId,
                flags: loadIdentifier.IsArguments ? Flag0 : (byte)0),
            StoreIdentifierExpressionOp storeIdentifier => new PackedExpressionOp(
                ExpressionOpKind.StoreIdentifier,
                data: storeIdentifier.Name,
                int0: storeIdentifier.ScopeId,
                int1: storeIdentifier.SlotIndex,
                int2: storeIdentifier.FlatSlotId,
                flags: storeIdentifier.AllowNameInference ? Flag0 : (byte)0),
            ApplyBindingTargetExpressionOp applyBindingTarget => new PackedExpressionOp(ExpressionOpKind.ApplyBindingTarget, data: applyBindingTarget.TargetProgram),
            DuplicateTopExpressionOp => new PackedExpressionOp(ExpressionOpKind.DuplicateTop),
            DuplicateTopTwoExpressionOp => new PackedExpressionOp(ExpressionOpKind.DuplicateTopTwo),
            SwapTopTwoExpressionOp => new PackedExpressionOp(ExpressionOpKind.SwapTopTwo),
            RotateTopThreeRightExpressionOp => new PackedExpressionOp(ExpressionOpKind.RotateTopThreeRight),
            LoadThisExpressionOp => new PackedExpressionOp(ExpressionOpKind.LoadThis),
            LoadNewTargetExpressionOp => new PackedExpressionOp(ExpressionOpKind.LoadNewTarget),
            LoadNamedCallTargetExpressionOp namedCallTarget => new PackedExpressionOp(ExpressionOpKind.LoadNamedCallTarget, data: namedCallTarget.PropertyName),
            LoadComputedCallTargetExpressionOp => new PackedExpressionOp(ExpressionOpKind.LoadComputedCallTarget),
            LoadNamedSuperCallTargetExpressionOp namedSuperCallTarget => new PackedExpressionOp(ExpressionOpKind.LoadNamedSuperCallTarget, data: namedSuperCallTarget.PropertyName),
            LoadComputedSuperCallTargetExpressionOp => new PackedExpressionOp(ExpressionOpKind.LoadComputedSuperCallTarget),
            EnsureSuperReferenceExpressionOp => new PackedExpressionOp(ExpressionOpKind.EnsureSuperReference),
            CreateArrayExpressionOp => new PackedExpressionOp(ExpressionOpKind.CreateArray),
            ArrayPushExpressionOp => new PackedExpressionOp(ExpressionOpKind.ArrayPush),
            ArrayPushHoleExpressionOp => new PackedExpressionOp(ExpressionOpKind.ArrayPushHole),
            ArraySpreadExpressionOp => new PackedExpressionOp(ExpressionOpKind.ArraySpread),
            CreateObjectExpressionOp => new PackedExpressionOp(ExpressionOpKind.CreateObject),
            RequireObjectCoercibleExpressionOp requireObjectCoercible => new PackedExpressionOp(ExpressionOpKind.RequireObjectCoercible, int0: requireObjectCoercible.Depth),
            ResolvePropertyKeyExpressionOp => new PackedExpressionOp(ExpressionOpKind.ResolvePropertyKey),
            DefineObjectPropertyExpressionOp defineObjectProperty => new PackedExpressionOp(
                ExpressionOpKind.DefineObjectProperty,
                data: defineObjectProperty.PropertyName,
                flags: (byte)((defineObjectProperty.IsPrototypeMutation ? Flag0 : 0) |
                              (defineObjectProperty.AllowNameInference ? Flag1 : 0))),
            DefineComputedObjectPropertyExpressionOp defineComputedObjectProperty => new PackedExpressionOp(
                ExpressionOpKind.DefineComputedObjectProperty,
                flags: defineComputedObjectProperty.AllowNameInference ? Flag0 : (byte)0),
            DefineObjectMethodExpressionOp defineObjectMethod => new PackedExpressionOp(ExpressionOpKind.DefineObjectMethod, data: defineObjectMethod.PropertyName),
            DefineComputedObjectMethodExpressionOp => new PackedExpressionOp(ExpressionOpKind.DefineComputedObjectMethod),
            DefineObjectAccessorExpressionOp defineObjectAccessor => new PackedExpressionOp(
                ExpressionOpKind.DefineObjectAccessor,
                data: defineObjectAccessor.PropertyName,
                int0: (int)defineObjectAccessor.AccessorKind),
            DefineComputedObjectAccessorExpressionOp defineComputedObjectAccessor => new PackedExpressionOp(
                ExpressionOpKind.DefineComputedObjectAccessor,
                int0: (int)defineComputedObjectAccessor.AccessorKind),
            ObjectSpreadExpressionOp => new PackedExpressionOp(ExpressionOpKind.ObjectSpread),
            GetNamedPropertyExpressionOp getNamedProperty => new PackedExpressionOp(
                ExpressionOpKind.GetNamedProperty,
                data: getNamedProperty.PropertyName,
                flags: (byte)((getNamedProperty.IsOptional ? Flag0 : 0) |
                              (getNamedProperty.ShortCircuitOnNullishTarget ? Flag1 : 0))),
            GetComputedPropertyExpressionOp getComputedProperty => new PackedExpressionOp(
                ExpressionOpKind.GetComputedProperty,
                flags: getComputedProperty.ShortCircuitOnNullishTarget ? Flag1 : (byte)0),
            GetNamedSuperPropertyExpressionOp getNamedSuperProperty => new PackedExpressionOp(ExpressionOpKind.GetNamedSuperProperty, data: getNamedSuperProperty.PropertyName),
            GetComputedSuperPropertyExpressionOp => new PackedExpressionOp(ExpressionOpKind.GetComputedSuperProperty),
            SetNamedPropertyExpressionOp setNamedProperty => new PackedExpressionOp(
                ExpressionOpKind.SetNamedProperty,
                data: setNamedProperty.PropertyName,
                flags: setNamedProperty.AllowNameInference ? Flag0 : (byte)0),
            SetComputedPropertyExpressionOp setComputedProperty => new PackedExpressionOp(
                ExpressionOpKind.SetComputedProperty,
                flags: setComputedProperty.AllowNameInference ? Flag0 : (byte)0),
            SetNamedSuperPropertyExpressionOp setNamedSuperProperty => new PackedExpressionOp(
                ExpressionOpKind.SetNamedSuperProperty,
                data: setNamedSuperProperty.PropertyName,
                flags: setNamedSuperProperty.AllowNameInference ? Flag0 : (byte)0),
            SetComputedSuperPropertyExpressionOp setComputedSuperProperty => new PackedExpressionOp(
                ExpressionOpKind.SetComputedSuperProperty,
                flags: setComputedSuperProperty.AllowNameInference ? Flag0 : (byte)0),
            UpdateIdentifierExpressionOp updateIdentifier => new PackedExpressionOp(
                ExpressionOpKind.UpdateIdentifier,
                data: updateIdentifier.Name,
                int0: updateIdentifier.ScopeId,
                int1: updateIdentifier.SlotIndex,
                int2: updateIdentifier.FlatSlotId,
                flags: (byte)((updateIdentifier.IsIncrement ? Flag0 : 0) |
                              (updateIdentifier.IsPrefix ? Flag1 : 0) |
                              (updateIdentifier.IsArguments ? Flag2 : 0))),
            UpdateNamedPropertyExpressionOp updateNamedProperty => new PackedExpressionOp(
                ExpressionOpKind.UpdateNamedProperty,
                data: updateNamedProperty.PropertyName,
                flags: (byte)((updateNamedProperty.IsIncrement ? Flag0 : 0) |
                              (updateNamedProperty.IsPrefix ? Flag1 : 0))),
            UpdateComputedPropertyExpressionOp updateComputedProperty => new PackedExpressionOp(
                ExpressionOpKind.UpdateComputedProperty,
                flags: (byte)((updateComputedProperty.IsIncrement ? Flag0 : 0) |
                              (updateComputedProperty.IsPrefix ? Flag1 : 0))),
            UpdateNamedSuperPropertyExpressionOp updateNamedSuperProperty => new PackedExpressionOp(
                ExpressionOpKind.UpdateNamedSuperProperty,
                data: updateNamedSuperProperty.PropertyName,
                flags: (byte)((updateNamedSuperProperty.IsIncrement ? Flag0 : 0) |
                              (updateNamedSuperProperty.IsPrefix ? Flag1 : 0))),
            UpdateComputedSuperPropertyExpressionOp updateComputedSuperProperty => new PackedExpressionOp(
                ExpressionOpKind.UpdateComputedSuperProperty,
                flags: (byte)((updateComputedSuperProperty.IsIncrement ? Flag0 : 0) |
                              (updateComputedSuperProperty.IsPrefix ? Flag1 : 0))),
            TypeOfExpressionOp => new PackedExpressionOp(ExpressionOpKind.TypeOf),
            TypeOfIdentifierExpressionOp typeofIdentifier => new PackedExpressionOp(
                ExpressionOpKind.TypeOfIdentifier,
                data: typeofIdentifier.Name,
                int0: typeofIdentifier.ScopeId,
                int1: typeofIdentifier.SlotIndex,
                int2: typeofIdentifier.FlatSlotId,
                flags: typeofIdentifier.IsArguments ? Flag0 : (byte)0),
            DeleteIdentifierExpressionOp deleteIdentifier => new PackedExpressionOp(ExpressionOpKind.DeleteIdentifier, data: deleteIdentifier.Name),
            DeleteNamedPropertyExpressionOp deleteNamedProperty => new PackedExpressionOp(ExpressionOpKind.DeleteNamedProperty, data: deleteNamedProperty.PropertyName),
            DeleteComputedPropertyExpressionOp => new PackedExpressionOp(ExpressionOpKind.DeleteComputedProperty),
            UnaryPlusExpressionOp => new PackedExpressionOp(ExpressionOpKind.UnaryPlus),
            UnaryMinusExpressionOp => new PackedExpressionOp(ExpressionOpKind.UnaryMinus),
            UnaryBitwiseNotExpressionOp => new PackedExpressionOp(ExpressionOpKind.UnaryBitwiseNot),
            UnaryVoidExpressionOp => new PackedExpressionOp(ExpressionOpKind.UnaryVoid),
            ToStringExpressionOp => new PackedExpressionOp(ExpressionOpKind.ToString),
            UnaryLogicalNotExpressionOp => new PackedExpressionOp(ExpressionOpKind.UnaryLogicalNot),
            BinaryExpressionOp binary => new PackedExpressionOp(ExpressionOpKind.Binary, int0: (int)binary.Operator),
            PrivateFieldInExpressionOp privateFieldIn => new PackedExpressionOp(ExpressionOpKind.PrivateFieldIn, data: privateFieldIn.PrivateName),
            ThrowReferenceErrorExpressionOp throwReferenceError => new PackedExpressionOp(ExpressionOpKind.ThrowReferenceError, data: throwReferenceError.Message),
            PopExpressionOp => new PackedExpressionOp(ExpressionOpKind.Pop),
            JumpExpressionOp jump => new PackedExpressionOp(ExpressionOpKind.Jump, int0: jump.Target),
            JumpIfNullishExpressionOp jumpIfNullish => new PackedExpressionOp(
                ExpressionOpKind.JumpIfNullish,
                int0: jumpIfNullish.Target,
                flags: jumpIfNullish.ReplaceWithUndefined ? Flag1 : (byte)0),
            JumpIfShortCircuitedExpressionOp jumpIfShortCircuited => new PackedExpressionOp(ExpressionOpKind.JumpIfShortCircuited, int0: jumpIfShortCircuited.Target),
            JumpIfTrueExpressionOp jumpIfTrue => new PackedExpressionOp(ExpressionOpKind.JumpIfTrue, int0: jumpIfTrue.Target),
            JumpIfFalseExpressionOp jumpIfFalse => new PackedExpressionOp(ExpressionOpKind.JumpIfFalse, int0: jumpIfFalse.Target),
            JumpIfNotNullishExpressionOp jumpIfNotNullish => new PackedExpressionOp(ExpressionOpKind.JumpIfNotNullish, int0: jumpIfNotNullish.Target),
            SuperConstructExpressionOp superConstruct => new PackedExpressionOp(
                ExpressionOpKind.SuperConstruct,
                data: superConstruct.SpreadMask.IsDefaultOrEmpty ? null : new SpreadMaskPayload(superConstruct.SpreadMask),
                int0: superConstruct.ArgumentCount),
            CallExpressionOp call => new PackedExpressionOp(
                ExpressionOpKind.Call,
                data: call.SpreadMask.IsDefaultOrEmpty ? null : new SpreadMaskPayload(call.SpreadMask),
                int0: call.ArgumentCount,
                flags: (byte)((call.HasExplicitThis ? Flag0 : 0) |
                              (call.IsDirectEval ? Flag1 : 0))),
            ConstructExpressionOp construct => new PackedExpressionOp(
                ExpressionOpKind.Construct,
                data: construct.SpreadMask.IsDefaultOrEmpty ? null : new SpreadMaskPayload(construct.SpreadMask),
                int0: construct.ArgumentCount),
            _ => throw new NotSupportedException($"Unsupported expression op '{operation.GetType().Name}'.")
        };
    }

    public ExpressionOp ToLegacyExpressionOp()
    {
        return Kind switch
        {
            ExpressionOpKind.LoadLiteral => new LoadLiteralExpressionOp(LiteralValue),
            ExpressionOpKind.LoadRegexLiteral => new LoadRegexLiteralExpressionOp(Pattern, RegexFlags),
            ExpressionOpKind.LoadFunctionLiteral => new LoadFunctionLiteralExpressionOp(Function, IsConstructorFunction),
            ExpressionOpKind.LoadClassLiteral => new LoadClassLiteralExpressionOp(Class),
            ExpressionOpKind.LoadTemplateObject => new LoadTemplateObjectExpressionOp(TemplateDescriptor),
            ExpressionOpKind.LoadIdentifier => new LoadIdentifierExpressionOp(Name, ScopeId, SlotIndex, FlatSlotId, IsArguments),
            ExpressionOpKind.StoreIdentifier => new StoreIdentifierExpressionOp(Name, ScopeId, SlotIndex, FlatSlotId, AllowNameInference),
            ExpressionOpKind.ApplyBindingTarget => new ApplyBindingTargetExpressionOp(TargetProgram),
            ExpressionOpKind.DuplicateTop => new DuplicateTopExpressionOp(),
            ExpressionOpKind.DuplicateTopTwo => new DuplicateTopTwoExpressionOp(),
            ExpressionOpKind.SwapTopTwo => new SwapTopTwoExpressionOp(),
            ExpressionOpKind.RotateTopThreeRight => new RotateTopThreeRightExpressionOp(),
            ExpressionOpKind.LoadThis => new LoadThisExpressionOp(),
            ExpressionOpKind.LoadNewTarget => new LoadNewTargetExpressionOp(),
            ExpressionOpKind.LoadNamedCallTarget => new LoadNamedCallTargetExpressionOp(Text),
            ExpressionOpKind.LoadComputedCallTarget => new LoadComputedCallTargetExpressionOp(),
            ExpressionOpKind.LoadNamedSuperCallTarget => new LoadNamedSuperCallTargetExpressionOp(Text),
            ExpressionOpKind.LoadComputedSuperCallTarget => new LoadComputedSuperCallTargetExpressionOp(),
            ExpressionOpKind.EnsureSuperReference => new EnsureSuperReferenceExpressionOp(),
            ExpressionOpKind.CreateArray => new CreateArrayExpressionOp(),
            ExpressionOpKind.ArrayPush => new ArrayPushExpressionOp(),
            ExpressionOpKind.ArrayPushHole => new ArrayPushHoleExpressionOp(),
            ExpressionOpKind.ArraySpread => new ArraySpreadExpressionOp(),
            ExpressionOpKind.CreateObject => new CreateObjectExpressionOp(),
            ExpressionOpKind.RequireObjectCoercible => new RequireObjectCoercibleExpressionOp(Depth),
            ExpressionOpKind.ResolvePropertyKey => new ResolvePropertyKeyExpressionOp(),
            ExpressionOpKind.DefineObjectProperty => new DefineObjectPropertyExpressionOp(Text, IsPrototypeMutation, AllowNameInference),
            ExpressionOpKind.DefineComputedObjectProperty => new DefineComputedObjectPropertyExpressionOp(AllowNameInference),
            ExpressionOpKind.DefineObjectMethod => new DefineObjectMethodExpressionOp(Text),
            ExpressionOpKind.DefineComputedObjectMethod => new DefineComputedObjectMethodExpressionOp(),
            ExpressionOpKind.DefineObjectAccessor => new DefineObjectAccessorExpressionOp(Text, AccessorKind),
            ExpressionOpKind.DefineComputedObjectAccessor => new DefineComputedObjectAccessorExpressionOp(AccessorKind),
            ExpressionOpKind.ObjectSpread => new ObjectSpreadExpressionOp(),
            ExpressionOpKind.GetNamedProperty => new GetNamedPropertyExpressionOp(Text, IsOptional, ShortCircuitOnNullishTarget),
            ExpressionOpKind.GetComputedProperty => new GetComputedPropertyExpressionOp(ShortCircuitOnNullishTarget),
            ExpressionOpKind.GetNamedSuperProperty => new GetNamedSuperPropertyExpressionOp(Text),
            ExpressionOpKind.GetComputedSuperProperty => new GetComputedSuperPropertyExpressionOp(),
            ExpressionOpKind.SetNamedProperty => new SetNamedPropertyExpressionOp(Text, AllowNameInference),
            ExpressionOpKind.SetComputedProperty => new SetComputedPropertyExpressionOp(AllowNameInference),
            ExpressionOpKind.SetNamedSuperProperty => new SetNamedSuperPropertyExpressionOp(Text, AllowNameInference),
            ExpressionOpKind.SetComputedSuperProperty => new SetComputedSuperPropertyExpressionOp(AllowNameInference),
            ExpressionOpKind.UpdateIdentifier => new UpdateIdentifierExpressionOp(Name, ScopeId, SlotIndex, FlatSlotId, IsIncrement, IsPrefix, IsArguments),
            ExpressionOpKind.UpdateNamedProperty => new UpdateNamedPropertyExpressionOp(Text, IsIncrement, IsPrefix),
            ExpressionOpKind.UpdateComputedProperty => new UpdateComputedPropertyExpressionOp(IsIncrement, IsPrefix),
            ExpressionOpKind.UpdateNamedSuperProperty => new UpdateNamedSuperPropertyExpressionOp(Text, IsIncrement, IsPrefix),
            ExpressionOpKind.UpdateComputedSuperProperty => new UpdateComputedSuperPropertyExpressionOp(IsIncrement, IsPrefix),
            ExpressionOpKind.TypeOf => new TypeOfExpressionOp(),
            ExpressionOpKind.TypeOfIdentifier => new TypeOfIdentifierExpressionOp(Name, ScopeId, SlotIndex, FlatSlotId, IsArguments),
            ExpressionOpKind.DeleteIdentifier => new DeleteIdentifierExpressionOp(Name),
            ExpressionOpKind.DeleteNamedProperty => new DeleteNamedPropertyExpressionOp(Text),
            ExpressionOpKind.DeleteComputedProperty => new DeleteComputedPropertyExpressionOp(),
            ExpressionOpKind.UnaryPlus => new UnaryPlusExpressionOp(),
            ExpressionOpKind.UnaryMinus => new UnaryMinusExpressionOp(),
            ExpressionOpKind.UnaryBitwiseNot => new UnaryBitwiseNotExpressionOp(),
            ExpressionOpKind.UnaryVoid => new UnaryVoidExpressionOp(),
            ExpressionOpKind.ToString => new ToStringExpressionOp(),
            ExpressionOpKind.UnaryLogicalNot => new UnaryLogicalNotExpressionOp(),
            ExpressionOpKind.Binary => new BinaryExpressionOp(Operator),
            ExpressionOpKind.PrivateFieldIn => new PrivateFieldInExpressionOp(Text),
            ExpressionOpKind.ThrowReferenceError => new ThrowReferenceErrorExpressionOp(Text),
            ExpressionOpKind.Pop => new PopExpressionOp(),
            ExpressionOpKind.Jump => new JumpExpressionOp(Target),
            ExpressionOpKind.JumpIfNullish => new JumpIfNullishExpressionOp(Target, ReplaceWithUndefined),
            ExpressionOpKind.JumpIfShortCircuited => new JumpIfShortCircuitedExpressionOp(Target),
            ExpressionOpKind.JumpIfTrue => new JumpIfTrueExpressionOp(Target),
            ExpressionOpKind.JumpIfFalse => new JumpIfFalseExpressionOp(Target),
            ExpressionOpKind.JumpIfNotNullish => new JumpIfNotNullishExpressionOp(Target),
            ExpressionOpKind.SuperConstruct => new SuperConstructExpressionOp(ArgumentCount, SpreadMask),
            ExpressionOpKind.Call => new CallExpressionOp(ArgumentCount, HasExplicitThis, IsDirectEval, SpreadMask),
            ExpressionOpKind.Construct => new ConstructExpressionOp(ArgumentCount, SpreadMask),
            _ => throw new NotSupportedException($"Unsupported packed expression op '{Kind}'.")
        };
    }
}

internal static class ExpressionOps
{
    public static readonly EnsureSuperReferenceExpressionOp EnsureSuperReference = new();
    public static readonly LoadThisExpressionOp LoadThis = new();
    public static readonly LoadNewTargetExpressionOp LoadNewTarget = new();
    public static readonly DuplicateTopExpressionOp DuplicateTop = new();
    public static readonly DuplicateTopTwoExpressionOp DuplicateTopTwo = new();
    public static readonly SwapTopTwoExpressionOp SwapTopTwo = new();
    public static readonly RotateTopThreeRightExpressionOp RotateTopThreeRight = new();
    public static readonly LoadComputedCallTargetExpressionOp LoadComputedCallTarget = new();
    public static readonly LoadComputedSuperCallTargetExpressionOp LoadComputedSuperCallTarget = new();
    public static readonly CreateArrayExpressionOp CreateArray = new();
    public static readonly ArrayPushExpressionOp ArrayPush = new();
    public static readonly ArrayPushHoleExpressionOp ArrayPushHole = new();
    public static readonly ArraySpreadExpressionOp ArraySpread = new();
    public static readonly CreateObjectExpressionOp CreateObject = new();
    public static readonly ResolvePropertyKeyExpressionOp ResolvePropertyKey = new();
    public static readonly ObjectSpreadExpressionOp ObjectSpread = new();
    public static readonly GetComputedSuperPropertyExpressionOp GetComputedSuperProperty = new();
    public static readonly TypeOfExpressionOp TypeOf = new();
    public static readonly DeleteComputedPropertyExpressionOp DeleteComputedProperty = new();
    public static readonly UnaryPlusExpressionOp UnaryPlus = new();
    public static readonly UnaryMinusExpressionOp UnaryMinus = new();
    public static readonly UnaryBitwiseNotExpressionOp UnaryBitwiseNot = new();
    public static readonly UnaryVoidExpressionOp UnaryVoid = new();
    public static readonly ToStringExpressionOp ToString = new();
    public static readonly UnaryLogicalNotExpressionOp UnaryLogicalNot = new();
    public static readonly PopExpressionOp Pop = new();

    private static readonly RequireObjectCoercibleExpressionOp RequireObjectCoercibleDefault = new();
    private static readonly RequireObjectCoercibleExpressionOp RequireObjectCoercibleDepthOne = new(1);
    private static readonly GetComputedPropertyExpressionOp GetComputedPropertyDefault = new();
    private static readonly GetComputedPropertyExpressionOp GetComputedPropertyShortCircuit = new(true);
    private static readonly SetComputedPropertyExpressionOp SetComputedPropertyDefault = new();
    private static readonly SetComputedPropertyExpressionOp SetComputedPropertyNoInference = new(false);
    private static readonly SetComputedSuperPropertyExpressionOp SetComputedSuperPropertyDefault = new();
    private static readonly SetComputedSuperPropertyExpressionOp SetComputedSuperPropertyNoInference = new(false);

    public static RequireObjectCoercibleExpressionOp RequireObjectCoercible(int depth = 0)
    {
        return depth switch
        {
            0 => RequireObjectCoercibleDefault,
            1 => RequireObjectCoercibleDepthOne,
            _ => new RequireObjectCoercibleExpressionOp(depth)
        };
    }

    public static GetComputedPropertyExpressionOp GetComputedProperty(bool shortCircuitOnNullishTarget = false)
    {
        return shortCircuitOnNullishTarget
            ? GetComputedPropertyShortCircuit
            : GetComputedPropertyDefault;
    }

    public static SetComputedPropertyExpressionOp SetComputedProperty(bool allowNameInference = true)
    {
        return allowNameInference
            ? SetComputedPropertyDefault
            : SetComputedPropertyNoInference;
    }

    public static SetComputedSuperPropertyExpressionOp SetComputedSuperProperty(bool allowNameInference = true)
    {
        return allowNameInference
            ? SetComputedSuperPropertyDefault
            : SetComputedSuperPropertyNoInference;
    }
}

internal sealed record LoadLiteralExpressionOp(JsValue Value)
    : ExpressionOp(ExpressionOpKind.LoadLiteral);

internal sealed record LoadRegexLiteralExpressionOp(string Pattern, string Flags)
    : ExpressionOp(ExpressionOpKind.LoadRegexLiteral);

internal sealed record LoadFunctionLiteralExpressionOp(
    FunctionExpression Function,
    bool IsConstructorFunction = true)
    : ExpressionOp(ExpressionOpKind.LoadFunctionLiteral);

internal sealed record LoadClassLiteralExpressionOp(ClassExpression Class)
    : ExpressionOp(ExpressionOpKind.LoadClassLiteral);

internal sealed record LoadTemplateObjectExpressionOp(TaggedTemplateDescriptor Descriptor)
    : ExpressionOp(ExpressionOpKind.LoadTemplateObject);

internal sealed record LoadIdentifierExpressionOp(
    Symbol Name,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1,
    bool IsArguments = false)
    : ExpressionOp(ExpressionOpKind.LoadIdentifier);

internal sealed record StoreIdentifierExpressionOp(
    Symbol Name,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1,
    bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.StoreIdentifier);

internal sealed record ApplyBindingTargetExpressionOp(BindingTargetProgram TargetProgram)
    : ExpressionOp(ExpressionOpKind.ApplyBindingTarget);

internal sealed record DuplicateTopExpressionOp()
    : ExpressionOp(ExpressionOpKind.DuplicateTop);

internal sealed record DuplicateTopTwoExpressionOp()
    : ExpressionOp(ExpressionOpKind.DuplicateTopTwo);

internal sealed record SwapTopTwoExpressionOp()
    : ExpressionOp(ExpressionOpKind.SwapTopTwo);

internal sealed record RotateTopThreeRightExpressionOp()
    : ExpressionOp(ExpressionOpKind.RotateTopThreeRight);

internal sealed record LoadThisExpressionOp()
    : ExpressionOp(ExpressionOpKind.LoadThis);

internal sealed record LoadNewTargetExpressionOp()
    : ExpressionOp(ExpressionOpKind.LoadNewTarget);

internal sealed record LoadNamedCallTargetExpressionOp(string PropertyName)
    : ExpressionOp(ExpressionOpKind.LoadNamedCallTarget);

internal sealed record LoadComputedCallTargetExpressionOp()
    : ExpressionOp(ExpressionOpKind.LoadComputedCallTarget);

internal sealed record LoadNamedSuperCallTargetExpressionOp(string PropertyName)
    : ExpressionOp(ExpressionOpKind.LoadNamedSuperCallTarget);

internal sealed record LoadComputedSuperCallTargetExpressionOp()
    : ExpressionOp(ExpressionOpKind.LoadComputedSuperCallTarget);

internal sealed record EnsureSuperReferenceExpressionOp()
    : ExpressionOp(ExpressionOpKind.EnsureSuperReference);

internal sealed record CreateArrayExpressionOp()
    : ExpressionOp(ExpressionOpKind.CreateArray);

internal sealed record ArrayPushExpressionOp()
    : ExpressionOp(ExpressionOpKind.ArrayPush);

internal sealed record ArrayPushHoleExpressionOp()
    : ExpressionOp(ExpressionOpKind.ArrayPushHole);

internal sealed record ArraySpreadExpressionOp()
    : ExpressionOp(ExpressionOpKind.ArraySpread);

internal sealed record CreateObjectExpressionOp()
    : ExpressionOp(ExpressionOpKind.CreateObject);

/// <summary>
/// Checks that the value at [stackIndex - 1 - Depth] is not null/undefined.
/// Throws TypeError if it is. Per ES spec, RequireObjectCoercible must be called
/// before ToPropertyKey in compound assignment (13.15.2 step 1.e).
/// </summary>
internal sealed record RequireObjectCoercibleExpressionOp(int Depth = 0)
    : ExpressionOp(ExpressionOpKind.RequireObjectCoercible);

internal sealed record ResolvePropertyKeyExpressionOp()
    : ExpressionOp(ExpressionOpKind.ResolvePropertyKey);

internal sealed record DefineObjectPropertyExpressionOp(
    string PropertyName,
    bool IsPrototypeMutation = false,
    bool AllowNameInference = false)
    : ExpressionOp(ExpressionOpKind.DefineObjectProperty);

internal sealed record DefineComputedObjectPropertyExpressionOp(bool AllowNameInference = false)
    : ExpressionOp(ExpressionOpKind.DefineComputedObjectProperty);

internal sealed record DefineObjectMethodExpressionOp(string PropertyName)
    : ExpressionOp(ExpressionOpKind.DefineObjectMethod);

internal sealed record DefineComputedObjectMethodExpressionOp()
    : ExpressionOp(ExpressionOpKind.DefineComputedObjectMethod);

internal sealed record DefineObjectAccessorExpressionOp(string PropertyName, ObjectAccessorKind AccessorKind)
    : ExpressionOp(ExpressionOpKind.DefineObjectAccessor);

internal sealed record DefineComputedObjectAccessorExpressionOp(ObjectAccessorKind AccessorKind)
    : ExpressionOp(ExpressionOpKind.DefineComputedObjectAccessor);

internal sealed record ObjectSpreadExpressionOp()
    : ExpressionOp(ExpressionOpKind.ObjectSpread);

internal sealed record GetNamedPropertyExpressionOp(
    string PropertyName,
    bool IsOptional = false,
    bool ShortCircuitOnNullishTarget = false)
    : ExpressionOp(ExpressionOpKind.GetNamedProperty);

internal sealed record GetComputedPropertyExpressionOp(bool ShortCircuitOnNullishTarget = false)
    : ExpressionOp(ExpressionOpKind.GetComputedProperty);

internal sealed record GetNamedSuperPropertyExpressionOp(string PropertyName)
    : ExpressionOp(ExpressionOpKind.GetNamedSuperProperty);

internal sealed record GetComputedSuperPropertyExpressionOp()
    : ExpressionOp(ExpressionOpKind.GetComputedSuperProperty);

internal sealed record SetNamedPropertyExpressionOp(string PropertyName, bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.SetNamedProperty);

internal sealed record SetComputedPropertyExpressionOp(bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.SetComputedProperty);

internal sealed record SetNamedSuperPropertyExpressionOp(string PropertyName, bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.SetNamedSuperProperty);

internal sealed record SetComputedSuperPropertyExpressionOp(bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.SetComputedSuperProperty);

internal sealed record UpdateIdentifierExpressionOp(
    Symbol Name,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1,
    bool IsIncrement = true,
    bool IsPrefix = true,
    bool IsArguments = false)
    : ExpressionOp(ExpressionOpKind.UpdateIdentifier);

internal sealed record UpdateNamedPropertyExpressionOp(
    string PropertyName,
    bool IsIncrement = true,
    bool IsPrefix = true)
    : ExpressionOp(ExpressionOpKind.UpdateNamedProperty);

internal sealed record UpdateComputedPropertyExpressionOp(
    bool IsIncrement = true,
    bool IsPrefix = true)
    : ExpressionOp(ExpressionOpKind.UpdateComputedProperty);

internal sealed record UpdateNamedSuperPropertyExpressionOp(
    string PropertyName,
    bool IsIncrement = true,
    bool IsPrefix = true)
    : ExpressionOp(ExpressionOpKind.UpdateNamedSuperProperty);

internal sealed record UpdateComputedSuperPropertyExpressionOp(
    bool IsIncrement = true,
    bool IsPrefix = true)
    : ExpressionOp(ExpressionOpKind.UpdateComputedSuperProperty);

internal sealed record TypeOfExpressionOp()
    : ExpressionOp(ExpressionOpKind.TypeOf);

internal sealed record TypeOfIdentifierExpressionOp(
    Symbol Name,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1,
    bool IsArguments = false)
    : ExpressionOp(ExpressionOpKind.TypeOfIdentifier);

internal sealed record DeleteIdentifierExpressionOp(Symbol Name)
    : ExpressionOp(ExpressionOpKind.DeleteIdentifier);

internal sealed record DeleteNamedPropertyExpressionOp(string PropertyName)
    : ExpressionOp(ExpressionOpKind.DeleteNamedProperty);

internal sealed record DeleteComputedPropertyExpressionOp()
    : ExpressionOp(ExpressionOpKind.DeleteComputedProperty);

internal sealed record UnaryPlusExpressionOp()
    : ExpressionOp(ExpressionOpKind.UnaryPlus);

internal sealed record UnaryMinusExpressionOp()
    : ExpressionOp(ExpressionOpKind.UnaryMinus);

internal sealed record UnaryBitwiseNotExpressionOp()
    : ExpressionOp(ExpressionOpKind.UnaryBitwiseNot);

internal sealed record UnaryVoidExpressionOp()
    : ExpressionOp(ExpressionOpKind.UnaryVoid);

internal sealed record ToStringExpressionOp()
    : ExpressionOp(ExpressionOpKind.ToString);

internal sealed record UnaryLogicalNotExpressionOp()
    : ExpressionOp(ExpressionOpKind.UnaryLogicalNot);

internal sealed record BinaryExpressionOp(BinaryOperator Operator)
    : ExpressionOp(ExpressionOpKind.Binary);

internal sealed record PrivateFieldInExpressionOp(string PrivateName)
    : ExpressionOp(ExpressionOpKind.PrivateFieldIn);

internal sealed record ThrowReferenceErrorExpressionOp(string Message)
    : ExpressionOp(ExpressionOpKind.ThrowReferenceError);

internal sealed record PopExpressionOp()
    : ExpressionOp(ExpressionOpKind.Pop);

internal sealed record JumpExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.Jump);

internal sealed record JumpIfNullishExpressionOp(int Target, bool ReplaceWithUndefined = false)
    : ExpressionOp(ExpressionOpKind.JumpIfNullish);

internal sealed record JumpIfShortCircuitedExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfShortCircuited);

internal sealed record JumpIfTrueExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfTrue);

internal sealed record JumpIfFalseExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfFalse);

internal sealed record JumpIfNotNullishExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfNotNullish);

internal sealed record SuperConstructExpressionOp(
    int ArgumentCount,
    ImmutableArray<bool> SpreadMask = default)
    : ExpressionOp(ExpressionOpKind.SuperConstruct);

internal sealed record CallExpressionOp(
    int ArgumentCount,
    bool HasExplicitThis = false,
    bool IsDirectEval = false,
    ImmutableArray<bool> SpreadMask = default)
    : ExpressionOp(ExpressionOpKind.Call);

internal sealed record ConstructExpressionOp(
    int ArgumentCount,
    ImmutableArray<bool> SpreadMask = default)
    : ExpressionOp(ExpressionOpKind.Construct);
