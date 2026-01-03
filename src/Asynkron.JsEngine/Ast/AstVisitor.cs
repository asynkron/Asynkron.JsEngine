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
            StatementNode? next = statement switch
            {
                ExpressionStatement node => VisitExpressionStatement(node),
                BlockStatement node => VisitBlock(node),
                ReturnStatement node => VisitReturnStatement(node),
                ThrowStatement node => VisitThrowStatement(node),
                VariableDeclaration node => VisitVariableDeclaration(node),
                IfStatement node => VisitIfStatement(node),
                WhileStatement node => VisitWhileStatement(node),
                DoWhileStatement node => VisitDoWhileStatement(node),
                ForStatement node => VisitForStatement(node),
                ForEachStatement node => VisitForEachStatement(node),
                TryStatement node => VisitTryStatement(node),
                SwitchStatement node => VisitSwitchStatement(node),
                LabeledStatement node => VisitLabeledStatement(node),
                WithStatement node => VisitWithStatement(node),
                BreakStatement node => VisitBreakStatement(node),
                ContinueStatement node => VisitContinueStatement(node),
                FunctionDeclaration node => VisitFunctionDeclaration(node),
                _ => null
            };

            if (next is null)
            {
                break;
            }

            statement = next;
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
                    VisitIdentifier(node);
                    break;
                case BinaryExpression node:
                    VisitExpression(node.Left);
                    expression = node.Right;
                    continue;
                case UnaryExpression node:
                    expression = node.Operand;
                    continue;
                case AssignmentExpression node:
                    VisitAssignment(node);
                    break;
                case PropertyAssignmentExpression node:
                    VisitExpression(node.Target);
                    VisitExpression(node.Property);
                    expression = node.Value;
                    continue;
                case IndexAssignmentExpression node:
                    VisitExpression(node.Target);
                    VisitExpression(node.Index);
                    expression = node.Value;
                    continue;
                case DestructuringAssignmentExpression node:
                    VisitBindingTarget(node.Target);
                    expression = node.Value;
                    continue;
                case CallExpression node:
                    VisitCallExpression(node);
                    break;
                case MemberExpression node:
                    VisitExpression(node.Target);
                    if (node.IsComputed)
                    {
                        expression = node.Property;
                        continue;
                    }

                    break;
                case ConditionalExpression node:
                    VisitExpression(node.Test);
                    VisitExpression(node.Consequent);
                    expression = node.Alternate;
                    continue;
                case SequenceExpression node:
                    VisitExpression(node.Left);
                    expression = node.Right;
                    continue;
                case ArrayExpression node:
                    VisitArrayExpression(node);
                    break;
                case ObjectExpression node:
                    VisitObjectExpression(node);
                    break;
                case YieldExpression node:
                    if (node.Expression is not null)
                    {
                        expression = node.Expression;
                        continue;
                    }

                    break;
                case AwaitExpression node:
                    expression = node.Expression;
                    continue;
                case NewExpression node:
                    VisitNewExpression(node);
                    break;
                case FunctionExpression node:
                    VisitFunctionExpression(node);
                    break;
            }

            break;
        }
    }

    protected virtual StatementNode? VisitExpressionStatement(ExpressionStatement node)
    {
        VisitExpression(node.Expression);
        return null;
    }

    protected virtual StatementNode? VisitBlock(BlockStatement node)
    {
        VisitBlockStatement(node);
        return null;
    }

    protected virtual StatementNode? VisitReturnStatement(ReturnStatement node)
    {
        if (node.Expression is not null)
        {
            VisitExpression(node.Expression);
        }

        return null;
    }

    protected virtual StatementNode? VisitThrowStatement(ThrowStatement node)
    {
        VisitExpression(node.Expression);
        return null;
    }

    protected virtual StatementNode? VisitVariableDeclaration(VariableDeclaration node)
    {
        foreach (var declarator in node.Declarators)
        {
            VisitBindingTarget(declarator.Target);
            if (declarator.Initializer is not null)
            {
                VisitExpression(declarator.Initializer);
            }
        }

        return null;
    }

    protected virtual StatementNode? VisitIfStatement(IfStatement node)
    {
        VisitExpression(node.Condition);
        VisitStatement(node.Then);
        return node.Else;
    }

    protected virtual StatementNode? VisitWhileStatement(WhileStatement node)
    {
        VisitExpression(node.Condition);
        return node.Body;
    }

    protected virtual StatementNode? VisitDoWhileStatement(DoWhileStatement node)
    {
        VisitStatement(node.Body);
        VisitExpression(node.Condition);
        return null;
    }

    protected virtual StatementNode? VisitForStatement(ForStatement node)
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

        return node.Body;
    }

    protected virtual StatementNode? VisitForEachStatement(ForEachStatement node)
    {
        VisitBindingTarget(node.Target);
        VisitExpression(node.Iterable);
        return node.Body;
    }

    protected virtual StatementNode? VisitTryStatement(TryStatement node)
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

        return null;
    }

    protected virtual StatementNode? VisitSwitchStatement(SwitchStatement node)
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

        return null;
    }

    protected virtual StatementNode? VisitLabeledStatement(LabeledStatement node)
    {
        return node.Statement;
    }

    protected virtual StatementNode? VisitWithStatement(WithStatement node)
    {
        VisitExpression(node.Object);
        return node.Body;
    }

    protected virtual StatementNode? VisitBreakStatement(BreakStatement node)
    {
        VisitBreak(node);
        return null;
    }

    protected virtual StatementNode? VisitContinueStatement(ContinueStatement node)
    {
        VisitContinue(node);
        return null;
    }

    protected virtual StatementNode? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        VisitFunctionExpression(node.Function);
        return null;
    }

    protected virtual void VisitCallExpression(CallExpression node)
    {
        VisitExpression(node.Callee);
        foreach (var arg in node.Arguments)
        {
            if (arg.Expression is not null)
            {
                VisitExpression(arg.Expression);
            }
        }
    }

    protected virtual void VisitArrayExpression(ArrayExpression node)
    {
        foreach (var element in node.Elements)
        {
            if (element.Expression is not null)
            {
                VisitExpression(element.Expression);
            }
        }
    }

    protected virtual void VisitObjectExpression(ObjectExpression node)
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
    }

    protected virtual void VisitNewExpression(NewExpression node)
    {
        VisitExpression(node.Constructor);
        foreach (var arg in node.Arguments)
        {
            if (arg.Expression is not null)
            {
                VisitExpression(arg.Expression);
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

    protected virtual void VisitFunctionExpression(FunctionExpression node)
    {
        foreach (var parameter in node.Parameters)
        {
            if (parameter.Pattern is not null)
            {
                VisitBindingTarget(parameter.Pattern);
            }

            if (parameter.DefaultValue is not null)
            {
                VisitExpression(parameter.DefaultValue);
            }
        }

        VisitBlockStatement(node.Body);
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
