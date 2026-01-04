#region

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Base class for visiting AST nodes without modification.
/// Override Visit* methods to handle specific node types.
/// Set ShouldStop = true to terminate traversal early.
/// </summary>
public abstract class AstVisitor
{
    /// <summary>
    /// Set to true to stop traversal early. Checked before visiting each node.
    /// </summary>
    protected bool ShouldStop { get; set; }

    // Entry points
    public virtual void Visit(StatementNode node)
    {
        if (ShouldStop) return;
        VisitStatement(node);
    }

    public virtual void Visit(ExpressionNode node)
    {
        if (ShouldStop) return;
        VisitExpression(node);
    }

    public virtual void Visit(BindingTarget node)
    {
        if (ShouldStop) return;
        VisitBindingTarget(node);
    }

    #region Statement Dispatch

    protected virtual void VisitStatement(StatementNode statement)
    {
        if (ShouldStop) return;

        switch (statement)
        {
            case BlockStatement node:
                VisitBlockStatement(node);
                break;
            case ExpressionStatement node:
                VisitExpressionStatement(node);
                break;
            case ReturnStatement node:
                VisitReturnStatement(node);
                break;
            case ThrowStatement node:
                VisitThrowStatement(node);
                break;
            case VariableDeclaration node:
                VisitVariableDeclaration(node);
                break;
            case IfStatement node:
                VisitIfStatement(node);
                break;
            case WhileStatement node:
                VisitWhileStatement(node);
                break;
            case DoWhileStatement node:
                VisitDoWhileStatement(node);
                break;
            case ForStatement node:
                VisitForStatement(node);
                break;
            case ForEachStatement node:
                VisitForEachStatement(node);
                break;
            case TryStatement node:
                VisitTryStatement(node);
                break;
            case SwitchStatement node:
                VisitSwitchStatement(node);
                break;
            case LabeledStatement node:
                VisitLabeledStatement(node);
                break;
            case WithStatement node:
                VisitWithStatement(node);
                break;
            case BreakStatement node:
                VisitBreakStatement(node);
                break;
            case ContinueStatement node:
                VisitContinueStatement(node);
                break;
            case FunctionDeclaration node:
                VisitFunctionDeclaration(node);
                break;
            case EmptyStatement node:
                VisitEmptyStatement(node);
                break;
            // Module statements
            case ImportStatement node:
                VisitImportStatement(node);
                break;
            case ExportAllStatement node:
                VisitExportAllStatement(node);
                break;
            case ExportDefaultStatement node:
                VisitExportDefaultStatement(node);
                break;
            case ExportDeclarationStatement node:
                VisitExportDeclarationStatement(node);
                break;
            case ExportNamespaceAsStatement node:
                VisitExportNamespaceAsStatement(node);
                break;
            case ExportNamedStatement node:
                VisitExportNamedStatement(node);
                break;
        }
    }

    #endregion

    #region Expression Dispatch

    protected virtual void VisitExpression(ExpressionNode expression)
    {
        if (ShouldStop) return;

        switch (expression)
        {
            case LiteralExpression node:
                VisitLiteralExpression(node);
                break;
            case IdentifierExpression node:
                VisitIdentifierExpression(node);
                break;
            case ThisExpression node:
                VisitThisExpression(node);
                break;
            case SuperExpression node:
                VisitSuperExpression(node);
                break;
            case BinaryExpression node:
                VisitBinaryExpression(node);
                break;
            case UnaryExpression node:
                VisitUnaryExpression(node);
                break;
            case AssignmentExpression node:
                VisitAssignmentExpression(node);
                break;
            case PropertyAssignmentExpression node:
                VisitPropertyAssignmentExpression(node);
                break;
            case IndexAssignmentExpression node:
                VisitIndexAssignmentExpression(node);
                break;
            case DestructuringAssignmentExpression node:
                VisitDestructuringAssignmentExpression(node);
                break;
            case CallExpression node:
                VisitCallExpression(node);
                break;
            case MemberExpression node:
                VisitMemberExpression(node);
                break;
            case ConditionalExpression node:
                VisitConditionalExpression(node);
                break;
            case SequenceExpression node:
                VisitSequenceExpression(node);
                break;
            case ArrayExpression node:
                VisitArrayExpression(node);
                break;
            case ObjectExpression node:
                VisitObjectExpression(node);
                break;
            case YieldExpression node:
                VisitYieldExpression(node);
                break;
            case AwaitExpression node:
                VisitAwaitExpression(node);
                break;
            case NewExpression node:
                VisitNewExpression(node);
                break;
            case FunctionExpression node:
                VisitFunctionExpression(node);
                break;
            case ClassExpression node:
                VisitClassExpression(node);
                break;
            case TemplateLiteralExpression node:
                VisitTemplateLiteralExpression(node);
                break;
            case TaggedTemplateExpression node:
                VisitTaggedTemplateExpression(node);
                break;
            case RegexLiteralExpression node:
                VisitRegexLiteralExpression(node);
                break;
            case PrivateIdentifierExpression node:
                VisitPrivateIdentifierExpression(node);
                break;
            case ImportMetaExpression node:
                VisitImportMetaExpression(node);
                break;
            case NewTargetExpression node:
                VisitNewTargetExpression(node);
                break;
            case DecoratorExpression node:
                VisitDecoratorExpression(node);
                break;
        }
    }

    #endregion

    #region Statement Visit Methods

    protected virtual void VisitBlockStatement(BlockStatement node)
    {
        foreach (var stmt in node.Statements)
        {
            if (ShouldStop) break;
            VisitStatement(stmt);
        }
    }

    protected virtual void VisitExpressionStatement(ExpressionStatement node)
    {
        VisitExpression(node.Expression);
    }

    protected virtual void VisitReturnStatement(ReturnStatement node)
    {
        if (node.Expression is not null)
        {
            VisitExpression(node.Expression);
        }
    }

    protected virtual void VisitThrowStatement(ThrowStatement node)
    {
        VisitExpression(node.Expression);
    }

    protected virtual void VisitVariableDeclaration(VariableDeclaration node)
    {
        foreach (var declarator in node.Declarators)
        {
            if (ShouldStop) break;
            VisitBindingTarget(declarator.Target);
            if (!ShouldStop && declarator.Initializer is not null)
            {
                VisitExpression(declarator.Initializer);
            }
        }
    }

    protected virtual void VisitIfStatement(IfStatement node)
    {
        VisitExpression(node.Condition);
        if (!ShouldStop) VisitStatement(node.Then);
        if (!ShouldStop && node.Else is not null) VisitStatement(node.Else);
    }

    protected virtual void VisitWhileStatement(WhileStatement node)
    {
        VisitExpression(node.Condition);
        if (!ShouldStop) VisitStatement(node.Body);
    }

    protected virtual void VisitDoWhileStatement(DoWhileStatement node)
    {
        VisitStatement(node.Body);
        if (!ShouldStop) VisitExpression(node.Condition);
    }

    protected virtual void VisitForStatement(ForStatement node)
    {
        if (node.Initializer is not null)
        {
            VisitStatement(node.Initializer);
        }

        if (!ShouldStop && node.Condition is not null)
        {
            VisitExpression(node.Condition);
        }

        if (!ShouldStop && node.Increment is not null)
        {
            VisitExpression(node.Increment);
        }

        if (!ShouldStop)
        {
            VisitStatement(node.Body);
        }
    }

    protected virtual void VisitForEachStatement(ForEachStatement node)
    {
        VisitBindingTarget(node.Target);
        if (!ShouldStop) VisitExpression(node.Iterable);
        if (!ShouldStop) VisitStatement(node.Body);
    }

    protected virtual void VisitTryStatement(TryStatement node)
    {
        VisitBlockStatement(node.TryBlock);

        if (!ShouldStop && node.Catch is not null)
        {
            if (node.Catch.Binding is not null)
            {
                VisitBindingTarget(node.Catch.Binding);
            }
            if (!ShouldStop)
            {
                VisitBlockStatement(node.Catch.Body);
            }
        }

        if (!ShouldStop && node.Finally is not null)
        {
            VisitBlockStatement(node.Finally);
        }
    }

    protected virtual void VisitSwitchStatement(SwitchStatement node)
    {
        VisitExpression(node.Discriminant);

        foreach (var caseNode in node.Cases)
        {
            if (ShouldStop) break;

            if (caseNode.Test is not null)
            {
                VisitExpression(caseNode.Test);
            }

            if (!ShouldStop)
            {
                VisitBlockStatement(caseNode.Body);
            }
        }
    }

    protected virtual void VisitLabeledStatement(LabeledStatement node)
    {
        VisitStatement(node.Statement);
    }

    protected virtual void VisitWithStatement(WithStatement node)
    {
        VisitExpression(node.Object);
        if (!ShouldStop) VisitStatement(node.Body);
    }

    protected virtual void VisitBreakStatement(BreakStatement node)
    {
        // Leaf node
    }

    protected virtual void VisitContinueStatement(ContinueStatement node)
    {
        // Leaf node
    }

    protected virtual void VisitFunctionDeclaration(FunctionDeclaration node)
    {
        VisitFunctionExpression(node.Function);
    }

    protected virtual void VisitEmptyStatement(EmptyStatement node)
    {
        // Leaf node
    }

    // Module statements
    protected virtual void VisitImportStatement(ImportStatement node)
    {
        // Leaf node - imports have no traversable children
    }

    protected virtual void VisitExportAllStatement(ExportAllStatement node)
    {
        // Leaf node
    }

    protected virtual void VisitExportDefaultStatement(ExportDefaultStatement node)
    {
        if (node.Value is ExportDefaultExpression exportExpr)
        {
            VisitExpression(exportExpr.Expression);
        }
    }

    protected virtual void VisitExportDeclarationStatement(ExportDeclarationStatement node)
    {
        VisitStatement(node.Declaration);
    }

    protected virtual void VisitExportNamespaceAsStatement(ExportNamespaceAsStatement node)
    {
        // Leaf node
    }

    protected virtual void VisitExportNamedStatement(ExportNamedStatement node)
    {
        // Leaf node
    }

    #endregion

    #region Expression Visit Methods

    protected virtual void VisitLiteralExpression(LiteralExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitIdentifierExpression(IdentifierExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitThisExpression(ThisExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitSuperExpression(SuperExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitBinaryExpression(BinaryExpression node)
    {
        VisitExpression(node.Left);
        if (!ShouldStop) VisitExpression(node.Right);
    }

    protected virtual void VisitUnaryExpression(UnaryExpression node)
    {
        VisitExpression(node.Operand);
    }

    protected virtual void VisitAssignmentExpression(AssignmentExpression node)
    {
        VisitExpression(node.Value);
    }

    protected virtual void VisitPropertyAssignmentExpression(PropertyAssignmentExpression node)
    {
        VisitExpression(node.Target);
        if (!ShouldStop) VisitExpression(node.Property);
        if (!ShouldStop) VisitExpression(node.Value);
    }

    protected virtual void VisitIndexAssignmentExpression(IndexAssignmentExpression node)
    {
        VisitExpression(node.Target);
        if (!ShouldStop) VisitExpression(node.Index);
        if (!ShouldStop) VisitExpression(node.Value);
    }

    protected virtual void VisitDestructuringAssignmentExpression(DestructuringAssignmentExpression node)
    {
        VisitBindingTarget(node.Target);
        if (!ShouldStop) VisitExpression(node.Value);
    }

    protected virtual void VisitCallExpression(CallExpression node)
    {
        VisitExpression(node.Callee);

        foreach (var arg in node.Arguments)
        {
            if (ShouldStop) break;
            if (arg.Expression is not null)
            {
                VisitExpression(arg.Expression);
            }
        }
    }

    protected virtual void VisitMemberExpression(MemberExpression node)
    {
        VisitExpression(node.Target);
        if (!ShouldStop && node.IsComputed)
        {
            VisitExpression(node.Property);
        }
    }

    protected virtual void VisitConditionalExpression(ConditionalExpression node)
    {
        VisitExpression(node.Test);
        if (!ShouldStop) VisitExpression(node.Consequent);
        if (!ShouldStop) VisitExpression(node.Alternate);
    }

    protected virtual void VisitSequenceExpression(SequenceExpression node)
    {
        VisitExpression(node.Left);
        if (!ShouldStop) VisitExpression(node.Right);
    }

    protected virtual void VisitArrayExpression(ArrayExpression node)
    {
        foreach (var element in node.Elements)
        {
            if (ShouldStop) break;
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
            if (ShouldStop) break;

            if (member.Key is ExpressionNode keyExpr)
            {
                VisitExpression(keyExpr);
            }

            if (!ShouldStop && member.Value is not null)
            {
                VisitExpression(member.Value);
            }
        }
    }

    protected virtual void VisitYieldExpression(YieldExpression node)
    {
        if (node.Expression is not null)
        {
            VisitExpression(node.Expression);
        }
    }

    protected virtual void VisitAwaitExpression(AwaitExpression node)
    {
        VisitExpression(node.Expression);
    }

    protected virtual void VisitNewExpression(NewExpression node)
    {
        VisitExpression(node.Constructor);

        foreach (var arg in node.Arguments)
        {
            if (ShouldStop) break;
            if (arg.Expression is not null)
            {
                VisitExpression(arg.Expression);
            }
        }
    }

    protected virtual void VisitFunctionExpression(FunctionExpression node)
    {
        foreach (var parameter in node.Parameters)
        {
            if (ShouldStop) break;

            if (parameter.Pattern is not null)
            {
                VisitBindingTarget(parameter.Pattern);
            }

            if (!ShouldStop && parameter.DefaultValue is not null)
            {
                VisitExpression(parameter.DefaultValue);
            }
        }

        if (!ShouldStop)
        {
            VisitBlockStatement(node.Body);
        }
    }

    protected virtual void VisitClassExpression(ClassExpression node)
    {
        if (node.Definition.Extends is not null)
        {
            VisitExpression(node.Definition.Extends);
        }

        if (ShouldStop) return;

        VisitFunctionExpression(node.Definition.Constructor);

        foreach (var member in node.Definition.Members)
        {
            if (ShouldStop) break;

            if (member.IsComputed && member.ComputedName is not null)
            {
                VisitExpression(member.ComputedName);
            }

            if (!ShouldStop)
            {
                VisitFunctionExpression(member.Function);
            }
        }

        foreach (var field in node.Definition.Fields)
        {
            if (ShouldStop) break;
            if (field.Initializer is not null)
            {
                VisitExpression(field.Initializer);
            }
        }

        foreach (var staticBlock in node.Definition.StaticBlocks)
        {
            if (ShouldStop) break;
            VisitBlockStatement(staticBlock.Body);
        }
    }

    protected virtual void VisitTemplateLiteralExpression(TemplateLiteralExpression node)
    {
        foreach (var part in node.Parts)
        {
            if (ShouldStop) break;
            if (part.Expression is not null)
            {
                VisitExpression(part.Expression);
            }
        }
    }

    protected virtual void VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        VisitExpression(node.Tag);
        foreach (var expr in node.Expressions)
        {
            if (ShouldStop) break;
            VisitExpression(expr);
        }
    }

    protected virtual void VisitRegexLiteralExpression(RegexLiteralExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitPrivateIdentifierExpression(PrivateIdentifierExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitImportMetaExpression(ImportMetaExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitNewTargetExpression(NewTargetExpression node)
    {
        // Leaf node
    }

    protected virtual void VisitDecoratorExpression(DecoratorExpression node)
    {
        VisitExpression(node.Expression);
    }

    #endregion

    #region Binding Target Visit

    protected virtual void VisitBindingTarget(BindingTarget target)
    {
        if (ShouldStop) return;

        switch (target)
        {
            case IdentifierBinding node:
                VisitIdentifierBinding(node);
                break;

            case ArrayBinding arrayBinding:
                VisitArrayBinding(arrayBinding);
                break;

            case ObjectBinding objectBinding:
                VisitObjectBinding(objectBinding);
                break;
        }
    }

    protected virtual void VisitIdentifierBinding(IdentifierBinding node)
    {
        // Leaf node
    }

    protected virtual void VisitArrayBinding(ArrayBinding node)
    {
        foreach (var element in node.Elements)
        {
            if (ShouldStop) break;

            if (element.Target is not null)
            {
                VisitBindingTarget(element.Target);
            }

            if (!ShouldStop && element.DefaultValue is not null)
            {
                VisitExpression(element.DefaultValue);
            }
        }

        if (!ShouldStop && node.RestElement is not null)
        {
            VisitBindingTarget(node.RestElement);
        }
    }

    protected virtual void VisitObjectBinding(ObjectBinding node)
    {
        foreach (var prop in node.Properties)
        {
            if (ShouldStop) break;

            if (prop.NameExpression is not null)
            {
                VisitExpression(prop.NameExpression);
            }

            if (!ShouldStop)
            {
                VisitBindingTarget(prop.Target);
            }

            if (!ShouldStop && prop.DefaultValue is not null)
            {
                VisitExpression(prop.DefaultValue);
            }
        }

        if (!ShouldStop && node.RestElement is not null)
        {
            VisitBindingTarget(node.RestElement);
        }
    }

    #endregion
}
