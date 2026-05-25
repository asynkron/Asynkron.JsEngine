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
        var labelStack = new Stack<(Symbol Label, LabelTargetKind Kind)>();
        foreach (var statement in statements)
        {
            if (TryGetFirstIllegalControlFlow(statement, labelStack, 0, 0, out message, out source))
            {
                return true;
            }
        }

        message = string.Empty;
        source = null;
        return false;
    }

    private static bool TryGetFirstIllegalControlFlow(
        StatementNode statement,
        Stack<(Symbol Label, LabelTargetKind Kind)> labels,
        int iterationDepth,
        int switchDepth,
        out string message,
        out SourceReference? source)
    {
        while (true)
        {
            switch (statement)
            {
                case BreakStatement breakStatement:
                    if (breakStatement.Label is null)
                    {
                        if (iterationDepth == 0 && switchDepth == 0)
                        {
                            message = "Illegal break statement.";
                            source = breakStatement.Source;
                            return true;
                        }

                        break;
                    }

                    if (!TryResolveLabel(labels, breakStatement.Label, requireIteration: false))
                    {
                        message = $"Undefined label '{breakStatement.Label.Name}' for break statement.";
                        source = breakStatement.Source;
                        return true;
                    }

                    break;
                case ContinueStatement continueStatement:
                    if (continueStatement.Label is null)
                    {
                        if (iterationDepth == 0)
                        {
                            message = "Illegal continue statement.";
                            source = continueStatement.Source;
                            return true;
                        }

                        break;
                    }

                    if (!TryResolveLabel(labels, continueStatement.Label, requireIteration: true))
                    {
                        message = $"Undefined iteration label '{continueStatement.Label.Name}' for continue statement.";
                        source = continueStatement.Source;
                        return true;
                    }

                    break;
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        if (TryGetFirstIllegalControlFlow(inner, labels, iterationDepth, switchDepth, out message,
                                out source))
                        {
                            return true;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    if (TryGetFirstIllegalControlFlow(ifStatement.Then, labels, iterationDepth, switchDepth,
                            out message, out source))
                    {
                        return true;
                    }

                    if (ifStatement.Else is not null &&
                        TryGetFirstIllegalControlFlow(ifStatement.Else, labels, iterationDepth, switchDepth,
                            out message, out source))
                    {
                        return true;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    iterationDepth++;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    iterationDepth++;
                    continue;
                case ForStatement forStatement:
                    statement = forStatement.Body;
                    iterationDepth++;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    iterationDepth++;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var @case in switchStatement.Cases)
                    {
                        if (TryGetFirstIllegalControlFlow(@case.Body, labels, iterationDepth, switchDepth + 1,
                                out message, out source))
                        {
                            return true;
                        }
                    }

                    break;
                case TryStatement tryStatement:
                    if (TryGetFirstIllegalControlFlow(tryStatement.TryBlock, labels, iterationDepth, switchDepth,
                            out message, out source))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause &&
                        TryGetFirstIllegalControlFlow(catchClause.Body, labels, iterationDepth, switchDepth,
                            out message, out source))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null &&
                        TryGetFirstIllegalControlFlow(tryStatement.Finally, labels, iterationDepth, switchDepth,
                            out message, out source))
                    {
                        return true;
                    }

                    break;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
                case LabeledStatement labeledStatement:
                    var targetKind = GetLabelTargetKind(labeledStatement.Statement);
                    labels.Push((labeledStatement.Label, targetKind));
                    var invalid = TryGetFirstIllegalControlFlow(
                        labeledStatement.Statement,
                        labels,
                        iterationDepth,
                        switchDepth,
                        out message,
                        out source);
                    labels.Pop();
                    if (invalid)
                    {
                        return true;
                    }

                    break;
            }

            break;
        }

        message = string.Empty;
        source = null;
        return false;
    }

    private static LabelTargetKind GetLabelTargetKind(StatementNode statement)
    {
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
