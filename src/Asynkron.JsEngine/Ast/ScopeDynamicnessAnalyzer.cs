using System.Collections.Immutable;

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

        return !ContainsWithOrDirectEval(function.Body);
    }

    /// <summary>
    /// Determines whether a program is free of dynamic scope features that would
    /// invalidate identifier binding caches.
    /// </summary>
    internal static bool AllowsIdentifierCaching(ProgramNode program)
    {
        var block = new BlockStatement(program.Source, program.Body, program.IsStrict);
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
        var work = new Stack<StatementNode>();
        work.Push(block);

        while (work.Count > 0)
        {
            var statement = work.Pop();
            switch (statement)
            {
                case WithStatement:
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
                        return true;
                    }

                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null &&
                        ContainsDirectEval(returnStatement.Expression))
                    {
                        return true;
                    }

                    break;
                case ThrowStatement throwStatement:
                    if (ContainsDirectEval(throwStatement.Expression))
                    {
                        return true;
                    }

                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (ContainsDirectEval(declarator.Target))
                        {
                            return true;
                        }

                        if (declarator.Initializer is not null &&
                            ContainsDirectEval(declarator.Initializer))
                        {
                            return true;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    if (ContainsDirectEval(ifStatement.Condition))
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
                    if (ContainsDirectEval(whileStatement.Condition))
                    {
                        return true;
                    }

                    work.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    if (ContainsDirectEval(doWhileStatement.Condition))
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
                        ContainsDirectEval(forStatement.Condition))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ContainsDirectEval(forStatement.Increment))
                    {
                        return true;
                    }

                    work.Push(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    if (ContainsDirectEval(forEachStatement.Target) ||
                        ContainsDirectEval(forEachStatement.Iterable))
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
                        if (catchClause.Binding is not null && ContainsDirectEval(catchClause.Binding))
                        {
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
                        return true;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null && ContainsDirectEval(switchCase.Test))
                        {
                            return true;
                        }

                        work.Push(switchCase.Body);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    if (classDeclaration.Definition.Extends is not null &&
                        ContainsDirectEval(classDeclaration.Definition.Extends))
                    {
                        return true;
                    }

                    foreach (var member in classDeclaration.Definition.Members)
                    {
                        if (member.ComputedName is not null &&
                            ContainsDirectEval(member.ComputedName))
                        {
                            return true;
                        }
                    }

                    foreach (var field in classDeclaration.Definition.Fields)
                    {
                        if (field.ComputedName is not null &&
                            ContainsDirectEval(field.ComputedName))
                        {
                            return true;
                        }

                        if (field.Initializer is not null &&
                            ContainsDirectEval(field.Initializer))
                        {
                            return true;
                        }
                    }

                    foreach (var staticBlock in classDeclaration.Definition.StaticBlocks)
                    {
                        if (ContainsWithOrDirectEval(staticBlock.Body))
                        {
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
                default:
                    break;
            }
        }

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
                        return true;
                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null &&
                        ContainsArgumentsReference(returnStatement.Expression))
                        return true;
                    break;
                case ThrowStatement throwStatement:
                    if (ContainsArgumentsReference(throwStatement.Expression))
                        return true;
                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ContainsArgumentsReference(declarator.Initializer))
                            return true;
                    }
                    break;
                case IfStatement ifStatement:
                    if (ContainsArgumentsReference(ifStatement.Condition))
                        return true;
                    work.Push(ifStatement.Then);
                    if (ifStatement.Else is not null)
                        work.Push(ifStatement.Else);
                    break;
                case WhileStatement whileStatement:
                    if (ContainsArgumentsReference(whileStatement.Condition))
                        return true;
                    work.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    if (ContainsArgumentsReference(doWhileStatement.Condition))
                        return true;
                    work.Push(doWhileStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                        work.Push(forStatement.Initializer);
                    if (forStatement.Condition is not null &&
                        ContainsArgumentsReference(forStatement.Condition))
                        return true;
                    if (forStatement.Increment is not null &&
                        ContainsArgumentsReference(forStatement.Increment))
                        return true;
                    work.Push(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    if (ContainsArgumentsReference(forEachStatement.Iterable))
                        return true;
                    work.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeledStatement:
                    work.Push(labeledStatement.Statement);
                    break;
                case TryStatement tryStatement:
                    work.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                        work.Push(catchClause.Body);
                    if (tryStatement.Finally is not null)
                        work.Push(tryStatement.Finally);
                    break;
                case SwitchStatement switchStatement:
                    if (ContainsArgumentsReference(switchStatement.Discriminant))
                        return true;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null && ContainsArgumentsReference(switchCase.Test))
                            return true;
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
                        work.Push(argument.Expression);
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
                        work.Push(argument.Expression);
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
                            work.Push(element.Expression);
                    }
                    break;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode keyExpression)
                            work.Push(keyExpression);
                        if (member.Value is not null)
                            work.Push(member.Value);
                    }
                    break;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                            work.Push(part.Expression);
                    }
                    break;
                case TaggedTemplateExpression tagged:
                    work.Push(tagged.Tag);
                    foreach (var expr in tagged.Expressions)
                        work.Push(expr);
                    break;
                case YieldExpression yield:
                    if (yield.Expression is not null)
                        work.Push(yield.Expression);
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
                        if (member.IsComputed && member.Key is ExpressionNode keyExpression)
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
                default:
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
                default:
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
                        return true;
                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null &&
                        ContainsInnerFunctionExpression(returnStatement.Expression))
                        return true;
                    break;
                case ThrowStatement throwStatement:
                    if (ContainsInnerFunctionExpression(throwStatement.Expression))
                        return true;
                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ContainsInnerFunctionExpression(declarator.Initializer))
                            return true;
                    }
                    break;
                case IfStatement ifStatement:
                    if (ContainsInnerFunctionExpression(ifStatement.Condition))
                        return true;
                    work.Push(ifStatement.Then);
                    if (ifStatement.Else is not null)
                        work.Push(ifStatement.Else);
                    break;
                case WhileStatement whileStatement:
                    if (ContainsInnerFunctionExpression(whileStatement.Condition))
                        return true;
                    work.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    if (ContainsInnerFunctionExpression(doWhileStatement.Condition))
                        return true;
                    work.Push(doWhileStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                        work.Push(forStatement.Initializer);
                    if (forStatement.Condition is not null &&
                        ContainsInnerFunctionExpression(forStatement.Condition))
                        return true;
                    if (forStatement.Increment is not null &&
                        ContainsInnerFunctionExpression(forStatement.Increment))
                        return true;
                    work.Push(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    if (ContainsInnerFunctionExpression(forEachStatement.Iterable))
                        return true;
                    work.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeledStatement:
                    work.Push(labeledStatement.Statement);
                    break;
                case TryStatement tryStatement:
                    work.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                        work.Push(catchClause.Body);
                    if (tryStatement.Finally is not null)
                        work.Push(tryStatement.Finally);
                    break;
                case SwitchStatement switchStatement:
                    if (ContainsInnerFunctionExpression(switchStatement.Discriminant))
                        return true;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null && ContainsInnerFunctionExpression(switchCase.Test))
                            return true;
                        work.Push(switchCase.Body);
                    }
                    break;
                // FunctionDeclaration creates a closure - the function itself captures the environment
                case FunctionDeclaration:
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
                        work.Push(argument.Expression);
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
                        work.Push(argument.Expression);
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
                            work.Push(element.Expression);
                    }
                    break;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode keyExpression)
                            work.Push(keyExpression);
                        // Methods in object literals are FunctionExpressions - check Value
                        if (member.Value is not null)
                            work.Push(member.Value);
                        // Getters/setters are functions too
                        if (member.Function is not null)
                            return true;
                    }
                    break;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                            work.Push(part.Expression);
                    }
                    break;
                case TaggedTemplateExpression tagged:
                    work.Push(tagged.Tag);
                    foreach (var expr in tagged.Expressions)
                        work.Push(expr);
                    break;
                case YieldExpression yield:
                    if (yield.Expression is not null)
                        work.Push(yield.Expression);
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
}
