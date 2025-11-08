namespace Asynkron.JsEngine;

internal sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _current;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public Cons ParseProgram()
    {
        var statements = new List<object?> { JsSymbols.Program };
        while (!Check(TokenType.Eof))
        {
            statements.Add(ParseDeclaration());
        }

        return Cons.FromEnumerable(statements);
    }

    private object ParseDeclaration()
    {
        if (Match(TokenType.Function))
        {
            return ParseFunctionDeclaration();
        }

        if (Match(TokenType.Class))
        {
            return ParseClassDeclaration();
        }

        if (Match(TokenType.Let))
        {
            return ParseVariableDeclaration(TokenType.Let);
        }

        if (Match(TokenType.Var))
        {
            return ParseVariableDeclaration(TokenType.Var);
        }

        if (Match(TokenType.Const))
        {
            return ParseVariableDeclaration(TokenType.Const);
        }

        return ParseStatement();
    }

    private object ParseFunctionDeclaration()
    {
        var nameToken = Consume(TokenType.Identifier, "Expected function name.");
        var name = Symbol.Intern(nameToken.Lexeme);
        Consume(TokenType.LeftParen, "Expected '(' after function name.");
        var parameters = ParseParameterList();
        Consume(TokenType.RightParen, "Expected ')' after function parameters.");
        var body = ParseBlock();

        return Cons.FromEnumerable(new object?[] { JsSymbols.Function, name, parameters, body });
    }

    private object ParseClassDeclaration()
    {
        var nameToken = Consume(TokenType.Identifier, "Expected class name.");
        var name = Symbol.Intern(nameToken.Lexeme);

        Cons? extendsClause = null;
        if (Match(TokenType.Extends))
        {
            var baseExpression = ParseExpression();
            extendsClause = Cons.FromEnumerable(new object?[] { JsSymbols.Extends, baseExpression });
        }

        Consume(TokenType.LeftBrace, "Expected '{' after class name or extends clause.");

        Cons? constructor = null;
        var methods = new List<object?>();

        while (!Check(TokenType.RightBrace))
        {
            var methodNameToken = Consume(TokenType.Identifier, "Expected method name in class body.");
            var methodName = methodNameToken.Lexeme;
            Consume(TokenType.LeftParen, "Expected '(' after method name.");
            var parameters = ParseParameterList();
            Consume(TokenType.RightParen, "Expected ')' after method parameters.");
            var body = ParseBlock();

            var lambdaName = string.Equals(methodName, "constructor", StringComparison.Ordinal)
                ? name
                : null;
            var lambda = Cons.FromEnumerable(new object?[] { JsSymbols.Lambda, lambdaName, parameters, body });

            if (string.Equals(methodName, "constructor", StringComparison.Ordinal))
            {
                if (constructor is not null)
                {
                    throw new ParseException("Class cannot declare multiple constructors.");
                }

                constructor = lambda;
            }
            else
            {
                methods.Add(Cons.FromEnumerable(new object?[] { JsSymbols.Method, methodName, lambda }));
            }
        }

        Consume(TokenType.RightBrace, "Expected '}' after class body.");
        Match(TokenType.Semicolon); // allow optional semicolon terminator

        constructor ??= CreateDefaultConstructor(name);
        var methodList = Cons.FromEnumerable(methods);

        return Cons.FromEnumerable(new object?[] { JsSymbols.Class, name, extendsClause, constructor, methodList });
    }

    private Cons ParseParameterList()
    {
        var parameters = new List<object?>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                var identifier = Consume(TokenType.Identifier, "Expected parameter name.");
                parameters.Add(Symbol.Intern(identifier.Lexeme));
            } while (Match(TokenType.Comma));
        }

        return Cons.FromEnumerable(parameters);
    }

    private object ParseVariableDeclaration(TokenType kind)
    {
        var keyword = kind switch
        {
            TokenType.Let => "let",
            TokenType.Var => "var",
            TokenType.Const => "const",
            _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
        };

        var nameToken = Consume(TokenType.Identifier, $"Expected variable name after '{keyword}'.");
        var name = Symbol.Intern(nameToken.Lexeme);
        object? initializer;

        if (Match(TokenType.Equal))
        {
            initializer = ParseExpression();
        }
        else
        {
            if (kind == TokenType.Const)
            {
                throw new ParseException("Const declarations require an initializer.");
            }

            if (kind == TokenType.Let)
            {
                throw new ParseException("Let declarations require an initializer in this interpreter.");
            }

            initializer = JsSymbols.Uninitialized;
        }

        Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
        var tag = kind switch
        {
            TokenType.Let => JsSymbols.Let,
            TokenType.Var => JsSymbols.Var,
            TokenType.Const => JsSymbols.Const,
            _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
        };

        return Cons.FromEnumerable(new object?[] { tag, name, initializer });
    }

    private object ParseStatement()
    {
        if (Match(TokenType.Try))
        {
            return ParseTryStatement();
        }

        if (Match(TokenType.Switch))
        {
            return ParseSwitchStatement();
        }

        if (Match(TokenType.If))
        {
            return ParseIfStatement();
        }

        if (Match(TokenType.For))
        {
            return ParseForStatement();
        }

        if (Match(TokenType.While))
        {
            return ParseWhileStatement();
        }

        if (Match(TokenType.Do))
        {
            return ParseDoWhileStatement();
        }

        if (Match(TokenType.Break))
        {
            Consume(TokenType.Semicolon, "Expected ';' after break statement.");
            return Cons.FromEnumerable(new object?[] { JsSymbols.Break });
        }

        if (Match(TokenType.Continue))
        {
            Consume(TokenType.Semicolon, "Expected ';' after continue statement.");
            return Cons.FromEnumerable(new object?[] { JsSymbols.Continue });
        }

        if (Match(TokenType.Return))
        {
            return ParseReturnStatement();
        }

        if (Match(TokenType.Throw))
        {
            return ParseThrowStatement();
        }

        if (Match(TokenType.LeftBrace))
        {
            return ParseBlock(leftBraceConsumed: true);
        }

        return ParseExpressionStatement();
    }

    private object ParseSwitchStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'switch'.");
        var discriminant = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after switch expression.");
        Consume(TokenType.LeftBrace, "Expected '{' to begin switch body.");

        var clauses = new List<object?>();
        var seenDefault = false;

        while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
        {
            if (Match(TokenType.Case))
            {
                var test = ParseExpression();
                Consume(TokenType.Colon, "Expected ':' after case expression.");
                clauses.Add(Cons.FromEnumerable(new object?[]
                {
                    JsSymbols.Case,
                    test,
                    ParseSwitchClauseStatements()
                }));
                continue;
            }

            if (Match(TokenType.Default))
            {
                if (seenDefault)
                {
                    throw new ParseException("Switch statement can only contain one default clause.");
                }

                seenDefault = true;
                Consume(TokenType.Colon, "Expected ':' after default keyword.");
                clauses.Add(Cons.FromEnumerable(new object?[]
                {
                    JsSymbols.Default,
                    ParseSwitchClauseStatements()
                }));
                continue;
            }

            throw new ParseException("Unexpected token in switch body.");
        }

        Consume(TokenType.RightBrace, "Expected '}' after switch body.");
        return Cons.FromEnumerable(new object?[]
        {
            JsSymbols.Switch,
            discriminant,
            Cons.FromEnumerable(clauses)
        });
    }

    private Cons ParseSwitchClauseStatements()
    {
        var statements = new List<object?> { JsSymbols.Block };
        while (!Check(TokenType.Case) && !Check(TokenType.Default) && !Check(TokenType.RightBrace) && !Check(TokenType.Eof))
        {
            statements.Add(ParseDeclaration());
        }

        return Cons.FromEnumerable(statements);
    }

    private object ParseTryStatement()
    {
        var tryBlock = ParseBlock();

        Cons? catchClause = null;
        if (Match(TokenType.Catch))
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'catch'.");
            var identifier = Consume(TokenType.Identifier, "Expected identifier in catch clause.");
            var catchSymbol = Symbol.Intern(identifier.Lexeme);
            Consume(TokenType.RightParen, "Expected ')' after catch parameter.");
            var catchBlock = ParseBlock();
            catchClause = Cons.FromEnumerable(new object?[]
            {
                JsSymbols.Catch,
                catchSymbol,
                catchBlock
            });
        }

        Cons? finallyBlock = null;
        if (Match(TokenType.Finally))
        {
            finallyBlock = ParseBlock();
        }

        if (catchClause is null && finallyBlock is null)
        {
            throw new ParseException("Try statement requires at least a catch or finally clause.");
        }

        return Cons.FromEnumerable(new object?[]
        {
            JsSymbols.Try,
            tryBlock,
            catchClause,
            finallyBlock
        });
    }

    private object ParseIfStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'if'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after if condition.");
        var thenBranch = ParseStatement();
        object? elseBranch = null;
        if (Match(TokenType.Else))
        {
            elseBranch = ParseStatement();
        }

        return Cons.FromEnumerable(new object?[] { JsSymbols.If, condition, thenBranch, elseBranch });
    }

    private object ParseWhileStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after while condition.");
        var body = ParseStatement();
        return Cons.FromEnumerable(new object?[] { JsSymbols.While, condition, body });
    }

    private object ParseDoWhileStatement()
    {
        var body = ParseStatement();
        Consume(TokenType.While, "Expected 'while' after do-while body.");
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after do-while condition.");
        Consume(TokenType.Semicolon, "Expected ';' after do-while statement.");
        return Cons.FromEnumerable(new object?[] { JsSymbols.DoWhile, condition, body });
    }

    private object ParseForStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'for'.");

        object? initializer = null;
        if (Match(TokenType.Semicolon))
        {
            initializer = null;
        }
        else if (Match(TokenType.Let))
        {
            initializer = ParseVariableDeclaration(TokenType.Let);
        }
        else if (Match(TokenType.Var))
        {
            initializer = ParseVariableDeclaration(TokenType.Var);
        }
        else if (Match(TokenType.Const))
        {
            initializer = ParseVariableDeclaration(TokenType.Const);
        }
        else
        {
            initializer = ParseExpressionStatement();
        }

        object? condition = null;
        if (!Check(TokenType.Semicolon))
        {
            condition = ParseExpression();
        }

        Consume(TokenType.Semicolon, "Expected ';' after for loop condition.");

        object? increment = null;
        if (!Check(TokenType.RightParen))
        {
            increment = ParseExpression();
        }

        Consume(TokenType.RightParen, "Expected ')' after for clauses.");
        var body = ParseStatement();

        return Cons.FromEnumerable(new object?[] { JsSymbols.For, initializer, condition, increment, body });
    }

    private object ParseReturnStatement()
    {
        object? value = null;
        var hasValue = false;
        if (!Check(TokenType.Semicolon))
        {
            value = ParseExpression();
            hasValue = true;
        }

        Consume(TokenType.Semicolon, "Expected ';' after return statement.");
        return hasValue
            ? Cons.FromEnumerable(new object?[] { JsSymbols.Return, value })
            : Cons.FromEnumerable(new object?[] { JsSymbols.Return });
    }

    private object ParseThrowStatement()
    {
        var value = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after throw statement.");
        return Cons.FromEnumerable(new object?[] { JsSymbols.Throw, value });
    }

    private object ParseExpressionStatement()
    {
        var expression = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression statement.");
        return Cons.FromEnumerable(new object?[] { JsSymbols.ExpressionStatement, expression });
    }

    private Cons ParseBlock(bool leftBraceConsumed = false)
    {
        if (!leftBraceConsumed)
        {
            Consume(TokenType.LeftBrace, "Expected '{' to begin block.");
        }

        var statements = new List<object?> { JsSymbols.Block };
        while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
        {
            statements.Add(ParseDeclaration());
        }

        Consume(TokenType.RightBrace, "Expected '}' after block.");
        return Cons.FromEnumerable(statements);
    }

    private object? ParseExpression() => ParseAssignment();

    private object? ParseAssignment()
    {
        var expr = ParseTernary();

        if (Match(TokenType.Equal))
        {
            var equals = Previous();
            var value = ParseAssignment();

            if (expr is Symbol symbol)
            {
                return Cons.FromEnumerable(new object?[] { JsSymbols.Assign, symbol, value });
            }

            if (expr is Cons { Head: Symbol head } assignmentTarget && ReferenceEquals(head, JsSymbols.GetProperty))
            {
                var target = assignmentTarget.Rest.Head;
                var propertyName = assignmentTarget.Rest.Rest.Head;
                return Cons.FromEnumerable(new object?[] { JsSymbols.SetProperty, target, propertyName, value });
            }

            if (expr is Cons { Head: Symbol indexHead } indexTarget && ReferenceEquals(indexHead, JsSymbols.GetIndex))
            {
                var target = indexTarget.Rest.Head;
                var index = indexTarget.Rest.Rest.Head;
                return Cons.FromEnumerable(new object?[] { JsSymbols.SetIndex, target, index, value });
            }

            throw new ParseException($"Invalid assignment target near line {equals.Line} column {equals.Column}.");
        }

        return expr;
    }

    private object? ParseTernary()
    {
        var expr = ParseLogicalOr();

        if (Match(TokenType.Question))
        {
            var thenBranch = ParseExpression();
            Consume(TokenType.Colon, "Expected ':' after then branch of ternary expression.");
            var elseBranch = ParseTernary();
            return Cons.FromEnumerable(new object?[] { JsSymbols.Ternary, expr, thenBranch, elseBranch });
        }

        return expr;
    }

    private object? ParseLogicalOr()
    {
        var expr = ParseLogicalAnd();

        while (Match(TokenType.PipePipe))
        {
            var right = ParseLogicalAnd();
            expr = Cons.FromEnumerable(new object?[]
            {
                JsSymbols.Operator("||"),
                expr,
                right
            });
        }

        return expr;
    }

    private object? ParseLogicalAnd()
    {
        var expr = ParseNullishCoalescing();

        while (Match(TokenType.AmpAmp))
        {
            var right = ParseNullishCoalescing();
            expr = Cons.FromEnumerable(new object?[]
            {
                JsSymbols.Operator("&&"),
                expr,
                right
            });
        }

        return expr;
    }

    private object? ParseNullishCoalescing()
    {
        var expr = ParseEquality();

        while (Match(TokenType.QuestionQuestion))
        {
            var right = ParseEquality();
            expr = Cons.FromEnumerable(new object?[]
            {
                JsSymbols.Operator("??"),
                expr,
                right
            });
        }

        return expr;
    }

    private object? ParseEquality()
    {
        var expr = ParseComparison();

        while (Match(TokenType.BangEqual, TokenType.EqualEqual, TokenType.EqualEqualEqual, TokenType.BangEqualEqual))
        {
            var operatorToken = Previous();
            var right = ParseComparison();
            var op = operatorToken.Type switch
            {
                TokenType.EqualEqual => "==",
                TokenType.BangEqual => "!=",
                TokenType.EqualEqualEqual => "===",
                TokenType.BangEqualEqual => "!==",
                _ => throw new InvalidOperationException("Unexpected equality operator.")
            };

            expr = Cons.FromEnumerable(new object?[]
            {
                JsSymbols.Operator(op),
                expr,
                right
            });
        }

        return expr;
    }

    private object? ParseComparison()
    {
        var expr = ParseTerm();
        while (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual))
        {
            var op = Previous();
            var right = ParseTerm();
            var symbol = op.Type switch
            {
                TokenType.Greater => JsSymbols.Operator(">"),
                TokenType.GreaterEqual => JsSymbols.Operator(">="),
                TokenType.Less => JsSymbols.Operator("<"),
                TokenType.LessEqual => JsSymbols.Operator("<="),
                _ => throw new InvalidOperationException("Unexpected comparison operator.")
            };

            expr = Cons.FromEnumerable(new object?[] { symbol, expr, right });
        }

        return expr;
    }

    private object? ParseTerm()
    {
        var expr = ParseFactor();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous();
            var right = ParseFactor();
            var symbol = JsSymbols.Operator(op.Type == TokenType.Plus ? "+" : "-");
            expr = Cons.FromEnumerable(new object?[] { symbol, expr, right });
        }

        return expr;
    }

    private object? ParseFactor()
    {
        var expr = ParseUnary();
        while (Match(TokenType.Star, TokenType.Slash))
        {
            var op = Previous();
            var right = ParseUnary();
            var symbol = JsSymbols.Operator(op.Type == TokenType.Star ? "*" : "/");
            expr = Cons.FromEnumerable(new object?[] { symbol, expr, right });
        }

        return expr;
    }

    private object? ParseUnary()
    {
        if (Match(TokenType.Bang))
        {
            return Cons.FromEnumerable(new object?[] { JsSymbols.Not, ParseUnary() });
        }

        if (Match(TokenType.Minus))
        {
            return Cons.FromEnumerable(new object?[] { JsSymbols.Negate, ParseUnary() });
        }

        return ParseCall();
    }

    private object? ParseCall()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Match(TokenType.LeftParen))
            {
                expr = FinishCall(expr);
                continue;
            }

            if (Match(TokenType.Dot))
            {
                expr = FinishGet(expr);
                continue;
            }

            if (Match(TokenType.LeftBracket))
            {
                expr = FinishIndex(expr);
                continue;
            }

            break;
        }

        return expr;
    }

    private object FinishCall(object? callee)
    {
        var arguments = ParseArgumentList();
        var items = new List<object?> { JsSymbols.Call, callee };
        items.AddRange(arguments);
        return Cons.FromEnumerable(items);
    }

    private List<object?> ParseArgumentList()
    {
        var arguments = new List<object?>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                arguments.Add(ParseExpression());
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after arguments.");
        return arguments;
    }

    private object? ParsePrimary()
    {
        if (Match(TokenType.New))
        {
            return ParseNewExpression();
        }

        if (Match(TokenType.False))
        {
            return false;
        }

        if (Match(TokenType.True))
        {
            return true;
        }

        if (Match(TokenType.Null))
        {
            return null;
        }

        if (Match(TokenType.Number))
        {
            return Previous().Literal is double number ? number : 0d;
        }

        if (Match(TokenType.String))
        {
            return Previous().Literal as string ?? string.Empty;
        }

        if (Match(TokenType.TemplateLiteral))
        {
            return ParseTemplateLiteralExpression();
        }

        if (Match(TokenType.Identifier))
        {
            return Symbol.Intern(Previous().Lexeme);
        }

        if (Match(TokenType.This))
        {
            return JsSymbols.This;
        }

        if (Match(TokenType.Super))
        {
            return JsSymbols.Super;
        }

        if (Match(TokenType.Function))
        {
            return ParseFunctionExpression();
        }

        if (Match(TokenType.LeftBrace))
        {
            return ParseObjectLiteral();
        }

        if (Match(TokenType.LeftBracket))
        {
            return ParseArrayLiteral();
        }

        if (Match(TokenType.LeftParen))
        {
            var expr = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after expression.");
            return expr;
        }

        throw new ParseException($"Unexpected token {Peek().Type} at line {Peek().Line} column {Peek().Column}.");
    }

    private object ParseNewExpression()
    {
        var constructor = ParsePrimary();

        while (Match(TokenType.Dot))
        {
            constructor = FinishGet(constructor);
        }

        while (Match(TokenType.LeftBracket))
        {
            constructor = FinishIndex(constructor);
        }

        var arguments = new List<object?>();
        if (Match(TokenType.LeftParen))
        {
            arguments = ParseArgumentList();
        }

        var items = new List<object?> { JsSymbols.New, constructor };
        items.AddRange(arguments);
        return Cons.FromEnumerable(items);
    }

    private object ParseFunctionExpression()
    {
        Symbol? name = null;
        if (Check(TokenType.Identifier))
        {
            name = Symbol.Intern(Advance().Lexeme);
        }

        Consume(TokenType.LeftParen, "Expected '(' after function keyword.");
        var parameters = ParseParameterList();
        Consume(TokenType.RightParen, "Expected ')' after lambda parameters.");
        var body = ParseBlock();
        return Cons.FromEnumerable(new object?[] { JsSymbols.Lambda, name, parameters, body });
    }

    private object ParseObjectLiteral()
    {
        var properties = new List<object?>();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                var name = ParseObjectPropertyName();
                Consume(TokenType.Colon, "Expected ':' after property name.");
                var value = ParseExpression();
                properties.Add(Cons.FromEnumerable(new object?[] { JsSymbols.Property, name, value }));
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBrace, "Expected '}' after object literal.");
        var items = new List<object?> { JsSymbols.ObjectLiteral };
        items.AddRange(properties);
        return Cons.FromEnumerable(items);
    }

    private object ParseArrayLiteral()
    {
        var elements = new List<object?>();
        if (!Check(TokenType.RightBracket))
        {
            do
            {
                elements.Add(ParseExpression());
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBracket, "Expected ']' after array literal.");
        var items = new List<object?> { JsSymbols.ArrayLiteral };
        items.AddRange(elements);
        return Cons.FromEnumerable(items);
    }

    private object ParseTemplateLiteralExpression()
    {
        var token = Previous();
        var parts = token.Literal as List<object> ?? new List<object>();
        
        // (template part1 expr1 part2 expr2 ...)
        var items = new List<object?> { JsSymbols.TemplateLiteral };
        
        foreach (var part in parts)
        {
            if (part is string str)
            {
                items.Add(str);
            }
            else if (part is TemplateExpression expr)
            {
                // Parse the expression inside ${}
                // We need to parse just the expression, so we create a small wrapper program
                var exprText = expr.ExpressionText.Trim();
                var exprLexer = new Lexer(exprText);
                var exprTokens = exprLexer.Tokenize();
                
                // Create a parser and directly call internal parsing
                // Since we can't access ParseExpression directly, we'll use a trick:
                // Parse it as "exprText;" to make it a valid statement
                var wrappedLexer = new Lexer(exprText + ";");
                var wrappedTokens = wrappedLexer.Tokenize();
                var exprParser = new Parser(wrappedTokens);
                var exprProgram = exprParser.ParseProgram();
                
                // Extract the expression (skip the 'program' wrapper)
                if (exprProgram is Cons programCons && !programCons.IsEmpty)
                {
                    var firstStatement = programCons.Rest.Head;
                    // If it's an expression statement, unwrap it
                    if (firstStatement is Cons stmtCons && 
                        stmtCons.Head is Symbol sym && 
                        ReferenceEquals(sym, JsSymbols.ExpressionStatement))
                    {
                        items.Add(stmtCons.Rest.Head);
                    }
                    else
                    {
                        items.Add(firstStatement);
                    }
                }
            }
        }
        
        return Cons.FromEnumerable(items);
    }

    private string ParseObjectPropertyName()
    {
        if (Match(TokenType.String))
        {
            return Previous().Literal as string ?? string.Empty;
        }

        var identifier = Consume(TokenType.Identifier, "Expected property name.");
        return identifier.Lexeme;
    }

    private object FinishGet(object? target)
    {
        var nameToken = Consume(TokenType.Identifier, "Expected property name after '.'.");
        var propertyName = nameToken.Lexeme;
        return Cons.FromEnumerable(new object?[] { JsSymbols.GetProperty, target, propertyName });
    }

    private object FinishIndex(object? target)
    {
        var indexExpression = ParseExpression();
        Consume(TokenType.RightBracket, "Expected ']' after index expression.");
        return Cons.FromEnumerable(new object?[] { JsSymbols.GetIndex, target, indexExpression });
    }

    private static Cons CreateDefaultConstructor(Symbol name)
    {
        var body = Cons.FromEnumerable(new object?[] { JsSymbols.Block });
        return Cons.FromEnumerable(new object?[] { JsSymbols.Lambda, name, Cons.Empty, body });
    }

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
        if (IsAtEnd)
        {
            return type == TokenType.Eof;
        }

        return Peek().Type == type;
    }

    private Token Advance()
    {
        if (!IsAtEnd)
        {
            _current++;
        }

        return Previous();
    }

    private bool IsAtEnd => Peek().Type == TokenType.Eof;

    private Token Peek() => _tokens[_current];

    private Token Previous() => _tokens[_current - 1];

    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }

        throw new ParseException(message);
    }
}
