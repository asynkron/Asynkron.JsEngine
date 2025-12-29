using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Rewriter that updates AST nodes with scopeId and slotIndex information.
/// Uses AstRewriter base class for consistent AST transformation.
/// </summary>
internal sealed class SlotAssignmentRewriter(Dictionary<Symbol, (int scopeId, int slotIndex)> symbolToScope)
    : AstRewriter
{
    public ExecutionInstruction RewriteInstruction(ExecutionInstruction instruction)
    {
        return instruction switch
        {
            StatementInstruction stmt => stmt with
            {
                Statement = Rewrite(stmt.Statement)
            },
            ExpressionInstruction expr => expr with
            {
                Expression = Rewrite(expr.Expression)
            },
            EvaluateAndDiscardInstruction eval => eval with
            {
                Expression = Rewrite(eval.Expression)
            },
            YieldInstruction { YieldExpression: not null } yield => yield with
            {
                YieldExpression = Rewrite(yield.YieldExpression)
            },
            ReturnInstruction { ReturnExpression: not null } ret => ret with
            {
                ReturnExpression = Rewrite(ret.ReturnExpression)
            },
            ThrowInstruction thr => thr with
            {
                Expression = Rewrite(thr.Expression)
            },
            BranchInstruction branch => branch with
            {
                Condition = Rewrite(branch.Condition)
            },
            SimpleVariableDeclarationInstruction { Initializer: not null } varDecl => varDecl with
            {
                Initializer = Rewrite(varDecl.Initializer)
            },
            IteratorInitInstruction iterInit => iterInit with
            {
                IterableExpression = Rewrite(iterInit.IterableExpression)
            },
            EnterWithInstruction enterWith => enterWith with
            {
                ObjectExpression = Rewrite(enterWith.ObjectExpression)
            },
            YieldStarInstruction yieldStar => yieldStar with
            {
                IterableExpression = Rewrite(yieldStar.IterableExpression)
            },
            _ => instruction
        };
    }

    protected override IdentifierExpression RewriteIdentifier(IdentifierExpression node)
    {
        if (symbolToScope.TryGetValue(node.Name, out var scopeInfo))
        {
            return node with
            {
                ScopeId = scopeInfo.scopeId,
                SlotIndex = scopeInfo.slotIndex
            };
        }
        return node;
    }

    protected override AssignmentExpression RewriteAssignment(AssignmentExpression node)
    {
        // Update the assignment target slot info
        if (symbolToScope.TryGetValue(node.Target, out var targetInfo))
        {
            return node with
            {
                ScopeDepth = 0,
                ScopeId = targetInfo.scopeId,
                SlotIndex = targetInfo.slotIndex,
                Value = RewriteExpression(node.Value)
            };
        }
        return base.RewriteAssignment(node);
    }
}
