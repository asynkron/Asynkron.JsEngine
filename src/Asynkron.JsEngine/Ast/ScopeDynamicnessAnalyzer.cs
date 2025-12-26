#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Determines whether a function body is free of dynamic scope features
    /// (direct eval / with) that would invalidate identifier binding caches.
    /// Conservative: any direct eval or with anywhere in the function's
    /// evaluated expressions disables caching.
    /// </summary>
    internal static bool AllowsIdentifierCaching(FunctionExpression function)
    {
        if (ContainsDirectEvalOrWithInParameters(function.Parameters))
        {
            return false;
        }

        return AllowsIdentifierCaching(function.Body);
    }

    /// <summary>
    /// Determines whether a program is free of dynamic scope features that would
    /// invalidate identifier binding caches.
    /// </summary>
    internal static bool AllowsIdentifierCaching(ProgramNode program)
    {
        var block = new BlockStatement(program.Source, program.Body, program.IsStrict);
        return AllowsIdentifierCaching(block);
    }

    /// <summary>
    /// Determines whether a block is free of dynamic scope features (with/direct eval).
    /// </summary>
    internal static bool AllowsIdentifierCaching(BlockStatement block)
    {
        return !ContainsWithOrDirectEval(block);
    }

    private static bool ContainsDirectEvalOrWithInParameters(ImmutableArray<FunctionParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.DefaultValue is not null && ContainsDirectEval(parameter.DefaultValue))
            {
                return true;
            }

            if (parameter.Pattern is not null && ContainsDirectEval(parameter.Pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWithOrDirectEval(BlockStatement block)
    {
        if (block.TryGetContainsDynamicScope(out var cached))
        {
            return cached;
        }

        var work = new Stack<StatementNode>();
        work.Push(block);

        while (work.Count > 0)
        {
            var statement = work.Pop();
            switch (statement)
            {
                case WithStatement:
                    block.CacheContainsDynamicScope(true);
                    return true;
                case BlockStatement innerBlock:
                    foreach (var inner in innerBlock.Statements)
                    {
                        work.Push(inner);
                    }

                    break;
                case ExpressionStatement expressionStatement:
                    if (ContainsDirectEval(expressionStatement.Expression))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null &&
                        ContainsDirectEval(returnStatement.Expression))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    break;
                case ThrowStatement throwStatement:
                    if (ContainsDirectEval(throwStatement.Expression))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (ContainsDirectEval(declarator.Target))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }

                        if (declarator.Initializer is not null &&
                            ContainsDirectEval(declarator.Initializer))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    if (ContainsDirectEval(ifStatement.Condition))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    work.Push(ifStatement.Then);
                    if (ifStatement.Else is not null)
                    {
                        work.Push(ifStatement.Else);
                    }

                    break;
                case WhileStatement whileStatement:
                    if (ContainsDirectEval(whileStatement.Condition))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    work.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    if (ContainsDirectEval(doWhileStatement.Condition))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    work.Push(doWhileStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                    {
                        work.Push(forStatement.Initializer);
                    }

                    if (forStatement.Condition is not null &&
                        ContainsDirectEval(forStatement.Condition))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ContainsDirectEval(forStatement.Increment))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    work.Push(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    if (ContainsDirectEval(forEachStatement.Target) ||
                        ContainsDirectEval(forEachStatement.Iterable))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    work.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeledStatement:
                    work.Push(labeledStatement.Statement);
                    break;
                case TryStatement tryStatement:
                    work.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is not null && ContainsDirectEval(catchClause.Binding))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }

                        work.Push(catchClause.Body);
                    }

                    if (tryStatement.Finally is not null)
                    {
                        work.Push(tryStatement.Finally);
                    }

                    break;
                case SwitchStatement switchStatement:
                    if (ContainsDirectEval(switchStatement.Discriminant))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null && ContainsDirectEval(switchCase.Test))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }

                        work.Push(switchCase.Body);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    if (classDeclaration.Definition.Extends is not null &&
                        ContainsDirectEval(classDeclaration.Definition.Extends))
                    {
                        block.CacheContainsDynamicScope(true);
                        return true;
                    }

                    foreach (var member in classDeclaration.Definition.Members)
                    {
                        if (member.ComputedName is not null &&
                            ContainsDirectEval(member.ComputedName))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }
                    }

                    foreach (var field in classDeclaration.Definition.Fields)
                    {
                        if (field.ComputedName is not null &&
                            ContainsDirectEval(field.ComputedName))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }

                        if (field.Initializer is not null &&
                            ContainsDirectEval(field.Initializer))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }
                    }

                    foreach (var staticBlock in classDeclaration.Definition.StaticBlocks)
                    {
                        if (ContainsWithOrDirectEval(staticBlock.Body))
                        {
                            block.CacheContainsDynamicScope(true);
                            return true;
                        }
                    }

                    break;
                case ExportDefaultStatement exportDefault:
                    switch (exportDefault.Value)
                    {
                        case ExportDefaultExpression exportExpression:
                            if (ContainsDirectEval(exportExpression.Expression))
                            {
                                block.CacheContainsDynamicScope(true);
                                return true;
                            }

                            break;
                        case ExportDefaultDeclaration exportDeclaration:
                            work.Push(exportDeclaration.Declaration);
                            break;
                    }

                    break;
                case ExportDeclarationStatement exportDeclarationStatement:
                    work.Push(exportDeclarationStatement.Declaration);
                    break;
                case FunctionDeclaration:
                case EmptyStatement:
                case BreakStatement:
                case ContinueStatement:
                case ImportStatement:
                case ExportNamedStatement:
                case ExportAllStatement:
                case ExportNamespaceAsStatement:
                    break;
            }
        }

        block.CacheContainsDynamicScope(false);
        return false;
    }

    /// <summary>
    /// Determines whether a function body references the 'arguments' identifier.
    /// Used to skip creating the arguments object when it's not needed.
    /// Note: This doesn't cross function boundaries - nested functions have their own arguments.
    /// </summary>
    internal static bool UsesArgumentsIdentifier(FunctionExpression function)
    {
        return ContainsArgumentsReference(function.Body);
    }

    private static bool ContainsArgumentsReference(BlockStatement block)
    {
        var work = new Stack<StatementNode>();
        work.Push(block);

        while (work.Count > 0)
        {
            var statement = work.Pop();
            switch (statement)
            {
                case BlockStatement innerBlock:
                    foreach (var inner in innerBlock.Statements)
                    {
                        work.Push(inner);
                    }

                    break;
                case ExpressionStatement expressionStatement:
                    if (ContainsArgumentsReference(expressionStatement.Expression))
                    {
                        return true;
                    }

                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null &&
                        ContainsArgumentsReference(returnStatement.Expression))
                    {
                        return true;
                    }

                    break;
                case ThrowStatement throwStatement:
                    if (ContainsArgumentsReference(throwStatement.Expression))
                    {
                        return true;
                    }

                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ContainsArgumentsReference(declarator.Initializer))
                        {
                            return true;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    if (ContainsArgumentsReference(ifStatement.Condition))
                    {
                        return true;
                    }

                    work.Push(ifStatement.Then);
                    if (ifStatement.Else is not null)
                    {
                        work.Push(ifStatement.Else);
                    }

                    break;
                case WhileStatement whileStatement:
                    if (ContainsArgumentsReference(whileStatement.Condition))
                    {
                        return true;
                    }

                    work.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    if (ContainsArgumentsReference(doWhileStatement.Condition))
                    {
                        return true;
                    }

                    work.Push(doWhileStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                    {
                        work.Push(forStatement.Initializer);
                    }

                    if (forStatement.Condition is not null &&
                        ContainsArgumentsReference(forStatement.Condition))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ContainsArgumentsReference(forStatement.Increment))
                    {
                        return true;
                    }

                    work.Push(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    if (ContainsArgumentsReference(forEachStatement.Iterable))
                    {
                        return true;
                    }

                    work.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeledStatement:
                    work.Push(labeledStatement.Statement);
                    break;
                case TryStatement tryStatement:
                    work.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        work.Push(catchClause.Body);
                    }

                    if (tryStatement.Finally is not null)
                    {
                        work.Push(tryStatement.Finally);
                    }

                    break;
                case SwitchStatement switchStatement:
                    if (ContainsArgumentsReference(switchStatement.Discriminant))
                    {
                        return true;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null && ContainsArgumentsReference(switchCase.Test))
                        {
                            return true;
                        }

                        work.Push(switchCase.Body);
                    }

                    break;
                // Skip function declarations - they have their own arguments
                case FunctionDeclaration:
                case ClassDeclaration:
                case EmptyStatement:
                case BreakStatement:
                case ContinueStatement:
                case ImportStatement:
                case ExportNamedStatement:
                case ExportAllStatement:
                case ExportNamespaceAsStatement:
                case ExportDefaultStatement:
                case ExportDeclarationStatement:
                    break;
            }
        }

        return false;
    }

    private static bool ContainsArgumentsReference(ExpressionNode expression)
    {
        var work = new Stack<ExpressionNode>();
        work.Push(expression);

        while (work.Count > 0)
        {
            var node = work.Pop();
            switch (node)
            {
                case IdentifierExpression { Name.Name: "arguments" }:
                    return true;
                case CallExpression call:
                    work.Push(call.Callee);
                    foreach (var argument in call.Arguments)
                    {
                        work.Push(argument.Expression);
                    }

                    break;
                case BinaryExpression binary:
                    work.Push(binary.Left);
                    work.Push(binary.Right);
                    break;
                case UnaryExpression unary:
                    work.Push(unary.Operand);
                    break;
                case ConditionalExpression conditional:
                    work.Push(conditional.Test);
                    work.Push(conditional.Consequent);
                    work.Push(conditional.Alternate);
                    break;
                case NewExpression newExpression:
                    work.Push(newExpression.Constructor);
                    foreach (var argument in newExpression.Arguments)
                    {
                        work.Push(argument.Expression);
                    }

                    break;
                case MemberExpression member:
                    work.Push(member.Target);
                    work.Push(member.Property);
                    break;
                case AssignmentExpression assignment:
                    work.Push(assignment.Value);
                    break;
                case PropertyAssignmentExpression propertyAssignment:
                    work.Push(propertyAssignment.Target);
                    work.Push(propertyAssignment.Property);
                    work.Push(propertyAssignment.Value);
                    break;
                case IndexAssignmentExpression indexAssignment:
                    work.Push(indexAssignment.Target);
                    work.Push(indexAssignment.Index);
                    work.Push(indexAssignment.Value);
                    break;
                case SequenceExpression sequence:
                    work.Push(sequence.Left);
                    work.Push(sequence.Right);
                    break;
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            work.Push(element.Expression);
                        }
                    }

                    break;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member is { IsComputed: true, Key: ExpressionNode keyExpression })
                        {
                            work.Push(keyExpression);
                        }

                        if (member.Value is not null)
                        {
                            work.Push(member.Value);
                        }
                    }

                    break;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            work.Push(part.Expression);
                        }
                    }

                    break;
                case TaggedTemplateExpression tagged:
                    work.Push(tagged.Tag);
                    foreach (var expr in tagged.Expressions)
                    {
                        work.Push(expr);
                    }

                    break;
                case YieldExpression yield:
                    if (yield.Expression is not null)
                    {
                        work.Push(yield.Expression);
                    }

                    break;
                case AwaitExpression awaitExpression:
                    work.Push(awaitExpression.Expression);
                    break;
                // Don't recurse into nested functions - they have their own arguments
                case FunctionExpression:
                case ClassExpression:
                case LiteralExpression:
                case PrivateIdentifierExpression:
                case NewTargetExpression:
                case ImportMetaExpression:
                case ThisExpression:
                case SuperExpression:
                case IdentifierExpression: // Non-arguments identifiers
                    break;
            }
        }

        return false;
    }

    private static bool ContainsDirectEval(ExpressionNode expression)
    {
        var work = new Stack<ExpressionNode>();
        work.Push(expression);

        while (work.Count > 0)
        {
            var node = work.Pop();
            switch (node)
            {
                case CallExpression { IsOptional: false, Callee: IdentifierExpression { Name.Name: "eval" } }:
                    return true;
                case CallExpression call:
                    work.Push(call.Callee);
                    foreach (var argument in call.Arguments)
                    {
                        work.Push(argument.Expression);
                    }

                    break;
                case DecoratorExpression decorator:
                    work.Push(decorator.Expression);
                    break;
                case BinaryExpression binary:
                    work.Push(binary.Left);
                    work.Push(binary.Right);
                    break;
                case UnaryExpression unary:
                    work.Push(unary.Operand);
                    break;
                case ConditionalExpression conditional:
                    work.Push(conditional.Test);
                    work.Push(conditional.Consequent);
                    work.Push(conditional.Alternate);
                    break;
                case NewExpression newExpression:
                    work.Push(newExpression.Constructor);
                    foreach (var argument in newExpression.Arguments)
                    {
                        work.Push(argument.Expression);
                    }

                    break;
                case MemberExpression member:
                    work.Push(member.Target);
                    work.Push(member.Property);
                    break;
                case AssignmentExpression assignment:
                    work.Push(assignment.Value);
                    break;
                case PropertyAssignmentExpression propertyAssignment:
                    work.Push(propertyAssignment.Target);
                    work.Push(propertyAssignment.Property);
                    work.Push(propertyAssignment.Value);
                    break;
                case IndexAssignmentExpression indexAssignment:
                    work.Push(indexAssignment.Target);
                    work.Push(indexAssignment.Index);
                    work.Push(indexAssignment.Value);
                    break;
                case SequenceExpression sequence:
                    work.Push(sequence.Left);
                    work.Push(sequence.Right);
                    break;
                case DestructuringAssignmentExpression destructuring:
                    if (ContainsDirectEval(destructuring.Target))
                    {
                        return true;
                    }

                    work.Push(destructuring.Value);
                    break;
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            work.Push(element.Expression);
                        }
                    }

                    break;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member is { IsComputed: true, Key: ExpressionNode keyExpression })
                        {
                            work.Push(keyExpression);
                        }

                        if (member.Value is not null)
                        {
                            work.Push(member.Value);
                        }
                    }

                    break;
                case ClassExpression classExpression:
                    if (classExpression.Definition.Extends is not null &&
                        ContainsDirectEval(classExpression.Definition.Extends))
                    {
                        return true;
                    }

                    foreach (var member in classExpression.Definition.Members)
                    {
                        if (member.ComputedName is not null &&
                            ContainsDirectEval(member.ComputedName))
                        {
                            return true;
                        }
                    }

                    foreach (var field in classExpression.Definition.Fields)
                    {
                        if (field.ComputedName is not null && ContainsDirectEval(field.ComputedName))
                        {
                            return true;
                        }

                        if (field.Initializer is not null && ContainsDirectEval(field.Initializer))
                        {
                            return true;
                        }
                    }

                    foreach (var staticBlock in classExpression.Definition.StaticBlocks)
                    {
                        if (ContainsWithOrDirectEval(staticBlock.Body))
                        {
                            return true;
                        }
                    }

                    break;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            work.Push(part.Expression);
                        }
                    }

                    break;
                case TaggedTemplateExpression tagged:
                    work.Push(tagged.Tag);
                    work.Push(tagged.StringsArray);
                    work.Push(tagged.RawStringsArray);
                    foreach (var expr in tagged.Expressions)
                    {
                        work.Push(expr);
                    }

                    break;
                case YieldExpression yield:
                    if (yield.Expression is not null)
                    {
                        work.Push(yield.Expression);
                    }

                    break;
                case AwaitExpression awaitExpression:
                    work.Push(awaitExpression.Expression);
                    break;
                case FunctionExpression:
                case LiteralExpression:
                case IdentifierExpression:
                case PrivateIdentifierExpression:
                case NewTargetExpression:
                case ImportMetaExpression:
                case ThisExpression:
                case SuperExpression:
                    break;
            }
        }

        return false;
    }

    private static bool ContainsDirectEval(BindingTarget target)
    {
        var work = new Stack<BindingTarget>();
        work.Push(target);

        while (work.Count > 0)
        {
            var node = work.Pop();
            switch (node)
            {
                case IdentifierBinding:
                    break;
                case AssignmentTargetBinding assignmentTarget:
                    if (ContainsDirectEval(assignmentTarget.Expression))
                    {
                        return true;
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.DefaultValue is not null &&
                            ContainsDirectEval(element.DefaultValue))
                        {
                            return true;
                        }

                        if (element.Target is not null)
                        {
                            work.Push(element.Target);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        work.Push(arrayBinding.RestElement);
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        if (property.NameExpression is not null &&
                            ContainsDirectEval(property.NameExpression))
                        {
                            return true;
                        }

                        if (property.DefaultValue is not null &&
                            ContainsDirectEval(property.DefaultValue))
                        {
                            return true;
                        }

                        work.Push(property.Target);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        work.Push(objectBinding.RestElement);
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a function body contains inner function expressions (including arrows).
    /// Used to detect if the function creates closures that would capture the invocation environment.
    /// If no inner functions exist, the invocation environment can be pooled for reuse.
    /// </summary>
    internal static bool ContainsInnerFunctionExpression(FunctionExpression function)
    {
        return ContainsInnerFunctionExpression(function.Body);
    }

    internal static bool ContainsInnerFunctionExpression(BlockStatement block)
    {
        if (block.TryGetContainsInnerFunction(out var cached))
        {
            return cached;
        }

        var work = new Stack<StatementNode>();
        work.Push(block);

        while (work.Count > 0)
        {
            var statement = work.Pop();
            switch (statement)
            {
                case BlockStatement innerBlock:
                    foreach (var inner in innerBlock.Statements)
                    {
                        work.Push(inner);
                    }

                    break;
                case ExpressionStatement expressionStatement:
                    if (ContainsInnerFunctionExpression(expressionStatement.Expression))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null &&
                        ContainsInnerFunctionExpression(returnStatement.Expression))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    break;
                case ThrowStatement throwStatement:
                    if (ContainsInnerFunctionExpression(throwStatement.Expression))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ContainsInnerFunctionExpression(declarator.Initializer))
                        {
                            block.CacheContainsInnerFunction(true);
                            return true;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    if (ContainsInnerFunctionExpression(ifStatement.Condition))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    work.Push(ifStatement.Then);
                    if (ifStatement.Else is not null)
                    {
                        work.Push(ifStatement.Else);
                    }

                    break;
                case WhileStatement whileStatement:
                    if (ContainsInnerFunctionExpression(whileStatement.Condition))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    work.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    if (ContainsInnerFunctionExpression(doWhileStatement.Condition))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    work.Push(doWhileStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                    {
                        work.Push(forStatement.Initializer);
                    }

                    if (forStatement.Condition is not null &&
                        ContainsInnerFunctionExpression(forStatement.Condition))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ContainsInnerFunctionExpression(forStatement.Increment))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    work.Push(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    if (ContainsInnerFunctionExpression(forEachStatement.Iterable))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    // Also check the destructuring target for function expressions in defaults
                    if (ContainsInnerFunctionExpression(forEachStatement.Target))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    work.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeledStatement:
                    work.Push(labeledStatement.Statement);
                    break;
                case TryStatement tryStatement:
                    work.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        work.Push(catchClause.Body);
                    }

                    if (tryStatement.Finally is not null)
                    {
                        work.Push(tryStatement.Finally);
                    }

                    break;
                case SwitchStatement switchStatement:
                    if (ContainsInnerFunctionExpression(switchStatement.Discriminant))
                    {
                        block.CacheContainsInnerFunction(true);
                        return true;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null && ContainsInnerFunctionExpression(switchCase.Test))
                        {
                            block.CacheContainsInnerFunction(true);
                            return true;
                        }

                        work.Push(switchCase.Body);
                    }

                    break;
                // FunctionDeclaration creates a closure - the function itself captures the environment
                case FunctionDeclaration:
                    block.CacheContainsInnerFunction(true);
                    return true;
                case ClassDeclaration:
                case EmptyStatement:
                case BreakStatement:
                case ContinueStatement:
                case ImportStatement:
                case ExportNamedStatement:
                case ExportAllStatement:
                case ExportNamespaceAsStatement:
                case ExportDefaultStatement:
                case ExportDeclarationStatement:
                    break;
            }
        }

        block.CacheContainsInnerFunction(false);
        return false;
    }

    internal static bool ContainsInnerFunctionExpression(ExpressionNode expression)
    {
        var work = new Stack<ExpressionNode>();
        work.Push(expression);

        while (work.Count > 0)
        {
            var node = work.Pop();
            switch (node)
            {
                // These create closures - return true immediately
                case FunctionExpression:
                case ClassExpression:
                    return true;
                case CallExpression call:
                    work.Push(call.Callee);
                    foreach (var argument in call.Arguments)
                    {
                        work.Push(argument.Expression);
                    }

                    break;
                case BinaryExpression binary:
                    work.Push(binary.Left);
                    work.Push(binary.Right);
                    break;
                case UnaryExpression unary:
                    work.Push(unary.Operand);
                    break;
                case ConditionalExpression conditional:
                    work.Push(conditional.Test);
                    work.Push(conditional.Consequent);
                    work.Push(conditional.Alternate);
                    break;
                case NewExpression newExpression:
                    work.Push(newExpression.Constructor);
                    foreach (var argument in newExpression.Arguments)
                    {
                        work.Push(argument.Expression);
                    }

                    break;
                case MemberExpression member:
                    work.Push(member.Target);
                    work.Push(member.Property);
                    break;
                case AssignmentExpression assignment:
                    work.Push(assignment.Value);
                    break;
                case PropertyAssignmentExpression propertyAssignment:
                    work.Push(propertyAssignment.Target);
                    work.Push(propertyAssignment.Property);
                    work.Push(propertyAssignment.Value);
                    break;
                case IndexAssignmentExpression indexAssignment:
                    work.Push(indexAssignment.Target);
                    work.Push(indexAssignment.Index);
                    work.Push(indexAssignment.Value);
                    break;
                case SequenceExpression sequence:
                    work.Push(sequence.Left);
                    work.Push(sequence.Right);
                    break;
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            work.Push(element.Expression);
                        }
                    }

                    break;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member is { IsComputed: true, Key: ExpressionNode keyExpression })
                        {
                            work.Push(keyExpression);
                        }

                        // Methods in object literals are FunctionExpressions - check Value
                        if (member.Value is not null)
                        {
                            work.Push(member.Value);
                        }

                        // Getters/setters are functions too
                        if (member.Function is not null)
                        {
                            return true;
                        }
                    }

                    break;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            work.Push(part.Expression);
                        }
                    }

                    break;
                case TaggedTemplateExpression tagged:
                    work.Push(tagged.Tag);
                    foreach (var expr in tagged.Expressions)
                    {
                        work.Push(expr);
                    }

                    break;
                case YieldExpression yield:
                    if (yield.Expression is not null)
                    {
                        work.Push(yield.Expression);
                    }

                    break;
                case AwaitExpression awaitExpression:
                    work.Push(awaitExpression.Expression);
                    break;
                case DestructuringAssignmentExpression destructuring:
                    work.Push(destructuring.Value);
                    break;
                // These don't contain inner functions
                case LiteralExpression:
                case IdentifierExpression:
                case PrivateIdentifierExpression:
                case NewTargetExpression:
                case ImportMetaExpression:
                case ThisExpression:
                case SuperExpression:
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a binding target (destructuring pattern) contains inner function expressions.
    /// Used to detect if closures in destructuring defaults would capture the iteration environment.
    /// </summary>
    internal static bool ContainsInnerFunctionExpression(BindingTarget target)
    {
        var work = new Stack<BindingTarget>();
        work.Push(target);

        while (work.Count > 0)
        {
            var node = work.Pop();
            switch (node)
            {
                case IdentifierBinding:
                    break;
                case AssignmentTargetBinding assignmentTarget:
                    if (ContainsInnerFunctionExpression(assignmentTarget.Expression))
                    {
                        return true;
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        // Check default values for function expressions
                        if (element.DefaultValue is not null &&
                            ContainsInnerFunctionExpression(element.DefaultValue))
                        {
                            return true;
                        }

                        if (element.Target is not null)
                        {
                            work.Push(element.Target);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        work.Push(arrayBinding.RestElement);
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        // Check computed property names for function expressions
                        if (property.NameExpression is not null &&
                            ContainsInnerFunctionExpression(property.NameExpression))
                        {
                            return true;
                        }

                        // Check default values for function expressions
                        if (property.DefaultValue is not null &&
                            ContainsInnerFunctionExpression(property.DefaultValue))
                        {
                            return true;
                        }

                        work.Push(property.Target);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        work.Push(objectBinding.RestElement);
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a function body contains any CallExpression where the callee is an identifier
    /// that is not one of the function's parameters. This detects recursive calls and calls
    /// to closure-captured functions which cannot use environment reuse optimization.
    /// </summary>
    internal static bool ContainsNonParameterCalleeIdentifier(FunctionExpression function,
        HashSet<Symbol> parameterNames)
    {
        var work = new Stack<AstNode>();
        work.Push(function.Body);

        while (work.Count > 0)
        {
            var node = work.Pop();
            switch (node)
            {
                case BlockStatement block:
                    foreach (var stmt in block.Statements)
                    {
                        work.Push(stmt);
                    }

                    break;
                case ExpressionStatement exprStmt:
                    work.Push(exprStmt.Expression);
                    break;
                case ReturnStatement ret:
                    if (ret.Expression is not null)
                    {
                        work.Push(ret.Expression);
                    }

                    break;
                case IfStatement ifStmt:
                    work.Push(ifStmt.Condition);
                    work.Push(ifStmt.Then);
                    if (ifStmt.Else is not null)
                    {
                        work.Push(ifStmt.Else);
                    }

                    break;
                case WhileStatement whileStmt:
                    work.Push(whileStmt.Condition);
                    work.Push(whileStmt.Body);
                    break;
                case DoWhileStatement doWhileStmt:
                    work.Push(doWhileStmt.Condition);
                    work.Push(doWhileStmt.Body);
                    break;
                case ForStatement forStmt:
                    if (forStmt.Initializer is not null)
                    {
                        work.Push(forStmt.Initializer);
                    }

                    if (forStmt.Condition is not null)
                    {
                        work.Push(forStmt.Condition);
                    }

                    if (forStmt.Increment is not null)
                    {
                        work.Push(forStmt.Increment);
                    }

                    work.Push(forStmt.Body);
                    break;
                case ForEachStatement forEachStmt:
                    work.Push(forEachStmt.Iterable);
                    work.Push(forEachStmt.Body);
                    break;
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            work.Push(declarator.Initializer);
                        }
                    }

                    break;
                case TryStatement tryStmt:
                    work.Push(tryStmt.TryBlock);
                    if (tryStmt.Catch is { } catchClause)
                    {
                        work.Push(catchClause.Body);
                    }

                    if (tryStmt.Finally is not null)
                    {
                        work.Push(tryStmt.Finally);
                    }

                    break;
                case SwitchStatement switchStmt:
                    work.Push(switchStmt.Discriminant);
                    foreach (var switchCase in switchStmt.Cases)
                    {
                        if (switchCase.Test is not null)
                        {
                            work.Push(switchCase.Test);
                        }

                        work.Push(switchCase.Body);
                    }

                    break;
                case ThrowStatement throwStmt:
                    work.Push(throwStmt.Expression);
                    break;
                case LabeledStatement labeledStmt:
                    work.Push(labeledStmt.Statement);
                    break;

                // Key check: CallExpression with identifier callee
                case CallExpression call:
                    if (call.Callee is IdentifierExpression calleeId &&
                        !parameterNames.Contains(calleeId.Name))
                    {
                        // The callee is an identifier that's not a parameter - this is a call
                        // to a closure-captured function (like recursive calls or outer variables)
                        return true;
                    }

                    work.Push(call.Callee);
                    foreach (var arg in call.Arguments)
                    {
                        work.Push(arg.Expression);
                    }

                    break;

                // Other expressions - continue walking
                case BinaryExpression binary:
                    work.Push(binary.Left);
                    work.Push(binary.Right);
                    break;
                case UnaryExpression unary:
                    work.Push(unary.Operand);
                    break;
                case ConditionalExpression cond:
                    work.Push(cond.Test);
                    work.Push(cond.Consequent);
                    work.Push(cond.Alternate);
                    break;
                case AssignmentExpression assign:
                    work.Push(assign.Value);
                    break;
                case PropertyAssignmentExpression propAssign:
                    work.Push(propAssign.Target);
                    work.Push(propAssign.Property);
                    work.Push(propAssign.Value);
                    break;
                case IndexAssignmentExpression indexAssign:
                    work.Push(indexAssign.Target);
                    work.Push(indexAssign.Index);
                    work.Push(indexAssign.Value);
                    break;
                case MemberExpression member:
                    work.Push(member.Target);
                    work.Push(member.Property);
                    break;
                case NewExpression newExpr:
                    work.Push(newExpr.Constructor);
                    foreach (var arg in newExpr.Arguments)
                    {
                        work.Push(arg.Expression);
                    }

                    break;
                case ArrayExpression array:
                    foreach (var elem in array.Elements)
                    {
                        if (elem.Expression is not null)
                        {
                            work.Push(elem.Expression);
                        }
                    }

                    break;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null)
                        {
                            work.Push(member.Value);
                        }

                        if (member.Function is not null)
                        {
                            continue; // Skip inner functions - they have their own scope
                        }
                    }

                    break;
                case SequenceExpression seq:
                    work.Push(seq.Left);
                    work.Push(seq.Right);
                    break;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            work.Push(part.Expression);
                        }
                    }

                    break;
                case TaggedTemplateExpression tagged:
                    work.Push(tagged.Tag);
                    foreach (var expr in tagged.Expressions)
                    {
                        work.Push(expr);
                    }

                    break;
                case AwaitExpression awaitExpr:
                    work.Push(awaitExpr.Expression);
                    break;
                case YieldExpression yieldExpr:
                    if (yieldExpr.Expression is not null)
                    {
                        work.Push(yieldExpr.Expression);
                    }

                    break;

                // Skip inner functions - they have their own scope
                case FunctionExpression:
                case ClassExpression:
                case FunctionDeclaration:
                case ClassDeclaration:
                    break;

                // Terminals - no action needed
                case LiteralExpression:
                case IdentifierExpression:
                case PrivateIdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                case NewTargetExpression:
                case ImportMetaExpression:
                case EmptyStatement:
                case BreakStatement:
                case ContinueStatement:
                    break;
            }
        }

        return false;
    }
}
