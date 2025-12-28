#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Base class for visiting AST nodes without modification.
/// Override Visit* methods to handle specific node types.
/// </summary>
public abstract class AstVisitor
{
    // Entry points
    public virtual void Visit(StatementNode node) => VisitStatement(node);
    public virtual void Visit(ExpressionNode node) => VisitExpression(node);

    protected virtual void VisitStatement(StatementNode statement)
    {
        switch (statement)
        {
            case ExpressionStatement node:
            {
                VisitExpression(node.Expression);
                break;
            }
            case BlockStatement node:
            {
                foreach (var stmt in node.Statements)
                {
                    VisitStatement(stmt);
                }

                break;
            }
            case ReturnStatement node:
            {
                if (node.Expression is not null)
                    VisitExpression(node.Expression);
                break;
            }
            case ThrowStatement node:
            {
                VisitExpression(node.Expression);
                break;
            }
            case VariableDeclaration node:
            {
                foreach (var declarator in node.Declarators)
                {
                    VisitBindingTarget(declarator.Target);
                    if (declarator.Initializer is not null)
                        VisitExpression(declarator.Initializer);
                }
                break;
            }
            case IfStatement node:
            {
                VisitExpression(node.Condition);
                VisitStatement(node.Then);
                if (node.Else is not null)
                    VisitStatement(node.Else);
                break;
            }
            case WhileStatement node:
            {
                VisitExpression(node.Condition);
                VisitStatement(node.Body);
                break;
            }
            case DoWhileStatement node:
            {
                VisitStatement(node.Body);
                VisitExpression(node.Condition);
                break;
            }
            case ForStatement node:
            {
                if (node.Initializer is not null)
                    VisitStatement(node.Initializer);
                if (node.Condition is not null)
                    VisitExpression(node.Condition);
                if (node.Increment is not null)
                    VisitExpression(node.Increment);
                VisitStatement(node.Body);
                break;
            }
            case ForEachStatement node:
            {
                VisitBindingTarget(node.Target);
                VisitExpression(node.Iterable);
                VisitStatement(node.Body);
                break;
            }
            case TryStatement node:
            {
                VisitBlockStatement(node.TryBlock);
                if (node.Catch is not null)
                {
                    if (node.Catch.Binding is not null)
                        VisitBindingTarget(node.Catch.Binding);
                    VisitBlockStatement(node.Catch.Body);
                }
                if (node.Finally is not null)
                    VisitBlockStatement(node.Finally);
                break;
            }
            case SwitchStatement node:
            {
                VisitExpression(node.Discriminant);
                foreach (var caseNode in node.Cases)
                {
                    if (caseNode.Test is not null)
                        VisitExpression(caseNode.Test);
                    VisitBlockStatement(caseNode.Body);
                }
                break;
            }
            case LabeledStatement node:
            {
                VisitStatement(node.Statement);
                break;
            }
            case WithStatement node:
            {
                VisitExpression(node.Object);
                VisitStatement(node.Body);
                break;
            }
            case BreakStatement node:
            {
                // Break with optional label - override VisitBreak if you need to process the label
                VisitBreak(node);
                break;
            }
            case ContinueStatement node:
            {
                // Continue with optional label - override VisitContinue if you need to process the label
                VisitContinue(node);
                break;
            }
            case FunctionDeclaration node:
            {
                // Functions create their own scope, don't traverse body by default
                break;
            }
        }
    }

    protected virtual void VisitBlockStatement(BlockStatement node)
    {
        foreach (var stmt in node.Statements)
        {
            VisitStatement(stmt);
        }
    }

    protected virtual void VisitExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IdentifierExpression node:
            {
                VisitIdentifier(node);
                break;
            }
            case BinaryExpression node:
            {
                VisitExpression(node.Left);
                VisitExpression(node.Right);
                break;
            }
            case UnaryExpression node:
            {
                VisitExpression(node.Operand);
                break;
            }
            case AssignmentExpression node:
            {
                VisitAssignment(node);
                break;
            }
            case PropertyAssignmentExpression node:
            {
                VisitExpression(node.Target);
                VisitExpression(node.Property);
                VisitExpression(node.Value);
                break;
            }
            case IndexAssignmentExpression node:
            {
                VisitExpression(node.Target);
                VisitExpression(node.Index);
                VisitExpression(node.Value);
                break;
            }
            case DestructuringAssignmentExpression node:
            {
                VisitBindingTarget(node.Target);
                VisitExpression(node.Value);
                break;
            }
            case CallExpression node:
            {
                VisitExpression(node.Callee);
                foreach (var arg in node.Arguments)
                {
                    if (arg.Expression is not null)
                        VisitExpression(arg.Expression);
                }
                break;
            }
            case MemberExpression node:
            {
                VisitExpression(node.Target);
                if (node.IsComputed)
                    VisitExpression(node.Property);
                break;
            }
            case ConditionalExpression node:
            {
                VisitExpression(node.Test);
                VisitExpression(node.Consequent);
                VisitExpression(node.Alternate);
                break;
            }
            case SequenceExpression node:
            {
                VisitExpression(node.Left);
                VisitExpression(node.Right);
                break;
            }
            case ArrayExpression node:
            {
                foreach (var element in node.Elements)
                {
                    if (element.Expression is not null)
                        VisitExpression(element.Expression);
                }
                break;
            }
            case ObjectExpression node:
            {
                foreach (var member in node.Members)
                {
                    if (member.Key is ExpressionNode keyExpr)
                        VisitExpression(keyExpr);
                    if (member.Value is not null)
                        VisitExpression(member.Value);
                }
                break;
            }
            case YieldExpression node:
            {
                if (node.Expression is not null)
                    VisitExpression(node.Expression);
                break;
            }
            case AwaitExpression node:
            {
                VisitExpression(node.Expression);
                break;
            }
            case NewExpression node:
            {
                VisitExpression(node.Constructor);
                foreach (var arg in node.Arguments)
                {
                    if (arg.Expression is not null)
                        VisitExpression(arg.Expression);
                }
                break;
            }
            case FunctionExpression:
            {
                // Functions create their own scope, don't traverse body by default
                break;
            }
        }
    }

    protected virtual void VisitIdentifier(IdentifierExpression node)
    {
        // Override in derived class to collect/process identifiers
    }

    protected virtual void VisitAssignment(AssignmentExpression node)
    {
        // Assignment.Target is a Symbol, not an expression
        // Override in derived class to handle the symbol
        VisitExpression(node.Value);
    }

    protected virtual void VisitBreak(BreakStatement node)
    {
        // Override in derived class to process break statements and their labels
    }

    protected virtual void VisitContinue(ContinueStatement node)
    {
        // Override in derived class to process continue statements and their labels
    }

    protected virtual void VisitBindingTarget(BindingTarget target)
    {
        switch (target)
        {
            case IdentifierBinding:
            {
                // Override VisitIdentifierBinding if needed
                break;
            }
            case ArrayBinding arrayBinding:
            {
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.Target is not null)
                        VisitBindingTarget(element.Target);
                    if (element.DefaultValue is not null)
                        VisitExpression(element.DefaultValue);
                }
                if (arrayBinding.RestElement is not null)
                    VisitBindingTarget(arrayBinding.RestElement);
                break;
            }
            case ObjectBinding objectBinding:
            {
                foreach (var prop in objectBinding.Properties)
                {
                    if (prop.NameExpression is not null)
                        VisitExpression(prop.NameExpression);
                    VisitBindingTarget(prop.Target);
                    if (prop.DefaultValue is not null)
                        VisitExpression(prop.DefaultValue);
                }
                if (objectBinding.RestElement is not null)
                    VisitBindingTarget(objectBinding.RestElement);
                break;
            }
        }
    }
}
