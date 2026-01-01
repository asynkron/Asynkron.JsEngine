using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Resolves slot metadata for identifiers based on the current scope and its parents.
/// </summary>
internal sealed class ScopeAwareSlotResolver(
    Dictionary<int, Dictionary<Symbol, int>> declarations,
    Dictionary<int, int> scopeParents)
{
    public bool TryResolve(Symbol symbol, int currentScopeId, out int scopeId, out int slotIndex)
    {
        var scopeIdToCheck = currentScopeId;
        while (scopeIdToCheck >= 0)
        {
            if (declarations.TryGetValue(scopeIdToCheck, out var scopeMap) &&
                scopeMap.TryGetValue(symbol, out slotIndex))
            {
                scopeId = scopeIdToCheck;
                return true;
            }

            if (!scopeParents.TryGetValue(scopeIdToCheck, out var parentScopeId))
            {
                break;
            }

            scopeIdToCheck = parentScopeId;
        }

        scopeId = -1;
        slotIndex = -1;
        return false;
    }
}

/// <summary>
/// Rewriter that stamps AST nodes with scopeId and slotIndex information.
/// Uses AstRewriter base class for consistent AST transformation.
/// </summary>
internal sealed class ScopeAwareSlotRewriter(ScopeAwareSlotResolver resolver) : AstRewriter
{
    private int _currentScopeId;

    public ExecutionInstruction RewriteInstruction(ExecutionInstruction instruction, int currentScopeId)
    {
        _currentScopeId = currentScopeId;
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
            CompoundAssignmentSlotInstruction compoundAssign => compoundAssign with
            {
                RhsExpression = Rewrite(compoundAssign.RhsExpression)
            },
            _ => instruction
        };
    }

    protected override IdentifierExpression RewriteIdentifier(IdentifierExpression node)
    {
        if (resolver.TryResolve(node.Name, _currentScopeId, out var scopeId, out var slotIndex))
        {
            return node with
            {
                ScopeDepth = 0,
                ScopeId = scopeId,
                SlotIndex = slotIndex
            };
        }
        return node;
    }

    protected override AssignmentExpression RewriteAssignment(AssignmentExpression node)
    {
        // Update the assignment target slot info.
        if (resolver.TryResolve(node.Target, _currentScopeId, out var scopeId, out var slotIndex))
        {
            var targetIdentifier = node.TargetIdentifier;
            if (targetIdentifier is not null)
            {
                targetIdentifier = targetIdentifier with
                {
                    ScopeDepth = 0,
                    ScopeId = scopeId,
                    SlotIndex = slotIndex
                };
            }
            else
            {
                targetIdentifier = new IdentifierExpression(node.Source, node.Target, 0, slotIndex, scopeId);
            }

            return node with
            {
                ScopeDepth = 0,
                ScopeId = scopeId,
                SlotIndex = slotIndex,
                TargetIdentifier = targetIdentifier,
                Value = RewriteExpression(node.Value)
            };
        }
        return base.RewriteAssignment(node);
    }
}
