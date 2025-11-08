namespace Asynkron.JsEngine;

internal sealed class Parser(IReadOnlyList<Token> tokens)
{
    private readonly IReadOnlyList<Token> _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private int _current;

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
        // Check for async function
        if (Match(TokenType.Async))
        {
            if (Match(TokenType.Function))
            {
                return ParseAsyncFunctionDeclaration();
            }
            else
            {
                throw new ParseException("Expected 'function' after 'async'.");
            }
        }

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
        // Check if this is a generator function (function*)
        var isGenerator = Match(TokenType.Star);
        
        var nameToken = Consume(TokenType.Identifier, "Expected function name.");
        var name = Symbol.Intern(nameToken.Lexeme);
        Consume(TokenType.LeftParen, "Expected '(' after function name.");
        var parameters = ParseParameterList();
        Consume(TokenType.RightParen, "Expected ')' after function parameters.");
        var body = ParseBlock();

        var functionType = isGenerator ? JsSymbols.Generator : JsSymbols.Function;
        return Cons.FromEnumerable([functionType, name, parameters, body]);
    }

    private object ParseAsyncFunctionDeclaration()
    {
        var nameToken = Consume(TokenType.Identifier, "Expected function name.");
        var name = Symbol.Intern(nameToken.Lexeme);
        Consume(TokenType.LeftParen, "Expected '(' after function name.");
        var parameters = ParseParameterList();
        Consume(TokenType.RightParen, "Expected ')' after function parameters.");
        var body = ParseBlock();

        return Cons.FromEnumerable([JsSymbols.Async, name, parameters, body]);
    }

    private object ParseClassDeclaration()
    {
        var nameToken = Consume(TokenType.Identifier, "Expected class name.");
        var name = Symbol.Intern(nameToken.Lexeme);

        Cons? extendsClause = null;
        if (Match(TokenType.Extends))
        {
            var baseExpression = ParseExpression();
            extendsClause = Cons.FromEnumerable([JsSymbols.Extends, baseExpression]);
        }

        Consume(TokenType.LeftBrace, "Expected '{' after class name or extends clause.");

        Cons? constructor = null;
        var methods = new List<object?>();

        while (!Check(TokenType.RightBrace))
        {
            // Check for getter/setter in class
            if (Match(TokenType.Get))
            {
                var methodNameToken = Consume(TokenType.Identifier, "Expected getter name in class body.");
                var methodName = methodNameToken.Lexeme;
                Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                var body = ParseBlock();
                methods.Add(Cons.FromEnumerable([JsSymbols.Getter, methodName, body]));
            }
            else if (Match(TokenType.Set))
            {
                var methodNameToken = Consume(TokenType.Identifier, "Expected setter name in class body.");
                var methodName = methodNameToken.Lexeme;
                Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                var paramToken = Consume(TokenType.Identifier, "Expected parameter name in setter.");
                var param = Symbol.Intern(paramToken.Lexeme);
                Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                var body = ParseBlock();
                methods.Add(Cons.FromEnumerable([JsSymbols.Setter, methodName, param, body]));
            }
            else
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
                var lambda = Cons.FromEnumerable([JsSymbols.Lambda, lambdaName, parameters, body]);

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
                    methods.Add(Cons.FromEnumerable([JsSymbols.Method, methodName, lambda]));
                }
            }
        }

        Consume(TokenType.RightBrace, "Expected '}' after class body.");
        Match(TokenType.Semicolon); // allow optional semicolon terminator

        constructor ??= CreateDefaultConstructor(name);
        var methodList = Cons.FromEnumerable(methods);

        return Cons.FromEnumerable([JsSymbols.Class, name, extendsClause, constructor, methodList]);
    }

    private Cons ParseParameterList()
    {
        var parameters = new List<object?>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                // Check for rest parameter
                if (Match(TokenType.DotDotDot))
                {
                    var restIdentifier = Consume(TokenType.Identifier, "Expected parameter name after '...'.");
                    var restParam = Symbol.Intern(restIdentifier.Lexeme);
                    parameters.Add(Cons.FromEnumerable([JsSymbols.Rest, restParam]));
                    // Rest parameter must be last
                    break;
                }
                
                // Check for array destructuring parameter
                if (Check(TokenType.LeftBracket))
                {
                    Consume(TokenType.LeftBracket, "Expected '[' for array destructuring parameter.");
                    var pattern = ParseArrayDestructuringPattern();
                    parameters.Add(pattern);
                }
                // Check for object destructuring parameter
                else if (Check(TokenType.LeftBrace))
                {
                    Consume(TokenType.LeftBrace, "Expected '{' for object destructuring parameter.");
                    var pattern = ParseObjectDestructuringPattern();
                    parameters.Add(pattern);
                }
                else
                {
                    var identifier = Consume(TokenType.Identifier, "Expected parameter name.");
                    parameters.Add(Symbol.Intern(identifier.Lexeme));
                }
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

        // Check for destructuring patterns
        if (Check(TokenType.LeftBracket))
        {
            return ParseArrayDestructuring(kind, keyword);
        }
        
        if (Check(TokenType.LeftBrace))
        {
            return ParseObjectDestructuring(kind, keyword);
        }

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

        return Cons.FromEnumerable([tag, name, initializer]);
    }

    private object ParseArrayDestructuring(TokenType kind, string keyword)
    {
        Consume(TokenType.LeftBracket, "Expected '[' for array destructuring.");
        var pattern = ParseArrayDestructuringPattern();
        
        if (!Match(TokenType.Equal))
        {
            throw new ParseException($"Destructuring declarations require an initializer.");
        }
        
        var initializer = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
        
        var tag = kind switch
        {
            TokenType.Let => JsSymbols.Let,
            TokenType.Var => JsSymbols.Var,
            TokenType.Const => JsSymbols.Const,
            _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
        };
        
        return Cons.FromEnumerable([tag, pattern, initializer]);
    }
    
    private object ParseObjectDestructuring(TokenType kind, string keyword)
    {
        Consume(TokenType.LeftBrace, "Expected '{' for object destructuring.");
        var pattern = ParseObjectDestructuringPattern();
        
        if (!Match(TokenType.Equal))
        {
            throw new ParseException($"Destructuring declarations require an initializer.");
        }
        
        var initializer = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
        
        var tag = kind switch
        {
            TokenType.Let => JsSymbols.Let,
            TokenType.Var => JsSymbols.Var,
            TokenType.Const => JsSymbols.Const,
            _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
        };
        
        return Cons.FromEnumerable([tag, pattern, initializer]);
    }
    
    private Cons ParseArrayDestructuringPattern()
    {
        var elements = new List<object?> { JsSymbols.ArrayPattern };
        
        if (!Check(TokenType.RightBracket))
        {
            do
            {
                // Check for hole (skipped element)
                if (Check(TokenType.Comma))
                {
                    elements.Add(null); // null represents a hole
                    continue;
                }
                
                // Check for rest element
                if (Match(TokenType.DotDotDot))
                {
                    var name = Consume(TokenType.Identifier, "Expected identifier after '...'.");
                    elements.Add(Cons.FromEnumerable([JsSymbols.PatternRest, Symbol.Intern(name.Lexeme)]));
                    break; // Rest must be last
                }
                
                // Check for nested array pattern
                if (Check(TokenType.LeftBracket))
                {
                    Consume(TokenType.LeftBracket, "Expected '[' for nested array pattern.");
                    var nestedPattern = ParseArrayDestructuringPattern();
                    elements.Add(Cons.FromEnumerable([JsSymbols.PatternElement, nestedPattern, null]));
                }
                // Check for nested object pattern
                else if (Check(TokenType.LeftBrace))
                {
                    Consume(TokenType.LeftBrace, "Expected '{' for nested object pattern.");
                    var nestedPattern = ParseObjectDestructuringPattern();
                    elements.Add(Cons.FromEnumerable([JsSymbols.PatternElement, nestedPattern, null]));
                }
                else
                {
                    // Simple identifier
                    var name = Consume(TokenType.Identifier, "Expected identifier in array pattern.");
                    var identifier = Symbol.Intern(name.Lexeme);
                    
                    // Check for default value
                    object? defaultValue = null;
                    if (Match(TokenType.Equal))
                    {
                        defaultValue = ParseExpression();
                    }
                    
                    elements.Add(Cons.FromEnumerable([JsSymbols.PatternElement, identifier, defaultValue]));
                }
            } while (Match(TokenType.Comma));
        }
        
        Consume(TokenType.RightBracket, "Expected ']' after array pattern.");
        return Cons.FromEnumerable(elements);
    }
    
    private Cons ParseObjectDestructuringPattern()
    {
        var properties = new List<object?> { JsSymbols.ObjectPattern };
        
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                // Check for rest property
                if (Match(TokenType.DotDotDot))
                {
                    var name = Consume(TokenType.Identifier, "Expected identifier after '...'.");
                    properties.Add(Cons.FromEnumerable([JsSymbols.PatternRest, Symbol.Intern(name.Lexeme)]));
                    break; // Rest must be last
                }
                
                // Parse property name
                var propertyName = ParseObjectPropertyName();
                
                // Check for shorthand or renaming
                if (Match(TokenType.Colon))
                {
                    // Renaming: {x: newX} or nested pattern
                    if (Check(TokenType.LeftBracket))
                    {
                        Consume(TokenType.LeftBracket, "Expected '[' for nested array pattern.");
                        var nestedPattern = ParseArrayDestructuringPattern();
                        properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, propertyName, nestedPattern, null
                        ]));
                    }
                    else if (Check(TokenType.LeftBrace))
                    {
                        Consume(TokenType.LeftBrace, "Expected '{' for nested object pattern.");
                        var nestedPattern = ParseObjectDestructuringPattern();
                        properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, propertyName, nestedPattern, null
                        ]));
                    }
                    else
                    {
                        var targetName = Consume(TokenType.Identifier, "Expected identifier after ':'.");
                        var target = Symbol.Intern(targetName.Lexeme);
                        
                        // Check for default value
                        object? defaultValue = null;
                        if (Match(TokenType.Equal))
                        {
                            defaultValue = ParseExpression();
                        }
                        
                        properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, propertyName, target, defaultValue
                        ]));
                    }
                }
                else
                {
                    // Shorthand: {x} is same as {x: x}
                    var identifier = Symbol.Intern(propertyName);
                    
                    // Check for default value
                    object? defaultValue = null;
                    if (Match(TokenType.Equal))
                    {
                        defaultValue = ParseExpression();
                    }
                    
                    properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, propertyName, identifier, defaultValue
                    ]));
                }
            } while (Match(TokenType.Comma));
        }
        
        Consume(TokenType.RightBrace, "Expected '}' after object pattern.");
        return Cons.FromEnumerable(properties);
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
            return Cons.FromEnumerable([JsSymbols.Break]);
        }

        if (Match(TokenType.Continue))
        {
            Consume(TokenType.Semicolon, "Expected ';' after continue statement.");
            return Cons.FromEnumerable([JsSymbols.Continue]);
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
                clauses.Add(Cons.FromEnumerable([
                    JsSymbols.Case,
                    test,
                    ParseSwitchClauseStatements()
                ]));
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
                clauses.Add(Cons.FromEnumerable([
                    JsSymbols.Default,
                    ParseSwitchClauseStatements()
                ]));
                continue;
            }

            throw new ParseException("Unexpected token in switch body.");
        }

        Consume(TokenType.RightBrace, "Expected '}' after switch body.");
        return Cons.FromEnumerable([
            JsSymbols.Switch,
            discriminant,
            Cons.FromEnumerable(clauses)
        ]);
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
            catchClause = Cons.FromEnumerable([
                JsSymbols.Catch,
                catchSymbol,
                catchBlock
            ]);
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

        return Cons.FromEnumerable([
            JsSymbols.Try,
            tryBlock,
            catchClause,
            finallyBlock
        ]);
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

        return Cons.FromEnumerable([JsSymbols.If, condition, thenBranch, elseBranch]);
    }

    private object ParseWhileStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after while condition.");
        var body = ParseStatement();
        return Cons.FromEnumerable([JsSymbols.While, condition, body]);
    }

    private object ParseDoWhileStatement()
    {
        var body = ParseStatement();
        Consume(TokenType.While, "Expected 'while' after do-while body.");
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after do-while condition.");
        Consume(TokenType.Semicolon, "Expected ';' after do-while statement.");
        return Cons.FromEnumerable([JsSymbols.DoWhile, condition, body]);
    }

    private object ParseForStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'for'.");

        // Check for for...in or for...of loops
        // We need to look ahead to detect if this is a for...in/of loop
        var checkpointPosition = _current;
        
        // Try to parse variable declaration or identifier
        object? loopVariable = null;
        TokenType? varKind = null;
        
        if (Match(TokenType.Let))
        {
            varKind = TokenType.Let;
            var identifier = Consume(TokenType.Identifier, "Expected identifier in for loop.");
            loopVariable = Symbol.Intern(identifier.Lexeme);
        }
        else if (Match(TokenType.Var))
        {
            varKind = TokenType.Var;
            var identifier = Consume(TokenType.Identifier, "Expected identifier in for loop.");
            loopVariable = Symbol.Intern(identifier.Lexeme);
        }
        else if (Match(TokenType.Const))
        {
            varKind = TokenType.Const;
            var identifier = Consume(TokenType.Identifier, "Expected identifier in for loop.");
            loopVariable = Symbol.Intern(identifier.Lexeme);
        }
        
        // Check if this is for...in or for...of
        if (loopVariable != null && (Match(TokenType.In) || Match(TokenType.Of)))
        {
            var isForOf = Previous().Type == TokenType.Of;
            var iterableExpression = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after for...in/of clauses.");
            var body = ParseStatement();
            
            var keyword = varKind switch
            {
                TokenType.Let => "let",
                TokenType.Var => "var",
                TokenType.Const => "const",
                _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
            };
            
            var varDecl = Cons.FromEnumerable([Symbol.Intern(keyword), loopVariable, null]);
            
            if (isForOf)
            {
                return Cons.FromEnumerable([JsSymbols.ForOf, varDecl, iterableExpression, body]);
            }
            else
            {
                return Cons.FromEnumerable([JsSymbols.ForIn, varDecl, iterableExpression, body]);
            }
        }
        
        // Not a for...in/of loop, reset and parse as regular for loop
        _current = checkpointPosition;
        
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
        var body2 = ParseStatement();

        return Cons.FromEnumerable([JsSymbols.For, initializer, condition, increment, body2]);
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
            ? Cons.FromEnumerable([JsSymbols.Return, value])
            : Cons.FromEnumerable([JsSymbols.Return]);
    }

    private object ParseThrowStatement()
    {
        var value = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after throw statement.");
        return Cons.FromEnumerable([JsSymbols.Throw, value]);
    }

    private object ParseExpressionStatement()
    {
        var expression = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression statement.");
        return Cons.FromEnumerable([JsSymbols.ExpressionStatement, expression]);
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

        if (Match(TokenType.Equal, TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual,
                  TokenType.SlashEqual, TokenType.PercentEqual, TokenType.AmpEqual, TokenType.PipeEqual,
                  TokenType.CaretEqual, TokenType.LessLessEqual, TokenType.GreaterGreaterEqual, 
                  TokenType.GreaterGreaterGreaterEqual))
        {
            var op = Previous();
            var value = ParseAssignment();

            // Handle compound assignments by converting them to: variable = variable op value
            if (op.Type != TokenType.Equal)
            {
                var binaryOp = op.Type switch
                {
                    TokenType.PlusEqual => "+",
                    TokenType.MinusEqual => "-",
                    TokenType.StarEqual => "*",
                    TokenType.SlashEqual => "/",
                    TokenType.PercentEqual => "%",
                    TokenType.AmpEqual => "&",
                    TokenType.PipeEqual => "|",
                    TokenType.CaretEqual => "^",
                    TokenType.LessLessEqual => "<<",
                    TokenType.GreaterGreaterEqual => ">>",
                    TokenType.GreaterGreaterGreaterEqual => ">>>",
                    _ => throw new InvalidOperationException("Unexpected compound assignment operator.")
                };

                value = Cons.FromEnumerable([JsSymbols.Operator(binaryOp), expr, value]);
            }

            if (expr is Symbol symbol)
            {
                return Cons.FromEnumerable([JsSymbols.Assign, symbol, value]);
            }

            if (expr is Cons { Head: Symbol head } assignmentTarget && ReferenceEquals(head, JsSymbols.GetProperty))
            {
                var target = assignmentTarget.Rest.Head;
                var propertyName = assignmentTarget.Rest.Rest.Head;
                return Cons.FromEnumerable([JsSymbols.SetProperty, target, propertyName, value]);
            }

            if (expr is Cons { Head: Symbol indexHead } indexTarget && ReferenceEquals(indexHead, JsSymbols.GetIndex))
            {
                var target = indexTarget.Rest.Head;
                var index = indexTarget.Rest.Rest.Head;
                return Cons.FromEnumerable([JsSymbols.SetIndex, target, index, value]);
            }

            // Check if this is an array literal that should be treated as a destructuring pattern
            if (expr is Cons { Head: Symbol arrayHead } arrayLiteral && ReferenceEquals(arrayHead, JsSymbols.ArrayLiteral))
            {
                var pattern = ConvertArrayLiteralToPattern(arrayLiteral);
                return Cons.FromEnumerable([JsSymbols.DestructuringAssignment, pattern, value]);
            }

            // Check if this is an object literal that should be treated as a destructuring pattern
            if (expr is Cons { Head: Symbol objectHead } objectLiteral && ReferenceEquals(objectHead, JsSymbols.ObjectLiteral))
            {
                var pattern = ConvertObjectLiteralToPattern(objectLiteral);
                return Cons.FromEnumerable([JsSymbols.DestructuringAssignment, pattern, value]);
            }

            throw new ParseException($"Invalid assignment target near line {op.Line} column {op.Column}.");
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
            return Cons.FromEnumerable([JsSymbols.Ternary, expr, thenBranch, elseBranch]);
        }

        return expr;
    }

    private object? ParseLogicalOr()
    {
        var expr = ParseLogicalAnd();

        while (Match(TokenType.PipePipe))
        {
            var right = ParseLogicalAnd();
            expr = Cons.FromEnumerable([
                JsSymbols.Operator("||"),
                expr,
                right
            ]);
        }

        return expr;
    }

    private object? ParseLogicalAnd()
    {
        var expr = ParseNullishCoalescing();

        while (Match(TokenType.AmpAmp))
        {
            var right = ParseNullishCoalescing();
            expr = Cons.FromEnumerable([
                JsSymbols.Operator("&&"),
                expr,
                right
            ]);
        }

        return expr;
    }

    private object? ParseNullishCoalescing()
    {
        var expr = ParseBitwiseOr();

        while (Match(TokenType.QuestionQuestion))
        {
            var right = ParseBitwiseOr();
            expr = Cons.FromEnumerable([
                JsSymbols.Operator("??"),
                expr,
                right
            ]);
        }

        return expr;
    }

    private object? ParseBitwiseOr()
    {
        var expr = ParseBitwiseXor();

        while (Match(TokenType.Pipe))
        {
            var right = ParseBitwiseXor();
            expr = Cons.FromEnumerable([
                JsSymbols.Operator("|"),
                expr,
                right
            ]);
        }

        return expr;
    }

    private object? ParseBitwiseXor()
    {
        var expr = ParseBitwiseAnd();

        while (Match(TokenType.Caret))
        {
            var right = ParseBitwiseAnd();
            expr = Cons.FromEnumerable([
                JsSymbols.Operator("^"),
                expr,
                right
            ]);
        }

        return expr;
    }

    private object? ParseBitwiseAnd()
    {
        var expr = ParseEquality();

        while (Match(TokenType.Amp))
        {
            var right = ParseEquality();
            expr = Cons.FromEnumerable([
                JsSymbols.Operator("&"),
                expr,
                right
            ]);
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

            expr = Cons.FromEnumerable([
                JsSymbols.Operator(op),
                expr,
                right
            ]);
        }

        return expr;
    }

    private object? ParseComparison()
    {
        var expr = ParseShift();
        while (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual))
        {
            var op = Previous();
            var right = ParseShift();
            var symbol = op.Type switch
            {
                TokenType.Greater => JsSymbols.Operator(">"),
                TokenType.GreaterEqual => JsSymbols.Operator(">="),
                TokenType.Less => JsSymbols.Operator("<"),
                TokenType.LessEqual => JsSymbols.Operator("<="),
                _ => throw new InvalidOperationException("Unexpected comparison operator.")
            };

            expr = Cons.FromEnumerable([symbol, expr, right]);
        }

        return expr;
    }

    private object? ParseShift()
    {
        var expr = ParseTerm();
        while (Match(TokenType.LessLess, TokenType.GreaterGreater, TokenType.GreaterGreaterGreater))
        {
            var op = Previous();
            var right = ParseTerm();
            var symbol = op.Type switch
            {
                TokenType.LessLess => JsSymbols.Operator("<<"),
                TokenType.GreaterGreater => JsSymbols.Operator(">>"),
                TokenType.GreaterGreaterGreater => JsSymbols.Operator(">>>"),
                _ => throw new InvalidOperationException("Unexpected shift operator.")
            };

            expr = Cons.FromEnumerable([symbol, expr, right]);
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
            expr = Cons.FromEnumerable([symbol, expr, right]);
        }

        return expr;
    }

    private object? ParseFactor()
    {
        var expr = ParseUnary();
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var op = Previous();
            var right = ParseUnary();
            var symbol = op.Type switch
            {
                TokenType.Star => JsSymbols.Operator("*"),
                TokenType.Slash => JsSymbols.Operator("/"),
                TokenType.Percent => JsSymbols.Operator("%"),
                _ => throw new InvalidOperationException("Unexpected factor operator.")
            };
            expr = Cons.FromEnumerable([symbol, expr, right]);
        }

        return expr;
    }

    private object? ParseUnary()
    {
        if (Match(TokenType.Bang))
        {
            return Cons.FromEnumerable([JsSymbols.Not, ParseUnary()]);
        }

        if (Match(TokenType.Minus))
        {
            return Cons.FromEnumerable([JsSymbols.Negate, ParseUnary()]);
        }

        if (Match(TokenType.Tilde))
        {
            return Cons.FromEnumerable([JsSymbols.Operator("~"), ParseUnary()]);
        }

        if (Match(TokenType.Typeof))
        {
            return Cons.FromEnumerable([JsSymbols.Typeof, ParseUnary()]);
        }

        if (Match(TokenType.PlusPlus))
        {
            var operand = ParseUnary();
            return Cons.FromEnumerable([JsSymbols.Operator("++prefix"), operand]);
        }

        if (Match(TokenType.MinusMinus))
        {
            var operand = ParseUnary();
            return Cons.FromEnumerable([JsSymbols.Operator("--prefix"), operand]);
        }

        if (Match(TokenType.Yield))
        {
            // yield can be followed by an expression or nothing
            // We'll parse an assignment expression (one level below expression)
            var value = ParseAssignment();
            return Cons.FromEnumerable([JsSymbols.Yield, value]);
        }

        if (Match(TokenType.Await))
        {
            // await must be followed by an expression
            var value = ParseUnary();
            return Cons.FromEnumerable([JsSymbols.Await, value]);
        }

        return ParsePostfix();
    }

    private object? ParsePostfix()
    {
        var expr = ParseCall();

        if (Match(TokenType.PlusPlus))
        {
            return Cons.FromEnumerable([JsSymbols.Operator("++postfix"), expr]);
        }

        if (Match(TokenType.MinusMinus))
        {
            return Cons.FromEnumerable([JsSymbols.Operator("--postfix"), expr]);
        }

        return expr;
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

            if (Match(TokenType.QuestionDot))
            {
                // Optional chaining: obj?.prop or obj?.method() or obj?.[index]
                if (Match(TokenType.LeftParen))
                {
                    // obj?.()
                    var arguments = ParseArgumentList();
                    var items = new List<object?> { JsSymbols.OptionalCall, expr };
                    items.AddRange(arguments);
                    expr = Cons.FromEnumerable(items);
                }
                else if (Match(TokenType.LeftBracket))
                {
                    // obj?.[index]
                    var indexExpression = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                    expr = Cons.FromEnumerable([JsSymbols.OptionalGetIndex, expr, indexExpression]);
                }
                else
                {
                    // obj?.prop
                    expr = FinishOptionalGet(expr);
                }
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
                // Check for spread in arguments
                if (Match(TokenType.DotDotDot))
                {
                    var expr = ParseExpression();
                    arguments.Add(Cons.FromEnumerable([JsSymbols.Spread, expr]));
                }
                else
                {
                    arguments.Add(ParseExpression());
                }
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

        if (Match(TokenType.Undefined))
        {
            return JsSymbols.Undefined;
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

        if (Match(TokenType.RegexLiteral))
        {
            return ParseRegexLiteral();
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

        if (Match(TokenType.Async))
        {
            if (Match(TokenType.Function))
            {
                return ParseAsyncFunctionExpression();
            }
            else
            {
                throw new ParseException("Expected 'function' after 'async' in expression context.");
            }
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
        // Check if this is a generator function expression (function*)
        var isGenerator = Match(TokenType.Star);
        
        Symbol? name = null;
        if (Check(TokenType.Identifier))
        {
            name = Symbol.Intern(Advance().Lexeme);
        }

        Consume(TokenType.LeftParen, "Expected '(' after function keyword.");
        var parameters = ParseParameterList();
        Consume(TokenType.RightParen, "Expected ')' after lambda parameters.");
        var body = ParseBlock();
        
        // Use Lambda for both regular and generator function expressions
        // The isGenerator flag would need to be stored separately, but for now
        // we'll use Generator symbol for generator function expressions
        var functionType = isGenerator ? JsSymbols.Generator : JsSymbols.Lambda;
        return Cons.FromEnumerable([functionType, name, parameters, body]);
    }

    private object ParseAsyncFunctionExpression()
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
        
        return Cons.FromEnumerable([JsSymbols.Async, name, parameters, body]);
    }

    private object ParseObjectLiteral()
    {
        var properties = new List<object?>();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                // Check for spread in object literal (for object rest/spread - future feature)
                if (Match(TokenType.DotDotDot))
                {
                    var expr = ParseExpression();
                    properties.Add(Cons.FromEnumerable([JsSymbols.Spread, expr]));
                    continue;
                }

                // Check for getter/setter
                if (Match(TokenType.Get))
                {
                    var name = ParseObjectPropertyName();
                    Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                    Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                    var body = ParseBlock();
                    properties.Add(Cons.FromEnumerable([JsSymbols.Getter, name, body]));
                }
                else if (Match(TokenType.Set))
                {
                    var name = ParseObjectPropertyName();
                    Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                    var paramToken = Consume(TokenType.Identifier, "Expected parameter name in setter.");
                    var param = Symbol.Intern(paramToken.Lexeme);
                    Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                    var body = ParseBlock();
                    properties.Add(Cons.FromEnumerable([JsSymbols.Setter, name, param, body]));
                }
                // Check for computed property name
                else if (Check(TokenType.LeftBracket))
                {
                    Advance(); // consume '['
                    var keyExpression = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after computed property name.");
                    
                    // Check if this is a method
                    if (Check(TokenType.LeftParen))
                    {
                        Advance(); // consume '('
                        var parameters = ParseParameterList();
                        Consume(TokenType.RightParen, "Expected ')' after parameters.");
                        var body = ParseBlock();
                        var lambda = Cons.FromEnumerable([JsSymbols.Lambda, null, parameters, body]);
                        properties.Add(Cons.FromEnumerable([JsSymbols.Property, keyExpression, lambda]));
                    }
                    else
                    {
                        Consume(TokenType.Colon, "Expected ':' after computed property name.");
                        var value = ParseExpression();
                        properties.Add(Cons.FromEnumerable([JsSymbols.Property, keyExpression, value]));
                    }
                }
                else
                {
                    var name = ParseObjectPropertyName();
                    
                    // Check for method shorthand: name() { ... }
                    if (Check(TokenType.LeftParen))
                    {
                        Advance(); // consume '('
                        var parameters = ParseParameterList();
                        Consume(TokenType.RightParen, "Expected ')' after parameters.");
                        var body = ParseBlock();
                        var lambda = Cons.FromEnumerable([JsSymbols.Lambda, null, parameters, body]);
                        properties.Add(Cons.FromEnumerable([JsSymbols.Property, name, lambda]));
                    }
                    // Check for property shorthand: { name } instead of { name: name }
                    else if (name is string nameStr && !Check(TokenType.Colon))
                    {
                        // Property shorthand: use the identifier as both key and value
                        var symbol = Symbol.Intern(nameStr);
                        properties.Add(Cons.FromEnumerable([JsSymbols.Property, name, symbol]));
                    }
                    else
                    {
                        Consume(TokenType.Colon, "Expected ':' after property name.");
                        var value = ParseExpression();
                        properties.Add(Cons.FromEnumerable([JsSymbols.Property, name, value]));
                    }
                }
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
                // Check for spread in array literal
                if (Match(TokenType.DotDotDot))
                {
                    var expr = ParseExpression();
                    elements.Add(Cons.FromEnumerable([JsSymbols.Spread, expr]));
                }
                else
                {
                    elements.Add(ParseExpression());
                }
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
        var parts = token.Literal as List<object> ?? [];
        
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

    private object ParseRegexLiteral()
    {
        var token = Previous();
        var regexValue = token.Literal as RegexLiteralValue;
        if (regexValue == null)
        {
            throw new ParseException("Invalid regex literal.");
        }

        // Create a new RegExp(...) expression
        // (new RegExp pattern flags)
        var pattern = regexValue.Pattern;
        var flags = regexValue.Flags;
        
        var items = new List<object?> 
        { 
            JsSymbols.New, 
            Symbol.Intern("RegExp"),
            pattern
        };
        
        if (!string.IsNullOrEmpty(flags))
        {
            items.Add(flags);
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
        // Allow identifiers or keywords as property names (e.g., object.of, object.in, object.for)
        if (!Check(TokenType.Identifier) && !IsKeyword(Peek()))
        {
            throw new ParseException("Expected property name after '.'.");
        }
        var nameToken = Advance();
        var propertyName = nameToken.Lexeme;
        return Cons.FromEnumerable([JsSymbols.GetProperty, target, propertyName]);
    }

    private object FinishOptionalGet(object? target)
    {
        // Allow identifiers or keywords as property names
        if (!Check(TokenType.Identifier) && !IsKeyword(Peek()))
        {
            throw new ParseException("Expected property name after '?.'.");
        }
        var nameToken = Advance();
        var propertyName = nameToken.Lexeme;
        return Cons.FromEnumerable([JsSymbols.OptionalGetProperty, target, propertyName]);
    }

    private bool IsKeyword(Token token)
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
            TokenType.Undefined or TokenType.Typeof or TokenType.Get or TokenType.Set or
            TokenType.Yield or TokenType.Async or TokenType.Await => true,
            _ => false
        };
    }

    private object FinishIndex(object? target)
    {
        var indexExpression = ParseExpression();
        Consume(TokenType.RightBracket, "Expected ']' after index expression.");
        return Cons.FromEnumerable([JsSymbols.GetIndex, target, indexExpression]);
    }

    private static Cons CreateDefaultConstructor(Symbol name)
    {
        var body = Cons.FromEnumerable([JsSymbols.Block]);
        return Cons.FromEnumerable([JsSymbols.Lambda, name, Cons.Empty, body]);
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

    private Cons ConvertArrayLiteralToPattern(Cons arrayLiteral)
    {
        var elements = new List<object?> { JsSymbols.ArrayPattern };
        
        foreach (var item in arrayLiteral.Rest)
        {
            if (item is null)
            {
                elements.Add(null); // hole
            }
            else if (item is Cons { Head: Symbol spreadHead } spreadCons && ReferenceEquals(spreadHead, JsSymbols.Spread))
            {
                // Spread becomes rest pattern
                var restTarget = spreadCons.Rest.Head;
                if (restTarget is not Symbol restSymbol)
                {
                    throw new ParseException("Rest element must be an identifier");
                }
                elements.Add(Cons.FromEnumerable([JsSymbols.PatternRest, restSymbol]));
                break; // Rest must be last
            }
            else if (item is Symbol symbol)
            {
                // Simple identifier
                elements.Add(Cons.FromEnumerable([JsSymbols.PatternElement, symbol, null]));
            }
            else if (item is Cons { Head: Symbol itemHead } itemCons)
            {
                // Check for nested patterns
                if (ReferenceEquals(itemHead, JsSymbols.ArrayLiteral))
                {
                    var nestedPattern = ConvertArrayLiteralToPattern(itemCons);
                    elements.Add(Cons.FromEnumerable([JsSymbols.PatternElement, nestedPattern, null]));
                }
                else if (ReferenceEquals(itemHead, JsSymbols.ObjectLiteral))
                {
                    var nestedPattern = ConvertObjectLiteralToPattern(itemCons);
                    elements.Add(Cons.FromEnumerable([JsSymbols.PatternElement, nestedPattern, null]));
                }
                else
                {
                    throw new ParseException("Invalid destructuring pattern");
                }
            }
            else
            {
                throw new ParseException("Invalid destructuring pattern");
            }
        }
        
        return Cons.FromEnumerable(elements);
    }

    private Cons ConvertObjectLiteralToPattern(Cons objectLiteral)
    {
        var properties = new List<object?> { JsSymbols.ObjectPattern };
        
        foreach (var prop in objectLiteral.Rest)
        {
            if (prop is not Cons { Head: Symbol propHead } propCons)
            {
                throw new ParseException("Invalid object destructuring pattern");
            }
            
            if (ReferenceEquals(propHead, JsSymbols.Spread))
            {
                // Spread becomes rest property
                var restTarget = propCons.Rest.Head;
                if (restTarget is not Symbol restSymbol)
                {
                    throw new ParseException("Rest property must be an identifier");
                }
                properties.Add(Cons.FromEnumerable([JsSymbols.PatternRest, restSymbol]));
                break; // Rest must be last
            }
            else if (ReferenceEquals(propHead, JsSymbols.Property))
            {
                var key = propCons.Rest.Head as string;
                var value = propCons.Rest.Rest.Head;
                
                if (key is null)
                {
                    throw new ParseException("Property key must be a string");
                }
                
                if (value is Symbol targetSymbol)
                {
                    // Simple property: {x} or {x: y}
                    properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, key, targetSymbol, null]));
                }
                else if (value is Cons { Head: Symbol valueHead } valueCons)
                {
                    // Nested pattern: {x: [a, b]}
                    if (ReferenceEquals(valueHead, JsSymbols.ArrayLiteral))
                    {
                        var nestedPattern = ConvertArrayLiteralToPattern(valueCons);
                        properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, key, nestedPattern, null]));
                    }
                    else if (ReferenceEquals(valueHead, JsSymbols.ObjectLiteral))
                    {
                        var nestedPattern = ConvertObjectLiteralToPattern(valueCons);
                        properties.Add(Cons.FromEnumerable([JsSymbols.PatternProperty, key, nestedPattern, null]));
                    }
                    else
                    {
                        throw new ParseException("Invalid nested destructuring pattern");
                    }
                }
                else
                {
                    throw new ParseException("Invalid destructuring pattern value");
                }
            }
            else
            {
                throw new ParseException("Invalid object destructuring pattern");
            }
        }
        
        return Cons.FromEnumerable(properties);
    }
}
