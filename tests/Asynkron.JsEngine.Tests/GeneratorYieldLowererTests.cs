using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Xunit;

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

        Assert.IsType<BlockStatement>(loweredBody.Statements[2]);
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
}
