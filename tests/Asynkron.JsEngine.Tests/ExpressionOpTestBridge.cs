using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Instructions;

internal static class ExpressionOpTestBridge
{
    public static ExpressionOpView ToTestView(this PackedExpressionOp operation, ExpressionProgram program)
    {
        return new ExpressionOpView(operation, program);
    }

    public static IEnumerable<ExpressionOpView> GetOps(this ExpressionProgram program)
    {
        foreach (var operation in program.Operations)
        {
            yield return new ExpressionOpView(operation, program);
        }
    }

    public static IEnumerable<ExpressionOpView> GetOps(this ExpressionProgram program, ExpressionOpKind kind)
    {
        foreach (var operation in program.Operations)
        {
            if (operation.Kind == kind)
            {
                yield return new ExpressionOpView(operation, program);
            }
        }
    }
}

internal readonly record struct ExpressionOpView(PackedExpressionOp Operation, ExpressionProgram Program)
{
    public ExpressionOpKind Kind => Operation.Kind;

    public JsValue Value => Operation.LiteralValue;

    public string Pattern => Operation.GetString(StringConstants);

    public string Flags => Operation.RegexFlags;

    public FunctionExpression Function => Operation.GetObject<FunctionExpression>(ObjectConstants);

    public bool IsConstructorFunction => Operation.IsConstructorFunction;

    public ClassExpression Class => Operation.GetObject<ClassExpression>(ObjectConstants);

    public TaggedTemplateDescriptor Descriptor => Operation.GetObject<TaggedTemplateDescriptor>(ObjectConstants);

    public Symbol Name => Identifier.Name;

    public int ScopeId => Identifier.ScopeId;

    public int SlotIndex => Identifier.SlotIndex;

    public int FlatSlotId => Identifier.FlatSlotId;

    public bool IsArguments => Operation.IsArguments;

    public BindingTargetProgram TargetProgram => Operation.GetObject<BindingTargetProgram>(ObjectConstants);

    public int Depth => Operation.Depth;

    public string PropertyName => Operation.GetString(StringConstants);

    public bool IsPrototypeMutation => Operation.IsPrototypeMutation;

    public bool AllowNameInference => Operation.AllowNameInference;

    public ObjectAccessorKind AccessorKind => Operation.AccessorKind;

    public bool IsOptional => Operation.IsOptional;

    public bool ShortCircuitOnNullishTarget => Operation.ShortCircuitOnNullishTarget;

    public bool IsIncrement => Operation.IsIncrement;

    public bool IsPrefix => Operation.IsPrefix;

    public BinaryOperator Operator => Operation.Operator;

    public int Target => Operation.Target;

    public bool ReplaceWithUndefined => Operation.ReplaceWithUndefined;

    public int ArgumentCount => Operation.ArgumentCount;

    public bool HasExplicitThis => Operation.HasExplicitThis;

    public bool IsDirectEval => Operation.IsDirectEval;

    public ImmutableArray<bool> SpreadMask => Operation.GetSpreadMask(Program.SpreadMaskConstants.AsSpan());

    private ReadOnlySpan<string> StringConstants => Program.StringConstants.AsSpan();

    private ReadOnlySpan<object> ObjectConstants => Program.ObjectConstants.AsSpan();

    private IdentifierOperand Identifier => Operation.GetIdentifier(Program.IdentifierConstants.AsSpan());
}

internal interface IExpressionOpMarker
{
    static abstract ExpressionOpKind Kind { get; }
}

internal sealed class ApplyBindingTargetExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.ApplyBindingTarget; }
internal sealed class ArrayPushExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.ArrayPush; }
internal sealed class BinaryExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.Binary; }
internal sealed class CallExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.Call; }
internal sealed class ConstructExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.Construct; }
internal sealed class CreateArrayExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.CreateArray; }
internal sealed class CreateObjectExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.CreateObject; }
internal sealed class DefineComputedObjectPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DefineComputedObjectProperty; }
internal sealed class DefineObjectAccessorExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DefineObjectAccessor; }
internal sealed class DefineObjectMethodExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DefineObjectMethod; }
internal sealed class DefineObjectPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DefineObjectProperty; }
internal sealed class DeleteComputedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DeleteComputedProperty; }
internal sealed class DeleteNamedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DeleteNamedProperty; }
internal sealed class DuplicateTopExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DuplicateTop; }
internal sealed class DuplicateTopTwoExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.DuplicateTopTwo; }
internal sealed class EnsureSuperReferenceExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.EnsureSuperReference; }
internal sealed class GetComputedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.GetComputedProperty; }
internal sealed class GetComputedSuperPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.GetComputedSuperProperty; }
internal sealed class GetNamedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.GetNamedProperty; }
internal sealed class GetNamedSuperPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.GetNamedSuperProperty; }
internal sealed class JumpExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.Jump; }
internal sealed class JumpIfFalseExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.JumpIfFalse; }
internal sealed class JumpIfNotNullishExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.JumpIfNotNullish; }
internal sealed class JumpIfNullishExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.JumpIfNullish; }
internal sealed class JumpIfShortCircuitedExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.JumpIfShortCircuited; }
internal sealed class JumpIfTrueExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.JumpIfTrue; }
internal sealed class LoadClassLiteralExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadClassLiteral; }
internal sealed class LoadFunctionLiteralExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadFunctionLiteral; }
internal sealed class LoadIdentifierExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadIdentifier; }
internal sealed class LoadLiteralExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadLiteral; }
internal sealed class LoadNamedCallTargetExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadNamedCallTarget; }
internal sealed class LoadNamedSuperCallTargetExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadNamedSuperCallTarget; }
internal sealed class LoadTemplateObjectExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.LoadTemplateObject; }
internal sealed class PopExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.Pop; }
internal sealed class RotateTopThreeRightExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.RotateTopThreeRight; }
internal sealed class SetComputedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.SetComputedProperty; }
internal sealed class SetNamedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.SetNamedProperty; }
internal sealed class SetNamedSuperPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.SetNamedSuperProperty; }
internal sealed class StoreIdentifierExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.StoreIdentifier; }
internal sealed class SuperConstructExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.SuperConstruct; }
internal sealed class SwapTopTwoExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.SwapTopTwo; }
internal sealed class ToStringExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.ToString; }
internal sealed class TypeOfIdentifierExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.TypeOfIdentifier; }
internal sealed class UnaryMinusExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.UnaryMinus; }
internal sealed class UnaryVoidExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.UnaryVoid; }
internal sealed class UpdateComputedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.UpdateComputedProperty; }
internal sealed class UpdateComputedSuperPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.UpdateComputedSuperProperty; }
internal sealed class UpdateIdentifierExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.UpdateIdentifier; }
internal sealed class UpdateNamedPropertyExpressionOp : IExpressionOpMarker { public static ExpressionOpKind Kind => ExpressionOpKind.UpdateNamedProperty; }
