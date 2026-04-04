using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Instructions;

internal static class ExpressionOpTestBridge
{
    public static ExpressionOp ToLegacyExpressionOp(this PackedExpressionOp operation)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => new LoadLiteralExpressionOp(operation.LiteralValue),
            ExpressionOpKind.LoadRegexLiteral => new LoadRegexLiteralExpressionOp(operation.Pattern, operation.RegexFlags),
            ExpressionOpKind.LoadFunctionLiteral => new LoadFunctionLiteralExpressionOp(operation.Function, operation.IsConstructorFunction),
            ExpressionOpKind.LoadClassLiteral => new LoadClassLiteralExpressionOp(operation.Class),
            ExpressionOpKind.LoadTemplateObject => new LoadTemplateObjectExpressionOp(operation.TemplateDescriptor),
            ExpressionOpKind.LoadIdentifier => new LoadIdentifierExpressionOp(operation.Name, operation.ScopeId, operation.SlotIndex, operation.FlatSlotId, operation.IsArguments),
            ExpressionOpKind.StoreIdentifier => new StoreIdentifierExpressionOp(operation.Name, operation.ScopeId, operation.SlotIndex, operation.FlatSlotId, operation.AllowNameInference),
            ExpressionOpKind.ApplyBindingTarget => new ApplyBindingTargetExpressionOp(operation.TargetProgram),
            ExpressionOpKind.DuplicateTop => new DuplicateTopExpressionOp(),
            ExpressionOpKind.DuplicateTopTwo => new DuplicateTopTwoExpressionOp(),
            ExpressionOpKind.SwapTopTwo => new SwapTopTwoExpressionOp(),
            ExpressionOpKind.RotateTopThreeRight => new RotateTopThreeRightExpressionOp(),
            ExpressionOpKind.LoadThis => new LoadThisExpressionOp(),
            ExpressionOpKind.LoadNewTarget => new LoadNewTargetExpressionOp(),
            ExpressionOpKind.LoadNamedCallTarget => new LoadNamedCallTargetExpressionOp(operation.Text),
            ExpressionOpKind.LoadComputedCallTarget => new LoadComputedCallTargetExpressionOp(),
            ExpressionOpKind.LoadNamedSuperCallTarget => new LoadNamedSuperCallTargetExpressionOp(operation.Text),
            ExpressionOpKind.LoadComputedSuperCallTarget => new LoadComputedSuperCallTargetExpressionOp(),
            ExpressionOpKind.EnsureSuperReference => new EnsureSuperReferenceExpressionOp(),
            ExpressionOpKind.CreateArray => new CreateArrayExpressionOp(),
            ExpressionOpKind.ArrayPush => new ArrayPushExpressionOp(),
            ExpressionOpKind.ArrayPushHole => new ArrayPushHoleExpressionOp(),
            ExpressionOpKind.ArraySpread => new ArraySpreadExpressionOp(),
            ExpressionOpKind.CreateObject => new CreateObjectExpressionOp(),
            ExpressionOpKind.RequireObjectCoercible => new RequireObjectCoercibleExpressionOp(operation.Depth),
            ExpressionOpKind.ResolvePropertyKey => new ResolvePropertyKeyExpressionOp(),
            ExpressionOpKind.DefineObjectProperty => new DefineObjectPropertyExpressionOp(operation.Text, operation.IsPrototypeMutation, operation.AllowNameInference),
            ExpressionOpKind.DefineComputedObjectProperty => new DefineComputedObjectPropertyExpressionOp(operation.AllowNameInference),
            ExpressionOpKind.DefineObjectMethod => new DefineObjectMethodExpressionOp(operation.Text),
            ExpressionOpKind.DefineComputedObjectMethod => new DefineComputedObjectMethodExpressionOp(),
            ExpressionOpKind.DefineObjectAccessor => new DefineObjectAccessorExpressionOp(operation.Text, operation.AccessorKind),
            ExpressionOpKind.DefineComputedObjectAccessor => new DefineComputedObjectAccessorExpressionOp(operation.AccessorKind),
            ExpressionOpKind.ObjectSpread => new ObjectSpreadExpressionOp(),
            ExpressionOpKind.GetNamedProperty => new GetNamedPropertyExpressionOp(operation.Text, operation.IsOptional, operation.ShortCircuitOnNullishTarget),
            ExpressionOpKind.GetComputedProperty => new GetComputedPropertyExpressionOp(operation.ShortCircuitOnNullishTarget),
            ExpressionOpKind.GetNamedSuperProperty => new GetNamedSuperPropertyExpressionOp(operation.Text),
            ExpressionOpKind.GetComputedSuperProperty => new GetComputedSuperPropertyExpressionOp(),
            ExpressionOpKind.SetNamedProperty => new SetNamedPropertyExpressionOp(operation.Text, operation.AllowNameInference),
            ExpressionOpKind.SetComputedProperty => new SetComputedPropertyExpressionOp(operation.AllowNameInference),
            ExpressionOpKind.SetNamedSuperProperty => new SetNamedSuperPropertyExpressionOp(operation.Text, operation.AllowNameInference),
            ExpressionOpKind.SetComputedSuperProperty => new SetComputedSuperPropertyExpressionOp(operation.AllowNameInference),
            ExpressionOpKind.UpdateIdentifier => new UpdateIdentifierExpressionOp(operation.Name, operation.ScopeId, operation.SlotIndex, operation.FlatSlotId, operation.IsIncrement, operation.IsPrefix, operation.IsArguments),
            ExpressionOpKind.UpdateNamedProperty => new UpdateNamedPropertyExpressionOp(operation.Text, operation.IsIncrement, operation.IsPrefix),
            ExpressionOpKind.UpdateComputedProperty => new UpdateComputedPropertyExpressionOp(operation.IsIncrement, operation.IsPrefix),
            ExpressionOpKind.UpdateNamedSuperProperty => new UpdateNamedSuperPropertyExpressionOp(operation.Text, operation.IsIncrement, operation.IsPrefix),
            ExpressionOpKind.UpdateComputedSuperProperty => new UpdateComputedSuperPropertyExpressionOp(operation.IsIncrement, operation.IsPrefix),
            ExpressionOpKind.TypeOf => new TypeOfExpressionOp(),
            ExpressionOpKind.TypeOfIdentifier => new TypeOfIdentifierExpressionOp(operation.Name, operation.ScopeId, operation.SlotIndex, operation.FlatSlotId, operation.IsArguments),
            ExpressionOpKind.DeleteIdentifier => new DeleteIdentifierExpressionOp(operation.Name),
            ExpressionOpKind.DeleteNamedProperty => new DeleteNamedPropertyExpressionOp(operation.Text),
            ExpressionOpKind.DeleteComputedProperty => new DeleteComputedPropertyExpressionOp(),
            ExpressionOpKind.UnaryPlus => new UnaryPlusExpressionOp(),
            ExpressionOpKind.UnaryMinus => new UnaryMinusExpressionOp(),
            ExpressionOpKind.UnaryBitwiseNot => new UnaryBitwiseNotExpressionOp(),
            ExpressionOpKind.UnaryVoid => new UnaryVoidExpressionOp(),
            ExpressionOpKind.ToString => new ToStringExpressionOp(),
            ExpressionOpKind.UnaryLogicalNot => new UnaryLogicalNotExpressionOp(),
            ExpressionOpKind.Binary => new BinaryExpressionOp(operation.Operator),
            ExpressionOpKind.PrivateFieldIn => new PrivateFieldInExpressionOp(operation.Text),
            ExpressionOpKind.ThrowReferenceError => new ThrowReferenceErrorExpressionOp(operation.Text),
            ExpressionOpKind.Pop => new PopExpressionOp(),
            ExpressionOpKind.Jump => new JumpExpressionOp(operation.Target),
            ExpressionOpKind.JumpIfNullish => new JumpIfNullishExpressionOp(operation.Target, operation.ReplaceWithUndefined),
            ExpressionOpKind.JumpIfShortCircuited => new JumpIfShortCircuitedExpressionOp(operation.Target),
            ExpressionOpKind.JumpIfTrue => new JumpIfTrueExpressionOp(operation.Target),
            ExpressionOpKind.JumpIfFalse => new JumpIfFalseExpressionOp(operation.Target),
            ExpressionOpKind.JumpIfNotNullish => new JumpIfNotNullishExpressionOp(operation.Target),
            ExpressionOpKind.SuperConstruct => new SuperConstructExpressionOp(operation.ArgumentCount, operation.SpreadMask),
            ExpressionOpKind.Call => new CallExpressionOp(operation.ArgumentCount, operation.HasExplicitThis, operation.IsDirectEval, operation.SpreadMask),
            ExpressionOpKind.Construct => new ConstructExpressionOp(operation.ArgumentCount, operation.SpreadMask),
            _ => throw new NotSupportedException($"Unsupported packed expression op '{operation.Kind}'.")
        };
    }
}

internal abstract record ExpressionOp(ExpressionOpKind Kind);

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
