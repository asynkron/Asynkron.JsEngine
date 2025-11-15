using System.Globalization;
using static Asynkron.JsEngine.ConsDsl;
using static Asynkron.JsEngine.JsSymbols;

namespace Asynkron.JsEngine;

/// <summary>
/// Wrapper for multiple variable declarations from a single statement with comma-separated declarators.
/// </summary>
public sealed class MultipleDeclarations(List<object> declarations)
{
    public List<object> Declarations { get; } = declarations;
}

public sealed class Parser(IReadOnlyList<Token> tokens, string source)
{
    private readonly IReadOnlyList<Token> _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly string _source = source ?? string.Empty;
    private int _current;

    public Cons ParseProgram()
    {
        var statements = new List<object?> { Program };

        // Check for "use strict" directive at the beginning
        var hasUseStrict = CheckForUseStrictDirective();
        if (hasUseStrict)
        {
            statements.Add(S(UseStrict));
        }

        while (!Check(TokenType.Eof))
        {
            var declaration = ParseDeclaration();
            if (declaration is MultipleDeclarations multi)
            {
                // Expand multiple declarations into separate statements
                statements.AddRange(multi.Declarations);
            }
            else
            {
                statements.Add(declaration);
            }
        }

        return Cons.FromEnumerable(statements);
    }

    private object ParseDeclaration()
    {
        // Check for import statement vs import() expression
        if (Check(TokenType.Import))
        {
            // Peek ahead to see if this is import() (dynamic) or import ... from (static)
            var nextToken = PeekNext();
            if (nextToken.Type == TokenType.LeftParen)
            {
                // This is import() expression, treat as statement
                return ParseExpressionStatement();
            }

            // This is static import statement
            Match(TokenType.Import);
            return ParseImportDeclaration();
        }

        // Check for export statement
        if (Match(TokenType.Export))
        {
            return ParseExportDeclaration();
        }

        // Check for async function
        if (Match(TokenType.Async))
        {
            if (Match(TokenType.Function))
            {
                return ParseAsyncFunctionDeclaration();
            }

            throw new ParseException("Expected 'function' after 'async'.", Peek(), _source);
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

        var functionType = isGenerator ? Generator : Function;
        return S(functionType, name, parameters, body);
    }

    private object ParseAsyncFunctionDeclaration()
    {
        // Save the position - we need to go back to include 'async'
        // At this point both 'async' and 'function' have been consumed
        // So we go back 2 tokens to get 'async'
        var startTokenIndex = _current >= 2 ? _current - 2 : 0;
        var startToken = _tokens[startTokenIndex];

        var nameToken = Consume(TokenType.Identifier, "Expected function name.");
        var name = Symbol.Intern(nameToken.Lexeme);
        Consume(TokenType.LeftParen, "Expected '(' after function name.");
        var parameters = ParseParameterList();
        Consume(TokenType.RightParen, "Expected ')' after function parameters.");
        var body = ParseBlock();

        return MakeCons([Async, name, parameters, body], startToken);
    }

    private object ParseClassDeclaration()
    {
        var nameToken = Consume(TokenType.Identifier, "Expected class name.");
        var name = Symbol.Intern(nameToken.Lexeme);

        Cons? extendsClause = null;
        if (Match(TokenType.Extends))
        {
            var baseExpression = ParseExpression();
            extendsClause = S(Extends, baseExpression);
        }

        Consume(TokenType.LeftBrace, "Expected '{' after class name or extends clause.");

        Cons? constructor = null;
        var methods = new List<object?>();
        var privateFields = new List<object?>();
        var publicFields = new List<object?>();
        var staticFields = new List<object?>();

        while (!Check(TokenType.RightBrace))
        {
            // Check for static keyword
            var isStatic = false;
            if (Match(TokenType.Static))
            {
                isStatic = true;
            }

            // Check for private field declaration
            if (Check(TokenType.PrivateIdentifier))
            {
                var fieldToken = Advance();
                var fieldName = fieldToken.Lexeme; // Includes the '#'

                object? initializer = null;
                if (Match(TokenType.Equal))
                {
                    initializer = ParseExpression();
                }

                Match(TokenType.Semicolon); // optional semicolon

                if (isStatic)
                {
                    staticFields.Add(S(StaticField, fieldName, initializer));
                }
                else
                {
                    privateFields.Add(S(PrivateField, fieldName, initializer));
                }
            }
            // Check for getter/setter in class
            else if (Check(TokenType.Get) || Check(TokenType.Set))
            {
                var isGetter = Match(TokenType.Get);
                if (!isGetter)
                {
                    Match(TokenType.Set);
                }

                var methodNameToken = Consume(TokenType.Identifier,
                    isGetter ? "Expected getter name in class body." : "Expected setter name in class body.");
                var methodName = methodNameToken.Lexeme;

                if (isGetter)
                {
                    Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                    Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                    var body = ParseBlock();

                    if (isStatic)
                    {
                        methods.Add(S(StaticGetter, methodName, body));
                    }
                    else
                    {
                        methods.Add(S(Getter, methodName, body));
                    }
                }
                else
                {
                    Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                    var paramToken = Consume(TokenType.Identifier, "Expected parameter name in setter.");
                    var param = Symbol.Intern(paramToken.Lexeme);
                    Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                    var body = ParseBlock();

                    if (isStatic)
                    {
                        methods.Add(S(StaticSetter, methodName, param, body));
                    }
                    else
                    {
                        methods.Add(S(Setter, methodName, param, body));
                    }
                }
            }
            else if (Check(TokenType.Identifier))
            {
                var methodNameToken = Consume(TokenType.Identifier, "Expected method name in class body.");
                var methodName = methodNameToken.Lexeme;

                // Check if this is a field declaration
                if (Match(TokenType.Equal))
                {
                    var initializer = ParseExpression();
                    Match(TokenType.Semicolon); // optional semicolon

                    if (isStatic)
                    {
                        staticFields.Add(S(StaticField, methodName, initializer));
                    }
                    else
                        // Public instance field
                    {
                        publicFields.Add(S(PublicField, methodName, initializer));
                    }

                    continue;
                }

                // Check for constructor
                if (string.Equals(methodName, "constructor", StringComparison.Ordinal))
                {
                    if (isStatic)
                    {
                        throw new ParseException("Constructor cannot be static.", Peek(), _source);
                    }

                    if (constructor is not null)
                    {
                        throw new ParseException("Class cannot declare multiple constructors.", Peek(), _source);
                    }

                    Consume(TokenType.LeftParen, "Expected '(' after constructor name.");
                    var parameters = ParseParameterList();
                    Consume(TokenType.RightParen, "Expected ')' after constructor parameters.");
                    var body = ParseBlock();
                    constructor = S(Lambda, name, parameters, body);
                }
                else
                {
                    // Regular method
                    Consume(TokenType.LeftParen, "Expected '(' after method name.");
                    var parameters = ParseParameterList();
                    Consume(TokenType.RightParen, "Expected ')' after method parameters.");
                    var body = ParseBlock();

                    var lambda = S(Lambda, null, parameters, body);
                    if (isStatic)
                    {
                        methods.Add(S(StaticMethod, methodName, lambda));
                    }
                    else
                    {
                        methods.Add(S(Method, methodName, lambda));
                    }
                }
            }
            else
            {
                throw new ParseException("Expected method, field, getter, or setter in class body.", Peek(), _source);
            }
        }

        Consume(TokenType.RightBrace, "Expected '}' after class body.");
        Match(TokenType.Semicolon); // allow optional semicolon terminator

        constructor ??= CreateDefaultConstructor(name);
        var methodList = Cons.FromEnumerable(methods);

        // Merge all fields into a single list (private, public, and static)
        var allFields = new List<object?>();
        allFields.AddRange(privateFields);
        allFields.AddRange(publicFields);
        allFields.AddRange(staticFields);
        var fieldList = Cons.FromEnumerable(allFields);

        return S(Class, name, extendsClause, constructor, methodList, fieldList);
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
                    var restIdentifier = ConsumeParameterIdentifier("Expected parameter name after '...'.");
                    var restParam = Symbol.Intern(restIdentifier.Lexeme);
                    parameters.Add(S(Rest, restParam));
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
                    var identifier = ConsumeParameterIdentifier("Expected parameter name.");
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

        var declarations = new List<object>();

        // Parse first declaration (and potentially more comma-separated ones)
        do
        {
            // Check for destructuring patterns
            if (Check(TokenType.LeftBracket))
            {
                declarations.Add(ParseArrayDestructuringDeclarator(kind, keyword));
            }
            else if (Check(TokenType.LeftBrace))
            {
                declarations.Add(ParseObjectDestructuringDeclarator(kind, keyword));
            }
            else
            {
                // Regular identifier declaration
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
                        throw new ParseException("Const declarations require an initializer.", Peek(), _source);
                    }

                    initializer = Uninitialized;
                }

                var tag = kind switch
                {
                    TokenType.Let => Let,
                    TokenType.Var => Var,
                    TokenType.Const => Const,
                    _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
                };

                declarations.Add(S(tag, name, initializer));
            }
        } while (Match(TokenType.Comma)); // Continue if there's a comma

        Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");

        // If there's only one declaration, return it directly
        // If there are multiple, return them wrapped in MultipleDeclarations
        if (declarations.Count == 1)
        {
            return declarations[0];
        }

        return new MultipleDeclarations(declarations);
    }

    private object ParseArrayDestructuringDeclarator(TokenType kind, string keyword)
    {
        Consume(TokenType.LeftBracket, "Expected '[' for array destructuring.");
        var pattern = ParseArrayDestructuringPattern();

        if (!Match(TokenType.Equal))
        {
            throw new ParseException($"Destructuring declarations require an initializer.", Peek(), _source);
        }

        var initializer = ParseExpression();

        var tag = kind switch
        {
            TokenType.Let => Let,
            TokenType.Var => Var,
            TokenType.Const => Const,
            _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
        };

        return S(tag, pattern, initializer);
    }

    private object ParseObjectDestructuringDeclarator(TokenType kind, string keyword)
    {
        Consume(TokenType.LeftBrace, "Expected '{' for object destructuring.");
        var pattern = ParseObjectDestructuringPattern();

        if (!Match(TokenType.Equal))
        {
            throw new ParseException($"Destructuring declarations require an initializer.", Peek(), _source);
        }

        var initializer = ParseExpression();

        var tag = kind switch
        {
            TokenType.Let => Let,
            TokenType.Var => Var,
            TokenType.Const => Const,
            _ => throw new InvalidOperationException("Unsupported variable declaration keyword.")
        };

        return S(tag, pattern, initializer);
    }

    private Cons ParseArrayDestructuringPattern()
    {
        var elements = new List<object?> { ArrayPattern };

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
                    elements.Add(S(PatternRest, Symbol.Intern(name.Lexeme)));
                    break; // Rest must be last
                }

                // Check for nested array pattern
                if (Check(TokenType.LeftBracket))
                {
                    Consume(TokenType.LeftBracket, "Expected '[' for nested array pattern.");
                    var nestedPattern = ParseArrayDestructuringPattern();
                    elements.Add(S(PatternElement, nestedPattern, null));
                }
                // Check for nested object pattern
                else if (Check(TokenType.LeftBrace))
                {
                    Consume(TokenType.LeftBrace, "Expected '{' for nested object pattern.");
                    var nestedPattern = ParseObjectDestructuringPattern();
                    elements.Add(S(PatternElement, nestedPattern, null));
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

                    elements.Add(S(PatternElement, identifier, defaultValue));
                }
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBracket, "Expected ']' after array pattern.");
        return Cons.FromEnumerable(elements);
    }

    private Cons ParseObjectDestructuringPattern()
    {
        var properties = new List<object?> { ObjectPattern };

        if (!Check(TokenType.RightBrace))
        {
            do
            {
                // Check for rest property
                if (Match(TokenType.DotDotDot))
                {
                    var name = Consume(TokenType.Identifier, "Expected identifier after '...'.");
                    properties.Add(S(PatternRest, Symbol.Intern(name.Lexeme)));
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
                        properties.Add(S(PatternProperty, propertyName, nestedPattern, null
                        ));
                    }
                    else if (Check(TokenType.LeftBrace))
                    {
                        Consume(TokenType.LeftBrace, "Expected '{' for nested object pattern.");
                        var nestedPattern = ParseObjectDestructuringPattern();
                        properties.Add(S(PatternProperty, propertyName, nestedPattern, null
                        ));
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

                        properties.Add(S(PatternProperty, propertyName, target, defaultValue
                        ));
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

                    properties.Add(S(PatternProperty, propertyName, identifier, defaultValue
                    ));
                }
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBrace, "Expected '}' after object pattern.");
        return Cons.FromEnumerable(properties);
    }

    private object ParseStatement()
    {
        // Handle empty statement (just a semicolon)
        // This is valid JavaScript: while(condition); or if(x) {}; etc.
        if (Match(TokenType.Semicolon))
        {
            return S(EmptyStatement);
        }

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
            // Check if this is 'for await...of'
            var isForAwait = Match(TokenType.Await);
            return ParseForStatement(isForAwait);
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
            return S(Break);
        }

        if (Match(TokenType.Continue))
        {
            Consume(TokenType.Semicolon, "Expected ';' after continue statement.");
            return S(Continue);
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
            return ParseBlock(true);
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
                clauses.Add(S(
                    Case,
                    test,
                    ParseSwitchClauseStatements()
                ));
                continue;
            }

            if (Match(TokenType.Default))
            {
                if (seenDefault)
                {
                    throw new ParseException("Switch statement can only contain one default clause.", Peek(), _source);
                }

                seenDefault = true;
                Consume(TokenType.Colon, "Expected ':' after default keyword.");
                clauses.Add(S(
                    Default,
                    ParseSwitchClauseStatements()
                ));
                continue;
            }

            throw new ParseException("Unexpected token in switch body.", Peek(), _source);
        }

        Consume(TokenType.RightBrace, "Expected '}' after switch body.");
        return S(
            Switch,
            discriminant,
            Cons.FromEnumerable(clauses)
        );
    }

    private Cons ParseSwitchClauseStatements()
    {
        var statements = new List<object?> { Block };
        while (!Check(TokenType.Case) && !Check(TokenType.Default) && !Check(TokenType.RightBrace) &&
               !Check(TokenType.Eof)) statements.Add(ParseDeclaration());

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
            catchClause = S(
                Catch,
                catchSymbol,
                catchBlock
            );
        }

        Cons? finallyBlock = null;
        if (Match(TokenType.Finally))
        {
            finallyBlock = ParseBlock();
        }

        if (catchClause is null && finallyBlock is null)
        {
            throw new ParseException("Try statement requires at least a catch or finally clause.", Peek(), _source);
        }

        return S(
            Try,
            tryBlock,
            catchClause,
            finallyBlock
        );
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

        return S(If, condition, thenBranch, elseBranch);
    }

    private object ParseWhileStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after while condition.");
        var body = ParseStatement();
        return S(While, condition, body);
    }

    private object ParseDoWhileStatement()
    {
        var body = ParseStatement();
        Consume(TokenType.While, "Expected 'while' after do-while body.");
        Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after do-while condition.");
        Consume(TokenType.Semicolon, "Expected ';' after do-while statement.");
        return S(DoWhile, condition, body);
    }

    private object ParseForStatement(bool isForAwait = false)
    {
        // Save the position at the start of parsing (after 'for' and optionally 'await')
        var startTokenIndex = _current > 0 ? _current - 1 : 0;
        var startToken = _tokens[startTokenIndex];

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
        else if (Check(TokenType.Identifier))
        {
            // Check if this might be for...in/of without variable declaration
            var identifier = Advance();
            if (Match(TokenType.In) || Match(TokenType.Of))
            {
                var isForOf = Previous().Type == TokenType.Of;

                // for await requires for...of
                if (isForAwait && !isForOf)
                {
                    throw new ParseException("'for await' can only be used with 'of', not 'in'", Peek(), _source);
                }

                var iterableExpression = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after for...in/of clauses.");
                var body = ParseStatement();

                // Use the identifier directly (no variable declaration needed)
                loopVariable = Symbol.Intern(identifier.Lexeme);

                if (isForAwait)
                {
                    return MakeCons([ForAwaitOf, loopVariable, iterableExpression, body], startToken);
                }

                if (isForOf)
                {
                    return MakeCons([ForOf, loopVariable, iterableExpression, body], startToken);
                }

                return MakeCons([ForIn, loopVariable, iterableExpression, body], startToken);
            }

            // Not for...in/of, reset to checkpoint
            _current = checkpointPosition;
        }

        // Check if this is for...in or for...of with variable declaration
        if (loopVariable != null && varKind != null && (Match(TokenType.In) || Match(TokenType.Of)))
        {
            var isForOf = Previous().Type == TokenType.Of;

            // for await requires for...of
            if (isForAwait && !isForOf)
            {
                throw new ParseException("'for await' can only be used with 'of', not 'in'", Peek(), _source);
            }

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

            var varDecl = S(Symbol.Intern(keyword), loopVariable, null);

            if (isForAwait)
            {
                return MakeCons([ForAwaitOf, varDecl, iterableExpression, body], startToken);
            }

            if (isForOf)
            {
                return MakeCons([ForOf, varDecl, iterableExpression, body], startToken);
            }

            return MakeCons([ForIn, varDecl, iterableExpression, body], startToken);
        }

        // for await without of is an error
        if (isForAwait)
        {
            throw new ParseException("'for await' can only be used with 'for await...of' syntax", Peek(), _source);
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
            increment = ParseSequenceExpression();
        }

        Consume(TokenType.RightParen, "Expected ')' after for clauses.");
        var body2 = ParseStatement();

        return MakeCons([For, initializer, condition, increment, body2], startToken);
    }

    private object ParseReturnStatement()
    {
        // Restricted production: [no LineTerminator here] between return and its expression
        // If there's a line terminator, ASI applies and return has no value
        object? value = null;
        var hasValue = false;

        if (!Check(TokenType.Semicolon) && !Check(TokenType.RightBrace) && !Check(TokenType.Eof) && !HasLineTerminatorBefore())
        {
            value = ParseSequenceExpression();
            hasValue = true;
        }

        Consume(TokenType.Semicolon, "Expected ';' after return statement.");
        return hasValue
            ? S(Return, value)
            : S(Return);
    }

    private object ParseThrowStatement()
    {
        // Restricted production: [no LineTerminator here] between throw and its expression
        // Line terminator after throw is a syntax error, not ASI
        if (HasLineTerminatorBefore())
        {
            throw new ParseException("Line terminator is not allowed between 'throw' and its expression.", Peek(), _source);
        }

        var value = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after throw statement.");
        return S(Throw, value);
    }

    private object ParseExpressionStatement()
    {
        var expression = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression statement.");
        return S(ExpressionStatement, expression);
    }

    private Cons ParseBlock(bool leftBraceConsumed = false)
    {
        if (!leftBraceConsumed)
        {
            Consume(TokenType.LeftBrace, "Expected '{' to begin block.");
        }

        var statements = new List<object?> { Block };

        // Check for "use strict" directive at the beginning of the block
        var hasUseStrict = CheckForUseStrictDirective();
        if (hasUseStrict)
        {
            statements.Add(S(UseStrict));
        }

        while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
        {
            var declaration = ParseDeclaration();
            if (declaration is MultipleDeclarations multi)
            {
                // Expand multiple declarations into separate statements
                statements.AddRange(multi.Declarations);
            }
            else
            {
                statements.Add(declaration);
            }
        }

        Consume(TokenType.RightBrace, "Expected '}' after block.");
        return Cons.FromEnumerable(statements);
    }

    private object? ParseExpression()
    {
        return ParseAssignment();
    }

    /// <summary>
    /// Parse a sequence expression (comma operator).
    /// This is used in contexts where the comma operator is allowed, such as inside parentheses.
    /// </summary>
    private object? ParseSequenceExpression()
    {
        var left = ParseAssignment();

        // Handle comma operator (sequence expressions)
        // The comma operator has the lowest precedence
        while (Match(TokenType.Comma))
        {
            var right = ParseAssignment();
            left = S(Operator(","), left, right);
        }

        return left;
    }

    private object? ParseAssignment()
    {
        var expr = ParseTernary();

        // Check for arrow function: identifier => ...  or  (...) => ...
        if (Match(TokenType.Arrow))
        {
            return FinishArrowFunction(expr);
        }

        if (Match(TokenType.Equal, TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual,
                TokenType.StarStarEqual, TokenType.SlashEqual, TokenType.PercentEqual, TokenType.AmpEqual,
                TokenType.PipeEqual,
                TokenType.CaretEqual, TokenType.LessLessEqual, TokenType.GreaterGreaterEqual,
                TokenType.GreaterGreaterGreaterEqual, TokenType.AmpAmpEqual, TokenType.PipePipeEqual,
                TokenType.QuestionQuestionEqual))
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
                    TokenType.StarStarEqual => "**",
                    TokenType.SlashEqual => "/",
                    TokenType.PercentEqual => "%",
                    TokenType.AmpEqual => "&",
                    TokenType.PipeEqual => "|",
                    TokenType.CaretEqual => "^",
                    TokenType.LessLessEqual => "<<",
                    TokenType.GreaterGreaterEqual => ">>",
                    TokenType.GreaterGreaterGreaterEqual => ">>>",
                    TokenType.AmpAmpEqual => "&&",
                    TokenType.PipePipeEqual => "||",
                    TokenType.QuestionQuestionEqual => "??",
                    _ => throw new InvalidOperationException("Unexpected compound assignment operator.")
                };

                value = S(Operator(binaryOp), expr, value);
            }

            if (expr is Symbol symbol)
            {
                return S(Assign, symbol, value);
            }

            if (expr is Cons { Head: Symbol head } assignmentTarget && ReferenceEquals(head, GetProperty))
            {
                var target = assignmentTarget.Rest.Head;
                var propertyName = assignmentTarget.Rest.Rest.Head;
                return S(SetProperty, target, propertyName, value);
            }

            if (expr is Cons { Head: Symbol indexHead } indexTarget && ReferenceEquals(indexHead, GetIndex))
            {
                var target = indexTarget.Rest.Head;
                var index = indexTarget.Rest.Rest.Head;
                return S(SetIndex, target, index, value);
            }

            // Check if this is an array literal that should be treated as a destructuring pattern
            if (expr is Cons { Head: Symbol arrayHead } arrayLiteral && ReferenceEquals(arrayHead, ArrayLiteral))
            {
                var pattern = ConvertArrayLiteralToPattern(arrayLiteral);
                return S(DestructuringAssignment, pattern, value);
            }

            // Check if this is an object literal that should be treated as a destructuring pattern
            if (expr is Cons { Head: Symbol objectHead } objectLiteral && ReferenceEquals(objectHead, ObjectLiteral))
            {
                var pattern = ConvertObjectLiteralToPattern(objectLiteral);
                return S(DestructuringAssignment, pattern, value);
            }

            throw new ParseException($"Invalid assignment target near line {op.Line} column {op.Column}.", op, _source);
        }

        return expr;
    }

    private object FinishArrowFunction(object? paramExpr)
    {
        // Convert the parameter expression to a parameter list
        Cons parameters;

        if (paramExpr is Cons empty && ReferenceEquals(empty, Cons.Empty))
        {
            // Empty parameter list: () => ...
            parameters = Cons.Empty;
        }
        else if (paramExpr is Symbol singleParam)
        {
            // Single parameter without parentheses: x => ...
            parameters = S(singleParam);
        }
        else if (paramExpr is Cons paramList)
        {
            // Multiple parameters or single with parentheses: (x, y) => ... or (x) => ...
            // The paramExpr could be a sequence from comma operator or a single expression
            // We need to extract identifiers from the expression
            parameters = ConvertArrowParametersToList(paramList);
        }
        else
        {
            // This shouldn't happen, but default to empty
            parameters = Cons.Empty;
        }

        // Parse the body
        object body;
        if (Check(TokenType.LeftBrace))
        {
            // Block body: () => { ... }
            body = ParseBlock();
        }
        else
        {
            // Expression body: () => expr
            // Wrap in a return statement
            var expr = ParseAssignment();
            body = S(Block, S(Return, expr));
        }

        return S(Lambda, null, parameters, body);
    }

    private static Cons ConvertArrowParametersToList(Cons expr)
    {
        // If expr is already a list of symbols (from parsing (a, b, c)), use it directly
        // Check if all elements are symbols
        var allSymbols = true;
        var current = expr;
        var symbols = new List<object?>();

        while (current != null && !ReferenceEquals(current, Cons.Empty))
        {
            if (current.Head is Symbol sym)
            {
                symbols.Add(sym);
                current = current.Rest as Cons;
            }
            else
            {
                allSymbols = false;
                break;
            }
        }

        if (allSymbols && symbols.Count > 0)
        {
            // Already a proper parameter list
            return Cons.FromEnumerable(symbols);
        }

        // Otherwise, try to extract parameters using the old logic
        var parameters = new List<object?>();
        CollectParameters(expr, parameters);
        return Cons.FromEnumerable(parameters);
    }

    private static void CollectParameters(object? expr, List<object?> parameters)
    {
        if (expr is Symbol sym)
        {
            parameters.Add(sym);
        }
        else if (expr is Cons { Head: Symbol head } cons)
        {
            // Check if this is a comma operator (sequence)
            if (ReferenceEquals(head, Operator(",")))
            {
                // Recursively collect parameters from left and right
                CollectParameters(cons.Rest.Head, parameters);
                CollectParameters(cons.Rest.Rest.Head, parameters);
            }
            else
            {
                // Single symbol wrapped in some structure, extract it
                parameters.Add(expr);
            }
        }
        else
        {
            // For any other expression, treat it as a single parameter
            parameters.Add(expr);
        }
    }

    private object? ParseTernary()
    {
        var expr = ParseLogicalOr();

        if (Match(TokenType.Question))
        {
            var thenBranch = ParseExpression();
            Consume(TokenType.Colon, "Expected ':' after then branch of ternary expression.");
            // Both branches of ternary should be AssignmentExpression per ECMAScript spec
            // This allows assignments in both branches and maintains right-associativity for nested ternaries
            var elseBranch = ParseAssignment();
            return S(Ternary, expr, thenBranch, elseBranch);
        }

        return expr;
    }

    private object? ParseLogicalOr()
    {
        var startToken = _tokens[_current];
        var expr = ParseLogicalAnd();

        while (Match(TokenType.PipePipe))
        {
            var right = ParseLogicalAnd();
            expr = S(
                Operator("||"),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseLogicalAnd()
    {
        var startToken = _tokens[_current];
        var expr = ParseNullishCoalescing();

        while (Match(TokenType.AmpAmp))
        {
            var right = ParseNullishCoalescing();
            expr = S(
                Operator("&&"),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseNullishCoalescing()
    {
        var startToken = _tokens[_current];
        var expr = ParseBitwiseOr();

        while (Match(TokenType.QuestionQuestion))
        {
            var right = ParseBitwiseOr();
            expr = S(
                Operator("??"),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseBitwiseOr()
    {
        var startToken = _tokens[_current];
        var expr = ParseBitwiseXor();

        while (Match(TokenType.Pipe))
        {
            var right = ParseBitwiseXor();
            expr = S(
                Operator("|"),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseBitwiseXor()
    {
        var startToken = _tokens[_current];
        var expr = ParseBitwiseAnd();

        while (Match(TokenType.Caret))
        {
            var right = ParseBitwiseAnd();
            expr = S(
                Operator("^"),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseBitwiseAnd()
    {
        var startToken = _tokens[_current];
        var expr = ParseEquality();

        while (Match(TokenType.Amp))
        {
            var right = ParseEquality();
            expr = S(
                Operator("&"),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseEquality()
    {
        var startToken = _tokens[_current];
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

            expr = S(
                Operator(op),
                expr,
                right
            );
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseComparison()
    {
        var startToken = _tokens[_current];
        var expr = ParseShift();
        while (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual,
                     TokenType.In))
        {
            var op = Previous();
            var right = ParseShift();
            var symbol = op.Type switch
            {
                TokenType.Greater => Operator(">"),
                TokenType.GreaterEqual => Operator(">="),
                TokenType.Less => Operator("<"),
                TokenType.LessEqual => Operator("<="),
                TokenType.In => Operator("in"),
                _ => throw new InvalidOperationException("Unexpected comparison operator.")
            };

            expr = S(symbol, expr, right);
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseShift()
    {
        var startToken = _tokens[_current];
        var expr = ParseTerm();
        while (Match(TokenType.LessLess, TokenType.GreaterGreater, TokenType.GreaterGreaterGreater))
        {
            var op = Previous();
            var right = ParseTerm();
            var symbol = op.Type switch
            {
                TokenType.LessLess => Operator("<<"),
                TokenType.GreaterGreater => Operator(">>"),
                TokenType.GreaterGreaterGreater => Operator(">>>"),
                _ => throw new InvalidOperationException("Unexpected shift operator.")
            };

            expr = S(symbol, expr, right);
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseTerm()
    {
        var startToken = _tokens[_current];
        var expr = ParseFactor();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous();
            var right = ParseFactor();
            var symbol = Operator(op.Type == TokenType.Plus ? "+" : "-");
            expr = S(symbol, expr, right);
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseFactor()
    {
        var startToken = _tokens[_current];
        var expr = ParseExponentiation();
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var op = Previous();
            var right = ParseExponentiation();
            var symbol = op.Type switch
            {
                TokenType.Star => Operator("*"),
                TokenType.Slash => Operator("/"),
                TokenType.Percent => Operator("%"),
                _ => throw new InvalidOperationException("Unexpected factor operator.")
            };
            expr = S(symbol, expr, right);
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseExponentiation()
    {
        var startToken = _tokens[_current];
        var expr = ParseUnary();

        // Exponentiation is right-associative in JavaScript
        if (Match(TokenType.StarStar))
        {
            var right = ParseExponentiation(); // Right-associative recursion
            expr = S(Operator("**"), expr, right);
            // Set SourceReference for the binary operation
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(_source, startToken.StartPosition, endToken.EndPosition,
                startToken.Line, startToken.Column, endToken.Line, endToken.Column);
            if (expr is Cons cons)
            {
                cons.WithSourceReference(sourceRef);
            }
        }

        return expr;
    }

    private object? ParseUnary()
    {
        if (Match(TokenType.Bang))
        {
            return S(Not, ParseUnary());
        }

        if (Match(TokenType.Minus))
        {
            return S(Negate, ParseUnary());
        }

        if (Match(TokenType.Tilde))
        {
            return S(Operator("~"), ParseUnary());
        }

        if (Match(TokenType.Typeof))
        {
            return S(Typeof, ParseUnary());
        }

        if (Match(TokenType.Void))
        {
            return S(JsSymbols.Void, ParseUnary());
        }

        if (Match(TokenType.Delete))
        {
            return S(Delete, ParseUnary());
        }

        if (Match(TokenType.PlusPlus))
        {
            var operand = ParseUnary();
            return S(Operator("++prefix"), operand);
        }

        if (Match(TokenType.MinusMinus))
        {
            var operand = ParseUnary();
            return S(Operator("--prefix"), operand);
        }

        if (Match(TokenType.Yield))
        {
            // yield can be followed by an expression or nothing
            // We'll parse an assignment expression (one level below expression)
            var value = ParseAssignment();
            return S(Yield, value);
        }

        if (Match(TokenType.Await))
        {
            // await must be followed by an expression
            var value = ParseUnary();
            return S(Await, value);
        }

        return ParsePostfix();
    }

    private object? ParsePostfix()
    {
        var expr = ParseCall();

        if (Match(TokenType.PlusPlus))
        {
            return S(Operator("++postfix"), expr);
        }

        if (Match(TokenType.MinusMinus))
        {
            return S(Operator("--postfix"), expr);
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
                    var items = new List<object?> { OptionalCall, expr };
                    items.AddRange(arguments);
                    expr = Cons.FromEnumerable(items);
                }
                else if (Match(TokenType.LeftBracket))
                {
                    // obj?.[index]
                    var indexExpression = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                    expr = S(OptionalGetIndex, expr, indexExpression);
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

            // Tagged template literals
            if (Check(TokenType.TemplateLiteral))
            {
                Advance(); // Consume the template literal token
                expr = FinishTaggedTemplate(expr);
                continue;
            }

            break;
        }

        return expr;
    }

    private object FinishCall(object? callee)
    {
        var arguments = ParseArgumentList();
        var items = new List<object?> { Call, callee };
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
                    arguments.Add(S(Spread, expr));
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
            return Undefined;
        }

        if (Match(TokenType.Number))
        {
            return Previous().Literal is double number ? number : 0d;
        }

        if (Match(TokenType.BigInt))
        {
            return Previous().Literal;
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

        if (Match(TokenType.Import))
        {
            // Dynamic import: import(specifier)
            // This is different from the static import statement
            if (Check(TokenType.LeftParen))
                // Return a symbol that will be handled as a callable
            {
                return Symbol.Intern("import");
            }

            throw new ParseException(
                "'import' can only be used as dynamic import with parentheses: import(specifier)", Peek(), _source);
        }

        if (Match(TokenType.Identifier))
        {
            return Symbol.Intern(Previous().Lexeme);
        }

        // In JavaScript, 'get' and 'set' are contextual keywords that can be used as identifiers
        // in most contexts (not just in class methods or object literals)
        if (Match(TokenType.Get, TokenType.Set))
        {
            return Symbol.Intern(Previous().Lexeme);
        }

        if (Match(TokenType.This))
        {
            return This;
        }

        if (Match(TokenType.Super))
        {
            return Super;
        }

        if (Match(TokenType.Async))
        {
            if (Match(TokenType.Function))
            {
                return ParseAsyncFunctionExpression();
            }

            throw new ParseException("Expected 'function' after 'async' in expression context.", Peek(), _source);
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
            // This could be a grouped expression or arrow function parameters
            var startPos = _current;

            // Try to parse as arrow function parameters first
            var arrowParams = TryParseArrowParameters();
            if (arrowParams != null)
            {
                return arrowParams;
            }

            // Not arrow parameters, parse as normal grouped expression
            // Inside parentheses, we need to handle the comma operator (sequence expressions)
            _current = startPos;
            var expr = ParseSequenceExpression();
            Consume(TokenType.RightParen, "Expected ')' after expression.");
            return expr;
        }

        throw new ParseException($"Unexpected token {Peek().Type} at line {Peek().Line} column {Peek().Column}.", Peek(), _source);
    }

    private object? TryParseArrowParameters()
    {
        // Try to parse () => or (id1, id2, ...) =>
        // Returns the parameter expression if successful, null otherwise

        if (Check(TokenType.RightParen))
        {
            Advance(); // consume ')'
            if (Check(TokenType.Arrow))
            {
                // Empty parameters: () =>
                return Cons.Empty;
            }
            // Not arrow function, fail
            return null;
        }

        if (!CheckParameterIdentifier())
        {
            // Not starting with identifier, can't be simple arrow params
            return null;
        }

        // Parse comma-separated identifiers
        var parameters = new List<object?>();
        parameters.Add(Symbol.Intern(Advance().Lexeme));

        while (Match(TokenType.Comma))
        {
            if (!CheckParameterIdentifier())
            {
                // Not all identifiers, not arrow params
                return null;
            }
            parameters.Add(Symbol.Intern(Advance().Lexeme));
        }

        if (!Check(TokenType.RightParen))
        {
            // No closing paren, not arrow params
            return null;
        }

        Advance(); // consume ')'

        if (!Check(TokenType.Arrow))
        {
            // No arrow, not arrow params
            return null;
        }

        // Success! Return the parameters
        if (parameters.Count == 1)
        {
            return parameters[0];
        }

        return Cons.FromEnumerable(parameters);
    }

    private object ParseNewExpression()
    {
        var constructor = ParsePrimary();

        while (Match(TokenType.Dot)) constructor = FinishGet(constructor);

        while (Match(TokenType.LeftBracket)) constructor = FinishIndex(constructor);

        var arguments = new List<object?>();
        if (Match(TokenType.LeftParen))
        {
            arguments = ParseArgumentList();
        }

        var items = new List<object?> { New, constructor };
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
        var functionType = isGenerator ? Generator : Lambda;
        return S(functionType, name, parameters, body);
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

        return S(Async, name, parameters, body);
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
                    properties.Add(S(Spread, expr));
                    continue;
                }

                // Check for getter/setter
                // We need to distinguish between:
                // - `get foo() {}` (getter)
                // - `get: 10` or `get: function() {}` (regular property named "get")
                if (Check(TokenType.Get) && !IsGetOrSetPropertyName())
                {
                    Advance(); // consume 'get'
                    var name = ParseObjectPropertyName();
                    Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                    Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                    var body = ParseBlock();
                    properties.Add(S(Getter, name, body));
                }
                else if (Check(TokenType.Set) && !IsGetOrSetPropertyName())
                {
                    Advance(); // consume 'set'
                    var name = ParseObjectPropertyName();
                    Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                    var paramToken = Consume(TokenType.Identifier, "Expected parameter name in setter.");
                    var param = Symbol.Intern(paramToken.Lexeme);
                    Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                    var body = ParseBlock();
                    properties.Add(S(Setter, name, param, body));
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
                        var lambda = S(Lambda, null, parameters, body);
                        properties.Add(S(Property, keyExpression, lambda));
                    }
                    else
                    {
                        Consume(TokenType.Colon, "Expected ':' after computed property name.");
                        var value = ParseExpression();
                        properties.Add(S(Property, keyExpression, value));
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
                        var lambda = S(Lambda, null, parameters, body);
                        properties.Add(S(Property, name, lambda));
                    }
                    // Check for property shorthand: { name } instead of { name: name }
                    else if (name is string nameStr && !Check(TokenType.Colon))
                    {
                        // Property shorthand: use the identifier as both key and value
                        var symbol = Symbol.Intern(nameStr);
                        properties.Add(S(Property, name, symbol));
                    }
                    else
                    {
                        Consume(TokenType.Colon, "Expected ':' after property name.");
                        var value = ParseExpression();
                        properties.Add(S(Property, name, value));
                    }
                }
            } while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));
        }

        Consume(TokenType.RightBrace, "Expected '}' after object literal.");
        var items = new List<object?> { ObjectLiteral };
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
                    elements.Add(S(Spread, expr));
                }
                else
                {
                    elements.Add(ParseExpression());
                }
            } while (Match(TokenType.Comma) && !Check(TokenType.RightBracket));
        }

        Consume(TokenType.RightBracket, "Expected ']' after array literal.");
        var items = new List<object?> { ArrayLiteral };
        items.AddRange(elements);
        return Cons.FromEnumerable(items);
    }

    private object ParseTemplateLiteralExpression()
    {
        var token = Previous();
        var parts = token.Literal as List<object> ?? [];

        // (template part1 expr1 part2 expr2 ...)
        var items = new List<object?> { TemplateLiteral };

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
                var wrappedSource = exprText + ";";
                var wrappedLexer = new Lexer(wrappedSource);
                var wrappedTokens = wrappedLexer.Tokenize();
                var exprParser = new Parser(wrappedTokens, wrappedSource);
                var exprProgram = exprParser.ParseProgram();

                // Extract the expression (skip the 'program' wrapper)
                if (exprProgram is Cons programCons && !programCons.IsEmpty)
                {
                    var firstStatement = programCons.Rest.Head;
                    // If it's an expression statement, unwrap it
                    if (firstStatement is Cons stmtCons &&
                        stmtCons.Head is Symbol sym &&
                        ReferenceEquals(sym, ExpressionStatement))
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

    private object FinishTaggedTemplate(object? tag)
    {
        // Parse the template literal that was already consumed
        var token = Previous();
        var parts = token.Literal as List<object> ?? [];

        // Build the template object with strings and raw strings
        var strings = new List<object?>();
        var rawStrings = new List<object?>();
        var expressions = new List<object?>();

        foreach (var part in parts)
        {
            if (part is string str)
            {
                strings.Add(str);
                rawStrings.Add(str); // For now, raw and cooked are the same
            }
            else if (part is TemplateExpression expr)
            {
                // Parse the expression
                var exprText = expr.ExpressionText.Trim();
                var wrappedSource = exprText + ";";
                var wrappedLexer = new Lexer(wrappedSource);
                var wrappedTokens = wrappedLexer.Tokenize();
                var exprParser = new Parser(wrappedTokens, wrappedSource);
                var exprProgram = exprParser.ParseProgram();

                if (exprProgram is Cons programCons && !programCons.IsEmpty)
                {
                    var firstStatement = programCons.Rest.Head;
                    if (firstStatement is Cons stmtCons &&
                        stmtCons.Head is Symbol sym &&
                        ReferenceEquals(sym, ExpressionStatement))
                    {
                        expressions.Add(stmtCons.Rest.Head);
                    }
                    else
                    {
                        expressions.Add(firstStatement);
                    }
                }
            }
        }

        // Make sure we have one more string than expressions
        // (template literals always start and end with a string part, even if empty)
        while (strings.Count <= expressions.Count)
        {
            strings.Add("");
            rawStrings.Add("");
        }

        // Create a tagged template call: (taggedTemplate tag strings rawStrings expr1 expr2 ...)
        var items = new List<object?> { TaggedTemplate, tag };

        // Add strings array
        var stringsArray = Cons.FromEnumerable(strings.Prepend(ArrayLiteral));
        items.Add(stringsArray);

        // Add raw strings array
        var rawStringsArray = Cons.FromEnumerable(rawStrings.Prepend(ArrayLiteral));
        items.Add(rawStringsArray);

        // Add expressions
        items.AddRange(expressions);

        return Cons.FromEnumerable(items);
    }

    private object ParseRegexLiteral()
    {
        var token = Previous();
        var regexValue = token.Literal as RegexLiteralValue;
        if (regexValue == null)
        {
            throw new ParseException("Invalid regex literal.", Peek(), _source);
        }

        // Create a new RegExp(...) expression
        // (new RegExp pattern flags)
        var pattern = regexValue.Pattern;
        var flags = regexValue.Flags;

        var items = new List<object?>
        {
            New,
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

        // Support numeric keys in object literals - JavaScript coerces numbers to strings
        if (Match(TokenType.Number))
        {
            var numToken = Previous();
            // Convert the numeric literal to a string representation
            // Use the literal value if available, otherwise use the lexeme
            if (numToken.Literal != null)
            {
                return numToken.Literal switch
                {
                    int i => i.ToString(CultureInfo.InvariantCulture),
                    long l => l.ToString(CultureInfo.InvariantCulture),
                    double d => d.ToString(CultureInfo.InvariantCulture),
                    _ => numToken.Lexeme
                };
            }
            return numToken.Lexeme;
        }

        // Allow keywords as property names in object literals
        if (IsKeyword(Peek()))
        {
            var keywordToken = Advance();
            return keywordToken.Lexeme;
        }

        var identifier = Consume(TokenType.Identifier, "Expected property name.");
        return identifier.Lexeme;
    }

    private object FinishGet(object? target)
    {
        // Check for private field access
        if (Check(TokenType.PrivateIdentifier))
        {
            var privateToken = Advance();
            var fieldName = privateToken.Lexeme; // Includes the '#'
            return S(GetProperty, target, fieldName);
        }

        // Allow identifiers or keywords as property names (e.g., object.of, object.in, object.for)
        if (!Check(TokenType.Identifier) && !IsKeyword(Peek()))
        {
            throw new ParseException("Expected property name after '.'.", Peek(), _source);
        }

        var nameToken = Advance();
        var propertyName = nameToken.Lexeme;
        return S(GetProperty, target, propertyName);
    }

    private object FinishOptionalGet(object? target)
    {
        // Allow identifiers or keywords as property names
        if (!Check(TokenType.Identifier) && !IsKeyword(Peek()))
        {
            throw new ParseException("Expected property name after '?.'.", Peek(), _source);
        }

        var nameToken = Advance();
        var propertyName = nameToken.Lexeme;
        return S(OptionalGetProperty, target, propertyName);
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

    /// <summary>
    /// Determines if 'get' or 'set' should be treated as a property name rather than a getter/setter.
    /// Returns true if it's followed by a colon (indicating a regular property like `get: value`).
    /// </summary>
    private bool IsGetOrSetPropertyName()
    {
        // If the next token is a colon, then 'get' or 'set' is a property name
        return PeekNext().Type == TokenType.Colon;
    }

    /// <summary>
    /// Checks if the current token can be used as an identifier in a parameter context.
    /// In JavaScript, 'get' and 'set' are contextual keywords that can be used as parameter names.
    /// </summary>
    private bool CheckParameterIdentifier()
    {
        return Check(TokenType.Identifier) || Check(TokenType.Get) || Check(TokenType.Set);
    }

    /// <summary>
    /// Consumes a token that can be used as an identifier in a parameter context.
    /// Returns the token so its lexeme can be used as the parameter name.
    /// </summary>
    private Token ConsumeParameterIdentifier(string errorMessage)
    {
        if (Check(TokenType.Identifier) || Check(TokenType.Get) || Check(TokenType.Set))
        {
            return Advance();
        }
        throw new ParseException(errorMessage, Peek(), _source);
    }

    private object FinishIndex(object? target)
    {
        var indexExpression = ParseExpression();
        Consume(TokenType.RightBracket, "Expected ']' after index expression.");
        return S(GetIndex, target, indexExpression);
    }

    private static Cons CreateDefaultConstructor(Symbol name)
    {
        var body = S(Block);
        return S(Lambda, name, Cons.Empty, body);
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

    private Token Peek()
    {
        return _tokens[_current];
    }

    private Token PeekNext()
    {
        return _current + 1 < _tokens.Count ? _tokens[_current + 1] : _tokens[_current];
    }

    private Token Previous()
    {
        return _tokens[_current - 1];
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }

        // Apply Automatic Semicolon Insertion (ASI) for semicolons
        if (type == TokenType.Semicolon && CanInsertSemicolon())
        {
            // Return a synthetic semicolon token without advancing
            return new Token(TokenType.Semicolon, ";", null, Peek().Line, Peek().Column, Peek().StartPosition, Peek().StartPosition);
        }

        var currentToken = Peek();
        throw new ParseException(message, currentToken, _source);
    }

    /// <summary>
    /// Determines if a semicolon can be automatically inserted according to ECMAScript ASI rules.
    /// </summary>
    private bool CanInsertSemicolon()
    {
        // Rule 1: Offending token is separated from previous token by at least one line terminator
        if (_current > 0 && HasLineTerminatorBefore())
        {
            return true;
        }

        // Rule 2: Offending token is }
        if (Check(TokenType.RightBrace))
        {
            return true;
        }

        // Rule 3: End of input
        if (Check(TokenType.Eof))
        {
            return true;
        }

        // Special case: Previous token is ) and this would be the terminating semicolon of a do-while
        // This is handled separately in ParseDoWhileStatement

        return false;
    }

    /// <summary>
    /// Checks if there is a line terminator between the previous token and the current token.
    /// </summary>
    private bool HasLineTerminatorBefore()
    {
        if (_current <= 0 || _current >= _tokens.Count)
        {
            return false;
        }

        var previousToken = _tokens[_current - 1];
        var currentToken = _tokens[_current];

        // Check if current token is on a different line than the previous token
        return currentToken.Line > previousToken.Line;
    }

    private Cons ConvertArrayLiteralToPattern(Cons arrayLiteral)
    {
        var elements = new List<object?> { ArrayPattern };

        foreach (var item in arrayLiteral.Rest)
        {
            if (item is null)
            {
                elements.Add(null); // hole
            }
            else if (item is Cons { Head: Symbol spreadHead } spreadCons && ReferenceEquals(spreadHead, Spread))
            {
                // Spread becomes rest pattern
                var restTarget = spreadCons.Rest.Head;
                if (restTarget is not Symbol restSymbol)
                {
                    throw new ParseException("Rest element must be an identifier", Peek(), _source);
                }

                elements.Add(S(PatternRest, restSymbol));
                break; // Rest must be last
            }
            else if (item is Symbol symbol)
            {
                // Simple identifier
                elements.Add(S(PatternElement, symbol, null));
            }
            else if (item is Cons { Head: Symbol itemHead } itemCons)
            {
                // Check for nested patterns
                if (ReferenceEquals(itemHead, ArrayLiteral))
                {
                    var nestedPattern = ConvertArrayLiteralToPattern(itemCons);
                    elements.Add(S(PatternElement, nestedPattern, null));
                }
                else if (ReferenceEquals(itemHead, ObjectLiteral))
                {
                    var nestedPattern = ConvertObjectLiteralToPattern(itemCons);
                    elements.Add(S(PatternElement, nestedPattern, null));
                }
                else
                {
                    throw new ParseException("Invalid destructuring pattern", Peek(), _source);
                }
            }
            else
            {
                throw new ParseException("Invalid destructuring pattern", Peek(), _source);
            }
        }

        return Cons.FromEnumerable(elements);
    }

    private Cons ConvertObjectLiteralToPattern(Cons objectLiteral)
    {
        var properties = new List<object?> { ObjectPattern };

        foreach (var prop in objectLiteral.Rest)
        {
            if (prop is not Cons { Head: Symbol propHead } propCons)
            {
                throw new ParseException("Invalid object destructuring pattern", Peek(), _source);
            }

            if (ReferenceEquals(propHead, Spread))
            {
                // Spread becomes rest property
                var restTarget = propCons.Rest.Head;
                if (restTarget is not Symbol restSymbol)
                {
                    throw new ParseException("Rest property must be an identifier", Peek(), _source);
                }

                properties.Add(S(PatternRest, restSymbol));
                break; // Rest must be last
            }

            if (ReferenceEquals(propHead, Property))
            {
                var key = propCons.Rest.Head as string;
                var value = propCons.Rest.Rest.Head;

                if (key is null)
                {
                    throw new ParseException("Property key must be a string", Peek(), _source);
                }

                if (value is Symbol targetSymbol)
                {
                    // Simple property: {x} or {x: y}
                    properties.Add(S(PatternProperty, key, targetSymbol, null));
                }
                else if (value is Cons { Head: Symbol valueHead } valueCons)
                {
                    // Nested pattern: {x: [a, b]}
                    if (ReferenceEquals(valueHead, ArrayLiteral))
                    {
                        var nestedPattern = ConvertArrayLiteralToPattern(valueCons);
                        properties.Add(S(PatternProperty, key, nestedPattern, null));
                    }
                    else if (ReferenceEquals(valueHead, ObjectLiteral))
                    {
                        var nestedPattern = ConvertObjectLiteralToPattern(valueCons);
                        properties.Add(S(PatternProperty, key, nestedPattern, null));
                    }
                    else
                    {
                        throw new ParseException("Invalid nested destructuring pattern", Peek(), _source);
                    }
                }
                else
                {
                    throw new ParseException("Invalid destructuring pattern value", Peek(), _source);
                }
            }
            else
            {
                throw new ParseException("Invalid object destructuring pattern", Peek(), _source);
            }
        }

        return Cons.FromEnumerable(properties);
    }

    private object ParseImportDeclaration()
    {
        // import defaultExport from "module-name";
        // import * as name from "module-name";
        // import { export1 } from "module-name";
        // import { export1 as alias1 } from "module-name";
        // import { export1, export2 } from "module-name";
        // import defaultExport, { export1 } from "module-name";
        // import defaultExport, * as name from "module-name";
        // import "module-name"; // side-effects only

        if (Check(TokenType.String))
        {
            // Side-effect import: import "module-name";
            var moduleToken = Consume(TokenType.String, "Expected module path.");
            var modulePath = (string)moduleToken.Literal!;
            Consume(TokenType.Semicolon, "Expected ';' after import statement.");
            return S(Import, modulePath);
        }

        object? defaultImport = null;
        Cons? namedImports = null;
        object? namespaceImport = null;

        // Check for default import or star import
        if (Check(TokenType.Identifier) && !CheckContextualKeyword("from"))
        {
            var nameToken = Consume(TokenType.Identifier, "Expected identifier.");
            defaultImport = Symbol.Intern(nameToken.Lexeme);

            // Check if there's a comma after the default import
            if (Match(TokenType.Comma))
            {
                if (Match(TokenType.Star))
                {
                    // import defaultExport, * as name from "module"
                    ConsumeContextualKeyword("as", "Expected 'as' after '*'.");
                    var nsNameToken = Consume(TokenType.Identifier, "Expected identifier after 'as'.");
                    namespaceImport = Symbol.Intern(nsNameToken.Lexeme);
                }
                else if (Match(TokenType.LeftBrace))
                {
                    // import defaultExport, { export1 } from "module"
                    namedImports = ParseNamedImports();
                    Consume(TokenType.RightBrace, "Expected '}' after named imports.");
                }
            }
        }
        else if (Match(TokenType.Star))
        {
            // Namespace import: import * as name from "module"
            ConsumeContextualKeyword("as", "Expected 'as' after '*'.");
            var nameToken = Consume(TokenType.Identifier, "Expected identifier after 'as'.");
            namespaceImport = Symbol.Intern(nameToken.Lexeme);
        }
        else if (Match(TokenType.LeftBrace))
        {
            // Named imports: import { x, y as z } from "module"
            namedImports = ParseNamedImports();
            Consume(TokenType.RightBrace, "Expected '}' after named imports.");
        }

        ConsumeContextualKeyword("from", "Expected 'from' in import statement.");
        var modulePathToken = Consume(TokenType.String, "Expected module path.");
        var path = (string)modulePathToken.Literal!;
        Consume(TokenType.Semicolon, "Expected ';' after import statement.");

        // Build import S-expression
        // (import module-path default-import namespace-import named-imports)
        return S(Import, path, defaultImport, namespaceImport, namedImports);
    }

    private Cons ParseNamedImports()
    {
        var imports = new List<object?>();

        do
        {
            var importedToken = Consume(TokenType.Identifier, "Expected identifier in import list.");
            var imported = Symbol.Intern(importedToken.Lexeme);
            Symbol local;

            if (MatchContextualKeyword("as"))
            {
                var localToken = Consume(TokenType.Identifier, "Expected identifier after 'as'.");
                local = Symbol.Intern(localToken.Lexeme);
            }
            else
            {
                local = imported;
            }

            // (import-named imported local)
            imports.Add(S(ImportNamed, imported, local));
        } while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));

        return Cons.FromEnumerable(imports);
    }

    private object ParseExportDeclaration()
    {
        // export { name1, name2 };
        // export { name1 as exportedName1, name2 };
        // export let name1, name2;
        // export function functionName() { }
        // export class ClassName { }
        // export default expression;
        // export default function() { }
        // export default class { }

        if (Match(TokenType.Default))
        {
            // export default ...
            object expression;

            if (Match(TokenType.Function))
            {
                // export default function name() {} or export default function() {}
                var isGenerator = Match(TokenType.Star);
                Symbol? name = null;

                if (Check(TokenType.Identifier))
                {
                    var nameToken = Consume(TokenType.Identifier, "Expected function name.");
                    name = Symbol.Intern(nameToken.Lexeme);
                }

                Consume(TokenType.LeftParen, "Expected '(' after function.");
                var parameters = ParseParameterList();
                Consume(TokenType.RightParen, "Expected ')' after function parameters.");
                var body = ParseBlock();

                var functionType = isGenerator ? Generator : Lambda;
                expression = S(functionType, name, parameters, body);
            }
            else if (Match(TokenType.Class))
            {
                // export default class Name {} or export default class {}
                Symbol? name = null;

                if (Check(TokenType.Identifier))
                {
                    var nameToken = Consume(TokenType.Identifier, "Expected class name.");
                    name = Symbol.Intern(nameToken.Lexeme);
                }

                Cons? extendsClause = null;
                if (Match(TokenType.Extends))
                {
                    var baseExpression = ParseExpression();
                    extendsClause = S(Extends, baseExpression);
                }

                Consume(TokenType.LeftBrace, "Expected '{' after class name or extends clause.");

                var (constructor, methods, privateFields) = ParseClassBody();

                // If no constructor was defined, create a default one
                constructor ??= CreateDefaultConstructor(name);
                var methodList = Cons.FromEnumerable(methods);
                var privateFieldList = Cons.FromEnumerable(privateFields);

                expression = S(Class, name, extendsClause, constructor, methodList, privateFieldList);
            }
            else
            {
                // export default expression;
                expression = ParseExpression();
                Consume(TokenType.Semicolon, "Expected ';' after export default expression.");
            }

            return S(ExportDefault, expression);
        }

        if (Match(TokenType.LeftBrace))
        {
            // export { name1, name2 as exported };
            var exports = ParseNamedExports();
            Consume(TokenType.RightBrace, "Expected '}' after export list.");

            // Optional: export { ... } from "module";
            string? fromModule = null;
            if (MatchContextualKeyword("from"))
            {
                var moduleToken = Consume(TokenType.String, "Expected module path.");
                fromModule = (string)moduleToken.Literal!;
            }

            Consume(TokenType.Semicolon, "Expected ';' after export statement.");
            return S(ExportNamed, exports, fromModule);
        }

        // Export declaration: export let/const/var/function/class
        if (Check(TokenType.Let) || Check(TokenType.Const) || Check(TokenType.Var) ||
            Check(TokenType.Function) || Check(TokenType.Class) || Check(TokenType.Async))
        {
            var declaration = ParseDeclaration();
            return S(ExportDeclaration, declaration);
        }

        throw new ParseException("Invalid export statement.", Peek(), _source);
    }

    private Cons ParseNamedExports()
    {
        var exports = new List<object?>();

        do
        {
            var localToken = Consume(TokenType.Identifier, "Expected identifier in export list.");
            var local = Symbol.Intern(localToken.Lexeme);
            Symbol exported;

            if (MatchContextualKeyword("as"))
            {
                var exportedToken = Consume(TokenType.Identifier, "Expected identifier after 'as'.");
                exported = Symbol.Intern(exportedToken.Lexeme);
            }
            else
            {
                exported = local;
            }

            // (export-named local exported)
            exports.Add(S(ExportNamed, local, exported));
        } while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));

        return Cons.FromEnumerable(exports);
    }

    private (Cons? constructor, List<object?> methods, List<object?> privateFields) ParseClassBody()
    {
        Cons? constructor = null;
        var methods = new List<object?>();
        var privateFields = new List<object?>();
        var publicFields = new List<object?>();
        var staticFields = new List<object?>(); // Track static fields separately

        while (!Check(TokenType.RightBrace))
        {
            // Check for static keyword
            var isStatic = false;
            if (Match(TokenType.Static))
            {
                isStatic = true;
            }

            // Check for private field declaration
            if (Check(TokenType.PrivateIdentifier))
            {
                var fieldToken = Advance();
                var fieldName = fieldToken.Lexeme; // Includes the '#'

                object? initializer = null;
                if (Match(TokenType.Equal))
                {
                    initializer = ParseExpression();
                }

                Match(TokenType.Semicolon); // optional semicolon

                if (isStatic)
                    // Static private fields - add to static fields list with a special marker
                {
                    staticFields.Add(S(StaticField, fieldName, initializer));
                }
                else
                {
                    privateFields.Add(S(PrivateField, fieldName, initializer));
                }
            }
            // Check for getter/setter in class
            else if (Check(TokenType.Get) || Check(TokenType.Set))
            {
                var isGetter = Match(TokenType.Get);
                if (!isGetter)
                {
                    Match(TokenType.Set); // Must be setter
                }

                var methodNameToken = Consume(TokenType.Identifier,
                    isGetter ? "Expected getter name in class body." : "Expected setter name in class body.");
                var methodName = methodNameToken.Lexeme;

                if (isGetter)
                {
                    Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                    Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                    var body = ParseBlock();

                    if (isStatic)
                    {
                        methods.Add(S(StaticGetter, methodName, body));
                    }
                    else
                    {
                        methods.Add(S(Getter, methodName, body));
                    }
                }
                else
                {
                    Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                    var paramToken = Consume(TokenType.Identifier, "Expected parameter name in setter.");
                    var param = Symbol.Intern(paramToken.Lexeme);
                    Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                    var body = ParseBlock();

                    if (isStatic)
                    {
                        methods.Add(S(StaticSetter, methodName, param, body));
                    }
                    else
                    {
                        methods.Add(S(Setter, methodName, param, body));
                    }
                }
            }
            else if (Check(TokenType.Identifier))
            {
                var methodNameToken = Consume(TokenType.Identifier, "Expected method name in class body.");
                var methodName = methodNameToken.Lexeme;

                // Check if this is a field declaration (public or static)
                if (Match(TokenType.Equal))
                {
                    var initializer = ParseExpression();
                    Match(TokenType.Semicolon); // optional semicolon

                    if (isStatic)
                        // Static public field
                    {
                        staticFields.Add(S(StaticField, methodName, initializer));
                    }
                    else
                        // Public instance field
                    {
                        publicFields.Add(S(PublicField, methodName, initializer));
                    }

                    continue;
                }

                // Check for constructor - cannot be static
                if (string.Equals(methodName, "constructor", StringComparison.Ordinal))
                {
                    if (isStatic)
                    {
                        throw new ParseException("Constructor cannot be static.", Peek(), _source);
                    }

                    if (constructor is not null)
                    {
                        throw new ParseException("Class cannot declare multiple constructors.", Peek(), _source);
                    }

                    Consume(TokenType.LeftParen, "Expected '(' after constructor name.");
                    var parameters = ParseParameterList();
                    Consume(TokenType.RightParen, "Expected ')' after constructor parameters.");
                    var body = ParseBlock();
                    constructor = S(Lambda, null, parameters, body);
                }
                else
                {
                    // Regular method
                    Consume(TokenType.LeftParen, "Expected '(' after method name.");
                    var parameters = ParseParameterList();
                    Consume(TokenType.RightParen, "Expected ')' after method parameters.");
                    var body = ParseBlock();

                    var lambda = S(Lambda, null, parameters, body);
                    if (isStatic)
                    {
                        methods.Add(S(StaticMethod, methodName, lambda));
                    }
                    else
                    {
                        methods.Add(S(Method, methodName, lambda));
                    }
                }
            }
            else
            {
                throw new ParseException("Expected method, field, getter, or setter in class body.", Peek(), _source);
            }
        }

        Consume(TokenType.RightBrace, "Expected '}' after class body.");

        // Merge all fields into private fields list (will handle separately in evaluator)
        privateFields.AddRange(publicFields);
        privateFields.AddRange(staticFields);

        return (constructor, methods, privateFields);
    }

    private bool CheckAhead(TokenType type)
    {
        if (_current + 1 >= _tokens.Count)
        {
            return false;
        }

        return _tokens[_current + 1].Type == type;
    }

    // Helper methods for contextual keywords
    private bool CheckContextualKeyword(string keyword)
    {
        return Check(TokenType.Identifier) && Peek().Lexeme == keyword;
    }

    private bool MatchContextualKeyword(string keyword)
    {
        if (CheckContextualKeyword(keyword))
        {
            Advance();
            return true;
        }

        return false;
    }

    private Token ConsumeContextualKeyword(string keyword, string errorMessage)
    {
        if (!CheckContextualKeyword(keyword))
        {
            var currentToken = Peek();
            throw new ParseException(errorMessage, currentToken, _source);
        }
        return Advance();
    }

    /// <summary>
    /// Checks if the next statement is a "use strict" directive and consumes it if found.
    /// A directive is a string literal expression statement that appears at the beginning of a program or function body.
    /// </summary>
    private bool CheckForUseStrictDirective()
    {
        // Save current position in case we need to backtrack
        var savedPosition = _current;

        // Check if next token is a string literal
        if (!Check(TokenType.String))
        {
            return false;
        }

        var stringToken = Advance();

        // Check if the string is "use strict"
        if (stringToken.Literal as string != "use strict")
        {
            // Not a "use strict" directive, backtrack
            _current = savedPosition;
            return false;
        }

        // Check if followed by a semicolon (optional in JavaScript)
        Match(TokenType.Semicolon);

        return true;
    }

    /// <summary>
    /// Creates a Cons from an enumerable with a source reference from the given start token to the current position.
    /// </summary>
    private Cons MakeCons(IEnumerable<object?> items, Token startToken)
    {
        var cons = Cons.FromEnumerable(items);
        if (_current > 0 && _current <= _tokens.Count)
        {
            var endToken = _tokens[_current - 1];
            var sourceRef = new SourceReference(
                _source,
                startToken.StartPosition,
                endToken.EndPosition,
                startToken.Line,
                startToken.Column,
                endToken.Line,
                endToken.Column
            );
            cons.WithSourceReference(sourceRef);
        }

        return cons;
    }

    /// <summary>
    /// Creates a Cons from an enumerable with a source reference from the current token to the current position.
    /// </summary>
    private Cons MakeCons(IEnumerable<object?> items)
    {
        if (_current > 0 && _current <= _tokens.Count)
        {
            return MakeCons(items, _tokens[_current - 1]);
        }

        return Cons.FromEnumerable(items);
    }
}
