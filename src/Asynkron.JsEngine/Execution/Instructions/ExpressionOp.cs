using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution.Instructions;

internal enum ExpressionOpKind : byte
{
    LoadLiteral,
    LoadIdentifier,
    StoreIdentifier,
    DuplicateTop,
    DuplicateTopTwo,
    SwapTopTwo,
    RotateTopThreeRight,
    LoadThis,
    LoadNewTarget,
    LoadNamedCallTarget,
    LoadComputedCallTarget,
    CreateArray,
    ArrayPush,
    ArrayPushHole,
    ArraySpread,
    CreateObject,
    DefineObjectProperty,
    DefineComputedObjectProperty,
    ObjectSpread,
    GetNamedProperty,
    GetComputedProperty,
    SetNamedProperty,
    SetComputedProperty,
    UpdateIdentifier,
    ToString,
    UnaryLogicalNot,
    Binary,
    Pop,
    Jump,
    JumpIfNullish,
    JumpIfTrue,
    JumpIfFalse,
    JumpIfNotNullish,
    Call,
    Construct
}

internal readonly record struct ExpressionProgram(ImmutableArray<ExpressionOp> Operations)
{
    public static ExpressionProgram Empty { get; } = new([]);

    public bool IsEmpty => Operations.IsDefaultOrEmpty || Operations.Length == 0;

    public override string ToString() => $"{Operations.Length} ops";
}

internal abstract record ExpressionOp(ExpressionOpKind Kind);

internal sealed record LoadLiteralExpressionOp(JsValue Value)
    : ExpressionOp(ExpressionOpKind.LoadLiteral);

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

internal sealed record DefineObjectPropertyExpressionOp(string PropertyName, bool IsPrototypeMutation = false)
    : ExpressionOp(ExpressionOpKind.DefineObjectProperty);

internal sealed record DefineComputedObjectPropertyExpressionOp()
    : ExpressionOp(ExpressionOpKind.DefineComputedObjectProperty);

internal sealed record ObjectSpreadExpressionOp()
    : ExpressionOp(ExpressionOpKind.ObjectSpread);

internal sealed record GetNamedPropertyExpressionOp(
    string PropertyName,
    bool IsOptional = false,
    bool ShortCircuitOnNullishTarget = false)
    : ExpressionOp(ExpressionOpKind.GetNamedProperty);

internal sealed record GetComputedPropertyExpressionOp(bool ShortCircuitOnNullishTarget = false)
    : ExpressionOp(ExpressionOpKind.GetComputedProperty);

internal sealed record SetNamedPropertyExpressionOp(string PropertyName, bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.SetNamedProperty);

internal sealed record SetComputedPropertyExpressionOp(bool AllowNameInference = true)
    : ExpressionOp(ExpressionOpKind.SetComputedProperty);

internal sealed record UpdateIdentifierExpressionOp(
    Symbol Name,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1,
    bool IsIncrement = true,
    bool IsPrefix = true,
    bool IsArguments = false)
    : ExpressionOp(ExpressionOpKind.UpdateIdentifier);

internal sealed record ToStringExpressionOp()
    : ExpressionOp(ExpressionOpKind.ToString);

internal sealed record UnaryLogicalNotExpressionOp()
    : ExpressionOp(ExpressionOpKind.UnaryLogicalNot);

internal sealed record BinaryExpressionOp(BinaryOperator Operator)
    : ExpressionOp(ExpressionOpKind.Binary);

internal sealed record PopExpressionOp()
    : ExpressionOp(ExpressionOpKind.Pop);

internal sealed record JumpExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.Jump);

internal sealed record JumpIfNullishExpressionOp(int Target, bool ReplaceWithUndefined = false)
    : ExpressionOp(ExpressionOpKind.JumpIfNullish);

internal sealed record JumpIfTrueExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfTrue);

internal sealed record JumpIfFalseExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfFalse);

internal sealed record JumpIfNotNullishExpressionOp(int Target)
    : ExpressionOp(ExpressionOpKind.JumpIfNotNullish);

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
