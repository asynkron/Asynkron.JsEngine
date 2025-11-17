using System;
using System.Collections.Immutable;
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

            if (Match(TokenType.Return))
            {
                return ParseReturnStatement();
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

        private StatementNode ParseExpressionStatement()
        {
            var start = Peek();
            var expression = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after expression.");
            return new ExpressionStatement(CreateSourceReference(start), expression);
        }

        private StatementNode ParseVariableDeclaration(VariableKind kind)
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
                else if (kind == VariableKind.Const)
                {
                    throw new ParseException("Const declarations require an initializer.", Peek(), _source);
                }

                var target = new IdentifierBinding(CreateSourceReference(nameToken), name);
                declarators.Add(new VariableDeclarator(CreateSourceReference(nameToken), target, initializer));
            } while (Match(TokenType.Comma));

            Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
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

        #region Expressions

        private ExpressionNode ParseExpression()
        {
            return ParseAddition();
        }

        private ExpressionNode ParseAddition()
        {
            var expr = ParseMultiplication();

            while (Match(TokenType.Plus, TokenType.Minus))
            {
                var op = Previous();
                var right = ParseMultiplication();
                expr = new BinaryExpression(CreateSourceReference(op),
                    op.Type == TokenType.Plus ? "+" : "-", expr, right);
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

                expr = new BinaryExpression(CreateSourceReference(op), symbol, expr, right);
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
