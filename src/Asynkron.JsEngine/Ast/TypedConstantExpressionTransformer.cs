using System.Collections.Immutable;
using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Performs constant folding directly on the typed AST so optimization passes
///     no longer need to bounce back through cons cells.
/// </summary>
public sealed class TypedConstantExpressionTransformer
{
    /// <summary>
    ///     Traverses the program and folds any constant expressions it encounters.
    ///     The original <see cref="ProgramNode" /> is returned when no changes were made.
    /// </summary>
    public ProgramNode Transform(ProgramNode program)
    {
        var body = TransformImmutableArray(program.Body, TransformStatement, out var changed);
        if (!changed)
        {
            return program;
        }

        return program with { Body = body };
    }

    private StatementNode TransformStatement(StatementNode statement)
    {
        return statement switch
        {
            BlockStatement block => TransformBlock(block),
            VariableDeclaration declaration => TransformVariableDeclaration(declaration),
            ExpressionStatement expressionStatement => TransformExpressionStatement(expressionStatement),
            ReturnStatement returnStatement => TransformReturn(returnStatement),
            ThrowStatement throwStatement => TransformThrow(throwStatement),
            IfStatement ifStatement => TransformIf(ifStatement),
            WhileStatement whileStatement => TransformWhile(whileStatement),
            DoWhileStatement doWhileStatement => TransformDoWhile(doWhileStatement),
            ForStatement forStatement => TransformFor(forStatement),
            ForEachStatement forEachStatement => TransformForEach(forEachStatement),
            LabeledStatement labeledStatement => TransformLabeled(labeledStatement),
            TryStatement tryStatement => TransformTry(tryStatement),
            SwitchStatement switchStatement => TransformSwitch(switchStatement),
            FunctionDeclaration functionDeclaration => TransformFunctionDeclaration(functionDeclaration),
            ClassDeclaration classDeclaration => TransformClassDeclaration(classDeclaration),
            ExportDefaultStatement exportDefaultStatement => TransformExportDefault(exportDefaultStatement),
            ExportDeclarationStatement exportDeclarationStatement => TransformExportDeclaration(
                exportDeclarationStatement),
            _ => statement
        };
    }

    private BlockStatement TransformBlock(BlockStatement block)
    {
        var statements = TransformImmutableArray(block.Statements, TransformStatement, out var changed);
        return !changed ? block : block with { Statements = statements };
    }

    private StatementNode TransformExpressionStatement(ExpressionStatement statement)
    {
        var expression = TransformExpression(statement.Expression);
        return ReferenceEquals(expression, statement.Expression)
            ? statement
            : statement with { Expression = expression };
    }

    private StatementNode TransformReturn(ReturnStatement statement)
    {
        var expression = TransformOptionalExpression(statement.Expression);
        return ReferenceEquals(expression, statement.Expression)
            ? statement
            : statement with { Expression = expression };
    }

    private StatementNode TransformThrow(ThrowStatement statement)
    {
        var expression = TransformExpression(statement.Expression);
        return ReferenceEquals(expression, statement.Expression)
            ? statement
            : statement with { Expression = expression };
    }

    private StatementNode TransformIf(IfStatement statement)
    {
        var condition = TransformExpression(statement.Condition);
        var thenBranch = TransformStatement(statement.Then);
        var elseBranch = statement.Else is null ? null : TransformStatement(statement.Else);

        if (ReferenceEquals(condition, statement.Condition) && ReferenceEquals(thenBranch, statement.Then) &&
            ReferenceEquals(elseBranch, statement.Else))
        {
            return statement;
        }

        return statement with { Condition = condition, Then = thenBranch, Else = elseBranch };
    }

    private StatementNode TransformWhile(WhileStatement statement)
    {
        var condition = TransformExpression(statement.Condition);
        var body = TransformStatement(statement.Body);
        if (ReferenceEquals(condition, statement.Condition) && ReferenceEquals(body, statement.Body))
        {
            return statement;
        }

        return statement with { Condition = condition, Body = body };
    }

    private StatementNode TransformDoWhile(DoWhileStatement statement)
    {
        var condition = TransformExpression(statement.Condition);
        var body = TransformStatement(statement.Body);
        if (ReferenceEquals(condition, statement.Condition) && ReferenceEquals(body, statement.Body))
        {
            return statement;
        }

        return statement with { Condition = condition, Body = body };
    }

    private StatementNode TransformFor(ForStatement statement)
    {
        var initializer = statement.Initializer is null ? null : TransformStatement(statement.Initializer);
        var condition = TransformOptionalExpression(statement.Condition);
        var increment = TransformOptionalExpression(statement.Increment);
        var body = TransformStatement(statement.Body);

        if (ReferenceEquals(initializer, statement.Initializer) && ReferenceEquals(condition, statement.Condition) &&
            ReferenceEquals(increment, statement.Increment) && ReferenceEquals(body, statement.Body))
        {
            return statement;
        }

        return statement with { Initializer = initializer, Condition = condition, Increment = increment, Body = body };
    }

    private StatementNode TransformForEach(ForEachStatement statement)
    {
        var target = TransformBindingTarget(statement.Target);
        var iterable = TransformExpression(statement.Iterable);
        var body = TransformStatement(statement.Body);

        if (ReferenceEquals(target, statement.Target) && ReferenceEquals(iterable, statement.Iterable) &&
            ReferenceEquals(body, statement.Body))
        {
            return statement;
        }

        return statement with { Target = target, Iterable = iterable, Body = body };
    }

    private StatementNode TransformLabeled(LabeledStatement statement)
    {
        var inner = TransformStatement(statement.Statement);
        return ReferenceEquals(inner, statement.Statement) ? statement : statement with { Statement = inner };
    }

    private StatementNode TransformTry(TryStatement statement)
    {
        var tryBlock = TransformBlock(statement.TryBlock);
        var catchClause = statement.Catch is null ? null : TransformCatch(statement.Catch);
        var finallyBlock = statement.Finally is null ? null : TransformBlock(statement.Finally);

        if (ReferenceEquals(tryBlock, statement.TryBlock) && ReferenceEquals(catchClause, statement.Catch) &&
            ReferenceEquals(finallyBlock, statement.Finally))
        {
            return statement;
        }

        return statement with { TryBlock = tryBlock, Catch = catchClause, Finally = finallyBlock };
    }

    private CatchClause TransformCatch(CatchClause clause)
    {
        var body = TransformBlock(clause.Body);
        return ReferenceEquals(body, clause.Body) ? clause : clause with { Body = body };
    }

    private StatementNode TransformSwitch(SwitchStatement statement)
    {
        var discriminant = TransformExpression(statement.Discriminant);
        var cases = TransformImmutableArray(statement.Cases, TransformSwitchCase, out var changedCases);
        if (ReferenceEquals(discriminant, statement.Discriminant) && !changedCases)
        {
            return statement;
        }

        return statement with { Discriminant = discriminant, Cases = cases };
    }

    private SwitchCase TransformSwitchCase(SwitchCase switchCase)
    {
        var test = TransformOptionalExpression(switchCase.Test);
        var body = TransformBlock(switchCase.Body);
        if (ReferenceEquals(test, switchCase.Test) && ReferenceEquals(body, switchCase.Body))
        {
            return switchCase;
        }

        return switchCase with { Test = test, Body = body };
    }

    private StatementNode TransformFunctionDeclaration(FunctionDeclaration declaration)
    {
        var function = TransformFunctionExpression(declaration.Function);
        return ReferenceEquals(function, declaration.Function) ? declaration : declaration with { Function = function };
    }

    private StatementNode TransformClassDeclaration(ClassDeclaration declaration)
    {
        var definition = TransformClassDefinition(declaration.Definition);
        return ReferenceEquals(definition, declaration.Definition)
            ? declaration
            : declaration with { Definition = definition };
    }

    private StatementNode TransformExportDefault(ExportDefaultStatement statement)
    {
        var value = TransformExportDefaultValue(statement.Value);
        return ReferenceEquals(value, statement.Value) ? statement : statement with { Value = value };
    }

    private StatementNode TransformExportDeclaration(ExportDeclarationStatement statement)
    {
        var declaration = TransformStatement(statement.Declaration);
        return ReferenceEquals(declaration, statement.Declaration)
            ? statement
            : statement with { Declaration = declaration };
    }

    private VariableDeclaration TransformVariableDeclaration(VariableDeclaration declaration)
    {
        var declarators =
            TransformImmutableArray(declaration.Declarators, TransformVariableDeclarator, out var changed);
        return !changed ? declaration : declaration with { Declarators = declarators };
    }

    private VariableDeclarator TransformVariableDeclarator(VariableDeclarator declarator)
    {
        var target = TransformBindingTarget(declarator.Target);
        var initializer = TransformOptionalExpression(declarator.Initializer);
        if (ReferenceEquals(target, declarator.Target) && ReferenceEquals(initializer, declarator.Initializer))
        {
            return declarator;
        }

        return declarator with { Target = target, Initializer = initializer };
    }

    private BindingTarget TransformBindingTarget(BindingTarget target)
    {
        return target switch
        {
            IdentifierBinding => target,
            ArrayBinding arrayBinding => TransformArrayBinding(arrayBinding),
            ObjectBinding objectBinding => TransformObjectBinding(objectBinding),
            _ => target
        };
    }

    private BindingTarget? TransformOptionalBindingTarget(BindingTarget? target)
    {
        return target is null ? null : TransformBindingTarget(target);
    }

    private ArrayBinding TransformArrayBinding(ArrayBinding binding)
    {
        var elements = TransformImmutableArray(binding.Elements, TransformArrayBindingElement, out var changedElements);
        var rest = TransformOptionalBindingTarget(binding.RestElement);
        if (!changedElements && ReferenceEquals(rest, binding.RestElement))
        {
            return binding;
        }

        return binding with { Elements = elements, RestElement = rest };
    }

    private ArrayBindingElement TransformArrayBindingElement(ArrayBindingElement element)
    {
        var target = TransformOptionalBindingTarget(element.Target);
        var defaultValue = TransformOptionalExpression(element.DefaultValue);
        if (ReferenceEquals(target, element.Target) && ReferenceEquals(defaultValue, element.DefaultValue))
        {
            return element;
        }

        return element with { Target = target, DefaultValue = defaultValue };
    }

    private ObjectBinding TransformObjectBinding(ObjectBinding binding)
    {
        var properties = TransformImmutableArray(binding.Properties, TransformObjectBindingProperty,
            out var changedProperties);
        var rest = TransformOptionalBindingTarget(binding.RestElement);
        if (!changedProperties && ReferenceEquals(rest, binding.RestElement))
        {
            return binding;
        }

        return binding with { Properties = properties, RestElement = rest };
    }

    private ObjectBindingProperty TransformObjectBindingProperty(ObjectBindingProperty property)
    {
        var target = TransformBindingTarget(property.Target);
        var defaultValue = TransformOptionalExpression(property.DefaultValue);
        var nameExpression = TransformOptionalExpression(property.NameExpression);
        if (ReferenceEquals(target, property.Target) &&
            ReferenceEquals(defaultValue, property.DefaultValue) &&
            ReferenceEquals(nameExpression, property.NameExpression))
        {
            return property;
        }

        return property with { Target = target, DefaultValue = defaultValue, NameExpression = nameExpression };
    }

    private FunctionExpression TransformFunctionExpression(FunctionExpression expression)
    {
        var parameters =
            TransformImmutableArray(expression.Parameters, TransformFunctionParameter, out var changedParameters);
        var body = TransformBlock(expression.Body);
        if (!changedParameters && ReferenceEquals(body, expression.Body))
        {
            return expression;
        }

        return expression with { Parameters = parameters, Body = body };
    }

    private FunctionParameter TransformFunctionParameter(FunctionParameter parameter)
    {
        var pattern = TransformOptionalBindingTarget(parameter.Pattern);
        var defaultValue = TransformOptionalExpression(parameter.DefaultValue);
        if (ReferenceEquals(pattern, parameter.Pattern) && ReferenceEquals(defaultValue, parameter.DefaultValue))
        {
            return parameter;
        }

        return parameter with { Pattern = pattern, DefaultValue = defaultValue };
    }

    private ClassDefinition TransformClassDefinition(ClassDefinition definition)
    {
        var extendsExpression = TransformOptionalExpression(definition.Extends);
        var constructor = TransformFunctionExpression(definition.Constructor);
        var members = TransformImmutableArray(definition.Members, TransformClassMember, out var membersChanged);
        var fields = TransformImmutableArray(definition.Fields, TransformClassField, out var fieldsChanged);
        var staticBlocks =
            TransformImmutableArray(definition.StaticBlocks, TransformClassStaticBlock, out var staticBlocksChanged);

        if (ReferenceEquals(extendsExpression, definition.Extends) &&
            ReferenceEquals(constructor, definition.Constructor) &&
            !membersChanged && !fieldsChanged && !staticBlocksChanged)
        {
            return definition;
        }

        return definition with
        {
            Extends = extendsExpression,
            Constructor = constructor,
            Members = members,
            Fields = fields,
            StaticBlocks = staticBlocks
        };
    }

    private ClassMember TransformClassMember(ClassMember member)
    {
        var function = TransformFunctionExpression(member.Function);
        var computedName = TransformOptionalExpression(member.ComputedName);
        if (ReferenceEquals(function, member.Function) && ReferenceEquals(computedName, member.ComputedName))
        {
            return member;
        }

        return member with { Function = function, ComputedName = computedName };
    }

    private ClassField TransformClassField(ClassField field)
    {
        var initializer = TransformOptionalExpression(field.Initializer);
        var computedName = TransformOptionalExpression(field.ComputedName);
        if (ReferenceEquals(initializer, field.Initializer) && ReferenceEquals(computedName, field.ComputedName))
        {
            return field;
        }

        return field with { Initializer = initializer, ComputedName = computedName };
    }

    private ClassStaticBlock TransformClassStaticBlock(ClassStaticBlock block)
    {
        var body = TransformBlock(block.Body);
        return ReferenceEquals(body, block.Body) ? block : block with { Body = body };
    }

    private ExportDefaultValue TransformExportDefaultValue(ExportDefaultValue value)
    {
        return value switch
        {
            ExportDefaultExpression expression => TransformExportDefaultExpression(expression),
            ExportDefaultDeclaration declaration => TransformExportDefaultDeclaration(declaration),
            _ => value
        };
    }

    private ExportDefaultExpression TransformExportDefaultExpression(ExportDefaultExpression expression)
    {
        var inner = TransformExpression(expression.Expression);
        return ReferenceEquals(inner, expression.Expression) ? expression : expression with { Expression = inner };
    }

    private ExportDefaultDeclaration TransformExportDefaultDeclaration(ExportDefaultDeclaration declaration)
    {
        var statement = TransformStatement(declaration.Declaration);
        return ReferenceEquals(statement, declaration.Declaration)
            ? declaration
            : declaration with { Declaration = statement };
    }

    private ExpressionNode? TransformOptionalExpression(ExpressionNode? expression)
    {
        return expression is null ? null : TransformExpression(expression);
    }

    private ExpressionNode TransformExpression(ExpressionNode expression)
    {
        return expression switch
        {
            LiteralExpression => expression,
            IdentifierExpression => expression,
            BinaryExpression binary => TransformBinary(binary),
            UnaryExpression unary => TransformUnary(unary),
            ConditionalExpression conditional => TransformConditional(conditional),
            FunctionExpression function => TransformFunctionExpression(function),
            CallExpression call => TransformCall(call),
            NewExpression newExpression => TransformNew(newExpression),
            MemberExpression member => TransformMember(member),
            AssignmentExpression assignment => TransformAssignment(assignment),
            PropertyAssignmentExpression propertyAssignment => TransformPropertyAssignment(propertyAssignment),
            IndexAssignmentExpression indexAssignment => TransformIndexAssignment(indexAssignment),
            SequenceExpression sequence => TransformSequence(sequence),
            DestructuringAssignmentExpression destructuring => TransformDestructuringAssignment(destructuring),
            ArrayExpression array => TransformArray(array),
            ObjectExpression obj => TransformObject(obj),
            ClassExpression classExpression => TransformClassExpression(classExpression),
            TemplateLiteralExpression templateLiteral => TransformTemplateLiteral(templateLiteral),
            TaggedTemplateExpression taggedTemplate => TransformTaggedTemplate(taggedTemplate),
            YieldExpression yieldExpression => TransformYield(yieldExpression),
            AwaitExpression awaitExpression => TransformAwait(awaitExpression),
            ThisExpression => expression,
            SuperExpression => expression,
            _ => expression
        };
    }

    private ExpressionNode TransformBinary(BinaryExpression expression)
    {
        var left = TransformExpression(expression.Left);
        var right = TransformExpression(expression.Right);

        if (TryGetLiteralValue(left, out var leftValue) && TryGetLiteralValue(right, out var rightValue) &&
            TryFoldBinary(expression.Operator, leftValue, rightValue, out var foldedValue))
        {
            return new LiteralExpression(expression.Source, foldedValue);
        }

        if (ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right))
        {
            return expression;
        }

        return expression with { Left = left, Right = right };
    }

    private ExpressionNode TransformUnary(UnaryExpression expression)
    {
        var operand = TransformExpression(expression.Operand);
        if (TryGetLiteralValue(operand, out var operandValue) &&
            TryFoldUnary(expression.Operator, operandValue, out var foldedValue))
        {
            return new LiteralExpression(expression.Source, foldedValue);
        }

        return ReferenceEquals(operand, expression.Operand) ? expression : expression with { Operand = operand };
    }

    private ExpressionNode TransformConditional(ConditionalExpression expression)
    {
        var test = TransformExpression(expression.Test);
        var consequent = TransformExpression(expression.Consequent);
        var alternate = TransformExpression(expression.Alternate);

        if (ReferenceEquals(test, expression.Test) && ReferenceEquals(consequent, expression.Consequent) &&
            ReferenceEquals(alternate, expression.Alternate))
        {
            return expression;
        }

        return expression with { Test = test, Consequent = consequent, Alternate = alternate };
    }

    private ExpressionNode TransformCall(CallExpression expression)
    {
        var callee = TransformExpression(expression.Callee);
        var arguments = TransformImmutableArray(expression.Arguments, TransformCallArgument, out var changed);

        if (ReferenceEquals(callee, expression.Callee) && !changed)
        {
            return expression;
        }

        return expression with { Callee = callee, Arguments = arguments };
    }

    private CallArgument TransformCallArgument(CallArgument argument)
    {
        var expression = TransformExpression(argument.Expression);
        return ReferenceEquals(expression, argument.Expression) ? argument : argument with { Expression = expression };
    }

    private ExpressionNode TransformNew(NewExpression expression)
    {
        var constructor = TransformExpression(expression.Constructor);
        var arguments = TransformImmutableArray(expression.Arguments, TransformExpression, out var changed);

        if (ReferenceEquals(constructor, expression.Constructor) && !changed)
        {
            return expression;
        }

        return expression with { Constructor = constructor, Arguments = arguments };
    }

    private ExpressionNode TransformMember(MemberExpression expression)
    {
        var target = TransformExpression(expression.Target);
        var property = TransformExpression(expression.Property);

        if (ReferenceEquals(target, expression.Target) && ReferenceEquals(property, expression.Property))
        {
            return expression;
        }

        return expression with { Target = target, Property = property };
    }

    private ExpressionNode TransformAssignment(AssignmentExpression expression)
    {
        var value = TransformExpression(expression.Value);
        return ReferenceEquals(value, expression.Value) ? expression : expression with { Value = value };
    }

    private ExpressionNode TransformPropertyAssignment(PropertyAssignmentExpression expression)
    {
        var target = TransformExpression(expression.Target);
        var property = TransformExpression(expression.Property);
        var value = TransformExpression(expression.Value);

        if (ReferenceEquals(target, expression.Target) && ReferenceEquals(property, expression.Property) &&
            ReferenceEquals(value, expression.Value))
        {
            return expression;
        }

        return expression with { Target = target, Property = property, Value = value };
    }

    private ExpressionNode TransformIndexAssignment(IndexAssignmentExpression expression)
    {
        var target = TransformExpression(expression.Target);
        var index = TransformExpression(expression.Index);
        var value = TransformExpression(expression.Value);

        if (ReferenceEquals(target, expression.Target) && ReferenceEquals(index, expression.Index) &&
            ReferenceEquals(value, expression.Value))
        {
            return expression;
        }

        return expression with { Target = target, Index = index, Value = value };
    }

    private ExpressionNode TransformSequence(SequenceExpression expression)
    {
        var left = TransformExpression(expression.Left);
        var right = TransformExpression(expression.Right);
        if (ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right))
        {
            return expression;
        }

        return expression with { Left = left, Right = right };
    }

    private ExpressionNode TransformDestructuringAssignment(DestructuringAssignmentExpression expression)
    {
        var target = TransformBindingTarget(expression.Target);
        var value = TransformExpression(expression.Value);

        if (ReferenceEquals(target, expression.Target) && ReferenceEquals(value, expression.Value))
        {
            return expression;
        }

        return expression with { Target = target, Value = value };
    }

    private ExpressionNode TransformArray(ArrayExpression expression)
    {
        var elements = TransformImmutableArray(expression.Elements, TransformArrayElement, out var changed);
        return !changed ? expression : expression with { Elements = elements };
    }

    private ArrayElement TransformArrayElement(ArrayElement element)
    {
        var expression = TransformOptionalExpression(element.Expression);
        return ReferenceEquals(expression, element.Expression) ? element : element with { Expression = expression };
    }

    private ExpressionNode TransformObject(ObjectExpression expression)
    {
        var members = TransformImmutableArray(expression.Members, TransformObjectMember, out var changed);
        return !changed ? expression : expression with { Members = members };
    }

    private ObjectMember TransformObjectMember(ObjectMember member)
    {
        var value = TransformOptionalExpression(member.Value);
        var function = member.Function is null ? null : TransformFunctionExpression(member.Function);

        if (ReferenceEquals(value, member.Value) && ReferenceEquals(function, member.Function))
        {
            return member;
        }

        return member with { Value = value, Function = function };
    }

    private ExpressionNode TransformClassExpression(ClassExpression expression)
    {
        var definition = TransformClassDefinition(expression.Definition);
        return ReferenceEquals(definition, expression.Definition)
            ? expression
            : expression with { Definition = definition };
    }

    private ExpressionNode TransformTemplateLiteral(TemplateLiteralExpression expression)
    {
        var parts = TransformImmutableArray(expression.Parts, TransformTemplatePart, out var changed);
        return !changed ? expression : expression with { Parts = parts };
    }

    private TemplatePart TransformTemplatePart(TemplatePart part)
    {
        var expression = TransformOptionalExpression(part.Expression);
        return ReferenceEquals(expression, part.Expression) ? part : part with { Expression = expression };
    }

    private ExpressionNode TransformTaggedTemplate(TaggedTemplateExpression expression)
    {
        var tag = TransformExpression(expression.Tag);
        var stringsArray = TransformExpression(expression.StringsArray);
        var rawStringsArray = TransformExpression(expression.RawStringsArray);
        var expressions =
            TransformImmutableArray(expression.Expressions, TransformExpression, out var changedExpressions);

        if (ReferenceEquals(tag, expression.Tag) && ReferenceEquals(stringsArray, expression.StringsArray) &&
            ReferenceEquals(rawStringsArray, expression.RawStringsArray) && !changedExpressions)
        {
            return expression;
        }

        return expression with
        {
            Tag = tag, StringsArray = stringsArray, RawStringsArray = rawStringsArray, Expressions = expressions
        };
    }

    private ExpressionNode TransformYield(YieldExpression expression)
    {
        var inner = TransformOptionalExpression(expression.Expression);
        return ReferenceEquals(inner, expression.Expression) ? expression : expression with { Expression = inner };
    }

    private ExpressionNode TransformAwait(AwaitExpression expression)
    {
        var inner = TransformExpression(expression.Expression);
        return ReferenceEquals(inner, expression.Expression) ? expression : expression with { Expression = inner };
    }

    private static bool TryGetLiteralValue(ExpressionNode expression, out object? value)
    {
        if (expression is LiteralExpression literal && IsFoldableLiteral(literal.Value))
        {
            value = literal.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsFoldableLiteral(object? value)
    {
        return value is double || value is string || value is bool || value is null;
    }

    private static bool TryFoldBinary(string op, object? left, object? right, out object? value)
    {
        value = null;
        return op switch
        {
            "+" => TryFoldAddition(left, right, out value),
            "-" => TryFoldSubtraction(left, right, out value),
            "*" => TryFoldMultiplication(left, right, out value),
            "/" => TryFoldDivision(left, right, out value),
            "%" => TryFoldModulo(left, right, out value),
            "**" => TryFoldExponentiation(left, right, out value),
            "&&" => TryFoldLogicalAnd(left, right, out value),
            "||" => TryFoldLogicalOr(left, right, out value),
            "==" => TryFoldEquals(left, right, out value),
            "!=" => TryFoldNotEquals(left, right, out value),
            "===" => TryFoldStrictEquals(left, right, out value),
            "!==" => TryFoldStrictNotEquals(left, right, out value),
            "<" => TryFoldLessThan(left, right, out value),
            "<=" => TryFoldLessThanOrEqual(left, right, out value),
            ">" => TryFoldGreaterThan(left, right, out value),
            ">=" => TryFoldGreaterThanOrEqual(left, right, out value),
            "&" => TryFoldBitwiseAnd(left, right, out value),
            "|" => TryFoldBitwiseOr(left, right, out value),
            "^" => TryFoldBitwiseXor(left, right, out value),
            "<<" => TryFoldLeftShift(left, right, out value),
            ">>" => TryFoldRightShift(left, right, out value),
            ">>>" => TryFoldUnsignedRightShift(left, right, out value),
            _ => false
        };
    }

    private static bool TryFoldUnary(string op, object? operand, out object? value)
    {
        value = null;
        return op switch
        {
            "unary-" => TryFoldUnaryMinus(operand, out value),
            "unary+" => TryFoldUnaryPlus(operand, out value),
            "!" => TryFoldLogicalNot(operand, out value),
            "~" => TryFoldBitwiseNot(operand, out value),
            _ => false
        };
    }

    private static bool TryFoldAddition(object? left, object? right, out object? value)
    {
        value = null;
        if (left is string || right is string)
        {
            value = CoerceToString(left) + CoerceToString(right);
            return true;
        }

        if (left is double leftNum && right is double rightNum)
        {
            value = leftNum + rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldSubtraction(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = leftNum - rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldMultiplication(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = leftNum * rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldDivision(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
            {
                value = double.NaN;
                return true;
            }

            if (rightNum == 0)
            {
                if (leftNum == 0)
                {
                    value = double.NaN;
                    return true;
                }

                var numeratorNegative = HasNegativeSign(leftNum);
                var denominatorNegative = HasNegativeSign(rightNum);
                value = numeratorNegative ^ denominatorNegative
                    ? double.NegativeInfinity
                    : double.PositiveInfinity;
                return true;
            }

            value = leftNum / rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldModulo(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = rightNum == 0 ? double.NaN : leftNum % rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldExponentiation(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = Math.Pow(leftNum, rightNum);
            return true;
        }

        return false;
    }

    private static bool TryFoldUnaryMinus(object? operand, out object? value)
    {
        value = null;
        if (operand is double number)
        {
            value = -number;
            return true;
        }

        return false;
    }

    private static bool TryFoldUnaryPlus(object? operand, out object? value)
    {
        value = null;
        if (operand is double number)
        {
            value = number;
            return true;
        }

        return false;
    }

    private static bool TryFoldLogicalNot(object? operand, out object? value)
    {
        value = !CoerceToBoolean(operand);
        return true;
    }

    private static bool TryFoldLogicalAnd(object? left, object? right, out object? value)
    {
        if (!CoerceToBoolean(left))
        {
            value = left;
            return true;
        }

        value = right;
        return true;
    }

    private static bool TryFoldLogicalOr(object? left, object? right, out object? value)
    {
        if (CoerceToBoolean(left))
        {
            value = left;
            return true;
        }

        value = right;
        return true;
    }

    private static bool TryFoldEquals(object? left, object? right, out object? value)
    {
        value = LooseEquals(left, right);
        return true;
    }

    private static bool TryFoldNotEquals(object? left, object? right, out object? value)
    {
        value = !LooseEquals(left, right);
        return true;
    }

    private static bool TryFoldStrictEquals(object? left, object? right, out object? value)
    {
        value = StrictEquals(left, right);
        return true;
    }

    private static bool TryFoldStrictNotEquals(object? left, object? right, out object? value)
    {
        value = !StrictEquals(left, right);
        return true;
    }

    private static bool TryFoldLessThan(object? left, object? right, out object? value)
    {
        value = null;
        if (left is string leftStr && right is string rightStr)
        {
            value = string.CompareOrdinal(leftStr, rightStr) < 0;
            return true;
        }

        if (left is double leftNum && right is double rightNum)
        {
            value = !(double.IsNaN(leftNum) || double.IsNaN(rightNum)) && leftNum < rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldLessThanOrEqual(object? left, object? right, out object? value)
    {
        value = null;
        if (left is string leftStr && right is string rightStr)
        {
            value = string.CompareOrdinal(leftStr, rightStr) <= 0;
            return true;
        }

        if (left is double leftNum && right is double rightNum)
        {
            value = !(double.IsNaN(leftNum) || double.IsNaN(rightNum)) && leftNum <= rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldGreaterThan(object? left, object? right, out object? value)
    {
        value = null;
        if (left is string leftStr && right is string rightStr)
        {
            value = string.CompareOrdinal(leftStr, rightStr) > 0;
            return true;
        }

        if (left is double leftNum && right is double rightNum)
        {
            value = !(double.IsNaN(leftNum) || double.IsNaN(rightNum)) && leftNum > rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldGreaterThanOrEqual(object? left, object? right, out object? value)
    {
        value = null;
        if (left is string leftStr && right is string rightStr)
        {
            value = string.CompareOrdinal(leftStr, rightStr) >= 0;
            return true;
        }

        if (left is double leftNum && right is double rightNum)
        {
            value = !(double.IsNaN(leftNum) || double.IsNaN(rightNum)) && leftNum >= rightNum;
            return true;
        }

        return false;
    }

    private static bool TryFoldBitwiseAnd(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = (double)(ToInt32(leftNum) & ToInt32(rightNum));
            return true;
        }

        return false;
    }

    private static bool TryFoldBitwiseOr(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = (double)(ToInt32(leftNum) | ToInt32(rightNum));
            return true;
        }

        return false;
    }

    private static bool TryFoldBitwiseXor(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            value = (double)(ToInt32(leftNum) ^ ToInt32(rightNum));
            return true;
        }

        return false;
    }

    private static bool TryFoldBitwiseNot(object? operand, out object? value)
    {
        value = null;
        if (operand is double number)
        {
            value = (double)~ToInt32(number);
            return true;
        }

        return false;
    }

    private static bool TryFoldLeftShift(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            var shift = ToInt32(rightNum) & 0x1F;
            value = (double)(ToInt32(leftNum) << shift);
            return true;
        }

        return false;
    }

    private static bool TryFoldRightShift(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            var shift = ToInt32(rightNum) & 0x1F;
            value = (double)(ToInt32(leftNum) >> shift);
            return true;
        }

        return false;
    }

    private static bool TryFoldUnsignedRightShift(object? left, object? right, out object? value)
    {
        value = null;
        if (left is double leftNum && right is double rightNum)
        {
            var shift = ToInt32(rightNum) & 0x1F;
            value = (double)(ToUint32(leftNum) >> shift);
            return true;
        }

        return false;
    }

    private static string CoerceToString(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool CoerceToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            string s => s.Length > 0,
            _ => true
        };
    }

    private static bool LooseEquals(object? left, object? right)
    {
        return JsOps.LooseEquals(left, right);
    }

    private static double StringToNumber(string str)
    {
        return NumericStringParser.ParseJsNumber(str);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    private static int ToInt32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        var int64 = (long)value;
        return (int)(int64 & 0xFFFFFFFF);
    }

    private static uint ToUint32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        var int64 = (long)value;
        return (uint)(int64 & 0xFFFFFFFF);
    }

    private static ImmutableArray<T> TransformImmutableArray<T>(ImmutableArray<T> items, Func<T, T> transform,
        out bool changed)
    {
        changed = false;
        if (items.IsDefaultOrEmpty)
        {
            return items;
        }

        ImmutableArray<T>.Builder? builder = null;
        for (var i = 0; i < items.Length; i++)
        {
            var original = items[i];
            var transformed = transform(original);
            if (!ReferenceEquals(original, transformed))
            {
                builder ??= items.ToBuilder();
                builder[i] = transformed;
                changed = true;
            }
        }

        return builder is null ? items : builder.ToImmutable();
    }

    private static bool HasNegativeSign(double value)
    {
        const long signMask = 1L << 63;
        var bits = BitConverter.DoubleToInt64Bits(value);
        return (bits & signMask) != 0;
    }
}
