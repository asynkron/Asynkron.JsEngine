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
        return operation switch
        {
            LoadLiteralExpressionOp => 1,
            LoadRegexLiteralExpressionOp => 1,
            LoadFunctionLiteralExpressionOp => 1,
            LoadClassLiteralExpressionOp => 1,
            LoadIdentifierExpressionOp => 1,
            LoadTemplateObjectExpressionOp => 1,
            StoreIdentifierExpressionOp => 0,
            ApplyBindingTargetExpressionOp => -1,
            DuplicateTopExpressionOp => 1,
            DuplicateTopTwoExpressionOp => 2,
            SwapTopTwoExpressionOp => 0,
            RotateTopThreeRightExpressionOp => 0,
            LoadThisExpressionOp => 1,
            LoadNewTargetExpressionOp => 1,
            LoadNamedCallTargetExpressionOp => 1,
            LoadComputedCallTargetExpressionOp => 0,
            LoadNamedSuperCallTargetExpressionOp => 2,
            LoadComputedSuperCallTargetExpressionOp => 1,
            EnsureSuperReferenceExpressionOp => 0,
            CreateArrayExpressionOp => 1,
            ArrayPushExpressionOp => -1,
            ArrayPushHoleExpressionOp => 0,
            ArraySpreadExpressionOp => -1,
            CreateObjectExpressionOp => 1,
            RequireObjectCoercibleExpressionOp => 0,
            ResolvePropertyKeyExpressionOp => 0,
            DefineObjectPropertyExpressionOp => -1,
            DefineComputedObjectPropertyExpressionOp => -2,
            DefineObjectMethodExpressionOp => -1,
            DefineComputedObjectMethodExpressionOp => -2,
            DefineObjectAccessorExpressionOp => -1,
            DefineComputedObjectAccessorExpressionOp => -2,
            ObjectSpreadExpressionOp => -1,
            GetNamedPropertyExpressionOp => 0,
            GetComputedPropertyExpressionOp => -1,
            GetNamedSuperPropertyExpressionOp => 1,
            GetComputedSuperPropertyExpressionOp => 0,
            SetNamedPropertyExpressionOp => -1,
            SetComputedPropertyExpressionOp => -2,
            SetNamedSuperPropertyExpressionOp => 0,
            SetComputedSuperPropertyExpressionOp => -1,
            UpdateIdentifierExpressionOp => 1,
            UpdateNamedPropertyExpressionOp => 0,
            UpdateComputedPropertyExpressionOp => -1,
            UpdateNamedSuperPropertyExpressionOp => 1,
            UpdateComputedSuperPropertyExpressionOp => 0,
            TypeOfExpressionOp => 0,
            TypeOfIdentifierExpressionOp => 1,
            DeleteIdentifierExpressionOp => 1,
            DeleteNamedPropertyExpressionOp => 0,
            DeleteComputedPropertyExpressionOp => -1,
            UnaryPlusExpressionOp => 0,
            UnaryMinusExpressionOp => 0,
            UnaryBitwiseNotExpressionOp => 0,
            UnaryVoidExpressionOp => 0,
            ToStringExpressionOp => 0,
            UnaryLogicalNotExpressionOp => 0,
            BinaryExpressionOp => -1,
            PopExpressionOp => -1,
            JumpExpressionOp => 0,
            JumpIfNullishExpressionOp => 0,
            JumpIfShortCircuitedExpressionOp => 0,
            JumpIfTrueExpressionOp => 0,
            JumpIfFalseExpressionOp => 0,
            JumpIfNotNullishExpressionOp => 0,
            SuperConstructExpressionOp superConstruct => 1 - superConstruct.ArgumentCount,
            CallExpressionOp call => -(call.ArgumentCount + (call.HasExplicitThis ? 1 : 0)),
            ConstructExpressionOp construct => -construct.ArgumentCount,
            PrivateFieldInExpressionOp => 0,
            ThrowReferenceErrorExpressionOp => 0,
            _ => throw new NotSupportedException(
                $"Expression stack analysis does not support '{operation.GetType().Name}'.")
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
