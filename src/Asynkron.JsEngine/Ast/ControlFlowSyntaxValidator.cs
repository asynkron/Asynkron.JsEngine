using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

internal static class ControlFlowSyntaxValidator
{
    internal static bool TryGetFirstIllegalControlFlow(
        ImmutableArray<StatementNode> statements,
        out string message,
        out SourceReference? source)
    {
        var validator = new ControlFlowVisitor();
        foreach (var statement in statements)
        {
            validator.Visit(statement);
            if (validator.HasError)
            {
                message = validator.ErrorMessage;
                source = validator.ErrorSource;
                return true;
            }
        }

        message = string.Empty;
        source = null;
        return false;
    }

    private sealed class ControlFlowVisitor : AstVisitor
    {
        private readonly Stack<(Symbol Label, LabelTargetKind Kind)> _labels = new();
        private string _errorMessage = string.Empty;
        private SourceReference? _errorSource;
        private int _iterationDepth;
        private int _switchDepth;

        internal bool HasError => ShouldStop;
        internal string ErrorMessage => _errorMessage;
        internal SourceReference? ErrorSource => _errorSource;

        protected override void VisitBreakStatement(BreakStatement node)
        {
            if (node.Label is null)
            {
                if (_iterationDepth == 0 && _switchDepth == 0)
                {
                    SetError("Illegal break statement.", node.Source);
                }

                return;
            }

            if (!TryResolveLabel(_labels, node.Label, requireIteration: false))
            {
                SetError($"Undefined label '{node.Label.Name}' for break statement.", node.Source);
            }
        }

        protected override void VisitContinueStatement(ContinueStatement node)
        {
            if (node.Label is null)
            {
                if (_iterationDepth == 0)
                {
                    SetError("Illegal continue statement.", node.Source);
                }

                return;
            }

            if (!TryResolveLabel(_labels, node.Label, requireIteration: true))
            {
                SetError($"Undefined iteration label '{node.Label.Name}' for continue statement.", node.Source);
            }
        }

        protected override void VisitWhileStatement(WhileStatement node)
        {
            VisitExpression(node.Condition);
            if (ShouldStop)
            {
                return;
            }

            _iterationDepth++;
            Visit(node.Body);
            _iterationDepth--;
        }

        protected override void VisitDoWhileStatement(DoWhileStatement node)
        {
            _iterationDepth++;
            Visit(node.Body);
            _iterationDepth--;
            if (!ShouldStop)
            {
                VisitExpression(node.Condition);
            }
        }

        protected override void VisitForStatement(ForStatement node)
        {
            if (node.Initializer is not null)
            {
                Visit(node.Initializer);
            }

            if (!ShouldStop && node.Condition is not null)
            {
                VisitExpression(node.Condition);
            }

            if (!ShouldStop && node.Increment is not null)
            {
                VisitExpression(node.Increment);
            }

            if (ShouldStop)
            {
                return;
            }

            _iterationDepth++;
            Visit(node.Body);
            _iterationDepth--;
        }

        protected override void VisitForEachStatement(ForEachStatement node)
        {
            VisitBindingTarget(node.Target);
            if (!ShouldStop)
            {
                VisitExpression(node.Iterable);
            }

            if (ShouldStop)
            {
                return;
            }

            _iterationDepth++;
            Visit(node.Body);
            _iterationDepth--;
        }

        protected override void VisitSwitchStatement(SwitchStatement node)
        {
            VisitExpression(node.Discriminant);
            if (ShouldStop)
            {
                return;
            }

            _switchDepth++;
            foreach (var caseNode in node.Cases)
            {
                if (ShouldStop)
                {
                    break;
                }

                if (caseNode.Test is not null)
                {
                    VisitExpression(caseNode.Test);
                }

                if (!ShouldStop)
                {
                    VisitBlockStatement(caseNode.Body);
                }
            }

            _switchDepth--;
        }

        protected override void VisitLabeledStatement(LabeledStatement node)
        {
            var targetKind = GetLabelTargetKind(node.Statement);
            _labels.Push((node.Label, targetKind));
            Visit(node.Statement);
            _labels.Pop();
        }

        protected override void VisitFunctionExpression(FunctionExpression node)
        {
            VisitFunctionBoundary(() => base.VisitFunctionExpression(node));
        }

        protected override void VisitClassDeclaration(ClassDeclaration node)
        {
            VisitFunctionBoundary(() => base.VisitClassDeclaration(node));
        }

        protected override void VisitClassExpression(ClassExpression node)
        {
            VisitFunctionBoundary(() => base.VisitClassExpression(node));
        }

        private void VisitFunctionBoundary(Action visit)
        {
            var savedIterationDepth = _iterationDepth;
            var savedSwitchDepth = _switchDepth;
            var savedLabels = _labels.ToArray();

            _iterationDepth = 0;
            _switchDepth = 0;
            _labels.Clear();
            visit();
            _labels.Clear();

            for (var index = savedLabels.Length - 1; index >= 0; index--)
            {
                _labels.Push(savedLabels[index]);
            }

            _iterationDepth = savedIterationDepth;
            _switchDepth = savedSwitchDepth;
        }

        private void SetError(string message, SourceReference? source)
        {
            _errorMessage = message;
            _errorSource = source;
            ShouldStop = true;
        }
    }

    private static LabelTargetKind GetLabelTargetKind(StatementNode statement)
    {
        while (statement is LabeledStatement labeledStatement)
        {
            statement = labeledStatement.Statement;
        }

        return statement switch
        {
            WhileStatement => LabelTargetKind.Iteration,
            DoWhileStatement => LabelTargetKind.Iteration,
            ForStatement => LabelTargetKind.Iteration,
            ForEachStatement => LabelTargetKind.Iteration,
            SwitchStatement => LabelTargetKind.Switch,
            _ => LabelTargetKind.Other
        };
    }

    private static bool TryResolveLabel(
        Stack<(Symbol Label, LabelTargetKind Kind)> labels,
        Symbol target,
        bool requireIteration)
    {
        foreach (var (label, kind) in labels)
        {
            if (!ReferenceEquals(label, target))
            {
                continue;
            }

            if (!requireIteration)
            {
                return true;
            }

            return kind == LabelTargetKind.Iteration;
        }

        return false;
    }

    private enum LabelTargetKind
    {
        Other,
        Iteration,
        Switch
    }
}
