using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Instructions;

internal enum ExpressionOpKind : byte
{
    LoadLiteral,
    LoadRegexLiteral,
    LoadFunctionLiteral,
    LoadClassLiteral,
    LoadIdentifier,
    LoadIdentifierCallTarget,
    ResolveIdentifierReference,
    LoadResolvedIdentifierValue,
    PopResolvedIdentifierReference,
    StoreResolvedIdentifier,
    LoadTemplateObject,
    StoreIdentifier,
    ApplyBindingTarget,
    DuplicateTop,
    DuplicateTopTwo,
    SwapTopTwo,
    RotateTopThreeRight,
    LoadThis,
    LoadNewTarget,
    LoadImportMeta,
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
    public ExpressionProgram(
        ImmutableArray<PackedExpressionOp> operations,
        ImmutableArray<JsValue> literalConstants = default,
        ImmutableArray<string> stringConstants = default,
        ImmutableArray<object> objectConstants = default,
        ImmutableArray<IdentifierOperand> identifierConstants = default,
        ImmutableArray<ImmutableArray<int>> spreadMaskConstants = default)
    {
        Operations = operations;
        LiteralConstants = literalConstants.IsDefault ? ImmutableArray<JsValue>.Empty : literalConstants;
        StringConstants = stringConstants.IsDefault ? ImmutableArray<string>.Empty : stringConstants;
        ObjectConstants = objectConstants.IsDefault ? ImmutableArray<object>.Empty : objectConstants;
        IdentifierConstants = identifierConstants.IsDefault ? ImmutableArray<IdentifierOperand>.Empty : identifierConstants;
        SpreadMaskConstants = spreadMaskConstants.IsDefault ? ImmutableArray<ImmutableArray<int>>.Empty : spreadMaskConstants;
        MaxStackDepth = ComputeMaxStackDepth(operations);
    }

    public static ExpressionProgram Empty { get; } = new(ImmutableArray<PackedExpressionOp>.Empty);

    public ImmutableArray<PackedExpressionOp> Operations { get; init; }

    public ImmutableArray<JsValue> LiteralConstants { get; init; }

    public ImmutableArray<string> StringConstants { get; init; }

    public ImmutableArray<object> ObjectConstants { get; init; }

    public ImmutableArray<IdentifierOperand> IdentifierConstants { get; init; }

    public ImmutableArray<ImmutableArray<int>> SpreadMaskConstants { get; init; }

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
            ExpressionOpKind.LoadIdentifierCallTarget => 2,
            ExpressionOpKind.ResolveIdentifierReference => 0,
            ExpressionOpKind.LoadResolvedIdentifierValue => 1,
            ExpressionOpKind.PopResolvedIdentifierReference => 0,
            ExpressionOpKind.StoreResolvedIdentifier => 0,
            ExpressionOpKind.LoadTemplateObject => 1,
            ExpressionOpKind.StoreIdentifier => 0,
            ExpressionOpKind.ApplyBindingTarget => -1,
            ExpressionOpKind.DuplicateTop => 1,
            ExpressionOpKind.DuplicateTopTwo => 2,
            ExpressionOpKind.SwapTopTwo => 0,
            ExpressionOpKind.RotateTopThreeRight => 0,
            ExpressionOpKind.LoadThis => 1,
            ExpressionOpKind.LoadNewTarget => 1,
            ExpressionOpKind.LoadImportMeta => 1,
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

internal readonly record struct IdentifierOperand(
    Symbol Name,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1);

internal readonly struct PackedExpressionOp
{
    private const byte Flag0 = 1 << 0;
    private const byte Flag1 = 1 << 1;
    private const byte Flag2 = 1 << 2;
    private const int FlagShift = 8;

    public static readonly PackedExpressionOp EnsureSuperReference = new(ExpressionOpKind.EnsureSuperReference);
    public static readonly PackedExpressionOp LoadThis = new(ExpressionOpKind.LoadThis);
    public static readonly PackedExpressionOp LoadNewTarget = new(ExpressionOpKind.LoadNewTarget);
    public static readonly PackedExpressionOp LoadImportMeta = new(ExpressionOpKind.LoadImportMeta);
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

    private readonly ushort _opcode;
    private readonly int _int0;
    private readonly int _int1;

    private PackedExpressionOp(
        ExpressionOpKind kind,
        int int0 = 0,
        int int1 = 0,
        byte flags = 0)
    {
        _opcode = (ushort)((ushort)kind | (flags << FlagShift));
        _int0 = int0;
        _int1 = int1;
    }

    public ExpressionOpKind Kind => (ExpressionOpKind)(_opcode & 0xFF);

    private byte Flags => (byte)(_opcode >> FlagShift);

    public JsValue GetLiteral(ReadOnlySpan<JsValue> literalConstants)
    {
        return literalConstants[_int0];
    }

    public int StringConstantIndex => _int0;

    public string RegexFlags => JsRegExp.DecodeFlags(Flags);

    public byte EncodedRegexFlags => Flags;

    public int Depth => _int0;

    public int Target => _int0;

    public int ArgumentCount => _int0;

    public ObjectAccessorKind AccessorKind => (Flags & Flag0) != 0
        ? ObjectAccessorKind.Setter
        : ObjectAccessorKind.Getter;

    public BinaryOperator Operator => (BinaryOperator)_int0;

    public bool IsArguments => Kind == ExpressionOpKind.UpdateIdentifier
        ? (Flags & Flag2) != 0
        : (Flags & Flag0) != 0;

    public bool AllowNameInference => Kind == ExpressionOpKind.DefineObjectProperty
        ? (Flags & Flag1) != 0
        : (Flags & Flag0) != 0;

    public bool IsOptional => (Flags & Flag0) != 0;

    public bool IsIncrement => (Flags & Flag0) != 0;

    public bool HasExplicitThis => (Flags & Flag0) != 0;

    public bool IsPrototypeMutation => (Flags & Flag0) != 0;

    public bool IsConstructorFunction => (Flags & Flag0) != 0;

    public bool ShortCircuitOnNullishTarget => (Flags & Flag1) != 0;

    public bool IsPrefix => (Flags & Flag1) != 0;

    public bool IsDirectEval => (Flags & Flag1) != 0;

    public bool ReplaceWithUndefined => (Flags & Flag1) != 0;

    public int SpreadMaskConstantIndex => _int1 - 1;

    public string GetString(ReadOnlySpan<string> stringConstants)
    {
        return stringConstants[_int0];
    }

    public T GetObject<T>(ReadOnlySpan<object> objectConstants)
        where T : class
    {
        return (T)objectConstants[_int1];
    }

    public IdentifierOperand GetIdentifier(ReadOnlySpan<IdentifierOperand> identifierConstants)
    {
        return identifierConstants[_int0];
    }

    public ImmutableArray<int> GetSpreadIndices(ReadOnlySpan<ImmutableArray<int>> spreadMaskConstants)
    {
        return SpreadMaskConstantIndex < 0
            ? default
            : spreadMaskConstants[SpreadMaskConstantIndex];
    }

    public static PackedExpressionOp LoadLiteralConstant(int literalConstantIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadLiteral, int0: literalConstantIndex);
    }

    public static PackedExpressionOp LoadRegexLiteral(int PatternIndex, string Flags)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadRegexLiteral,
            int0: PatternIndex,
            flags: JsRegExp.EncodeFlags(Flags));
    }

    public static PackedExpressionOp LoadFunctionLiteral(
        int functionIndex,
        bool IsConstructorFunction = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadFunctionLiteral,
            int1: functionIndex,
            flags: IsConstructorFunction ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp LoadClassLiteral(int classIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadClassLiteral, int1: classIndex);
    }

    public static PackedExpressionOp LoadIdentifier(
        int identifierConstantIndex,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadIdentifier,
            int0: identifierConstantIndex,
            flags: IsArguments ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp LoadIdentifierCallTarget(
        int identifierConstantIndex,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.LoadIdentifierCallTarget,
            int0: identifierConstantIndex,
            flags: IsArguments ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp ResolveIdentifierReference(int identifierConstantIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.ResolveIdentifierReference, int0: identifierConstantIndex);
    }

    public static readonly PackedExpressionOp LoadResolvedIdentifierValue =
        new(ExpressionOpKind.LoadResolvedIdentifierValue);

    public static readonly PackedExpressionOp PopResolvedIdentifierReference =
        new(ExpressionOpKind.PopResolvedIdentifierReference);

    public static PackedExpressionOp LoadTemplateObject(int descriptorIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadTemplateObject, int1: descriptorIndex);
    }

    public static PackedExpressionOp StoreResolvedIdentifier(
        int identifierConstantIndex,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.StoreResolvedIdentifier,
            int0: identifierConstantIndex,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp StoreIdentifier(
        int identifierConstantIndex,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.StoreIdentifier,
            int0: identifierConstantIndex,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp ApplyBindingTarget(int targetProgramIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.ApplyBindingTarget, int1: targetProgramIndex);
    }

    public static PackedExpressionOp LoadNamedCallTarget(int propertyNameIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadNamedCallTarget, int0: propertyNameIndex);
    }

    public static PackedExpressionOp LoadNamedSuperCallTarget(int propertyNameIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.LoadNamedSuperCallTarget, int0: propertyNameIndex);
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
        int propertyNameIndex,
        bool IsPrototypeMutation = false,
        bool AllowNameInference = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineObjectProperty,
            int0: propertyNameIndex,
            flags: (byte)((IsPrototypeMutation ? Flag0 : 0) |
                          (AllowNameInference ? Flag1 : 0)));
    }

    public static PackedExpressionOp DefineComputedObjectProperty(bool AllowNameInference = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineComputedObjectProperty,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp DefineObjectMethod(int propertyNameIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.DefineObjectMethod, int0: propertyNameIndex);
    }

    public static PackedExpressionOp DefineObjectAccessor(int propertyNameIndex, ObjectAccessorKind AccessorKind)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineObjectAccessor,
            int0: propertyNameIndex,
            flags: AccessorKind == ObjectAccessorKind.Setter ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp DefineComputedObjectAccessor(ObjectAccessorKind AccessorKind)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.DefineComputedObjectAccessor,
            flags: AccessorKind == ObjectAccessorKind.Setter ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp GetNamedProperty(
        int propertyNameIndex,
        bool IsOptional = false,
        bool ShortCircuitOnNullishTarget = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.GetNamedProperty,
            int0: propertyNameIndex,
            flags: (byte)((IsOptional ? Flag0 : 0) |
                          (ShortCircuitOnNullishTarget ? Flag1 : 0)));
    }

    public static PackedExpressionOp GetComputedProperty(bool ShortCircuitOnNullishTarget = false)
    {
        return ShortCircuitOnNullishTarget
            ? GetComputedPropertyShortCircuit
            : GetComputedPropertyDefault;
    }

    public static PackedExpressionOp GetNamedSuperProperty(int propertyNameIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.GetNamedSuperProperty, int0: propertyNameIndex);
    }

    public static PackedExpressionOp SetNamedProperty(
        int propertyNameIndex,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.SetNamedProperty,
            int0: propertyNameIndex,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp SetComputedProperty(bool AllowNameInference = true)
    {
        return AllowNameInference
            ? SetComputedPropertyDefault
            : SetComputedPropertyNoInference;
    }

    public static PackedExpressionOp SetNamedSuperProperty(
        int propertyNameIndex,
        bool AllowNameInference = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.SetNamedSuperProperty,
            int0: propertyNameIndex,
            flags: AllowNameInference ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp SetComputedSuperProperty(bool AllowNameInference = true)
    {
        return AllowNameInference
            ? SetComputedSuperPropertyDefault
            : SetComputedSuperPropertyNoInference;
    }

    public static PackedExpressionOp UpdateIdentifier(
        int identifierConstantIndex,
        bool IsIncrement = true,
        bool IsPrefix = true,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateIdentifier,
            int0: identifierConstantIndex,
            flags: (byte)((IsIncrement ? Flag0 : 0) |
                          (IsPrefix ? Flag1 : 0) |
                          (IsArguments ? Flag2 : 0)));
    }

    public static PackedExpressionOp UpdateNamedProperty(
        int propertyNameIndex,
        bool IsIncrement = true,
        bool IsPrefix = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateNamedProperty,
            int0: propertyNameIndex,
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
        int propertyNameIndex,
        bool IsIncrement = true,
        bool IsPrefix = true)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.UpdateNamedSuperProperty,
            int0: propertyNameIndex,
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
        int identifierConstantIndex,
        bool IsArguments = false)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.TypeOfIdentifier,
            int0: identifierConstantIndex,
            flags: IsArguments ? Flag0 : (byte)0);
    }

    public static PackedExpressionOp DeleteIdentifier(int identifierConstantIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.DeleteIdentifier, int0: identifierConstantIndex);
    }

    public static PackedExpressionOp DeleteNamedProperty(int propertyNameIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.DeleteNamedProperty, int0: propertyNameIndex);
    }

    public static PackedExpressionOp Binary(BinaryOperator Operator)
    {
        return new PackedExpressionOp(ExpressionOpKind.Binary, int0: (int)Operator);
    }

    public static PackedExpressionOp PrivateFieldIn(int privateNameIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.PrivateFieldIn, int0: privateNameIndex);
    }

    public static PackedExpressionOp ThrowReferenceError(int messageIndex)
    {
        return new PackedExpressionOp(ExpressionOpKind.ThrowReferenceError, int0: messageIndex);
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
        int SpreadMaskConstantIndex = -1)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.SuperConstruct,
            int0: ArgumentCount,
            int1: SpreadMaskConstantIndex + 1);
    }

    public static PackedExpressionOp Call(
        int ArgumentCount,
        bool HasExplicitThis = false,
        bool IsDirectEval = false,
        int SpreadMaskConstantIndex = -1)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.Call,
            int0: ArgumentCount,
            int1: SpreadMaskConstantIndex + 1,
            flags: (byte)((HasExplicitThis ? Flag0 : 0) |
                          (IsDirectEval ? Flag1 : 0)));
    }

    public static PackedExpressionOp Construct(
        int ArgumentCount,
        int SpreadMaskConstantIndex = -1)
    {
        return new PackedExpressionOp(
            ExpressionOpKind.Construct,
            int0: ArgumentCount,
            int1: SpreadMaskConstantIndex + 1);
    }

    public PackedExpressionOp WithIdentifierConstant(int identifierConstantIndex)
    {
        return new PackedExpressionOp(Kind, identifierConstantIndex, _int1, Flags);
    }

    public PackedExpressionOp WithObjectConstant(int objectConstantIndex)
    {
        return new PackedExpressionOp(Kind, _int0, objectConstantIndex, Flags);
    }
}
