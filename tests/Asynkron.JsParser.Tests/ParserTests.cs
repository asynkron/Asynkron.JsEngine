namespace Asynkron.JsParser.Tests;

public class ParserTests
{
    private static ProgramNode Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new TypedAstParser(tokens, source);
        return parser.ParseProgram();
    }

    [Fact]
    public void ParseProgram_EmptySource_ReturnsEmptyProgram()
    {
        var program = Parse("");
        Assert.Empty(program.Body);
    }

    [Fact]
    public void ParseProgram_VariableDeclaration_Let()
    {
        var program = Parse("let x = 10;");

        Assert.Single(program.Body);
        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.Equal(VariableKind.Let, varDecl.Kind);
        Assert.Single(varDecl.Declarators);
        var binding = Assert.IsType<IdentifierBinding>(varDecl.Declarators[0].Target);
        Assert.Equal("x", binding.Name.Name);
    }

    [Fact]
    public void ParseProgram_VariableDeclaration_Const()
    {
        var program = Parse("const y = 20;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.Equal(VariableKind.Const, varDecl.Kind);
    }

    [Fact]
    public void ParseProgram_VariableDeclaration_Var()
    {
        var program = Parse("var z = 30;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.Equal(VariableKind.Var, varDecl.Kind);
    }

    [Fact]
    public void ParseProgram_FunctionDeclaration()
    {
        var program = Parse("function add(a, b) { return a + b; }");

        Assert.Single(program.Body);
        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        Assert.Equal("add", funcDecl.Name.Name);
        Assert.Equal(2, funcDecl.Function.Parameters.Count);
        Assert.Equal("a", funcDecl.Function.Parameters[0].Name!.Name);
        Assert.Equal("b", funcDecl.Function.Parameters[1].Name!.Name);
    }

    [Fact]
    public void ParseProgram_ArrowFunction()
    {
        var program = Parse("const fn = (x) => x * 2;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var funcExpr = Assert.IsType<FunctionExpression>(varDecl.Declarators[0].Initializer);
        Assert.True(funcExpr.IsArrow);
        Assert.Single(funcExpr.Parameters);
    }

    [Fact]
    public void ParseProgram_AsyncFunction()
    {
        var program = Parse("async function fetchData() { return await Promise.resolve(1); }");

        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        Assert.True(funcDecl.Function.IsAsync);
    }

    [Fact]
    public void ParseProgram_GeneratorFunction()
    {
        var program = Parse("function* generator() { yield 1; yield 2; }");

        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        Assert.True(funcDecl.Function.IsGenerator);
    }

    [Fact]
    public void ParseProgram_IfStatement()
    {
        var program = Parse("if (x > 0) { return true; } else { return false; }");

        var ifStmt = Assert.IsType<IfStatement>(program.Body[0]);
        Assert.NotNull(ifStmt.Condition);
        Assert.NotNull(ifStmt.Then);
        Assert.NotNull(ifStmt.Else);
    }

    [Fact]
    public void ParseProgram_ForLoop()
    {
        var program = Parse("for (let i = 0; i < 10; i++) { console.log(i); }");

        var forStmt = Assert.IsType<ForStatement>(program.Body[0]);
        Assert.NotNull(forStmt.Initializer);
        Assert.NotNull(forStmt.Condition);
        Assert.NotNull(forStmt.Increment);
    }

    [Fact]
    public void ParseProgram_ForOfLoop()
    {
        var program = Parse("for (const item of items) { console.log(item); }");

        var forOfStmt = Assert.IsType<ForEachStatement>(program.Body[0]);
        Assert.Equal(ForEachKind.Of, forOfStmt.Kind);
    }

    [Fact]
    public void ParseProgram_ForInLoop()
    {
        var program = Parse("for (const key in obj) { console.log(key); }");

        var forInStmt = Assert.IsType<ForEachStatement>(program.Body[0]);
        Assert.Equal(ForEachKind.In, forInStmt.Kind);
    }

    [Fact]
    public void ParseProgram_WhileLoop()
    {
        var program = Parse("while (x > 0) { x--; }");

        var whileStmt = Assert.IsType<WhileStatement>(program.Body[0]);
        Assert.NotNull(whileStmt.Condition);
        Assert.NotNull(whileStmt.Body);
    }

    [Fact]
    public void ParseProgram_DoWhileLoop()
    {
        var program = Parse("do { x++; } while (x < 10);");

        var doWhileStmt = Assert.IsType<DoWhileStatement>(program.Body[0]);
        Assert.NotNull(doWhileStmt.Condition);
        Assert.NotNull(doWhileStmt.Body);
    }

    [Fact]
    public void ParseProgram_ClassDeclaration()
    {
        var program = Parse("class Person { constructor(name) { this.name = name; } }");

        var classDecl = Assert.IsType<ClassDeclaration>(program.Body[0]);
        Assert.Equal("Person", classDecl.Name.Name);
    }

    [Fact]
    public void ParseProgram_ClassWithExtends()
    {
        var program = Parse("class Student extends Person { }");

        var classDecl = Assert.IsType<ClassDeclaration>(program.Body[0]);
        Assert.NotNull(classDecl.Definition.Extends);
    }

    [Fact]
    public void ParseProgram_TryCatchFinally()
    {
        var program = Parse("try { doSomething(); } catch (e) { handleError(e); } finally { cleanup(); }");

        var tryStmt = Assert.IsType<TryStatement>(program.Body[0]);
        Assert.NotNull(tryStmt.TryBlock);
        Assert.NotNull(tryStmt.Catch);
        Assert.NotNull(tryStmt.Finally);
    }

    [Fact]
    public void ParseProgram_SwitchStatement()
    {
        var program = Parse("switch (x) { case 1: break; default: break; }");

        var switchStmt = Assert.IsType<SwitchStatement>(program.Body[0]);
        Assert.Equal(2, switchStmt.Cases.Count);
    }

    [Fact]
    public void ParseProgram_ObjectLiteral()
    {
        var program = Parse("const obj = { a: 1, b: 2 };");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var objExpr = Assert.IsType<ObjectExpression>(varDecl.Declarators[0].Initializer);
        Assert.Equal(2, objExpr.Members.Count);
    }

    [Fact]
    public void ParseProgram_ArrayLiteral()
    {
        var program = Parse("const arr = [1, 2, 3];");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var arrExpr = Assert.IsType<ArrayExpression>(varDecl.Declarators[0].Initializer);
        Assert.Equal(3, arrExpr.Elements.Count);
    }

    [Fact]
    public void ParseProgram_SpreadOperator()
    {
        var program = Parse("const merged = [...arr1, ...arr2];");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var arrExpr = Assert.IsType<ArrayExpression>(varDecl.Declarators[0].Initializer);
        Assert.True(arrExpr.Elements[0].IsSpread);
        Assert.True(arrExpr.Elements[1].IsSpread);
    }

    [Fact]
    public void ParseProgram_DestructuringAssignment_Array()
    {
        var program = Parse("const [a, b] = [1, 2];");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.IsType<ArrayBinding>(varDecl.Declarators[0].Target);
    }

    [Fact]
    public void ParseProgram_DestructuringAssignment_Object()
    {
        var program = Parse("const { x, y } = point;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.IsType<ObjectBinding>(varDecl.Declarators[0].Target);
    }

    [Fact]
    public void ParseProgram_TemplateLiteral()
    {
        var program = Parse("const msg = `Hello ${name}!`;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.IsType<TemplateLiteralExpression>(varDecl.Declarators[0].Initializer);
    }

    [Fact]
    public void ParseProgram_OptionalChaining()
    {
        var program = Parse("const value = obj?.prop?.nested;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var memberExpr = Assert.IsType<MemberExpression>(varDecl.Declarators[0].Initializer);
        Assert.True(memberExpr.IsOptional);
    }

    [Fact]
    public void ParseProgram_NullishCoalescing()
    {
        var program = Parse("const value = x ?? defaultValue;");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var binaryExpr = Assert.IsType<BinaryExpression>(varDecl.Declarators[0].Initializer);
        Assert.Equal(BinaryOperator.NullishCoalescing, binaryExpr.Operator);
    }

    [Fact]
    public void ParseProgram_ThrowStatement()
    {
        var program = Parse("throw new Error('message');");

        var throwStmt = Assert.IsType<ThrowStatement>(program.Body[0]);
        Assert.NotNull(throwStmt.Expression);
    }

    [Fact]
    public void ParseProgram_BreakStatement()
    {
        var program = Parse("while (true) { break; }");

        var whileStmt = Assert.IsType<WhileStatement>(program.Body[0]);
        var block = Assert.IsType<BlockStatement>(whileStmt.Body);
        Assert.IsType<BreakStatement>(block.Statements[0]);
    }

    [Fact]
    public void ParseProgram_ContinueStatement()
    {
        var program = Parse("while (true) { continue; }");

        var whileStmt = Assert.IsType<WhileStatement>(program.Body[0]);
        var block = Assert.IsType<BlockStatement>(whileStmt.Body);
        Assert.IsType<ContinueStatement>(block.Statements[0]);
    }

    [Fact]
    public void ParseProgram_LabeledStatement()
    {
        var program = Parse("outer: for (let i = 0; i < 10; i++) { }");

        var labeledStmt = Assert.IsType<LabeledStatement>(program.Body[0]);
        Assert.Equal("outer", labeledStmt.Label.Name);
    }

    [Fact]
    public void ParseProgram_ReturnWithLineBreak_ASI()
    {
        // According to ASI rules, return\n{} should insert semicolon after return
        var program = Parse("function test() { return\n{} }");

        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        var block = funcDecl.Function.Body;

        // The return statement should have no expression (ASI inserted semicolon)
        var returnStmt = Assert.IsType<ReturnStatement>(block.Statements[0]);
        Assert.Null(returnStmt.Expression);
    }

    [Fact]
    public void ParseProgram_AutomaticSemicolonInsertion()
    {
        // Multiple statements without semicolons
        var program = Parse("let x = 1\nlet y = 2\nx + y");

        Assert.Equal(3, program.Body.Count);
    }

    [Fact]
    public void ParseError_MissingSemicolonAfterThrow()
    {
        // throw\nexpression should fail - line terminator not allowed after throw
        var exception = Assert.Throws<ParseException>(() => Parse("throw\nnew Error()"));

        Assert.NotNull(exception);
    }

    [Fact]
    public void ParseError_MissingExpression()
    {
        var exception = Assert.Throws<ParseException>(() => Parse("let x = ;"));

        Assert.NotNull(exception.Message);
        Assert.Contains("Source context:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseError_HasLineAndColumn()
    {
        var exception = Assert.Throws<ParseException>(() => Parse("let x = ;\nlet y = 2;"));

        Assert.NotNull(exception.Line);
        Assert.NotNull(exception.Column);
        Assert.True(exception.Line > 0);
    }

    [Fact]
    public void ParseProgram_StrictMode_DirectiveRecognized()
    {
        var program = Parse("'use strict';\nlet x = 1;");

        Assert.True(program.IsStrict);
    }

    [Fact]
    public void ParseProgram_ComputedPropertyName()
    {
        var program = Parse("const obj = { [key]: value };");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var objExpr = Assert.IsType<ObjectExpression>(varDecl.Declarators[0].Initializer);
        Assert.True(objExpr.Members[0].IsComputed);
    }

    [Fact]
    public void ParseProgram_MethodShorthand()
    {
        var program = Parse("const obj = { method() { return 1; } };");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var objExpr = Assert.IsType<ObjectExpression>(varDecl.Declarators[0].Initializer);
        Assert.Equal(ObjectMemberKind.Method, objExpr.Members[0].Kind);
    }

    [Fact]
    public void ParseProgram_GetterSetter()
    {
        var program = Parse("const obj = { get x() { return 1; }, set x(v) { } };");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var objExpr = Assert.IsType<ObjectExpression>(varDecl.Declarators[0].Initializer);
        Assert.Equal(ObjectMemberKind.Getter, objExpr.Members[0].Kind);
        Assert.Equal(ObjectMemberKind.Setter, objExpr.Members[1].Kind);
    }

    [Fact]
    public void ParseProgram_AwaitExpression()
    {
        var program = Parse("async function test() { const result = await promise; }");

        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        var block = funcDecl.Function.Body;
        var varDecl = Assert.IsType<VariableDeclaration>(block.Statements[0]);
        Assert.IsType<AwaitExpression>(varDecl.Declarators[0].Initializer);
    }

    [Fact]
    public void ParseProgram_YieldExpression()
    {
        var program = Parse("function* gen() { yield 1; }");

        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        var block = funcDecl.Function.Body;
        var exprStmt = Assert.IsType<ExpressionStatement>(block.Statements[0]);
        Assert.IsType<YieldExpression>(exprStmt.Expression);
    }

    [Fact]
    public void ParseProgram_YieldDelegated()
    {
        var program = Parse("function* gen() { yield* other(); }");

        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        var block = funcDecl.Function.Body;
        var exprStmt = Assert.IsType<ExpressionStatement>(block.Statements[0]);
        var yieldExpr = Assert.IsType<YieldExpression>(exprStmt.Expression);
        Assert.True(yieldExpr.IsDelegated);
    }

    [Fact]
    public void ParseProgram_NewExpression()
    {
        var program = Parse("const obj = new Constructor(arg1, arg2);");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var newExpr = Assert.IsType<NewExpression>(varDecl.Declarators[0].Initializer);
        Assert.Equal(2, newExpr.Arguments.Count);
    }

    [Fact]
    public void ParseProgram_ConditionalExpression()
    {
        var program = Parse("const result = condition ? 'yes' : 'no';");

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        Assert.IsType<ConditionalExpression>(varDecl.Declarators[0].Initializer);
    }

    [Fact]
    public void ParseProgram_UnaryExpression()
    {
        var program = Parse("const negative = -x; const not = !flag; const type = typeof obj;");

        Assert.Equal(3, program.Body.Count);
    }

    [Fact]
    public void ParseProgram_WithStatement()
    {
        // Note: with is deprecated but still valid in non-strict mode
        var program = Parse("with (obj) { x = 1; }");

        Assert.IsType<WithStatement>(program.Body[0]);
    }

    [Fact]
    public void ParseProgram_EmptyStatement()
    {
        var program = Parse(";;;");

        Assert.Equal(3, program.Body.Count);
        Assert.All(program.Body, stmt => Assert.IsType<EmptyStatement>(stmt));
    }

    [Fact]
    public void ParseProgram_PrivateField()
    {
        var program = Parse("class Foo { #privateField = 1; getPrivate() { return this.#privateField; } }");

        var classDecl = Assert.IsType<ClassDeclaration>(program.Body[0]);
        Assert.NotEmpty(classDecl.Definition.Members);
    }
}
