#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Base class for transforming AST nodes. Override Rewrite* methods to transform specific nodes.
/// Default behavior preserves the tree structure.
/// </summary>
public abstract class AstRewriter
{
    // Entry points
    public virtual StatementNode Rewrite(StatementNode node) => RewriteStatement(node);
    public virtual ExpressionNode Rewrite(ExpressionNode node) => RewriteExpression(node);

    protected virtual StatementNode RewriteStatement(StatementNode statement)
    {
        return statement switch
        {
            ExpressionStatement node => node with
            {
                Expression = RewriteExpression(node.Expression)
            },
            BlockStatement node => node with
            {
                Statements = RewriteStatementList(node.Statements)
            },
            ReturnStatement node => node with
            {
                Expression = node.Expression is not null ? RewriteExpression(node.Expression) : null
            },
            ThrowStatement node => node with
            {
                Expression = RewriteExpression(node.Expression)
            },
            VariableDeclaration node => node with
            {
                Declarators = RewriteList(node.Declarators, d => d with
                {
                    Initializer = d.Initializer is not null ? RewriteExpression(d.Initializer) : null
                })
            },
            IfStatement node => node with
            {
                Condition = RewriteExpression(node.Condition),
                Then = RewriteStatement(node.Then),
                Else = node.Else is not null ? RewriteStatement(node.Else) : null
            },
            WhileStatement node => node with
            {
                Condition = RewriteExpression(node.Condition),
                Body = RewriteStatement(node.Body)
            },
            DoWhileStatement node => node with
            {
                Body = RewriteStatement(node.Body),
                Condition = RewriteExpression(node.Condition)
            },
            ForStatement node => node with
            {
                Initializer = node.Initializer is not null ? RewriteStatement(node.Initializer) : null,
                Condition = node.Condition is not null ? RewriteExpression(node.Condition) : null,
                Increment = node.Increment is not null ? RewriteExpression(node.Increment) : null,
                Body = RewriteStatement(node.Body)
            },
            ForEachStatement node => node with
            {
                Iterable = RewriteExpression(node.Iterable),
                Body = RewriteStatement(node.Body)
            },
            TryStatement node => node with
            {
                TryBlock = (BlockStatement)RewriteStatement(node.TryBlock),
                Catch = node.Catch is not null
                    ? node.Catch with { Body = (BlockStatement)RewriteStatement(node.Catch.Body) }
                    : null,
                Finally = node.Finally is not null ? (BlockStatement)RewriteStatement(node.Finally) : null
            },
            SwitchStatement node => node with
            {
                Discriminant = RewriteExpression(node.Discriminant),
                Cases = RewriteList(node.Cases, c => c with
                {
                    Test = c.Test is not null ? RewriteExpression(c.Test) : null,
                    Body = (BlockStatement)RewriteStatement(c.Body)
                })
            },
            LabeledStatement node => node with
            {
                Statement = RewriteStatement(node.Statement)
            },
            WithStatement node => node with
            {
                Object = RewriteExpression(node.Object),
                Body = RewriteStatement(node.Body)
            },
            BreakStatement node => RewriteBreak(node),
            ContinueStatement node => RewriteContinue(node),
            _ => statement // EmptyStatement, etc.
        };
    }

    protected virtual ExpressionNode RewriteExpression(ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierExpression node => RewriteIdentifier(node),
            BinaryExpression node => node with
            {
                Left = RewriteExpression(node.Left),
                Right = RewriteExpression(node.Right)
            },
            UnaryExpression node => node with
            {
                Operand = RewriteExpression(node.Operand)
            },
            AssignmentExpression node => RewriteAssignment(node),
            PropertyAssignmentExpression node => node with
            {
                Target = RewriteExpression(node.Target),
                Property = RewriteExpression(node.Property),
                Value = RewriteExpression(node.Value)
            },
            IndexAssignmentExpression node => node with
            {
                Target = RewriteExpression(node.Target),
                Index = RewriteExpression(node.Index),
                Value = RewriteExpression(node.Value)
            },
            DestructuringAssignmentExpression node => node with
            {
                Value = RewriteExpression(node.Value)
            },
            CallExpression node => node with
            {
                Callee = RewriteExpression(node.Callee),
                Arguments = RewriteList(node.Arguments, a => a.Expression is not null
                    ? a with { Expression = RewriteExpression(a.Expression) }
                    : a)
            },
            MemberExpression node => node with
            {
                Target = RewriteExpression(node.Target),
                Property = node.IsComputed ? RewriteExpression(node.Property) : node.Property
            },
            ConditionalExpression node => node with
            {
                Test = RewriteExpression(node.Test),
                Consequent = RewriteExpression(node.Consequent),
                Alternate = RewriteExpression(node.Alternate)
            },
            SequenceExpression node => node with
            {
                Left = RewriteExpression(node.Left),
                Right = RewriteExpression(node.Right)
            },
            ArrayExpression node => node with
            {
                Elements = RewriteList(node.Elements, e => e.Expression is not null
                    ? e with { Expression = RewriteExpression(e.Expression) }
                    : e)
            },
            ObjectExpression node => node with
            {
                Members = RewriteList(node.Members, m => m with
                {
                    Key = m.Key is ExpressionNode keyExpr ? RewriteExpression(keyExpr) : m.Key,
                    Value = m.Value is not null ? RewriteExpression(m.Value) : null
                })
            },
            YieldExpression node => node with
            {
                Expression = node.Expression is not null ? RewriteExpression(node.Expression) : null
            },
            AwaitExpression node => node with
            {
                Expression = RewriteExpression(node.Expression)
            },
            NewExpression node => node with
            {
                Constructor = RewriteExpression(node.Constructor),
                Arguments = RewriteList(node.Arguments, a => a.Expression is not null
                    ? a with { Expression = RewriteExpression(a.Expression) }
                    : a)
            },
            _ => expression // Literals, ThisExpression, SuperExpression, FunctionExpression, etc.
        };
    }

    protected virtual IdentifierExpression RewriteIdentifier(IdentifierExpression node) => node;

    protected virtual AssignmentExpression RewriteAssignment(AssignmentExpression node)
    {
        // Default: just rewrite the value expression
        // Target is a Symbol, handle in derived class if needed
        return node with { Value = RewriteExpression(node.Value) };
    }

    protected virtual BreakStatement RewriteBreak(BreakStatement node) => node;

    protected virtual ContinueStatement RewriteContinue(ContinueStatement node) => node;

    // Helper methods
    protected ImmutableArray<StatementNode> RewriteStatementList(ImmutableArray<StatementNode> statements)
    {
        if (statements.IsDefaultOrEmpty) return statements;

        var changed = false;
        var builder = ImmutableArray.CreateBuilder<StatementNode>(statements.Length);

        foreach (var stmt in statements)
        {
            var rewritten = RewriteStatement(stmt);
            builder.Add(rewritten);
            if (!ReferenceEquals(stmt, rewritten))
                changed = true;
        }

        return changed ? builder.ToImmutable() : statements;
    }

    protected ImmutableArray<T> RewriteList<T>(ImmutableArray<T> list, Func<T, T> rewriter)
    {
        if (list.IsDefaultOrEmpty) return list;

        var changed = false;
        var builder = ImmutableArray.CreateBuilder<T>(list.Length);

        foreach (var item in list)
        {
            var rewritten = rewriter(item);
            builder.Add(rewritten);
            if (!ReferenceEquals(item, rewritten))
                changed = true;
        }

        return changed ? builder.ToImmutable() : list;
    }
}
