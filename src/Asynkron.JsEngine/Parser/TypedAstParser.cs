using System;
using System.Collections.Immutable;
using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Parser;

/// <summary>
/// Transitional parser that attempts to build the typed AST directly. Until the
/// implementation reaches feature parity with the legacy Cons parser we keep a
/// fallback that reuses the existing parser + builder pipeline.
/// </summary>
public sealed class TypedAstParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _source;
    private readonly SExpressionAstBuilder _astBuilder;
    private static readonly bool DisableFallback =
        string.Equals(Environment.GetEnvironmentVariable("ASYNKRON_DISABLE_TYPED_FALLBACK"), "1",
            StringComparison.Ordinal);

    public TypedAstParser(IReadOnlyList<Token> tokens, string source, SExpressionAstBuilder? astBuilder = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _source = source ?? string.Empty;
        _astBuilder = astBuilder ?? new SExpressionAstBuilder();
    }

    public ProgramNode ParseProgram()
    {
        try
        {
            var direct = new DirectParser(_tokens, _source);
            return direct.ParseProgram();
        }
        catch (Exception ex) when (ex is NotSupportedException or ParseException)
        {
            if (DisableFallback)
            {
                throw;
            }

            var consProgram = ParseConsProgram();
            return _astBuilder.BuildProgram(consProgram);
        }
    }

    private Cons ParseConsProgram()
    {
        var parser = new Parser(_tokens, _source);
        return parser.ParseProgram();
    }

    /// <summary>
    /// Direct typed parser. This currently supports only a subset of the full
    /// JavaScript grammar required by the test suite. Unsupported constructs
    /// throw <see cref="NotSupportedException"/> to trigger the fallback.
    /// </summary>
    private sealed class DirectParser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly string _source;
        private int _current;

        public DirectParser(IReadOnlyList<Token> tokens, string source)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _source = source ?? string.Empty;
        }

        public ProgramNode ParseProgram()
        {
            var statements = ImmutableArray.CreateBuilder<StatementNode>();
            var isStrict = CheckForUseStrictDirective();

            while (!Check(TokenType.Eof))
            {
                statements.Add(ParseStatement());
            }

            return new ProgramNode(CreateSourceReferenceFromRange(0, _current - 1), statements.ToImmutable(),
                isStrict);
        }

        private StatementNode ParseStatement()
        {
            if (Match(TokenType.Semicolon))
            {
                return new EmptyStatement(CreateSourceReference(Previous()));
            }

            if (Check(TokenType.Async) && CheckAhead(TokenType.Function))
            {
                var asyncToken = Advance();
                var functionToken = Advance();
                return ParseFunctionDeclaration(true, functionToken);
            }

            if (Match(TokenType.Function))
            {
                return ParseFunctionDeclaration(false, Previous());
            }

            if (Match(TokenType.Return))
            {
                return ParseReturnStatement();
            }

            if (Match(TokenType.If))
            {
                return ParseIfStatement();
            }

            if (Match(TokenType.While))
            {
                return ParseWhileStatement();
            }

            if (Match(TokenType.Do))
            {
                return ParseDoWhileStatement();
            }

            if (Match(TokenType.For))
            {
                return ParseForStatement(Previous());
            }

            if (Match(TokenType.Break))
            {
                return ParseBreakStatement();
            }

            if (Match(TokenType.Continue))
            {
                return ParseContinueStatement();
            }

            if (Match(TokenType.Throw))
            {
                return ParseThrowStatement();
            }

            if (Match(TokenType.LeftBrace))
            {
                return ParseBlock(true);
            }

            if (Match(TokenType.Let))
            {
                return ParseVariableDeclaration(VariableKind.Let);
            }

            if (Match(TokenType.Const))
            {
                return ParseVariableDeclaration(VariableKind.Const);
            }

            if (Match(TokenType.Var))
            {
                return ParseVariableDeclaration(VariableKind.Var);
            }

            return ParseExpressionStatement();
        }

        private StatementNode ParseFunctionDeclaration(bool isAsync, Token functionKeyword)
        {
            var nameToken = Consume(TokenType.Identifier, "Expected function name.");
            var name = Symbol.Intern(nameToken.Lexeme);
            var function = ParseFunctionTail(name, functionKeyword, isAsync);
            var source = function.Source ?? CreateSourceReference(functionKeyword);
            return new FunctionDeclaration(source, name, function);
        }

        private StatementNode ParseReturnStatement()
        {
            var keyword = Previous();
            ExpressionNode? expression = null;

            if (!Check(TokenType.Semicolon) && !HasLineTerminatorBefore())
            {
                expression = ParseExpression();
            }

            Consume(TokenType.Semicolon, "Expected ';' after return statement.");
            return new ReturnStatement(CreateSourceReference(keyword), expression);
        }

        private StatementNode ParseIfStatement()
        {
            var keyword = Previous();
            Consume(TokenType.LeftParen, "Expected '(' after 'if'.");
            var condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after condition.");
            var thenBranch = ParseStatement();
            StatementNode? elseBranch = null;
            if (Match(TokenType.Else))
            {
                elseBranch = ParseStatement();
            }

            return new IfStatement(CreateSourceReference(keyword), condition, thenBranch, elseBranch);
        }

        private StatementNode ParseWhileStatement()
        {
            var keyword = Previous();
            Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
            var condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after condition.");
            var body = ParseStatement();
            return new WhileStatement(CreateSourceReference(keyword), condition, body);
        }

        private StatementNode ParseDoWhileStatement()
        {
            var keyword = Previous();
            var body = ParseStatement();
            Consume(TokenType.While, "Expected 'while' after do-while body.");
            Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
            var condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after condition.");
            Consume(TokenType.Semicolon, "Expected ';' after do-while statement.");
            return new DoWhileStatement(CreateSourceReference(keyword), body, condition);
        }

        private StatementNode ParseBreakStatement()
        {
            var keyword = Previous();
            Symbol? label = null;
            if (Check(TokenType.Identifier) && !HasLineTerminatorBefore())
            {
                var labelToken = Advance();
                label = Symbol.Intern(labelToken.Lexeme);
            }

            Consume(TokenType.Semicolon, "Expected ';' after break statement.");
            return new BreakStatement(CreateSourceReference(keyword), label);
        }

        private StatementNode ParseContinueStatement()
        {
            var keyword = Previous();
            Symbol? label = null;
            if (Check(TokenType.Identifier) && !HasLineTerminatorBefore())
            {
                var labelToken = Advance();
                label = Symbol.Intern(labelToken.Lexeme);
            }

            Consume(TokenType.Semicolon, "Expected ';' after continue statement.");
            return new ContinueStatement(CreateSourceReference(keyword), label);
        }

        private StatementNode ParseThrowStatement()
        {
            var keyword = Previous();
            if (HasLineTerminatorBefore())
            {
                throw new ParseException("Illegal newline after 'throw'.", Peek(), _source);
            }

            var expression = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after throw.");
            return new ThrowStatement(CreateSourceReference(keyword), expression);
        }

        private StatementNode ParseExpressionStatement()
        {
            var start = Peek();
            var expression = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after expression.");
            return new ExpressionStatement(CreateSourceReference(start), expression);
        }

        private StatementNode ParseVariableDeclaration(VariableKind kind, bool requireSemicolon = true,
            bool allowInitializerless = false)
        {
            var start = Previous();
            var declarators = ImmutableArray.CreateBuilder<VariableDeclarator>();

            do
            {
                var nameToken = ConsumeParameterIdentifier("Expected variable name.");
                var name = Symbol.Intern(nameToken.Lexeme);
                ExpressionNode? initializer = null;

                if (Match(TokenType.Equal))
                {
                    initializer = ParseExpression();
                }
                else if (!allowInitializerless && kind == VariableKind.Const)
                {
                    throw new ParseException("Const declarations require an initializer.", Peek(), _source);
                }

                var target = new IdentifierBinding(CreateSourceReference(nameToken), name);
                declarators.Add(new VariableDeclarator(CreateSourceReference(nameToken), target, initializer));
            } while (Match(TokenType.Comma));

            if (requireSemicolon)
            {
                Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
            }

            return new VariableDeclaration(CreateSourceReference(start), kind, declarators.ToImmutable());
        }

        private BlockStatement ParseBlock(bool leftBraceConsumed = false)
        {
            Token startToken;
            if (leftBraceConsumed)
            {
                startToken = Previous();
            }
            else
            {
                startToken = Consume(TokenType.LeftBrace, "Expected '{'.");
            }

            var statements = ImmutableArray.CreateBuilder<StatementNode>();
            var isStrict = CheckForUseStrictDirective();

            while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
            {
                statements.Add(ParseStatement());
            }

            Consume(TokenType.RightBrace, "Expected '}' after block.");
            return new BlockStatement(CreateSourceReference(startToken), statements.ToImmutable(), isStrict);
        }

        private StatementNode ParseForStatement(Token forToken)
        {
            var isAwait = Match(TokenType.Await);
            Consume(TokenType.LeftParen, "Expected '(' after 'for'.");

            StatementNode? initializer = null;
            VariableDeclaration? initializerDeclaration = null;
            var firstClauseTerminated = false;

            if (Match(TokenType.Semicolon))
            {
                firstClauseTerminated = true;
            }
            else
            {
                if (Match(TokenType.Let))
                {
                    initializerDeclaration = (VariableDeclaration)ParseVariableDeclaration(VariableKind.Let,
                        requireSemicolon: false, allowInitializerless: true);
                    initializer = initializerDeclaration;
                }
                else if (Match(TokenType.Const))
                {
                    initializerDeclaration = (VariableDeclaration)ParseVariableDeclaration(VariableKind.Const,
                        requireSemicolon: false, allowInitializerless: true);
                    initializer = initializerDeclaration;
                }
                else if (Match(TokenType.Var))
                {
                    initializerDeclaration = (VariableDeclaration)ParseVariableDeclaration(VariableKind.Var,
                        requireSemicolon: false, allowInitializerless: true);
                    initializer = initializerDeclaration;
                }
                else
                {
                    var initExpr = ParseExpression();
                    initializer = new ExpressionStatement(initExpr.Source, initExpr);
                }
            }

            var isForEach = false;
            ForEachKind eachKind = ForEachKind.Of;

            if (!firstClauseTerminated)
            {
                if (isAwait)
                {
                    Consume(TokenType.Of, "Expected 'of' after 'await'.");
                    isForEach = true;
                    eachKind = ForEachKind.AwaitOf;
                }
                else if (Match(TokenType.Of))
                {
                    isForEach = true;
                    eachKind = ForEachKind.Of;
                }
                else if (Match(TokenType.In))
                {
                    isForEach = true;
                    eachKind = ForEachKind.In;
                }
            }

            if (isForEach)
            {
                if (initializer is null)
                {
                    throw new ParseException("Missing loop target in for-each statement.", Peek(), _source);
                }

                var target = ExtractBindingTarget(initializer)
                             ?? throw new NotSupportedException("Unsupported for-each binding target.");

                var iterable = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after for-each header.");
                var body = ParseStatement();
                var declarationKind = initializerDeclaration?.Kind;
                return new ForEachStatement(CreateSourceReference(forToken), target, iterable, body, eachKind,
                    declarationKind);
            }

            if (!firstClauseTerminated)
            {
                Consume(TokenType.Semicolon, "Expected ';' after for-loop initializer.");
            }

            ExpressionNode? condition = null;
            if (!Check(TokenType.Semicolon))
            {
                condition = ParseExpression();
            }

            Consume(TokenType.Semicolon, "Expected ';' after for-loop condition.");

            ExpressionNode? increment = null;
            if (!Check(TokenType.RightParen))
            {
                increment = ParseExpression();
            }

            Consume(TokenType.RightParen, "Expected ')' after for-loop clauses.");
            var bodyStatement = ParseStatement();
            return new ForStatement(CreateSourceReference(forToken), initializer, condition, increment, bodyStatement);
        }

        #region Expressions

        private ExpressionNode ParseExpression()
        {
            return ParseAssignment();
        }

        private ExpressionNode ParseAssignment()
        {
            var expr = ParseConditional();

            if (Match(TokenType.Equal))
            {
                var value = ParseAssignment();
                if (expr is IdentifierExpression identifier)
                {
                    return new AssignmentExpression(expr.Source ?? value.Source, identifier.Name, value);
                }

                if (expr is MemberExpression member)
                {
                    return CreateMemberAssignment(member, value);
                }

                throw new NotSupportedException("Unsupported assignment target.");
            }

            if (Match(TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual, TokenType.StarStarEqual,
                    TokenType.SlashEqual, TokenType.PercentEqual, TokenType.AmpEqual, TokenType.PipeEqual,
                    TokenType.CaretEqual, TokenType.LessLessEqual, TokenType.GreaterGreaterEqual,
                    TokenType.GreaterGreaterGreaterEqual, TokenType.AmpAmpEqual, TokenType.PipePipeEqual,
                    TokenType.QuestionQuestionEqual))
            {
                var opToken = Previous();
                var value = ParseAssignment();

                ExpressionNode combined = opToken.Type switch
                {
                    TokenType.AmpAmpEqual => CreateBinaryExpression("&&", expr, value),
                    TokenType.PipePipeEqual => CreateBinaryExpression("||", expr, value),
                    TokenType.QuestionQuestionEqual => CreateBinaryExpression("??", expr, value),
                    _ => CreateBinaryExpression(opToken.Type switch
                    {
                        TokenType.PlusEqual => "+",
                        TokenType.MinusEqual => "-",
                        TokenType.StarEqual => "*",
                        TokenType.StarStarEqual => "**",
                        TokenType.SlashEqual => "/",
                        TokenType.PercentEqual => "%",
                        TokenType.AmpEqual => "&",
                        TokenType.PipeEqual => "|",
                        TokenType.CaretEqual => "^",
                        TokenType.LessLessEqual => "<<",
                        TokenType.GreaterGreaterEqual => ">>",
                        TokenType.GreaterGreaterGreaterEqual => ">>>",
                        _ => throw new NotSupportedException($"Unsupported assignment operator '{opToken.Type}'.")
                    }, expr, value)
                };

                return expr switch
                {
                    IdentifierExpression identifier => new AssignmentExpression(expr.Source ?? combined.Source,
                        identifier.Name, combined),
                    MemberExpression member => CreateMemberAssignment(member, combined),
                    _ => throw new NotSupportedException("Unsupported assignment target.")
                };
            }

            return expr;
        }

        private ExpressionNode ParseConditional()
        {
            var expr = ParseLogicalOr();

            if (Match(TokenType.Question))
            {
                var consequent = ParseExpression();
                Consume(TokenType.Colon, "Expected ':' after conditional expression.");
                var alternate = ParseAssignment();
                return new ConditionalExpression(expr.Source ?? consequent.Source ?? alternate.Source, expr, consequent,
                    alternate);
            }

            return expr;
        }

        private ExpressionNode ParseLogicalOr()
        {
            var expr = ParseLogicalAnd();

            while (Match(TokenType.PipePipe))
            {
                var right = ParseLogicalAnd();
                expr = CreateBinaryExpression("||", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseLogicalAnd()
        {
            var expr = ParseNullish();

            while (Match(TokenType.AmpAmp))
            {
                var right = ParseNullish();
                expr = CreateBinaryExpression("&&", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseNullish()
        {
            var expr = ParseBitwiseOr();

            while (Match(TokenType.QuestionQuestion))
            {
                var right = ParseBitwiseOr();
                expr = CreateBinaryExpression("??", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseBitwiseOr()
        {
            var expr = ParseBitwiseXor();

            while (Match(TokenType.Pipe))
            {
                var right = ParseBitwiseXor();
                expr = CreateBinaryExpression("|", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseBitwiseXor()
        {
            var expr = ParseBitwiseAnd();

            while (Match(TokenType.Caret))
            {
                var right = ParseBitwiseAnd();
                expr = CreateBinaryExpression("^", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseBitwiseAnd()
        {
            var expr = ParseEquality();

            while (Match(TokenType.Amp))
            {
                var right = ParseEquality();
                expr = CreateBinaryExpression("&", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseEquality()
        {
            var expr = ParseRelational();

            while (Match(TokenType.EqualEqual, TokenType.EqualEqualEqual, TokenType.BangEqual, TokenType.BangEqualEqual))
            {
                var token = Previous();
                var op = token.Type switch
                {
                    TokenType.EqualEqual => "==",
                    TokenType.EqualEqualEqual => "===",
                    TokenType.BangEqual => "!=",
                    TokenType.BangEqualEqual => "!==",
                    _ => throw new InvalidOperationException()
                };
                var right = ParseRelational();
                expr = CreateBinaryExpression(op, expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseRelational()
        {
            var expr = ParseShift();

            while (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual,
                       TokenType.Instanceof, TokenType.In))
            {
                var token = Previous();
                var op = token.Type switch
                {
                    TokenType.Greater => ">",
                    TokenType.GreaterEqual => ">=",
                    TokenType.Less => "<",
                    TokenType.LessEqual => "<=",
                    TokenType.Instanceof => "instanceof",
                    TokenType.In => "in",
                    _ => throw new InvalidOperationException()
                };
                var right = ParseShift();
                expr = CreateBinaryExpression(op, expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseShift()
        {
            var expr = ParseAddition();

            while (Match(TokenType.LessLess, TokenType.GreaterGreater, TokenType.GreaterGreaterGreater))
            {
                var token = Previous();
                var op = token.Type switch
                {
                    TokenType.LessLess => "<<",
                    TokenType.GreaterGreater => ">>",
                    TokenType.GreaterGreaterGreater => ">>>",
                    _ => throw new InvalidOperationException()
                };
                var right = ParseAddition();
                expr = CreateBinaryExpression(op, expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseAddition()
        {
            var expr = ParseMultiplication();

            while (Match(TokenType.Plus, TokenType.Minus))
            {
                var op = Previous();
                var right = ParseMultiplication();
                expr = CreateBinaryExpression(op.Type == TokenType.Plus ? "+" : "-", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseMultiplication()
        {
            var expr = ParseUnary();

            while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
            {
                var op = Previous();
                var right = ParseUnary();
                var symbol = op.Type switch
                {
                    TokenType.Star => "*",
                    TokenType.Slash => "/",
                    TokenType.Percent => "%",
                    _ => throw new InvalidOperationException("Unexpected binary operator.")
                };

                expr = CreateBinaryExpression(symbol, expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseUnary()
        {
            if (Match(TokenType.Bang))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "!", ParseUnary(), true);
            }

            if (Match(TokenType.Minus))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "-", ParseUnary(), true);
            }

            if (Match(TokenType.Plus))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "+", ParseUnary(), true);
            }

            if (Match(TokenType.Tilde))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "~", ParseUnary(), true);
            }

            if (Match(TokenType.Typeof))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "typeof", ParseUnary(), true);
            }

            if (Match(TokenType.Void))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "void", ParseUnary(), true);
            }

            if (Match(TokenType.Delete))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "delete", ParseUnary(), true);
            }

            return ParsePostfix();
        }

        private ExpressionNode ParsePostfix()
        {
            var expr = ParsePrimary();

            if (Match(TokenType.PlusPlus))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "++", expr, false);
            }

            if (Match(TokenType.MinusMinus))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "--", expr, false);
            }

            return expr;
        }

        private ExpressionNode ParsePrimary()
        {
            ExpressionNode expr;

            if (Match(TokenType.Number))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), Previous().Literal);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.String))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), Previous().Literal);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.True))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), true);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.False))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), false);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.Null))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), null);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.Undefined))
            {
                var token = Previous();
                var symbol = JsSymbols.Undefined;
                expr = new IdentifierExpression(CreateSourceReference(token), symbol);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.Identifier))
            {
                var symbol = Symbol.Intern(Previous().Lexeme);
                expr = new IdentifierExpression(CreateSourceReference(Previous()), symbol);
                return ParseCallSuffix(expr);
            }

            if (Match(TokenType.LeftParen))
            {
                var grouped = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after expression.");
                return ParseCallSuffix(grouped);
            }

            if (Match(TokenType.LeftBracket))
            {
                return ParseArrayLiteral();
            }

            if (Match(TokenType.LeftBrace))
            {
                return ParseObjectLiteral();
            }

            if (Match(TokenType.Function))
            {
                return ParseFunctionExpression();
            }

            if (Match(TokenType.Async))
            {
                var asyncToken = Previous();
                if (Check(TokenType.Function))
                {
                    Advance(); // function
                    return ParseFunctionExpression(isAsync: true);
                }

                var asyncSymbol = Symbol.Intern(asyncToken.Lexeme);
                var asyncIdent = new IdentifierExpression(CreateSourceReference(asyncToken), asyncSymbol);
                return ParseCallSuffix(asyncIdent);
            }

            if (Match(TokenType.Await))
            {
                var awaitToken = Previous();
                var awaitSymbol = Symbol.Intern(awaitToken.Lexeme);
                return ParseCallSuffix(new IdentifierExpression(CreateSourceReference(awaitToken), awaitSymbol));
            }

            if (Match(TokenType.This))
            {
                return new ThisExpression(CreateSourceReference(Previous()));
            }

            if (Match(TokenType.Super))
            {
                return new SuperExpression(CreateSourceReference(Previous()));
            }

            if (Match(TokenType.New))
            {
                return ParseNewExpression();
            }

            if (Check(TokenType.Undefined))
            {
                var token = Advance();
                var symbol = JsSymbols.Undefined;
                var undefinedExpr = new IdentifierExpression(CreateSourceReference(token), symbol);
                return ParseCallSuffix(undefinedExpr);
            }

            if (Check(TokenType.Tilde) || Check(TokenType.Typeof) || Check(TokenType.Void) ||
                Check(TokenType.Delete))
            {
                var token = Advance();
                var operand = ParseUnary();
                var op = token.Type switch
                {
                    TokenType.Tilde => "~",
                    TokenType.Typeof => "typeof",
                    TokenType.Void => "void",
                    TokenType.Delete => "delete",
                    _ => throw new InvalidOperationException()
                };
                var unary = new UnaryExpression(CreateSourceReference(token), op, operand, true);
                return ParseCallSuffix(unary);
            }

            throw new NotSupportedException($"Token '{Peek().Type}' is not yet supported by the direct parser.");
        }

        private ExpressionNode ParseCallSuffix(ExpressionNode expression)
        {
            while (true)
            {
                if (Match(TokenType.LeftParen))
                {
                    var arguments = ParseArgumentList();
                    expression = new CallExpression(CreateSourceReference(Previous()), expression, arguments, false);
                    continue;
                }

                if (Match(TokenType.Dot))
                {
                    expression = FinishDotAccess(expression);
                    continue;
                }

                if (Match(TokenType.LeftBracket))
                {
                    expression = FinishIndexAccess(expression);
                    continue;
                }

                break;
            }

            return expression;
        }

        private ImmutableArray<CallArgument> ParseArgumentList()
        {
            var arguments = ImmutableArray.CreateBuilder<CallArgument>();

            if (Check(TokenType.RightParen))
            {
                Consume(TokenType.RightParen, "Expected ')' after arguments.");
                return arguments.ToImmutable();
            }

            do
            {
                var isSpread = Match(TokenType.DotDotDot);
                var expr = ParseExpression();
                arguments.Add(new CallArgument(expr.Source, expr, isSpread));
            } while (Match(TokenType.Comma));

            Consume(TokenType.RightParen, "Expected ')' after arguments.");
            return arguments.ToImmutable();
        }

        private ExpressionNode FinishDotAccess(ExpressionNode target)
        {
            if (!Check(TokenType.Identifier) && !IsKeyword(Peek()))
            {
                throw new ParseException("Expected property name after '.'.", Peek(), _source);
            }

            var nameToken = Advance();
            var property = new LiteralExpression(CreateSourceReference(nameToken), nameToken.Lexeme);
            return new MemberExpression(CreateSourceReference(nameToken), target, property, false, false);
        }

        private ExpressionNode FinishIndexAccess(ExpressionNode target)
        {
            var expression = ParseExpression();
            Consume(TokenType.RightBracket, "Expected ']' after index expression.");
            var source = target.Source ?? expression.Source;
            return new MemberExpression(source, target, expression, true, false);
        }

        private ExpressionNode ParseArrayLiteral()
        {
            var elements = ImmutableArray.CreateBuilder<ArrayElement>();
            var startToken = Previous();
            var expectElement = true;

            while (!Check(TokenType.RightBracket))
            {
                if (Match(TokenType.Comma))
                {
                    elements.Add(new ArrayElement(null, null, false));
                    expectElement = true;
                    continue;
                }

                var isSpread = Match(TokenType.DotDotDot);
                var expr = ParseExpression();
                elements.Add(new ArrayElement(expr.Source, expr, isSpread));
                expectElement = false;

                if (!Match(TokenType.Comma))
                {
                    break;
                }

                expectElement = true;
            }

            if (expectElement && elements.Count > 0 && !Check(TokenType.RightBracket))
            {
                throw new ParseException("Expected array element.", Peek(), _source);
            }

            Consume(TokenType.RightBracket, "Expected ']' after array literal.");
            return new ArrayExpression(CreateSourceReference(startToken), elements.ToImmutable());
        }

        private ExpressionNode ParseObjectLiteral()
        {
            var members = ImmutableArray.CreateBuilder<ObjectMember>();
            var startToken = Previous();
            var first = true;

            while (!Check(TokenType.RightBrace))
            {
                if (!first)
                {
                    Consume(TokenType.Comma, "Expected ',' between object properties.");
                    if (Check(TokenType.RightBrace))
                    {
                        break;
                    }
                }

                first = false;

                if (Match(TokenType.DotDotDot))
                {
                    var spreadExpr = ParseExpression();
                    members.Add(new ObjectMember(spreadExpr.Source, ObjectMemberKind.Spread, string.Empty, spreadExpr,
                        null, false, false, null));
                    continue;
                }

                var (key, isComputed, keySource) = ParseObjectPropertyKey();

                ExpressionNode? value = null;
                FunctionExpression? method = null;
                var kind = ObjectMemberKind.Property;

                if (Match(TokenType.LeftParen))
                {
                    var parameters = ParseParameterList();
                    Consume(TokenType.RightParen, "Expected ')' after method parameters.");
                    var body = ParseBlock();
                    method = new FunctionExpression(body.Source, null, parameters, body, false, false);
                    kind = ObjectMemberKind.Method;
                }
                else if (Match(TokenType.Colon))
                {
                    value = ParseExpression();
                }
                else
                {
                    if (key is not string shorthandName)
                    {
                        throw new ParseException("Shorthand properties must use identifiers.", Peek(), _source);
                    }

                    var symbol = Symbol.Intern(shorthandName);
                    value = new IdentifierExpression(keySource, symbol);
                }

                members.Add(new ObjectMember(method?.Source ?? value?.Source ?? keySource, kind, key, value, method,
                    isComputed, false, null));
            }

            Consume(TokenType.RightBrace, "Expected '}' after object literal.");
            return new ObjectExpression(CreateSourceReference(startToken), members.ToImmutable());
        }

        private (object key, bool isComputed, SourceReference? source) ParseObjectPropertyKey()
        {
            if (Match(TokenType.LeftBracket))
            {
                var expr = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']' after computed property key.");
                return (expr, true, expr.Source);
            }

            if (Match(TokenType.String))
            {
                var token = Previous();
                var value = token.Literal?.ToString() ?? string.Empty;
                return (value, false, CreateSourceReference(token));
            }

            if (Match(TokenType.Number))
            {
                var token = Previous();
                var value = Convert.ToString(token.Literal, CultureInfo.InvariantCulture) ?? string.Empty;
                return (value, false, CreateSourceReference(token));
            }

            var identifier = ConsumeParameterIdentifier("Expected property name.");
            return (identifier.Lexeme, false, CreateSourceReference(identifier));
        }

        private ExpressionNode ParseFunctionExpression(Symbol? explicitName = null, bool isAsync = false)
        {
            var functionKeyword = Previous();
            Symbol? name = explicitName;
            if (name is null && Check(TokenType.Identifier))
            {
                var nameToken = Advance();
                name = Symbol.Intern(nameToken.Lexeme);
            }

            return ParseFunctionTail(name, functionKeyword, isAsync);
        }

        private FunctionExpression ParseFunctionTail(Symbol? name, Token startToken, bool isAsync)
        {
            Consume(TokenType.LeftParen, "Expected '(' after function name.");
            var parameters = ParseParameterList();
            Consume(TokenType.RightParen, "Expected ')' after parameters.");
            var body = ParseBlock();
            var source = body.Source ?? CreateSourceReference(startToken);
            return new FunctionExpression(source, name, parameters, body, isAsync, false);
        }

        private ImmutableArray<FunctionParameter> ParseParameterList()
        {
            var builder = ImmutableArray.CreateBuilder<FunctionParameter>();
            if (Check(TokenType.RightParen))
            {
                return builder.ToImmutable();
            }

            do
            {
                var isRest = Match(TokenType.DotDotDot);
                var token = ConsumeParameterIdentifier("Expected parameter name.");
                var name = Symbol.Intern(token.Lexeme);
                builder.Add(new FunctionParameter(CreateSourceReference(token), name, isRest, null, null));
                if (isRest)
                {
                    break;
                }
            } while (Match(TokenType.Comma));

            return builder.ToImmutable();
        }

        private ExpressionNode ParseNewExpression()
        {
            if (!DisableFallback)
            {
                throw new NotSupportedException("New expressions are not yet fully supported by the typed parser.");
            }

            var constructor = ParsePostfix();
            ImmutableArray<ExpressionNode> args;
            if (Match(TokenType.LeftParen))
            {
                var callArgs = ParseArgumentList();
                var builder = ImmutableArray.CreateBuilder<ExpressionNode>(callArgs.Length);
                foreach (var arg in callArgs)
                {
                    builder.Add(arg.Expression);
                }

                args = builder.ToImmutable();
            }
            else
            {
                args = ImmutableArray<ExpressionNode>.Empty;
            }

            return new NewExpression(constructor.Source, constructor, args);
        }

        private BinaryExpression CreateBinaryExpression(string op, ExpressionNode left, ExpressionNode right)
        {
            var source = left.Source ?? right.Source;
            return new BinaryExpression(source, op, left, right);
        }

        private ExpressionNode CreateMemberAssignment(MemberExpression member, ExpressionNode value)
        {
            if (member.IsOptional)
            {
                throw new NotSupportedException("Cannot assign to optional chaining expressions.");
            }

            if (member.IsComputed)
            {
                return new IndexAssignmentExpression(member.Source ?? value.Source, member.Target, member.Property,
                    value);
            }

            return new PropertyAssignmentExpression(member.Source ?? value.Source, member.Target, member.Property,
                value, false);
        }

        private BindingTarget? ExtractBindingTarget(StatementNode initializer)
        {
            switch (initializer)
            {
                case VariableDeclaration { Declarators.Length: 1 } declaration:
                    return declaration.Declarators[0].Target;
                case ExpressionStatement { Expression: IdentifierExpression identifier }:
                    return new IdentifierBinding(identifier.Source, identifier.Name);
                default:
                    return null;
            }
        }

        private static bool IsKeyword(Token token)
        {
            return token.Type switch
            {
                TokenType.Let or TokenType.Var or TokenType.Const or TokenType.Function or
                    TokenType.Class or TokenType.Extends or TokenType.If or TokenType.Else or
                    TokenType.For or TokenType.In or TokenType.Of or TokenType.While or TokenType.Do or
                    TokenType.Switch or TokenType.Case or TokenType.Default or TokenType.Break or
                    TokenType.Continue or TokenType.Return or TokenType.Try or TokenType.Catch or
                    TokenType.Finally or TokenType.Throw or TokenType.This or TokenType.Super or
                    TokenType.New or TokenType.True or TokenType.False or TokenType.Null or
                    TokenType.Undefined or TokenType.Typeof or TokenType.Void or TokenType.Delete or
                    TokenType.Get or TokenType.Set or TokenType.Yield or TokenType.Async or
                    TokenType.Await => true,
                _ => false
            };
        }

        #endregion

        #region Helpers

        private bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }

            return false;
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd())
            {
                return type == TokenType.Eof;
            }

            return Peek().Type == type;
        }

        private bool CheckAhead(TokenType type)
        {
            if (_current + 1 >= _tokens.Count)
            {
                return false;
            }

            return _tokens[_current + 1].Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd())
            {
                _current++;
            }

            return Previous();
        }

        private bool IsAtEnd()
        {
            return _current >= _tokens.Count || Peek().Type == TokenType.Eof;
        }

        private Token Peek()
        {
            return _tokens[_current];
        }

        private Token PeekNext()
        {
            if (_current + 1 >= _tokens.Count)
            {
                return _tokens[^1];
            }

            return _tokens[_current + 1];
        }

        private Token Previous()
        {
            return _tokens[Math.Max(0, _current - 1)];
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type))
            {
                return Advance();
            }

            throw new ParseException(message, Peek(), _source);
        }

        private Token ConsumeParameterIdentifier(string message)
        {
            if (Check(TokenType.Identifier) || Check(TokenType.Async) || Check(TokenType.Await) ||
                Check(TokenType.Get) || Check(TokenType.Set) || Check(TokenType.Yield))
            {
                return Advance();
            }

            throw new ParseException(message, Peek(), _source);
        }

        private bool CheckForUseStrictDirective()
        {
            var saved = _current;

            if (!Check(TokenType.String))
            {
                return false;
            }

            var token = Advance();
            if (!string.Equals(token.Literal as string, "use strict", StringComparison.Ordinal))
            {
                _current = saved;
                return false;
            }

            Match(TokenType.Semicolon);
            return true;
        }

        private bool HasLineTerminatorBefore()
        {
            if (_current <= 0 || _current >= _tokens.Count)
            {
                return false;
            }

            var previousToken = _tokens[_current - 1];
            var currentToken = _tokens[_current];
            return currentToken.Line > previousToken.Line;
        }

        private SourceReference? CreateSourceReference(Token startToken)
        {
            if (_current <= 0 || _current > _tokens.Count)
            {
                return null;
            }

            var endToken = _tokens[Math.Max(0, _current - 1)];
            return new SourceReference(
                _source,
                startToken.StartPosition,
                endToken.EndPosition,
                startToken.Line,
                startToken.Column,
                endToken.Line,
                endToken.Column
            );
        }

        private SourceReference? CreateSourceReferenceFromRange(int startIndex, int endIndex)
        {
            if (_tokens.Count == 0)
            {
                return null;
            }

            startIndex = Math.Max(0, startIndex);
            endIndex = Math.Max(startIndex, Math.Min(endIndex, _tokens.Count - 1));

            var startToken = _tokens[startIndex];
            var endToken = _tokens[endIndex];

            return new SourceReference(
                _source,
                startToken.StartPosition,
                endToken.EndPosition,
                startToken.Line,
                startToken.Column,
                endToken.Line,
                endToken.Column
            );
        }

        #endregion
    }
}
