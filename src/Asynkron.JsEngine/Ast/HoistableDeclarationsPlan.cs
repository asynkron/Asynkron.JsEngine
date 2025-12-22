namespace Asynkron.JsEngine.Ast;

internal sealed class HoistableDeclarationsPlan
{
    private HoistableDeclarationsPlan(bool hasHoistableDeclarations)
    {
        HasHoistableDeclarations = hasHoistableDeclarations;
    }

    internal bool HasHoistableDeclarations { get; }

    internal static HoistableDeclarationsPlan Build(BlockStatement block)
    {
        var stack = new Stack<StatementNode>();
        stack.Push(block);

        while (stack.Count > 0)
        {
            var statement = stack.Pop();
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var }:
                case FunctionDeclaration:
                    return new HoistableDeclarationsPlan(true);
                case BlockStatement innerBlock:
                    foreach (var inner in innerBlock.Statements)
                    {
                        stack.Push(inner);
                    }

                    break;
                case IfStatement ifStatement:
                    stack.Push(ifStatement.Then);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        stack.Push(elseBranch);
                    }

                    break;
                case WhileStatement whileStatement:
                    stack.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    stack.Push(doWhileStatement.Body);
                    break;
                case WithStatement withStatement:
                    stack.Push(withStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var })
                    {
                        return new HoistableDeclarationsPlan(true);
                    }

                    if (forStatement.Body is not null)
                    {
                        stack.Push(forStatement.Body);
                    }

                    break;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind == VariableKind.Var)
                    {
                        return new HoistableDeclarationsPlan(true);
                    }

                    stack.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeled:
                    stack.Push(labeled.Statement);
                    break;
                case TryStatement tryStatement:
                    stack.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        stack.Push(catchClause.Body);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        stack.Push(finallyBlock);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        stack.Push(switchCase.Body);
                    }

                    break;
            }
        }

        return new HoistableDeclarationsPlan(false);
    }
}
