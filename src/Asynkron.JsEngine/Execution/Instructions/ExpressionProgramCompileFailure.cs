namespace Asynkron.JsEngine.Execution.Instructions;

public enum ExpressionProgramFailureCode
{
    UnsupportedExpressionNode,
    UnsupportedDeleteTarget,
    UnsupportedUnaryOperator,
    UnsupportedUpdateTarget,
    SuperCall,
    NestedOptionalCall,
    UnsupportedCompoundAssignmentShape,
    OptionalOrSuperMemberUpdate,
    SuperTaggedTemplate,
    OptionalTaggedTemplate,
    NestedOptionalTaggedTemplate,
    OptionalOrSuperPropertyAssignment,
    OptionalOrSuperIndexAssignment,
    SuperMemberAccess,
    UnsupportedObjectMemberKind,
    UnsupportedStaticObjectPropertyName,
    InvalidComputedObjectKey,
    UnsupportedDotAccessPropertyName,
    UnsupportedDirectMemberCallPropertyName,
    UnsupportedTaggedTemplateMemberAccessName,
    OptionalOrSuperMemberCallTarget
}

internal sealed record ExpressionProgramCompileFailure(ExpressionProgramFailureCode Code, string Detail);
