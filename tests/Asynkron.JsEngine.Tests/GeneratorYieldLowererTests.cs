using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Tests;

public class GeneratorYieldLowererTests
{
    [Fact]
    public void Lowerer_RewritesYieldingDeclarationAssignmentAndReturn()
    {
        var xSymbol = Symbol.Intern("x");
        var declaration = new VariableDeclaration(
            null,
            VariableKind.Const,
            [new VariableDeclarator(null, new IdentifierBinding(null, xSymbol),
                new YieldExpression(null, new LiteralExpression(null, "v"), false))]);
        var returnStatement = new ReturnStatement(null,
            new YieldExpression(null, new LiteralExpression(null, "r"), false));

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(declaration, returnStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(6, statements.Length);

        // Declaration lowering: let __yield_lower_resume0;
        var declTemp = Assert.IsType<VariableDeclaration>(statements[0]);
        var declTempDeclarator = Assert.Single(declTemp.Declarators);
        var declTempId = Assert.IsType<IdentifierBinding>(declTempDeclarator.Target);
        Assert.StartsWith("__yield_lower_resume", declTempId.Name.Name);
        Assert.Null(declTempDeclarator.Initializer);

        // Assignment to the temp: __yield_lower_resume0 = yield "v";
        var declAssign = Assert.IsType<ExpressionStatement>(statements[1]);
        var declAssignExpr = Assert.IsType<AssignmentExpression>(declAssign.Expression);
        Assert.Equal(declTempId.Name, declAssignExpr.Target);
        var declYield = Assert.IsType<YieldExpression>(declAssignExpr.Value);
        Assert.False(declYield.IsDelegated);
        Assert.Equal("v", Assert.IsType<LiteralExpression>(declYield.Expression).Value);

        // Original declaration rewritten to initializer that reads the temp.
        var finalDecl = Assert.IsType<VariableDeclaration>(statements[2]);
        var finalDeclarator = Assert.Single(finalDecl.Declarators);
        Assert.Equal(xSymbol, Assert.IsType<IdentifierBinding>(finalDeclarator.Target).Name);
        var finalInit = Assert.IsType<IdentifierExpression>(finalDeclarator.Initializer);
        Assert.Equal(declTempId.Name, finalInit.Name);

        // Return lowering temp: let __yield_lower_resume1;
        var returnTemp = Assert.IsType<VariableDeclaration>(statements[3]);
        var returnTempDeclarator = Assert.Single(returnTemp.Declarators);
        var returnTempId = Assert.IsType<IdentifierBinding>(returnTempDeclarator.Target);
        Assert.StartsWith("__yield_lower_resume", returnTempId.Name.Name);
        Assert.Null(returnTempDeclarator.Initializer);

        // Assignment to return temp: __yield_lower_resume1 = yield "r";
        var returnAssign = Assert.IsType<ExpressionStatement>(statements[4]);
        var returnAssignExpr = Assert.IsType<AssignmentExpression>(returnAssign.Expression);
        Assert.Equal(returnTempId.Name, returnAssignExpr.Target);
        var returnYield = Assert.IsType<YieldExpression>(returnAssignExpr.Value);
        Assert.False(returnYield.IsDelegated);
        Assert.Equal("r", Assert.IsType<LiteralExpression>(returnYield.Expression).Value);

        // Final return uses the temp.
        var loweredReturn = Assert.IsType<ReturnStatement>(statements[5]);
        var returnValue = Assert.IsType<IdentifierExpression>(loweredReturn.Expression);
        Assert.Equal(returnTempId.Name, returnValue.Name);
    }

    [Fact]
    public void Lowerer_RewritesIfConditionYield()
    {
        var ifStatement = new IfStatement(
            null,
            new YieldExpression(null, new LiteralExpression(null, "a"), false),
            new ExpressionStatement(null, new LiteralExpression(null, 1)),
            null);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(ifStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(3, statements.Length);

        var tempDecl = Assert.IsType<VariableDeclaration>(statements[0]);
        var tempBinding = Assert.IsType<IdentifierBinding>(Assert.Single(tempDecl.Declarators).Target);
        Assert.StartsWith("__yield_lower_resume", tempBinding.Name.Name);

        var tempAssign = Assert.IsType<ExpressionStatement>(statements[1]);
        var tempAssignExpr = Assert.IsType<AssignmentExpression>(tempAssign.Expression);
        Assert.Equal(tempBinding.Name, tempAssignExpr.Target);
        Assert.IsType<YieldExpression>(tempAssignExpr.Value);

        var loweredIf = Assert.IsType<IfStatement>(statements[2]);
        var loweredCondition = Assert.IsType<IdentifierExpression>(loweredIf.Condition);
        Assert.Equal(tempBinding.Name, loweredCondition.Name);
    }

    [Fact]
    public void Lowerer_DoesNotRewriteDelegatedYieldCondition()
    {
        var ifStatement = new IfStatement(
            null,
            new YieldExpression(null, new LiteralExpression(null, "a"), true),
            new ExpressionStatement(null, new LiteralExpression(null, 1)),
            null);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(ifStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Single(statements);

        var loweredIf = Assert.IsType<IfStatement>(statements[0]);
        var loweredCondition = Assert.IsType<YieldExpression>(loweredIf.Condition);
        Assert.True(loweredCondition.IsDelegated);
        Assert.Equal("a", Assert.IsType<LiteralExpression>(loweredCondition.Expression).Value);
    }

    [Fact]
    public void Lowerer_DoesNotRewriteMultiYieldCondition()
    {
        var condition = new BinaryExpression(
            null,
            "&&",
            new YieldExpression(null, new LiteralExpression(null, "left"), false),
            new YieldExpression(null, new LiteralExpression(null, "right"), false));

        var ifStatement = new IfStatement(
            null,
            condition,
            new ExpressionStatement(null, new LiteralExpression(null, 1)),
            null);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(ifStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Single(statements);

        var loweredIf = Assert.IsType<IfStatement>(statements[0]);
        var loweredCondition = Assert.IsType<BinaryExpression>(loweredIf.Condition);
        Assert.IsType<YieldExpression>(loweredCondition.Left);
        Assert.IsType<YieldExpression>(loweredCondition.Right);
    }

    [Fact]
    public void Lowerer_DoesNotRewriteNestedFunctionYields()
    {
        var innerReturn = new ReturnStatement(null,
            new YieldExpression(null, new LiteralExpression(null, "inner"), false));
        var innerFunction = new FunctionExpression(
            null,
            Symbol.Intern("inner"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(innerReturn), false),
            false,
            true);

        var expressionStatement = new ExpressionStatement(null, innerFunction);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(expressionStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        var loweredExpression = Assert.IsType<ExpressionStatement>(Assert.Single(statements)).Expression;
        var loweredInner = Assert.IsType<FunctionExpression>(loweredExpression);
        var innerStatements = loweredInner.Body.Statements;
        Assert.Single(innerStatements);
        var innerReturnStmt = Assert.IsType<ReturnStatement>(innerStatements[0]);
        Assert.IsType<YieldExpression>(innerReturnStmt.Expression);
    }

    [Fact]
    public void Lowerer_RewritesIfConditionYieldInSubexpression()
    {
        var condition = new BinaryExpression(
            null,
            "+",
            new LiteralExpression(null, 1),
            new YieldExpression(null, new LiteralExpression(null, "side"), false));

        var ifStatement = new IfStatement(
            null,
            condition,
            new ExpressionStatement(null, new LiteralExpression(null, 1)),
            null);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(ifStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(3, statements.Length);

        var tempDecl = Assert.IsType<VariableDeclaration>(statements[0]);
        var tempBinding = Assert.IsType<IdentifierBinding>(Assert.Single(tempDecl.Declarators).Target);

        var tempAssign = Assert.IsType<ExpressionStatement>(statements[1]);
        var yieldExpr = Assert.IsType<YieldExpression>(Assert.IsType<AssignmentExpression>(tempAssign.Expression).Value);
        Assert.Equal("side", Assert.IsType<LiteralExpression>(yieldExpr.Expression).Value);

        var loweredIf = Assert.IsType<IfStatement>(statements[2]);
        var loweredCondition = Assert.IsType<BinaryExpression>(loweredIf.Condition);
        var right = Assert.IsType<IdentifierExpression>(loweredCondition.Right);
        Assert.Equal(tempBinding.Name, right.Name);
    }

    [Fact]
    public void Lowerer_RewritesWhileConditionYield()
    {
        var whileStatement = new WhileStatement(
            null,
            new YieldExpression(null, new LiteralExpression(null, "probe"), false),
            new ExpressionStatement(null, new LiteralExpression(null, "body")));

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(whileStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(2, statements.Length);

        var tempDecl = Assert.IsType<VariableDeclaration>(statements[0]);
        var tempBinding = Assert.IsType<IdentifierBinding>(Assert.Single(tempDecl.Declarators).Target);
        Assert.StartsWith("__yield_lower_resume", tempBinding.Name.Name);

        var loweredWhile = Assert.IsType<WhileStatement>(statements[1]);
        var loweredBody = Assert.IsType<BlockStatement>(loweredWhile.Body);
        Assert.Equal(3, loweredBody.Statements.Length);

        var assign = Assert.IsType<ExpressionStatement>(loweredBody.Statements[0]);
        var assignExpr = Assert.IsType<AssignmentExpression>(assign.Expression);
        Assert.Equal(tempBinding.Name, assignExpr.Target);
        Assert.IsType<YieldExpression>(assignExpr.Value);

        var breakCheck = Assert.IsType<IfStatement>(loweredBody.Statements[1]);
        var negated = Assert.IsType<UnaryExpression>(breakCheck.Condition);
        var conditionId = Assert.IsType<IdentifierExpression>(negated.Operand);
        Assert.Equal(tempBinding.Name, conditionId.Name);
        Assert.IsType<BreakStatement>(breakCheck.Then);

        var loweredBodyStatement = Assert.IsType<BlockStatement>(loweredBody.Statements[2]);
        Assert.Equal("body",
            Assert.IsType<LiteralExpression>(Assert.IsType<ExpressionStatement>(
                Assert.Single(loweredBodyStatement.Statements)).Expression).Value);
    }

    [Fact]
    public void Lowerer_RewritesDoWhileConditionYield()
    {
        var doWhile = new DoWhileStatement(
            null,
            new ExpressionStatement(null, new LiteralExpression(null, "body")),
            new YieldExpression(null, new LiteralExpression(null, "probe"), false));

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(doWhile), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(2, statements.Length);

        var tempDecl = Assert.IsType<VariableDeclaration>(statements[0]);
        var tempBinding = Assert.IsType<IdentifierBinding>(Assert.Single(tempDecl.Declarators).Target);

        var loweredDoWhile = Assert.IsType<DoWhileStatement>(statements[1]);
        var loweredBody = Assert.IsType<BlockStatement>(loweredDoWhile.Body);
        Assert.Equal(2, loweredBody.Statements.Length);

        var assign = Assert.IsType<ExpressionStatement>(loweredBody.Statements[1]);
        var assignExpr = Assert.IsType<AssignmentExpression>(assign.Expression);
        Assert.Equal(tempBinding.Name, assignExpr.Target);
        Assert.IsType<YieldExpression>(assignExpr.Value);

        var loweredCondition = Assert.IsType<IdentifierExpression>(loweredDoWhile.Condition);
        Assert.Equal(tempBinding.Name, loweredCondition.Name);
    }

    [Fact]
    public void Lowerer_RewritesForConditionAndIncrementYield()
    {
        var iSymbol = Symbol.Intern("i");
        var initializer = new VariableDeclaration(
            null,
            VariableKind.Let,
            [new VariableDeclarator(null, new IdentifierBinding(null, iSymbol), new LiteralExpression(null, 0))]);

        var condition = new YieldExpression(null, new LiteralExpression(null, "cond"), false);
        var increment = new AssignmentExpression(
            null,
            iSymbol,
            new BinaryExpression(
                null,
                "+",
                new IdentifierExpression(null, iSymbol),
                new YieldExpression(null, new LiteralExpression(null, "inc"), false)));

        var body = new ExpressionStatement(null, new LiteralExpression(null, "body"));

        var forStatement = new ForStatement(null, initializer, condition, increment, body);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(forStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(4, statements.Length);

        Assert.IsType<VariableDeclaration>(statements[0]); // initializer

        var condTempDecl = Assert.IsType<VariableDeclaration>(statements[1]);
        var condTemp = Assert.IsType<IdentifierBinding>(Assert.Single(condTempDecl.Declarators).Target);
        Assert.StartsWith("__yield_lower_resume", condTemp.Name.Name);

        var incTempDecl = Assert.IsType<VariableDeclaration>(statements[2]);
        var incTemp = Assert.IsType<IdentifierBinding>(Assert.Single(incTempDecl.Declarators).Target);
        Assert.StartsWith("__yield_lower_resume", incTemp.Name.Name);

        var loweredWhile = Assert.IsType<WhileStatement>(statements[3]);
        var loopBlock = Assert.IsType<BlockStatement>(loweredWhile.Body);

        // condition assign + break, body, increment assign + expression
        Assert.True(loopBlock.Statements.Length >= 4);

        var condAssign = Assert.IsType<ExpressionStatement>(loopBlock.Statements[0]);
        var condAssignExpr = Assert.IsType<AssignmentExpression>(condAssign.Expression);
        Assert.Equal(condTemp.Name, condAssignExpr.Target);
        Assert.IsType<YieldExpression>(condAssignExpr.Value);

        var condBreak = Assert.IsType<IfStatement>(loopBlock.Statements[1]);
        var condBreakCheck = Assert.IsType<UnaryExpression>(condBreak.Condition);
        var condId = Assert.IsType<IdentifierExpression>(condBreakCheck.Operand);
        Assert.Equal(condTemp.Name, condId.Name);

        var incAssign = Assert.IsType<ExpressionStatement>(loopBlock.Statements[^2]);
        var incAssignExpr = Assert.IsType<AssignmentExpression>(incAssign.Expression);
        Assert.Equal(incTemp.Name, incAssignExpr.Target);
        Assert.IsType<YieldExpression>(incAssignExpr.Value);

        var incStatement = Assert.IsType<ExpressionStatement>(loopBlock.Statements[^1]);
        var incExpr = Assert.IsType<AssignmentExpression>(incStatement.Expression);
        var incValue = Assert.IsType<BinaryExpression>(incExpr.Value);
        var incRight = Assert.IsType<IdentifierExpression>(incValue.Right);
        Assert.Equal(incTemp.Name, incRight.Name);
    }

    [Fact]
    public void Lowerer_RewritesForIncrementWithTwoYields()
    {
        var iSymbol = Symbol.Intern("i");
        var initializer = new VariableDeclaration(
            null,
            VariableKind.Let,
            [new VariableDeclarator(null, new IdentifierBinding(null, iSymbol), new LiteralExpression(null, 0))]);

        var increment = new BinaryExpression(
            null,
            "+",
            new YieldExpression(null, new LiteralExpression(null, "a"), false),
            new YieldExpression(null, new LiteralExpression(null, "b"), false));

        var forStatement = new ForStatement(
            null,
            initializer,
            new BinaryExpression(null, "<", new IdentifierExpression(null, iSymbol), new LiteralExpression(null, 1)),
            increment,
            new ExpressionStatement(null, new LiteralExpression(null, "body")));

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(forStatement), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(4, statements.Length);

        Assert.IsType<VariableDeclaration>(statements[0]); // initializer

        var incTempDecl1 = Assert.IsType<VariableDeclaration>(statements[1]);
        var incTemp1 = Assert.IsType<IdentifierBinding>(Assert.Single(incTempDecl1.Declarators).Target);
        var incTempDecl2 = Assert.IsType<VariableDeclaration>(statements[2]);
        var incTemp2 = Assert.IsType<IdentifierBinding>(Assert.Single(incTempDecl2.Declarators).Target);

        Assert.StartsWith("__yield_lower_resume", incTemp1.Name.Name);
        Assert.StartsWith("__yield_lower_resume", incTemp2.Name.Name);
        Assert.NotEqual(incTemp1.Name, incTemp2.Name);

        var loweredWhile = Assert.IsType<WhileStatement>(statements[3]);
        var loopBlock = Assert.IsType<BlockStatement>(loweredWhile.Body);

        // condition break
        var breakCheck = Assert.IsType<IfStatement>(loopBlock.Statements[0]);
        Assert.IsType<UnaryExpression>(breakCheck.Condition);

        // body
        Assert.IsType<BlockStatement>(loopBlock.Statements[1]);

        // increment yields and final increment expression
        var incAssign1 = Assert.IsType<ExpressionStatement>(loopBlock.Statements[2]);
        var incAssignExpr1 = Assert.IsType<AssignmentExpression>(incAssign1.Expression);
        Assert.Equal(incTemp1.Name, incAssignExpr1.Target);
        Assert.IsType<YieldExpression>(incAssignExpr1.Value);

        var incAssign2 = Assert.IsType<ExpressionStatement>(loopBlock.Statements[3]);
        var incAssignExpr2 = Assert.IsType<AssignmentExpression>(incAssign2.Expression);
        Assert.Equal(incTemp2.Name, incAssignExpr2.Target);
        Assert.IsType<YieldExpression>(incAssignExpr2.Value);

        var finalInc = Assert.IsType<ExpressionStatement>(loopBlock.Statements[4]);
        var finalIncExpr = Assert.IsType<BinaryExpression>(finalInc.Expression);
        Assert.IsType<IdentifierExpression>(finalIncExpr.Left);
        Assert.IsType<IdentifierExpression>(finalIncExpr.Right);
    }

    [Fact]
    public void Lowerer_RewritesMultiYieldInitializerIntoSeparateTempBindings()
    {
        var targetSymbol = Symbol.Intern("value");
        var initializer = new BinaryExpression(
            null,
            "+",
            new YieldExpression(null, new LiteralExpression(null, "a"), false),
            new YieldExpression(null, new LiteralExpression(null, "b"), false));

        var declaration = new VariableDeclaration(
            null,
            VariableKind.Const,
            [new VariableDeclarator(null, new IdentifierBinding(null, targetSymbol), initializer)]);

        var function = new FunctionExpression(
            null,
            Symbol.Intern("gen"),
            ImmutableArray<FunctionParameter>.Empty,
            new BlockStatement(null, ImmutableArray.Create<StatementNode>(declaration), false),
            false,
            true);

        var loweredResult = GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var reason);

        Assert.True(loweredResult);
        Assert.Null(reason);

        var statements = lowered.Body.Statements;
        Assert.Equal(3, statements.Length);

        var firstDecl = Assert.IsType<VariableDeclaration>(statements[0]);
        var firstTemp = Assert.IsType<IdentifierBinding>(Assert.Single(firstDecl.Declarators).Target);

        var secondDecl = Assert.IsType<VariableDeclaration>(statements[1]);
        var secondTemp = Assert.IsType<IdentifierBinding>(Assert.Single(secondDecl.Declarators).Target);

        Assert.NotEqual(firstTemp.Name, secondTemp.Name);

        var finalDecl = Assert.IsType<VariableDeclaration>(statements[2]);
        var finalInit = Assert.IsType<BinaryExpression>(Assert.Single(finalDecl.Declarators).Initializer);
        var leftId = Assert.IsType<IdentifierExpression>(finalInit.Left);
        var rightId = Assert.IsType<IdentifierExpression>(finalInit.Right);
        Assert.Equal(firstTemp.Name, leftId.Name);
        Assert.Equal(secondTemp.Name, rightId.Name);
    }
}
