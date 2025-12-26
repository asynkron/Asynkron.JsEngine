using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for the ScopeAnalyzer to verify correct scope ID and slot index assignment.
/// Note: These tests use the parser and scope analyzer directly, so the EnableFastPaths
/// setting does not affect their behavior. Both FastPath and Reference modes run identically.
/// </summary>
public abstract class ScopeAnalyzerTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    private static ProgramNode ParseAndAnalyze(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new TypedAstParser(tokens, source);
        var program = parser.ParseProgram();
        var analyzer = new ScopeAnalyzer();
        return analyzer.Analyze(program);
    }

    [Fact]
    public void FunctionDeclaration_NameInOuterScope_ParameterInFunctionScope()
    {
        // function fibonacci(n) { return fibonacci(n - 1); }
        var program = ParseAndAnalyze(@"
            function fibonacci(n) {
                return fibonacci(n - 1);
            }
        ");

        // Program scope should be ScopeId=0
        Assert.Equal(0, program.ScopeId);

        // Get the function declaration
        var funcDecl = Assert.IsType<FunctionDeclaration>(program.Body[0]);
        var func = funcDecl.Function;

        // Function body scope should be ScopeId=1 (no intermediate scope for declarations)
        Assert.Equal(1, func.ScopeId);
        Assert.Equal(-1, func.FunctionNameScopeId); // No function name scope for declarations

        // Parameter 'n' should be slot 0 in the function scope
        Assert.Single(func.Parameters);
        Assert.Equal("n", func.Parameters[0].Name!.Name);

        // Inside the function body, find the return statement
        var returnStmt = Assert.IsType<ReturnStatement>(func.Body.Statements[0]);
        var callExpr = Assert.IsType<CallExpression>(returnStmt.Expression);

        // The callee 'fibonacci' should reference the outer scope (ScopeId=0)
        var calleeId = Assert.IsType<IdentifierExpression>(callExpr.Callee);
        Assert.Equal("fibonacci", calleeId.Name.Name);
        Assert.Equal(0, calleeId.ScopeId); // fibonacci is in outer scope

        // The argument 'n' should reference the function scope (ScopeId=1)
        var binaryExpr = Assert.IsType<BinaryExpression>(callExpr.Arguments[0].Expression);
        var argId = Assert.IsType<IdentifierExpression>(binaryExpr.Left);
        Assert.Equal("n", argId.Name.Name);
        Assert.Equal(1, argId.ScopeId); // n is in function scope
        Assert.Equal(0, argId.SlotIndex); // n is slot 0
    }

    [Fact]
    public void NamedFunctionExpression_NameInIntermediateScope_ParameterInFunctionScope()
    {
        // var f = function factorial(n) { return factorial(n - 1); };
        var program = ParseAndAnalyze(@"
            var f = function factorial(n) {
                return factorial(n - 1);
            };
        ");

        // Program scope should be ScopeId=0
        Assert.Equal(0, program.ScopeId);

        // Get the variable declaration and function expression
        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var funcExpr = Assert.IsType<FunctionExpression>(varDecl.Declarators[0].Initializer);

        // Function name scope should be ScopeId=1 (intermediate scope)
        Assert.Equal(1, funcExpr.FunctionNameScopeId);

        // Function body/parameter scope should be ScopeId=2
        Assert.Equal(2, funcExpr.ScopeId);

        // Parameter 'n' should be slot 0 in the function scope (ScopeId=2)
        Assert.Single(funcExpr.Parameters);
        Assert.Equal("n", funcExpr.Parameters[0].Name!.Name);

        // Inside the function body, find the return statement
        var returnStmt = Assert.IsType<ReturnStatement>(funcExpr.Body.Statements[0]);
        var callExpr = Assert.IsType<CallExpression>(returnStmt.Expression);

        // The callee 'factorial' should reference the function name scope (ScopeId=1)
        var calleeId = Assert.IsType<IdentifierExpression>(callExpr.Callee);
        Assert.Equal("factorial", calleeId.Name.Name);
        Assert.Equal(1, calleeId.ScopeId); // factorial is in function name scope
        Assert.Equal(0, calleeId.SlotIndex); // factorial is slot 0 in that scope

        // The argument 'n' should reference the function scope (ScopeId=2)
        var binaryExpr = Assert.IsType<BinaryExpression>(callExpr.Arguments[0].Expression);
        var argId = Assert.IsType<IdentifierExpression>(binaryExpr.Left);
        Assert.Equal("n", argId.Name.Name);
        Assert.Equal(2, argId.ScopeId); // n is in function parameter scope
        Assert.Equal(0, argId.SlotIndex); // n is slot 0
    }

    [Fact]
    public void AnonymousFunctionExpression_NoIntermediateScope()
    {
        // var f = function(n) { return n * 2; };
        var program = ParseAndAnalyze(@"
            var f = function(n) {
                return n * 2;
            };
        ");

        // Program scope should be ScopeId=0
        Assert.Equal(0, program.ScopeId);

        // Get the variable declaration and function expression
        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var funcExpr = Assert.IsType<FunctionExpression>(varDecl.Declarators[0].Initializer);

        // No function name scope for anonymous functions
        Assert.Equal(-1, funcExpr.FunctionNameScopeId);

        // Function body/parameter scope should be ScopeId=1 (directly after program scope)
        Assert.Equal(1, funcExpr.ScopeId);
    }

    [Fact]
    public void AnonymousFunctionExpression_OuterVariableReference_ScopeIds()
    {
        // This is the failing test case - anonymous function referencing outer variable
        var program = ParseAndAnalyze(@"
            var __func = function (arg){
                if (arg === 1) {
                    return arg;
                } else {
                    return __func(arg-1)*arg;
                }
            };
            __func(3);
        ");

        // Program scope should be ScopeId=0
        Assert.Equal(0, program.ScopeId);

        // Get the variable declaration and function expression
        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var funcExpr = Assert.IsType<FunctionExpression>(varDecl.Declarators[0].Initializer);

        // No function name scope for anonymous functions
        Assert.Equal(-1, funcExpr.FunctionNameScopeId);

        // Function body/parameter scope
        var functionScopeId = funcExpr.ScopeId;
        Console.WriteLine($"Function ScopeId: {functionScopeId}");

        // Find the reference to __func inside the else branch
        var ifStmt = Assert.IsType<IfStatement>(funcExpr.Body.Statements[0]);
        var elseBlock = Assert.IsType<BlockStatement>(ifStmt.Else);
        var elseReturn = Assert.IsType<ReturnStatement>(elseBlock.Statements[0]);
        var binaryExpr = Assert.IsType<BinaryExpression>(elseReturn.Expression);
        var callExpr = Assert.IsType<CallExpression>(binaryExpr.Left);
        var funcRef = Assert.IsType<IdentifierExpression>(callExpr.Callee);

        Console.WriteLine($"__func reference: ScopeId={funcRef.ScopeId}, SlotIndex={funcRef.SlotIndex}");

        // __func should reference the program scope (ScopeId=0)
        Assert.Equal(0, funcRef.ScopeId);
        // __func should have a slot index assigned
        Assert.True(funcRef.SlotIndex >= 0, $"__func SlotIndex should be >= 0, got {funcRef.SlotIndex}");

        // arg should reference the function scope
        var argRef = Assert.IsType<IdentifierExpression>(binaryExpr.Right);
        Console.WriteLine($"arg reference: ScopeId={argRef.ScopeId}, SlotIndex={argRef.SlotIndex}");
        Assert.Equal(functionScopeId, argRef.ScopeId);
    }

    [Fact]
    public void ArrowFunction_NoIntermediateScope()
    {
        // var f = (n) => n * 2;
        var program = ParseAndAnalyze(@"
            var f = (n) => n * 2;
        ");

        // Program scope should be ScopeId=0
        Assert.Equal(0, program.ScopeId);

        // Get the variable declaration and arrow function
        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var funcExpr = Assert.IsType<FunctionExpression>(varDecl.Declarators[0].Initializer);

        Assert.True(funcExpr.IsArrow);

        // No function name scope for arrow functions
        Assert.Equal(-1, funcExpr.FunctionNameScopeId);

        // Function body/parameter scope should be ScopeId=1
        Assert.Equal(1, funcExpr.ScopeId);
    }

    [Fact]
    public void ForLetLoop_AssignsPerIterationScopeAndSlots()
    {
        var program = ParseAndAnalyze(@"
            let sum = 0;
            for (let i = 0; i < 3; i = i + 1) {
                sum = sum + i;
            }
        ");

        var forStmt = Assert.IsType<ForStatement>(program.Body[1]);

        Assert.True(forStmt.PerIterationScopeId >= 0);
        Assert.Equal(1, forStmt.PerIterationSlotCount);
        Assert.Single(forStmt.PerIterationSlotIndices);
        Assert.Equal(0, forStmt.PerIterationSlotIndices[0]);

        var increment = Assert.IsType<AssignmentExpression>(forStmt.Increment);
        Assert.Equal("i", increment.Target.Name);
        Assert.Equal(forStmt.PerIterationScopeId, increment.ScopeId);
        Assert.Equal(0, increment.SlotIndex);
    }

    [Fact]
    public void NestedNamedFunctionExpressions_CorrectScopeIds()
    {
        // var outer = function outerFn(a) {
        //     var inner = function innerFn(b) {
        //         return outerFn(a) + innerFn(b);
        //     };
        //     return inner;
        // };
        var program = ParseAndAnalyze(@"
            var outer = function outerFn(a) {
                var inner = function innerFn(b) {
                    return outerFn(a) + innerFn(b);
                };
                return inner;
            };
        ");

        // Program: ScopeId=0
        Assert.Equal(0, program.ScopeId);

        var varDecl = Assert.IsType<VariableDeclaration>(program.Body[0]);
        var outerFunc = Assert.IsType<FunctionExpression>(varDecl.Declarators[0].Initializer);

        // outerFn name scope: ScopeId=1
        Assert.Equal(1, outerFunc.FunctionNameScopeId);
        // outerFn parameter scope: ScopeId=2
        Assert.Equal(2, outerFunc.ScopeId);

        // Find the inner function in the outer function's body
        var innerVarDecl = Assert.IsType<VariableDeclaration>(outerFunc.Body.Statements[0]);
        var innerFunc = Assert.IsType<FunctionExpression>(innerVarDecl.Declarators[0].Initializer);

        // innerFn name scope: ScopeId=4 (after block scope 3)
        Assert.True(innerFunc.FunctionNameScopeId > outerFunc.ScopeId);
        // innerFn parameter scope: ScopeId=5
        Assert.True(innerFunc.ScopeId > innerFunc.FunctionNameScopeId);
    }
}

public class FastPath_ScopeAnalyzerTests(ITestOutputHelper output) : ScopeAnalyzerTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class Reference_ScopeAnalyzerTests(ITestOutputHelper output) : ScopeAnalyzerTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
