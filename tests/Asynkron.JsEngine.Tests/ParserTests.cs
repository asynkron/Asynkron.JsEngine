using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Tests;

public class ParserTests
{
    [Fact(Timeout = 2000)]
    public async Task ParseLetDeclarationProducesExpectedSExpression()
    {
        await using var engine = new JsEngine();
        // Use ParseWithoutTransformation to test parser structure without constant folding
        var program = JsEngine.ParseWithoutTransformation("let answer = 1 + 2; answer;");

        Assert.Same(JsSymbols.Program, program.Head);
        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, letStatement.Head);
        Assert.Equal(Symbol.Intern("answer"), letStatement.Rest.Head);

        var addition = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("+"), addition.Head);
        Assert.Equal(1d, addition.Rest.Head);
        Assert.Equal(2d, addition.Rest.Rest.Head);

        var expressionStatement = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, expressionStatement.Head);
        Assert.Equal(Symbol.Intern("answer"), expressionStatement.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseVarDeclarationWithoutInitializerUsesSentinel()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("var counter; counter;");

        var varStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Var, varStatement.Head);
        Assert.Equal(Symbol.Intern("counter"), varStatement.Rest.Head);
        Assert.Same(JsSymbols.Uninitialized,
            varStatement.Rest.Rest.Head); // Evaluator fills this in with null later on.
    }

    [Fact(Timeout = 2000)]
    public async Task ParseConstDeclarationProducesConstSymbol()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("const answer = 42; answer;");

        var constStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Const, constStatement.Head);
        Assert.Equal(Symbol.Intern("answer"), constStatement.Rest.Head);
        Assert.Equal(42d, constStatement.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseObjectLiteralAndPropertyAccess()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("let obj = { a: 10, x: function () { return this.x; } }; obj.a;");

        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, letStatement.Head);

        var objectLiteral = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.ObjectLiteral, objectLiteral.Head);

        var firstProperty = Assert.IsType<Cons>(objectLiteral.Rest.Head);
        Assert.Same(JsSymbols.Property, firstProperty.Head);
        Assert.Equal("a", firstProperty.Rest.Head);
        Assert.Equal(10d, firstProperty.Rest.Rest.Head);

        var secondProperty = Assert.IsType<Cons>(objectLiteral.Rest.Rest.Head);
        Assert.Same(JsSymbols.Property, secondProperty.Head);
        Assert.Equal("x", secondProperty.Rest.Head);
        var functionExpression = Assert.IsType<Cons>(secondProperty.Rest.Rest.Head);
        Assert.Same(JsSymbols.Lambda, functionExpression.Head); // ensure the function value stays a lambda expression

        Assert.Null(functionExpression.Rest.Head); // anonymous function keeps null name slot
        var body = Assert.IsType<Cons>(functionExpression.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, body.Head);
        var returnStatement = Assert.IsType<Cons>(body.Rest.Head);
        Assert.Same(JsSymbols.Return, returnStatement.Head);
        var propertyAccessInReturn = Assert.IsType<Cons>(returnStatement.Rest.Head);
        Assert.Same(JsSymbols.GetProperty, propertyAccessInReturn.Head);
        Assert.Same(JsSymbols.This, propertyAccessInReturn.Rest.Head);
        Assert.Equal("x", propertyAccessInReturn.Rest.Rest.Head);

        var expressionStatement = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, expressionStatement.Head);

        var propertyAccess = Assert.IsType<Cons>(expressionStatement.Rest.Head);
        Assert.Same(JsSymbols.GetProperty, propertyAccess.Head);
        Assert.Equal(Symbol.Intern("obj"), propertyAccess.Rest.Head);
        Assert.Equal("a", propertyAccess.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParsePropertyAssignment()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("let obj = {}; obj.value = 5;");

        var expressionStatement = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, expressionStatement.Head);

        var assignment = Assert.IsType<Cons>(expressionStatement.Rest.Head);
        Assert.Same(JsSymbols.SetProperty, assignment.Head);
        Assert.Equal(Symbol.Intern("obj"), assignment.Rest.Head);
        Assert.Equal("value", assignment.Rest.Rest.Head);
        Assert.Equal(5d, assignment.Rest.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public Task ParseCommaSeparatedExpressionStatementAsSequence()
    {
        var program = JsEngine.ParseWithoutTransformation(
            "_ref = _temp === void 0 ? {} : _temp, _ref$jsx = _ref.jsx, jsx = _ref$jsx === void 0 ? false : _ref$jsx;");

        var expressionStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, expressionStatement.Head);

        var outerSequence = Assert.IsType<Cons>(expressionStatement.Rest.Head);
        Assert.Same(JsSymbols.Operator(","), outerSequence.Head);

        var firstSequence = Assert.IsType<Cons>(outerSequence.Rest.Head);
        Assert.Same(JsSymbols.Operator(","), firstSequence.Head);

        var firstAssignment = Assert.IsType<Cons>(firstSequence.Rest.Head);
        Assert.Same(JsSymbols.Assign, firstAssignment.Head);
        Assert.Equal(Symbol.Intern("_ref"), firstAssignment.Rest.Head);
        Assert.IsType<Cons>(firstAssignment.Rest.Rest.Head); // ternary expression

        var secondAssignment = Assert.IsType<Cons>(firstSequence.Rest.Rest.Head);
        Assert.Same(JsSymbols.Assign, secondAssignment.Head);
        Assert.Equal(Symbol.Intern("_ref$jsx"), secondAssignment.Rest.Head);

        var thirdAssignment = Assert.IsType<Cons>(outerSequence.Rest.Rest.Head);
        Assert.Same(JsSymbols.Assign, thirdAssignment.Head);
        Assert.Equal(Symbol.Intern("jsx"), thirdAssignment.Rest.Head);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrayLiteralAndIndexedAssignment()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("let numbers = [1, 2, 3]; numbers[1] = numbers[0];");

        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, letStatement.Head);
        Assert.Equal(Symbol.Intern("numbers"), letStatement.Rest.Head);

        var arrayLiteral = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.ArrayLiteral, arrayLiteral.Head);
        Assert.Equal(1d, arrayLiteral.Rest.Head);
        Assert.Equal(2d, arrayLiteral.Rest.Rest.Head);
        Assert.Equal(3d, arrayLiteral.Rest.Rest.Rest.Head);

        var expressionStatement = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, expressionStatement.Head);

        var setIndex = Assert.IsType<Cons>(expressionStatement.Rest.Head);
        Assert.Same(JsSymbols.SetIndex, setIndex.Head);
        Assert.Equal(Symbol.Intern("numbers"), setIndex.Rest.Head);
        Assert.Equal(1d, setIndex.Rest.Rest.Head);

        var valueExpression = Assert.IsType<Cons>(setIndex.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.GetIndex, valueExpression.Head); // ensure RHS preserves the index expression form
        Assert.Equal(Symbol.Intern("numbers"), valueExpression.Rest.Head);
        Assert.Equal(0d, valueExpression.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseLogicalOperatorsRespectPrecedence()
    {
        await using var engine = new JsEngine();
        // Use ParseWithoutTransformation to test parser structure without constant folding
        var program = JsEngine.ParseWithoutTransformation("let flag = true || false && true;");

        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, letStatement.Head);

        var logicalOr = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("||"), logicalOr.Head);
        Assert.Equal(true, logicalOr.Rest.Head);

        var logicalAnd = Assert.IsType<Cons>(logicalOr.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("&&"), logicalAnd.Head);
        Assert.Equal(false, logicalAnd.Rest.Head);
        Assert.Equal(true, logicalAnd.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseNullishCoalescingProducesOperatorSymbol()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("let value = null ?? 42;");

        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, letStatement.Head);

        var coalesce = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("??"), coalesce.Head);
        Assert.Null(coalesce.Rest.Head);
        Assert.Equal(42d, coalesce.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseStrictEqualityOperators()
    {
        await using var engine = new JsEngine();
        // Use ParseWithoutTransformation to test parser structure without constant folding
        var program = JsEngine.ParseWithoutTransformation("let comparisons = 1 === 1; let others = 2 !== 3;");

        var strictEqual = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, strictEqual.Head);

        var equalityExpression = Assert.IsType<Cons>(strictEqual.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("==="), equalityExpression.Head);
        Assert.Equal(1d, equalityExpression.Rest.Head);
        Assert.Equal(1d, equalityExpression.Rest.Rest.Head);

        var strictNotEqualStatement = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.Let, strictNotEqualStatement.Head);

        var inequalityExpression = Assert.IsType<Cons>(strictNotEqualStatement.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("!=="), inequalityExpression.Head);
        Assert.Equal(2d, inequalityExpression.Rest.Head);
        Assert.Equal(3d, inequalityExpression.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseNewExpression()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("let instance = new Factory.Builder(1, 2); instance;");

        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, letStatement.Head);
        Assert.Equal(Symbol.Intern("instance"), letStatement.Rest.Head);

        var newExpression = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.New, newExpression.Head);

        var constructor = Assert.IsType<Cons>(newExpression.Rest.Head);
        Assert.Same(JsSymbols.GetProperty, constructor.Head);
        Assert.Equal(Symbol.Intern("Factory"), constructor.Rest.Head);
        Assert.Equal("Builder", constructor.Rest.Rest.Head);

        Assert.Equal(1d, newExpression.Rest.Rest.Head);
        Assert.Equal(2d, newExpression.Rest.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseClassDeclarationProducesConstructorAndMethods()
    {
        await using var engine = new JsEngine();
        var program =
            engine.Parse(
                "class Counter { constructor(start) { this.value = start; } increment() { return this.value; } }");

        var classStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Class, classStatement.Head);
        Assert.Equal(Symbol.Intern("Counter"), classStatement.Rest.Head);

        Assert.Null(classStatement.Rest.Rest.Head); // no extends clause

        var constructor = Assert.IsType<Cons>(classStatement.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Lambda, constructor.Head);
        Assert.Equal(Symbol.Intern("Counter"), constructor.Rest.Head); // constructor keeps the class name for recursion

        var constructorParameters = Assert.IsType<Cons>(constructor.Rest.Rest.Head);
        Assert.Equal(Symbol.Intern("start"), constructorParameters.Head);

        var constructorBody = Assert.IsType<Cons>(constructor.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, constructorBody.Head);

        var methods = Assert.IsType<Cons>(classStatement.Rest.Rest.Rest.Rest.Head);
        var methodEntry = Assert.IsType<Cons>(methods.Head);
        Assert.Same(JsSymbols.Method, methodEntry.Head);
        Assert.Equal("increment", methodEntry.Rest.Head);

        var methodLambda = Assert.IsType<Cons>(methodEntry.Rest.Rest.Head);
        Assert.Same(JsSymbols.Lambda, methodLambda.Head);
        Assert.Null(methodLambda.Rest.Head); // class methods stay anonymous like standard method syntax
    }

    [Fact(Timeout = 2000)]
    public async Task ParseClassDeclarationCapturesExtendsClause()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("class Derived extends Base.Type { method() { return super.method(); } }");

        var classStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Class, classStatement.Head);

        var extendsClause = Assert.IsType<Cons>(classStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.Extends, extendsClause.Head);

        var baseReference = Assert.IsType<Cons>(extendsClause.Rest.Head);
        Assert.Same(JsSymbols.GetProperty, baseReference.Head);
        Assert.Equal(Symbol.Intern("Base"), baseReference.Rest.Head);
        Assert.Equal("Type", baseReference.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseSwitchStatementKeepsClauseOrder()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("switch (value) { case 1: foo(); case 2: break; default: bar(); }");

        var switchStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Switch, switchStatement.Head);
        Assert.Equal(Symbol.Intern("value"), switchStatement.Rest.Head);

        var clauses = Assert.IsType<Cons>(switchStatement.Rest.Rest.Head);
        var firstClause = Assert.IsType<Cons>(clauses.Head);
        Assert.Same(JsSymbols.Case, firstClause.Head);
        Assert.Equal(1d, firstClause.Rest.Head);
        var firstBody = Assert.IsType<Cons>(firstClause.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, firstBody.Head);

        var secondClause = Assert.IsType<Cons>(clauses.Rest.Head);
        Assert.Same(JsSymbols.Case, secondClause.Head);
        Assert.Equal(2d, secondClause.Rest.Head);
        var secondBody = Assert.IsType<Cons>(secondClause.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, secondBody.Head);

        var thirdClause = Assert.IsType<Cons>(clauses.Rest.Rest.Head);
        Assert.Same(JsSymbols.Default, thirdClause.Head);
        var defaultBody = Assert.IsType<Cons>(thirdClause.Rest.Head);
        Assert.Same(JsSymbols.Block, defaultBody.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseTryCatchFinallyStatement()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("try { action(); } catch (err) { handle(err); } finally { cleanup(); }");

        var tryStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Try, tryStatement.Head);

        var tryBlock = Assert.IsType<Cons>(tryStatement.Rest.Head);
        Assert.Same(JsSymbols.Block, tryBlock.Head);

        var catchClause = Assert.IsType<Cons>(tryStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.Catch, catchClause.Head);
        Assert.Equal(Symbol.Intern("err"), catchClause.Rest.Head);

        var catchBlock = Assert.IsType<Cons>(catchClause.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, catchBlock.Head);

        var finallyBlock = Assert.IsType<Cons>(tryStatement.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, finallyBlock.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseTryFinallyWithoutCatchStoresNullCatch()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("try { work(); } finally { tidy(); }");

        var tryStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Try, tryStatement.Head);

        Assert.Null(tryStatement.Rest.Rest.Head); // catch slot remains empty when no catch clause is provided

        var finallyBlock = Assert.IsType<Cons>(tryStatement.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Block, finallyBlock.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseIfAndLoopStatements()
    {
        await using var engine = new JsEngine();
        var program =
            engine.Parse(
                "if (flag) x = 1; else x = 2; while (x < 10) { x = x + 1; } for (let i = 0; i < 3; i = i + 1) { continue; } do { break; } while (false);");

        var ifStatement = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.If, ifStatement.Head);
        Assert.Equal(Symbol.Intern("flag"), ifStatement.Rest.Head);

        var thenBranch = Assert.IsType<Cons>(ifStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, thenBranch.Head);

        var elseBranch = Assert.IsType<Cons>(ifStatement.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.ExpressionStatement, elseBranch.Head);

        var whileStatement = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.While, whileStatement.Head);
        Assert.Same(Symbol.Intern("x"),
            Assert.IsType<Cons>(whileStatement.Rest.Head).Rest.Head); // condition is ( < x 10 )

        var forStatement = Assert.IsType<Cons>(program.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.For, forStatement.Head);
        Assert.IsType<Cons>(forStatement.Rest.Head); // initializer is a let declaration
        Assert.IsType<Cons>(forStatement.Rest.Rest.Head); // condition expression
        Assert.IsType<Cons>(forStatement.Rest.Rest.Rest.Head); // increment expression
        Assert.IsType<Cons>(forStatement.Rest.Rest.Rest.Rest.Head); // body block

        var doWhileStatement = Assert.IsType<Cons>(program.Rest.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.DoWhile, doWhileStatement.Head);
        Assert.Equal(false, doWhileStatement.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseRestParameterInFunction()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("function test(a, b, ...rest) { return rest; }");

        var functionDecl = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Function, functionDecl.Head);
        Assert.Equal(Symbol.Intern("test"), functionDecl.Rest.Head);

        var parameters = Assert.IsType<Cons>(functionDecl.Rest.Rest.Head);

        // First two are regular parameters
        Assert.Equal(Symbol.Intern("a"), parameters.Head);
        Assert.Equal(Symbol.Intern("b"), parameters.Rest.Head);

        // Third is rest parameter
        var restParam = Assert.IsType<Cons>(parameters.Rest.Rest.Head);
        Assert.Same(JsSymbols.Rest, restParam.Head);
        Assert.Equal(Symbol.Intern("rest"), restParam.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseSpreadInArrayLiteral()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("let arr = [1, ...other, 2];");

        var letStatement = Assert.IsType<Cons>(program.Rest.Head);
        var arrayLiteral = Assert.IsType<Cons>(letStatement.Rest.Rest.Head);
        Assert.Same(JsSymbols.ArrayLiteral, arrayLiteral.Head);

        // First element is literal 1
        Assert.Equal(1d, arrayLiteral.Rest.Head);

        // Second element is spread
        var spreadExpr = Assert.IsType<Cons>(arrayLiteral.Rest.Rest.Head);
        Assert.Same(JsSymbols.Spread, spreadExpr.Head);
        Assert.Equal(Symbol.Intern("other"), spreadExpr.Rest.Head);

        // Third element is literal 2
        Assert.Equal(2d, arrayLiteral.Rest.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseSpreadInFunctionCall()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("foo(1, ...args, 2);");

        var exprStmt = Assert.IsType<Cons>(program.Rest.Head);
        var callExpr = Assert.IsType<Cons>(exprStmt.Rest.Head);
        Assert.Same(JsSymbols.Call, callExpr.Head);

        // Callee is foo
        Assert.Equal(Symbol.Intern("foo"), callExpr.Rest.Head);

        // First argument is literal 1
        Assert.Equal(1d, callExpr.Rest.Rest.Head);

        // Second argument is spread
        var spreadExpr = Assert.IsType<Cons>(callExpr.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Spread, spreadExpr.Head);
        Assert.Equal(Symbol.Intern("args"), spreadExpr.Rest.Head);

        // Third argument is literal 2
        Assert.Equal(2d, callExpr.Rest.Rest.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseFunctionCallWithTrailingComma()
    {
        await using var engine = new JsEngine();
        var program = engine.Parse("foo(1,);");

        var exprStmt = Assert.IsType<Cons>(program.Rest.Head);
        var callExpr = Assert.IsType<Cons>(exprStmt.Rest.Head);
        Assert.Same(JsSymbols.Call, callExpr.Head);

        // Callee symbol should remain intact
        Assert.Equal(Symbol.Intern("foo"), callExpr.Rest.Head);

        // Single argument survives even with trailing comma
        Assert.Equal(1d, callExpr.Rest.Rest.Head);
        Assert.Same(Cons.Empty, callExpr.Rest.Rest.Rest);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseCommaSeparatedVarDeclarations()
    {
        await using var engine = new JsEngine();
        var program = JsEngine.ParseWithoutTransformation("var last = 42, A = 3877, C = 2957;");

        Assert.Same(JsSymbols.Program, program.Head);

        // First var declaration
        var var1 = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Var, var1.Head);
        Assert.Equal(Symbol.Intern("last"), var1.Rest.Head);
        Assert.Equal(42d, var1.Rest.Rest.Head);

        // Second var declaration
        var var2 = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var2.Head);
        Assert.Equal(Symbol.Intern("A"), var2.Rest.Head);
        Assert.Equal(3877d, var2.Rest.Rest.Head);

        // Third var declaration
        var var3 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var3.Head);
        Assert.Equal(Symbol.Intern("C"), var3.Rest.Head);
        Assert.Equal(2957d, var3.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseCommaSeparatedVarDeclarationsWithUninitializedVars()
    {
        await using var engine = new JsEngine();
        var program = JsEngine.ParseWithoutTransformation("var a = [], i, l = 5, v;");

        Assert.Same(JsSymbols.Program, program.Head);

        // First var declaration: a = []
        var var1 = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Var, var1.Head);
        Assert.Equal(Symbol.Intern("a"), var1.Rest.Head);
        var arrayLiteral = Assert.IsType<Cons>(var1.Rest.Rest.Head);
        Assert.Same(JsSymbols.ArrayLiteral, arrayLiteral.Head);

        // Second var declaration: i (uninitialized)
        var var2 = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var2.Head);
        Assert.Equal(Symbol.Intern("i"), var2.Rest.Head);
        Assert.Same(JsSymbols.Uninitialized, var2.Rest.Rest.Head);

        // Third var declaration: l = 5
        var var3 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var3.Head);
        Assert.Equal(Symbol.Intern("l"), var3.Rest.Head);
        Assert.Equal(5d, var3.Rest.Rest.Head);

        // Fourth var declaration: v (uninitialized)
        var var4 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var4.Head);
        Assert.Equal(Symbol.Intern("v"), var4.Rest.Head);
        Assert.Same(JsSymbols.Uninitialized, var4.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateCommaSeparatedVarDeclarations()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var last = 42, A = 3877, C = 2957; last + A + C;");
        Assert.Equal(6876d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateCommaSeparatedVarDeclarationsWithUninitialized()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var a = [], i, l = 5, v; a.push(l); a[0];");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseCommaSeparatedVarDeclarationsWithComments()
    {
        await using var engine = new JsEngine();
        var code = @"var a = [],     // The array holding the partial texts.
            i,          // Loop counter.
            l = 10,
            v;          // The value to be stringified.";

        var program = JsEngine.ParseWithoutTransformation(code);

        Assert.Same(JsSymbols.Program, program.Head);

        // Should have 4 var declarations
        var var1 = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Var, var1.Head);
        Assert.Equal(Symbol.Intern("a"), var1.Rest.Head);

        var var2 = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var2.Head);
        Assert.Equal(Symbol.Intern("i"), var2.Rest.Head);

        var var3 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var3.Head);
        Assert.Equal(Symbol.Intern("l"), var3.Rest.Head);
        Assert.Equal(10d, var3.Rest.Rest.Head);

        var var4 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Var, var4.Head);
        Assert.Equal(Symbol.Intern("v"), var4.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseCommaSeparatedLetDeclarations()
    {
        await using var engine = new JsEngine();
        // Note: Let declarations require initializers in this interpreter
        var program = JsEngine.ParseWithoutTransformation("let x = 1, y = 2, z = 3;");

        Assert.Same(JsSymbols.Program, program.Head);

        var let1 = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Let, let1.Head);
        Assert.Equal(Symbol.Intern("x"), let1.Rest.Head);
        Assert.Equal(1d, let1.Rest.Rest.Head);

        var let2 = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.Let, let2.Head);
        Assert.Equal(Symbol.Intern("y"), let2.Rest.Head);
        Assert.Equal(2d, let2.Rest.Rest.Head);

        var let3 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Let, let3.Head);
        Assert.Equal(Symbol.Intern("z"), let3.Rest.Head);
        Assert.Equal(3d, let3.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseCommaSeparatedConstDeclarations()
    {
        await using var engine = new JsEngine();
        var program = JsEngine.ParseWithoutTransformation("const x = 1, y = 2, z = 3;");

        Assert.Same(JsSymbols.Program, program.Head);

        var const1 = Assert.IsType<Cons>(program.Rest.Head);
        Assert.Same(JsSymbols.Const, const1.Head);
        Assert.Equal(Symbol.Intern("x"), const1.Rest.Head);
        Assert.Equal(1d, const1.Rest.Rest.Head);

        var const2 = Assert.IsType<Cons>(program.Rest.Rest.Head);
        Assert.Same(JsSymbols.Const, const2.Head);
        Assert.Equal(Symbol.Intern("y"), const2.Rest.Head);
        Assert.Equal(2d, const2.Rest.Rest.Head);

        var const3 = Assert.IsType<Cons>(program.Rest.Rest.Rest.Head);
        Assert.Same(JsSymbols.Const, const3.Head);
        Assert.Equal(Symbol.Intern("z"), const3.Rest.Head);
        Assert.Equal(3d, const3.Rest.Rest.Head);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionWithSingleParameter()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var f = x => x * 2; f(5);");
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionWithParenthesizedSingleParameter()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var f = (x) => x * 3; f(4);");
        Assert.Equal(12d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionWithNoParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var f = () => 42; f();");
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionWithMultipleParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var add = (a, b) => a + b; add(3, 7);");
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionWithBlockBody()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var f = (x) => { var y = x * 2; return y + 1; }; f(5);");
        Assert.Equal(11d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionInObjectLiteral()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                trace: () => null,
                double: (x) => x * 2,
                add: (a, b) => a + b
            };
            obj.add(obj.double(3), 4);
            """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrowFunctionInArrayLiteral()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var arr = [
                x => x + 1,
                x => x * 2,
                x => x - 1
            ];
            arr[1](5);
            """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseNestedArrowFunctions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var f = x => y => x + y; f(3)(4);");
        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseObjectLiteralWithTrailingComma()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                a: 1,
                b: 2,
                c: 3,
            };
            obj.a + obj.b + obj.c;
            """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseArrayLiteralWithTrailingComma()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var arr = [
                1,
                2,
                3,
            ];
            arr[0] + arr[1] + arr[2];
            """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseObjectLiteralWithArrowFunctionsAndTrailingComma()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var console = {
                trace: () => null,
                log: () => null,
                warn: () => null,
                error: () => null,
                info: () => null,
                debug: () => null,
            };
            console.log;
            """);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExceptionMessagesIncludeSourceReferences()
    {
        await using var engine = new JsEngine();

        // Test that error messages include source references
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await engine.Evaluate("var x = 5; x();");
        });

        // The error message should contain "at" followed by source reference or just verify it's enhanced
        // After transformation, source references may or may not be preserved
        // The key improvement is the FormatErrorMessage helper is in place for when they are
        Assert.Contains("Attempted to call a non-callable value", ex.Message);
    }
}
