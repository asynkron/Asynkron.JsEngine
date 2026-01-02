#region

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Base class for visiting AST nodes without modification.
/// Override Visit* methods to handle specific node types.
/// </summary>
public abstract class AstVisitor
{
    // Entry points
    public virtual void Visit(StatementNode node)
    {
        VisitStatement(node);
    }

    public virtual void Visit(ExpressionNode node)
    {
        VisitExpression(node);
    }

    protected virtual void VisitStatement(StatementNode statement)
    {
        while (true)
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
                    VisitBlockStatement(node);
                    break;
                }
                case ReturnStatement node:
                {
                    if (node.Expression is not null)
                    {
                        VisitExpression(node.Expression);
                    }

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
                        {
                            VisitExpression(declarator.Initializer);
                        }
                    }

                    break;
                }
                case IfStatement node:
                {
                    VisitExpression(node.Condition);
                    VisitStatement(node.Then);
                    if (node.Else is not null)
                    {
                        statement = node.Else;
                        continue;
                    }

                    break;
                }
                case WhileStatement node:
                {
                    VisitExpression(node.Condition);
                    statement = node.Body;
                    continue;
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
                    {
                        VisitStatement(node.Initializer);
                    }

                    if (node.Condition is not null)
                    {
                        VisitExpression(node.Condition);
                    }

                    if (node.Increment is not null)
                    {
                        VisitExpression(node.Increment);
                    }

                    statement = node.Body;
                    continue;
                }
                case ForEachStatement node:
                {
                    VisitBindingTarget(node.Target);
                    VisitExpression(node.Iterable);
                    statement = node.Body;
                    continue;
                }
                case TryStatement node:
                {
                    VisitBlockStatement(node.TryBlock);
                    if (node.Catch is not null)
                    {
                        if (node.Catch.Binding is not null)
                        {
                            VisitBindingTarget(node.Catch.Binding);
                        }

                        VisitBlockStatement(node.Catch.Body);
                    }

                    if (node.Finally is not null)
                    {
                        VisitBlockStatement(node.Finally);
                    }

                    break;
                }
                case SwitchStatement node:
                {
                    VisitExpression(node.Discriminant);
                    foreach (var caseNode in node.Cases)
                    {
                        if (caseNode.Test is not null)
                        {
                            VisitExpression(caseNode.Test);
                        }

                        VisitBlockStatement(caseNode.Body);
                    }

                    break;
                }
                case LabeledStatement node:
                {
                    statement = node.Statement;
                    continue;
                }
                case WithStatement node:
                {
                    VisitExpression(node.Object);
                    statement = node.Body;
                    continue;
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
                case FunctionDeclaration:
                {
                    // Functions create their own scope, don't traverse body by default
                    break;
                }
            }

            break;
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
        while (true)
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
                    expression = node.Right;
                    continue;
                }
                case UnaryExpression node:
                {
                    expression = node.Operand;
                    continue;
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
                    expression = node.Value;
                    continue;
                }
                case IndexAssignmentExpression node:
                {
                    VisitExpression(node.Target);
                    VisitExpression(node.Index);
                    expression = node.Value;
                    continue;
                }
                case DestructuringAssignmentExpression node:
                {
                    VisitBindingTarget(node.Target);
                    expression = node.Value;
                    continue;
                }
                case CallExpression node:
                {
                    VisitExpression(node.Callee);
                    foreach (var arg in node.Arguments)
                    {
                        if (arg.Expression is not null)
                        {
                            VisitExpression(arg.Expression);
                        }
                    }

                    break;
                }
                case MemberExpression node:
                {
                    VisitExpression(node.Target);
                    if (node.IsComputed)
                    {
                        expression = node.Property;
                        continue;
                    }

                    break;
                }
                case ConditionalExpression node:
                {
                    VisitExpression(node.Test);
                    VisitExpression(node.Consequent);
                    expression = node.Alternate;
                    continue;
                }
                case SequenceExpression node:
                {
                    VisitExpression(node.Left);
                    expression = node.Right;
                    continue;
                }
                case ArrayExpression node:
                {
                    foreach (var element in node.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            VisitExpression(element.Expression);
                        }
                    }

                    break;
                }
                case ObjectExpression node:
                {
                    foreach (var member in node.Members)
                    {
                        if (member.Key is ExpressionNode keyExpr)
                        {
                            VisitExpression(keyExpr);
                        }

                        if (member.Value is not null)
                        {
                            VisitExpression(member.Value);
                        }
                    }

                    break;
                }
                case YieldExpression node:
                {
                    if (node.Expression is not null)
                    {
                        expression = node.Expression;
                        continue;
                    }

                    break;
                }
                case AwaitExpression node:
                {
                    expression = node.Expression;
                    continue;
                }
                case NewExpression node:
                {
                    VisitExpression(node.Constructor);
                    foreach (var arg in node.Arguments)
                    {
                        if (arg.Expression is not null)
                        {
                            VisitExpression(arg.Expression);
                        }
                    }

                    break;
                }
                case FunctionExpression node:
                {
                    VisitFunctionExpression(node);
                    break;
                }
            }

            break;
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

    protected virtual void VisitFunctionExpression(FunctionExpression node)
    {
        // Functions create their own scope, don't traverse body by default
        // Override in derived class if you need to process function expressions
    }

    protected virtual void VisitBindingTarget(BindingTarget target)
    {
        while (true)
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
                        {
                            VisitBindingTarget(element.Target);
                        }

                        if (element.DefaultValue is not null)
                        {
                            VisitExpression(element.DefaultValue);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;
                }
                case ObjectBinding objectBinding:
                {
                    foreach (var prop in objectBinding.Properties)
                    {
                        if (prop.NameExpression is not null)
                        {
                            VisitExpression(prop.NameExpression);
                        }

                        VisitBindingTarget(prop.Target);
                        if (prop.DefaultValue is not null)
                        {
                            VisitExpression(prop.DefaultValue);
                        }
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
                }
            }

            break;
        }
    }
}
