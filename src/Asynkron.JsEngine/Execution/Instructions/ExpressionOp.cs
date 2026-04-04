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
        Operations = operations;
        MaxStackDepth = ComputeMaxStackDepth(operations);
    }

    public static ExpressionProgram Empty { get; } = new(ImmutableArray<ExpressionOp>.Empty);

    public ImmutableArray<ExpressionOp> Operations { get; init; }

    public int MaxStackDepth { get; init; }

    public bool IsEmpty => Operations.IsDefaultOrEmpty || Operations.Length == 0;

    public override string ToString() => $"{Operations.Length} ops, stack {MaxStackDepth}";

    private static int ComputeMaxStackDepth(ImmutableArray<ExpressionOp> operations)
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

    private static int GetStackDelta(ExpressionOp operation)
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
            ExpressionOpKind.SuperConstruct => 1 - ((SuperConstructExpressionOp)operation).ArgumentCount,
            ExpressionOpKind.Call => -(((CallExpressionOp)operation).ArgumentCount + (((CallExpressionOp)operation).HasExplicitThis ? 1 : 0)),
            ExpressionOpKind.Construct => -((ConstructExpressionOp)operation).ArgumentCount,
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
