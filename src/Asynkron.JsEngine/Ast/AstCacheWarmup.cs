#region

using Asynkron.JsEngine.Execution;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Eagerly computes AST caches so failures surface during parse, not during evaluation.
/// </summary>
internal static class AstCacheWarmup
{
    public static void Warm(ProgramNode program)
    {
        var visitor = new CacheWarmupVisitor();
        visitor.VisitProgram(program);
    }

    private sealed class CacheWarmupVisitor
    {
        public void VisitProgram(ProgramNode program)
        {
            foreach (var statement in program.Body)
            {
                VisitStatement(statement);
            }
        }

        private void VisitStatement(StatementNode statement)
        {
            while (true)
            {
                switch (statement)
                {
                    case BlockStatement block:
                        WarmBlockCaches(block);
                        foreach (var child in block.Statements)
                        {
                            VisitStatement(child);
                        }

                        return;
                    case ExpressionStatement expressionStatement:
                        VisitExpression(expressionStatement.Expression);
                        return;
                    case ReturnStatement returnStatement:
                        if (returnStatement.Expression is not null)
                        {
                            VisitExpression(returnStatement.Expression);
                        }

                        return;
                    case ThrowStatement throwStatement:
                        VisitExpression(throwStatement.Expression);
                        return;
                    case VariableDeclaration declaration:
                        foreach (var declarator in declaration.Declarators)
                        {
                            VisitBindingTarget(declarator.Target);
                            if (declarator.Initializer is not null)
                            {
                                VisitExpression(declarator.Initializer);
                            }
                        }

                        return;
                    case FunctionDeclaration functionDeclaration:
                        VisitFunction(functionDeclaration.Function);
                        return;
                    case IfStatement ifStatement:
                        VisitExpression(ifStatement.Condition);
                        VisitStatement(ifStatement.Then);
                        if (ifStatement.Else is not null)
                        {
                            VisitStatement(ifStatement.Else);
                        }

                        return;
                    case WhileStatement whileStatement:
                        WarmLoopCaches(whileStatement);
                        VisitExpression(whileStatement.Condition);
                        statement = whileStatement.Body;
                        continue;
                    case DoWhileStatement doWhileStatement:
                        WarmLoopCaches(doWhileStatement);
                        VisitStatement(doWhileStatement.Body);
                        VisitExpression(doWhileStatement.Condition);
                        return;
                    case ForStatement forStatement:
                        WarmLoopCaches(forStatement);
                        if (forStatement.Initializer is not null)
                        {
                            VisitStatement(forStatement.Initializer);
                        }

                        if (forStatement.Condition is not null)
                        {
                            VisitExpression(forStatement.Condition);
                        }

                        if (forStatement.Increment is not null)
                        {
                            VisitExpression(forStatement.Increment);
                        }

                        statement = forStatement.Body;
                        continue;
                    case ForEachStatement forEach:
                        WarmIteratorCaches(forEach);
                        VisitBindingTarget(forEach.Target);
                        VisitExpression(forEach.Iterable);
                        statement = forEach.Body;
                        continue;
                    case LabeledStatement labeled:
                        statement = labeled.Statement;
                        continue;
                    case TryStatement tryStatement:
                        VisitBlock(tryStatement.TryBlock);
                        if (tryStatement.Catch is not null)
                        {
                            VisitCatch(tryStatement.Catch);
                        }

                        if (tryStatement.Finally is not null)
                        {
                            VisitBlock(tryStatement.Finally);
                        }

                        return;
                    case SwitchStatement switchStatement:
                        WarmSwitchCaches(switchStatement);
                        VisitExpression(switchStatement.Discriminant);
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            if (switchCase.Test is not null)
                            {
                                VisitExpression(switchCase.Test);
                            }

                            VisitBlock(switchCase.Body);
                        }

                        return;
                    case WithStatement withStatement:
                        VisitExpression(withStatement.Object);
                        statement = withStatement.Body;
                        continue;
                    case ClassDeclaration classDeclaration:
                        VisitClassDefinition(classDeclaration.Definition);
                        return;
                    case ModuleStatement:
                    case BreakStatement:
                    case ContinueStatement:
                    case EmptyStatement:
                        return;
                    default:
                        return;
                }
            }
        }

        private void VisitBlock(BlockStatement block)
        {
            WarmBlockCaches(block);
            foreach (var statement in block.Statements)
            {
                VisitStatement(statement);
            }
        }

        private void VisitCatch(CatchClause clause)
        {
            VisitBlock(clause.Body);
        }

        private void VisitExpression(ExpressionNode expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case LiteralExpression:
                    case IdentifierExpression:
                    case ThisExpression:
                        return;
                    case BinaryExpression binary:
                        VisitExpression(binary.Left);
                        expression = binary.Right;
                        continue;
                    case UnaryExpression unary:
                        expression = unary.Operand;
                        continue;
                    case ConditionalExpression conditional:
                        VisitExpression(conditional.Test);
                        VisitExpression(conditional.Consequent);
                        expression = conditional.Alternate;
                        continue;
                    case CallExpression call:
                        VisitExpression(call.Callee);
                        foreach (var argument in call.Arguments)
                        {
                            VisitExpression(argument.Expression);
                        }

                        return;
                    case FunctionExpression function:
                        VisitFunction(function);
                        return;
                    case AssignmentExpression assignment:
                        expression = assignment.Value;
                        continue;
                    case PropertyAssignmentExpression propertyAssignment:
                        VisitExpression(propertyAssignment.Target);
                        VisitExpression(propertyAssignment.Property);
                        expression = propertyAssignment.Value;
                        continue;
                    case IndexAssignmentExpression indexAssignment:
                        VisitExpression(indexAssignment.Target);
                        VisitExpression(indexAssignment.Index);
                        expression = indexAssignment.Value;
                        continue;
                    case SequenceExpression sequence:
                        VisitExpression(sequence.Left);
                        expression = sequence.Right;
                        continue;
                    case MemberExpression member:
                        VisitExpression(member.Target);
                        expression = member.Property;
                        continue;
                    case NewExpression newExpression:
                        VisitExpression(newExpression.Constructor);
                        foreach (var argument in newExpression.Arguments)
                        {
                            VisitExpression(argument.Expression);
                        }

                        return;
                    case ArrayExpression arrayExpression:
                        foreach (var element in arrayExpression.Elements)
                        {
                            if (element.Expression is not null)
                            {
                                VisitExpression(element.Expression);
                            }
                        }

                        return;
                    case ObjectExpression objectExpression:
                        foreach (var member in objectExpression.Members)
                        {
                            VisitObjectMember(member);
                        }

                        return;
                    case ClassExpression classExpression:
                        VisitClassDefinition(classExpression.Definition);
                        return;
                    case TemplateLiteralExpression template:
                        foreach (var part in template.Parts)
                        {
                            if (part.Expression is not null)
                            {
                                VisitExpression(part.Expression);
                            }
                        }

                        return;
                    case TaggedTemplateExpression taggedTemplate:
                        VisitExpression(taggedTemplate.Tag);
                        VisitExpression(taggedTemplate.StringsArray);
                        VisitExpression(taggedTemplate.RawStringsArray);
                        foreach (var expr in taggedTemplate.Expressions)
                        {
                            VisitExpression(expr);
                        }

                        return;
                    case DestructuringAssignmentExpression destructuringAssignment:
                        VisitBindingTarget(destructuringAssignment.Target);
                        expression = destructuringAssignment.Value;
                        continue;
                    case AwaitExpression awaitExpression:
                        WarmAwaitCache(awaitExpression);
                        expression = awaitExpression.Expression;
                        continue;
                    case YieldExpression yieldExpression:
                        if (yieldExpression.Expression is not null)
                        {
                            expression = yieldExpression.Expression;
                            continue;
                        }

                        return;
                    case SuperExpression:
                        return;
                    default:
                        return;
                }
            }
        }

        private void VisitObjectMember(ObjectMember member)
        {
            if (member.Value is not null)
            {
                VisitExpression(member.Value);
            }

            if (member.Function is not null)
            {
                VisitFunction(member.Function);
            }

            if (member.Key is ExpressionNode keyExpression)
            {
                VisitExpression(keyExpression);
            }
        }

        private void VisitClassDefinition(ClassDefinition definition)
        {
            if (definition.Extends is { } extends)
            {
                VisitExpression(extends);
            }

            VisitFunction(definition.Constructor);
            foreach (var member in definition.Members)
            {
                VisitFunction(member.Function);
            }

            foreach (var field in definition.Fields)
            {
                if (field.Initializer is not null)
                {
                    VisitExpression(field.Initializer);
                }
            }
        }

        private void VisitFunction(FunctionExpression function)
        {
            WarmFunctionCaches(function);
            foreach (var parameter in function.Parameters)
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

            VisitBlock(function.Body);
        }

        private void VisitBindingTarget(BindingTarget target)
        {
            while (true)
            {
                switch (target)
                {
                    case IdentifierBinding:
                        return;
                    case ArrayBinding arrayBinding:
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

                        return;
                    case ObjectBinding objectBinding:
                        foreach (var property in objectBinding.Properties)
                        {
                            VisitBindingTarget(property.Target);
                            if (property.DefaultValue is not null)
                            {
                                VisitExpression(property.DefaultValue);
                            }
                        }

                        if (objectBinding.RestElement is null)
                        {
                            return;
                        }

                        target = objectBinding.RestElement;
                        continue;

                    case AssignmentTargetBinding assignmentTargetBinding:
                        VisitExpression(assignmentTargetBinding.Expression);
                        return;
                    default:
                        return;
                }
            }
        }

        private static void WarmBlockCaches(BlockStatement block)
        {
            _ = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();
            _ = ((IAstCacheable<HoistableDeclarationsPlan>)block).GetOrCreateCache();
        }

        private static void WarmLoopCaches(StatementNode statement)
        {
            _ = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        }

        private static void WarmIteratorCaches(ForEachStatement statement)
        {
            _ = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
        }

        private static void WarmSwitchCaches(SwitchStatement statement)
        {
            _ = ((IAstCacheable<SwitchInstantiationPlan>)statement).GetOrCreateCache();
        }

        private static void WarmFunctionCaches(FunctionExpression function)
        {
            _ = ((IAstCacheable<FunctionParameterNamesPlan>)function).GetOrCreateCache();

            if (function.IsAsync || function.WasAsync || function.IsGenerator)
            {
                function.WarmGeneratorPlanCache();
            }
        }

        private static void WarmAwaitCache(AwaitExpression expression)
        {
            _ = ((IAstCacheable<Symbol>)expression).GetOrCreateCache();
        }
    }
}
