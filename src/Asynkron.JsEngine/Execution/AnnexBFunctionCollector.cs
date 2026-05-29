#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Collects sloppy-mode block-level function declaration names per ES2024 Annex B.3.2.
/// </summary>
internal static class AnnexBFunctionCollector
{
    internal static HashSet<Symbol> CollectBlockFunctionNames(BlockStatement body)
    {
        var result = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var statement in body.Statements)
        {
            CollectFromStatement(statement, result, body.IsStrict, inBlockScope: false);
        }

        return result;
    }

    private static void CollectFromStatement(StatementNode statement, HashSet<Symbol> sink, bool isStrict, bool inBlockScope)
    {
        while (true)
        {
            switch (statement)
            {
                case FunctionDeclaration functionDeclaration:
                    if (!isStrict && inBlockScope &&
                        !functionDeclaration.Function.IsAsync &&
                        !functionDeclaration.Function.WasAsync &&
                        !functionDeclaration.Function.IsGenerator)
                    {
                        sink.Add(functionDeclaration.Name);
                    }

                    return;
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectFromStatement(inner, sink, isStrict, inBlockScope: true);
                    }

                    return;
                case IfStatement ifStatement:
                    CollectFromStatement(ifStatement.Then, sink, isStrict, inBlockScope: true);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }

                    return;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
                case ForStatement forStatement:
                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectFromStatement(switchCase.Body, sink, isStrict, inBlockScope: true);
                    }

                    return;
                case TryStatement tryStatement:
                    CollectFromStatement(tryStatement.TryBlock, sink, isStrict, inBlockScope: true);
                    if (tryStatement.Catch?.Body is { } catchBody)
                    {
                        CollectFromStatement(catchBody, sink, isStrict, inBlockScope: true);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        CollectFromStatement(finallyBlock, sink, isStrict, inBlockScope: true);
                    }

                    return;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                default:
                    return;
            }
        }
    }
}
