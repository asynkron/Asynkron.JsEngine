using System.Collections.Immutable;
using System.Globalization;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Parser;

/// <summary>
/// Parser that builds the typed AST directly from the token stream.
/// </summary>
public sealed class TypedAstParser(IReadOnlyList<Token> tokens, string source)
{
    private readonly IReadOnlyList<Token> _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly string _source = source ?? string.Empty;

    public ProgramNode ParseProgram()
    {
        var direct = new DirectParser(_tokens, _source);
        return direct.ParseProgram();
    }

    /// <summary>
        /// Direct typed parser. This currently supports only a subset of the full
        /// JavaScript grammar required by the test suite.
        /// </summary>
    private sealed class DirectParser(IReadOnlyList<Token> tokens, string source)
    {
        private readonly IReadOnlyList<Token> _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        private readonly string _source = source ?? string.Empty;
        private int _current;
        private readonly Stack<FunctionContext> _functionContexts = new();

        private bool InGeneratorContext => _functionContexts.Count > 0 && _functionContexts.Peek().IsGenerator;
        private bool InAsyncContext => _functionContexts.Count > 0 && _functionContexts.Peek().IsAsync;

        // Controls whether the `in` token is treated as a relational operator inside
        // expressions. For `for (x in y)` we temporarily disable `in` as an operator
        // so the parser can recognize the for-in / for-of shape.
        private bool _allowInExpressions = true;

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

            if (Check(TokenType.Import))
            {
                if (PeekNext().Type == TokenType.LeftParen)
                {
                    return ParseExpressionStatement();
                }

                Advance();
                return ParseImportStatement();
            }

            if (Match(TokenType.Export))
            {
                return ParseExportStatement();
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

            if (Match(TokenType.Class))
            {
                return ParseClassDeclaration();
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

            if (Match(TokenType.Switch))
            {
                return ParseSwitchStatement();
            }

            if (Match(TokenType.Try))
            {
                return ParseTryStatement();
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

            if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon)
            {
                return ParseLabeledStatement();
            }

            return ParseExpressionStatement();
        }

        private StatementNode ParseFunctionDeclaration(bool isAsync, Token functionKeyword)
        {
            var isGenerator = Match(TokenType.Star);
            var nameToken = ConsumeBindingIdentifier("Expected function name.");
            var name = Symbol.Intern(nameToken.Lexeme);
            var function = ParseFunctionTail(name, functionKeyword, isAsync, isGenerator);
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
                var target = ParseBindingTarget("Expected variable name.");
                ExpressionNode? initializer = null;
                var requiresInitializer = target is not IdentifierBinding;

                if (Match(TokenType.Equal))
                {
                    initializer = ParseExpression(false);
                }
                else if (!allowInitializerless && (kind == VariableKind.Const || requiresInitializer))
                {
                    var message = requiresInitializer
                        ? "Destructuring declarations require an initializer."
                        : "Const declarations require an initializer.";
                    throw new ParseException(message, Peek(), _source);
                }

                declarators.Add(new VariableDeclarator(target.Source ?? CreateSourceReference(start), target,
                    initializer));
            } while (Match(TokenType.Comma));

            if (requireSemicolon)
            {
                Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
            }

            return new VariableDeclaration(CreateSourceReference(start), kind, declarators.ToImmutable());
        }

        private BindingTarget ParseBindingTarget(string errorMessage = "Expected binding target.")
        {
            if (Match(TokenType.LeftBracket))
            {
                return ParseArrayBindingPattern(Previous());
            }

            if (Match(TokenType.LeftBrace))
            {
                return ParseObjectBindingPattern(Previous());
            }

            var identifier = ConsumeBindingIdentifier(errorMessage);
            var symbol = Symbol.Intern(identifier.Lexeme);
            return new IdentifierBinding(CreateSourceReference(identifier), symbol);
        }

        private ArrayBinding ParseArrayBindingPattern(Token startToken)
        {
            var elements = ImmutableArray.CreateBuilder<ArrayBindingElement>();
            BindingTarget? restTarget = null;

            while (!Check(TokenType.RightBracket))
            {
                if (Match(TokenType.Comma))
                {
                    elements.Add(new ArrayBindingElement(null, null, null));
                    continue;
                }

                if (Match(TokenType.DotDotDot))
                {
                    if (restTarget is not null)
                    {
                        throw new ParseException("Only one rest element is allowed in a binding pattern.", Peek(), _source);
                    }

                    restTarget = ParseBindingTarget("Expected identifier after '...'.");
                    if (restTarget is not IdentifierBinding)
                    {
                        throw new ParseException("Rest element must be an identifier.", Peek(), _source);
                    }

                    break;
                }

                var elementTarget = ParseBindingTarget("Expected binding target in array pattern.");
                ExpressionNode? defaultValue = null;
                if (Match(TokenType.Equal))
                {
                    defaultValue = ParseAssignment();
                }

                elements.Add(new ArrayBindingElement(elementTarget.Source, elementTarget, defaultValue));
                if (!Match(TokenType.Comma))
                {
                    break;
                }
            }

            Consume(TokenType.RightBracket, "Expected ']' after array pattern.");
            return new ArrayBinding(CreateSourceReference(startToken), elements.ToImmutable(), restTarget);
        }

        private ObjectBinding ParseObjectBindingPattern(Token startToken)
        {
            var properties = ImmutableArray.CreateBuilder<ObjectBindingProperty>();
            BindingTarget? restTarget = null;

            while (!Check(TokenType.RightBrace))
            {
                if (Match(TokenType.DotDotDot))
                {
                    if (restTarget is not null)
                    {
                        throw new ParseException("Only one rest element is allowed in a binding pattern.", Peek(), _source);
                    }

                    restTarget = ParseBindingTarget("Expected identifier after '...'.");
                    if (restTarget is not IdentifierBinding)
                    {
                        throw new ParseException("Rest property must be an identifier.", Peek(), _source);
                    }

                    break;
                }

                var (name, canUseShorthand, nameToken) = ParseBindingPropertyName();
                var source = CreateSourceReference(nameToken);
                BindingTarget target;

                if (Match(TokenType.Colon))
                {
                    target = ParseBindingTarget("Expected binding target after ':'.");
                }
                else
                {
                    if (!canUseShorthand)
                    {
                        throw new ParseException("Property name cannot use shorthand in binding pattern.", nameToken,
                            _source);
                    }

                    var symbol = Symbol.Intern(name);
                    target = new IdentifierBinding(source, symbol);
                }

                ExpressionNode? defaultValue = null;
                if (Match(TokenType.Equal))
                {
                    defaultValue = ParseAssignment();
                }

                properties.Add(new ObjectBindingProperty(source, name, target, defaultValue));
                if (!Match(TokenType.Comma))
                {
                    break;
                }
            }

            Consume(TokenType.RightBrace, "Expected '}' after object pattern.");
            return new ObjectBinding(CreateSourceReference(startToken), properties.ToImmutable(), restTarget);
        }

        private (string Name, bool CanUseShorthand, Token Token) ParseBindingPropertyName()
        {
            if (Match(TokenType.String))
            {
                var token = Previous();
                var value = token.Literal?.ToString() ?? string.Empty;
                return (value, false, token);
            }

            if (Match(TokenType.Number))
            {
                var token = Previous();
                var value = Convert.ToString(token.Literal, CultureInfo.InvariantCulture) ?? token.Lexeme;
                return (value, false, token);
            }

            if (Check(TokenType.Identifier))
            {
                var identifier = Advance();
                return (identifier.Lexeme, true, identifier);
            }

            if (IsKeyword(Peek()))
            {
                var keyword = Advance();
                return (keyword.Lexeme, false, keyword);
            }

            throw new ParseException("Expected property name.", Peek(), _source);
        }

        private StatementNode ParseSwitchStatement()
        {
            var keyword = Previous();
            Consume(TokenType.LeftParen, "Expected '(' after 'switch'.");
            var discriminant = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after switch expression.");
            Consume(TokenType.LeftBrace, "Expected '{' to begin switch body.");

            var cases = ImmutableArray.CreateBuilder<SwitchCase>();
            var seenDefault = false;

            while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
            {
                if (Match(TokenType.Case))
                {
                    var caseToken = Previous();
                    var test = ParseExpression();
                    Consume(TokenType.Colon, "Expected ':' after case expression.");
                    var body = ParseSwitchClauseBody(caseToken);
                    cases.Add(new SwitchCase(CreateSourceReference(caseToken), test, body));
                    continue;
                }

                if (Match(TokenType.Default))
                {
                    if (seenDefault)
                    {
                        throw new ParseException("Switch statement can only contain one default clause.", Peek(), _source);
                    }

                    seenDefault = true;
                    var defaultToken = Previous();
                    Consume(TokenType.Colon, "Expected ':' after 'default'.");
                    var body = ParseSwitchClauseBody(defaultToken);
                    cases.Add(new SwitchCase(CreateSourceReference(defaultToken), null, body));
                    continue;
                }

                throw new ParseException("Unexpected token in switch body.", Peek(), _source);
            }

            Consume(TokenType.RightBrace, "Expected '}' after switch body.");
            return new SwitchStatement(CreateSourceReference(keyword), discriminant, cases.ToImmutable());
        }

        private BlockStatement ParseSwitchClauseBody(Token clauseToken)
        {
            var statements = ImmutableArray.CreateBuilder<StatementNode>();
            var isStrict = CheckForUseStrictDirective();

            while (!Check(TokenType.Case) && !Check(TokenType.Default) && !Check(TokenType.RightBrace) &&
                   !Check(TokenType.Eof))
            {
                statements.Add(ParseStatement());
            }

            return new BlockStatement(CreateSourceReference(clauseToken), statements.ToImmutable(), isStrict);
        }

        private StatementNode ParseTryStatement()
        {
            var keyword = Previous();
            var tryBlock = ParseBlock();
            CatchClause? catchClause = null;

            if (Match(TokenType.Catch))
            {
                var catchToken = Previous();
                Consume(TokenType.LeftParen, "Expected '(' after 'catch'.");
                var identifier = ConsumeBindingIdentifier("Expected identifier in catch clause.");
                var binding = Symbol.Intern(identifier.Lexeme);
                Consume(TokenType.RightParen, "Expected ')' after catch binding.");
                var catchBody = ParseBlock();
                catchClause = new CatchClause(CreateSourceReference(catchToken), binding, catchBody);
            }

            BlockStatement? finallyBlock = null;
            if (Match(TokenType.Finally))
            {
                finallyBlock = ParseBlock();
            }

            if (catchClause is null && finallyBlock is null)
            {
                throw new ParseException("Try statement requires at least a catch or finally clause.", Peek(), _source);
            }

            return new TryStatement(CreateSourceReference(keyword), tryBlock, catchClause, finallyBlock);
        }

        private StatementNode ParseLabeledStatement()
        {
            var labelToken = Advance();
            var label = Symbol.Intern(labelToken.Lexeme);
            Consume(TokenType.Colon, "Expected ':' after label.");
            var statement = ParseStatement();
            return new LabeledStatement(CreateSourceReference(labelToken), label, statement);
        }

        private StatementNode ParseClassDeclaration()
        {
            var classToken = Previous();
            var nameToken = ConsumeBindingIdentifier("Expected class name.");
            var name = Symbol.Intern(nameToken.Lexeme);
            var definition = ParseClassDefinition(name, classToken);
            Match(TokenType.Semicolon);
            return new ClassDeclaration(definition.Source ?? CreateSourceReference(classToken), name, definition);
        }

        private ExpressionNode ParseClassExpression()
        {
            var classToken = Previous();
            Symbol? name = null;
            if (Check(TokenType.Identifier))
            {
                var nameToken = Advance();
                name = Symbol.Intern(nameToken.Lexeme);
            }

            var definition = ParseClassDefinition(name, classToken);
            return new ClassExpression(definition.Source ?? CreateSourceReference(classToken), name, definition);
        }

        private ClassDefinition ParseClassDefinition(Symbol? className, Token classToken)
        {
            ExpressionNode? extendsExpression = null;
            if (Match(TokenType.Extends))
            {
                extendsExpression = ParseExpression();
            }

            Consume(TokenType.LeftBrace, "Expected '{' after class name or extends clause.");
            var (constructor, members, fields) = ParseClassElements(className);
            Consume(TokenType.RightBrace, "Expected '}' after class body.");
            var ctor = constructor ?? CreateDefaultConstructor(className);
            var source = CreateSourceReference(classToken);
            return new ClassDefinition(source, extendsExpression, ctor, members, fields);
        }

        private (FunctionExpression? Constructor, ImmutableArray<ClassMember> Members,
            ImmutableArray<ClassField> Fields) ParseClassElements(Symbol? className)
        {
            FunctionExpression? constructor = null;
            var members = ImmutableArray.CreateBuilder<ClassMember>();
            var fields = ImmutableArray.CreateBuilder<ClassField>();

            while (!Check(TokenType.RightBrace) && !Check(TokenType.Eof))
            {
                if (Match(TokenType.Semicolon))
                {
                    continue;
                }

                var isStatic = Match(TokenType.Static);

                if (Check(TokenType.PrivateIdentifier))
                {
                    var fieldToken = Advance();
                    var fieldName = fieldToken.Lexeme;
                    ExpressionNode? initializer = null;
                    if (Match(TokenType.Equal))
                    {
                        initializer = ParseExpression(false);
                    }

                    Match(TokenType.Semicolon);
                    fields.Add(new ClassField(CreateSourceReference(fieldToken), fieldName, initializer, isStatic, true));
                    continue;
                }

                if (Check(TokenType.Get) || Check(TokenType.Set))
                {
                    var accessorToken = Advance();
                    var isGetter = accessorToken.Type == TokenType.Get;
                    var methodNameToken = ConsumePropertyIdentifierToken(
                        isGetter ? "Expected getter name in class body." : "Expected setter name in class body.");
                    var methodName = methodNameToken.Lexeme;

                    if (isGetter)
                    {
                        Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                        Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                        var body = ParseBlock();
                        var function = new FunctionExpression(body.Source ?? CreateSourceReference(methodNameToken), null,
                            ImmutableArray<FunctionParameter>.Empty, body, false, false);
                        members.Add(new ClassMember(CreateSourceReference(methodNameToken), ClassMemberKind.Getter,
                            methodName, function, isStatic));
                    }
                    else
                    {
                        Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                        var parameterToken = ConsumeParameterIdentifier("Expected parameter name in setter.");
                        var parameterSymbol = Symbol.Intern(parameterToken.Lexeme);
                        var parameter = new FunctionParameter(CreateSourceReference(parameterToken), parameterSymbol,
                            false, null, null);
                        Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                        var body = ParseBlock();
                        var parameters = ImmutableArray.Create(parameter);
                        var function = new FunctionExpression(body.Source ?? CreateSourceReference(methodNameToken), null,
                            parameters, body, false, false);
                        members.Add(new ClassMember(CreateSourceReference(methodNameToken), ClassMemberKind.Setter,
                            methodName, function, isStatic));
                    }

                    continue;
                }

                var isGeneratorMethod = Match(TokenType.Star);

                if (IsPropertyNameToken(Peek()))
                {
                    var methodNameToken = Advance();
                    var methodName = methodNameToken.Lexeme;

                    if (Match(TokenType.Equal))
                    {
                        if (isGeneratorMethod)
                        {
                            throw new ParseException("Class fields cannot be prefixed with '*'.", methodNameToken,
                                _source);
                        }

                        var initializer = ParseExpression(false);
                        Match(TokenType.Semicolon);
                        fields.Add(new ClassField(CreateSourceReference(methodNameToken), methodName, initializer,
                            isStatic, false));
                        continue;
                    }

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

                        if (isGeneratorMethod)
                        {
                            throw new ParseException("Constructor cannot be a generator.", methodNameToken, _source);
                        }

                        constructor = ParseClassMethod(className, methodNameToken, false);
                    }
                    else
                    {
                        var function = ParseClassMethod(null, methodNameToken, isGeneratorMethod);
                        members.Add(new ClassMember(CreateSourceReference(methodNameToken), ClassMemberKind.Method,
                            methodName, function, isStatic));
                    }

                    continue;
                }

                if (isGeneratorMethod)
                {
                    throw new ParseException("Expected method name after '*'.", Peek(), _source);
                }

                throw new ParseException("Expected method, field, getter, or setter in class body.", Peek(), _source);
            }

            return (constructor, members.ToImmutable(), fields.ToImmutable());
        }

        private FunctionExpression ParseClassMethod(Symbol? functionName, Token methodNameToken, bool isGenerator)
        {
            Consume(TokenType.LeftParen, "Expected '(' after method name.");
            var parameters = ParseParameterList();
            Consume(TokenType.RightParen, "Expected ')' after method parameters.");
            using var _ = EnterFunctionContext(false, isGenerator);
            var body = ParseBlock();
            var source = body.Source ?? CreateSourceReference(methodNameToken);
            return new FunctionExpression(source, functionName, parameters, body, false, isGenerator);
        }

        private FunctionExpression CreateDefaultConstructor(Symbol? className)
        {
            var body = new BlockStatement(null, ImmutableArray<StatementNode>.Empty, false);
            return new FunctionExpression(body.Source, className, ImmutableArray<FunctionParameter>.Empty, body, false,
                false);
        }

        private StatementNode ParseImportStatement()
        {
            var keyword = Previous();

            if (Check(TokenType.String))
            {
                var moduleToken = Advance();
                var modulePath = moduleToken.Literal as string ?? string.Empty;
                Consume(TokenType.Semicolon, "Expected ';' after import statement.");
                return new ImportStatement(CreateSourceReference(keyword), modulePath, null, null,
                    ImmutableArray<ImportBinding>.Empty);
            }

            Symbol? defaultBinding = null;
            Symbol? namespaceBinding = null;
            var namedImports = ImmutableArray<ImportBinding>.Empty;

            if (Check(TokenType.Identifier) && !CheckContextualKeyword("from"))
            {
                var nameToken = Advance();
                defaultBinding = Symbol.Intern(nameToken.Lexeme);

                if (Match(TokenType.Comma))
                {
                    if (Match(TokenType.Star))
                    {
                        ConsumeContextualKeyword("as", "Expected 'as' after '*'.");
                        var nsToken = ConsumeBindingIdentifier("Expected identifier after 'as'.");
                        namespaceBinding = Symbol.Intern(nsToken.Lexeme);
                    }
                    else if (Match(TokenType.LeftBrace))
                    {
                        namedImports = ParseNamedImports();
                        Consume(TokenType.RightBrace, "Expected '}' after named imports.");
                    }
                }
            }
            else if (Match(TokenType.Star))
            {
                ConsumeContextualKeyword("as", "Expected 'as' after '*'.");
                var namespaceToken = ConsumeBindingIdentifier("Expected identifier after 'as'.");
                namespaceBinding = Symbol.Intern(namespaceToken.Lexeme);
            }
            else if (Match(TokenType.LeftBrace))
            {
                namedImports = ParseNamedImports();
                Consume(TokenType.RightBrace, "Expected '}' after named imports.");
            }

            ConsumeContextualKeyword("from", "Expected 'from' in import statement.");
            var moduleTokenFinal = Consume(TokenType.String, "Expected module path.");
            var modulePathFinal = moduleTokenFinal.Literal as string ?? string.Empty;
            Consume(TokenType.Semicolon, "Expected ';' after import statement.");
            return new ImportStatement(CreateSourceReference(keyword), modulePathFinal, defaultBinding, namespaceBinding,
                namedImports);
        }

        private ImmutableArray<ImportBinding> ParseNamedImports()
        {
            var builder = ImmutableArray.CreateBuilder<ImportBinding>();

            do
            {
                var importedToken = ConsumeBindingIdentifier("Expected identifier in import list.");
                var imported = Symbol.Intern(importedToken.Lexeme);
                Symbol local;

                if (MatchContextualKeyword("as"))
                {
                    var localToken = ConsumeBindingIdentifier("Expected identifier after 'as'.");
                    local = Symbol.Intern(localToken.Lexeme);
                }
                else
                {
                    local = imported;
                }

                builder.Add(new ImportBinding(CreateSourceReference(importedToken), imported, local));
            } while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));

            return builder.ToImmutable();
        }

        private StatementNode ParseExportStatement()
        {
            var keyword = Previous();

            if (Match(TokenType.Default))
            {
                if (Check(TokenType.Async) && CheckAhead(TokenType.Function))
                {
                    Advance(); // async
                    var functionToken = Advance(); // function
                    return ParseExportDefaultFunction(CreateSourceReference(keyword), functionToken, true);
                }

                if (Match(TokenType.Class))
                {
                    Symbol? name = null;
                    if (Check(TokenType.Identifier))
                    {
                        var nameToken = Advance();
                        name = Symbol.Intern(nameToken.Lexeme);
                    }

                    var definition = ParseClassDefinition(name, Previous());
                    if (name is not null)
                    {
                        var declaration = new ClassDeclaration(definition.Source, name, definition);
                        return new ExportDefaultStatement(CreateSourceReference(keyword),
                            new ExportDefaultDeclaration(CreateSourceReference(keyword), declaration));
                    }

                    var classExpr = new ClassExpression(definition.Source, name, definition);
                    return new ExportDefaultStatement(CreateSourceReference(keyword),
                        new ExportDefaultExpression(CreateSourceReference(keyword), classExpr));
                }

                if (Match(TokenType.Function))
                {
                    return ParseExportDefaultFunction(CreateSourceReference(keyword), Previous(), false);
                }

                var expression = ParseExpression();
                Consume(TokenType.Semicolon, "Expected ';' after export default expression.");
                return new ExportDefaultStatement(CreateSourceReference(keyword),
                    new ExportDefaultExpression(CreateSourceReference(keyword), expression));
            }

            if (Match(TokenType.LeftBrace))
            {
                var specifiers = ParseExportSpecifiers();
                Consume(TokenType.RightBrace, "Expected '}' after export list.");
                string? fromModule = null;
                if (MatchContextualKeyword("from"))
                {
                    var moduleToken = Consume(TokenType.String, "Expected module path.");
                    fromModule = moduleToken.Literal as string ?? string.Empty;
                }

                Consume(TokenType.Semicolon, "Expected ';' after export statement.");
                return new ExportNamedStatement(CreateSourceReference(keyword), specifiers, fromModule);
            }

            if (Check(TokenType.Let))
            {
                Advance();
                var declaration = ParseVariableDeclaration(VariableKind.Let);
                return new ExportDeclarationStatement(CreateSourceReference(keyword), declaration);
            }

            if (Check(TokenType.Const))
            {
                Advance();
                var declaration = ParseVariableDeclaration(VariableKind.Const);
                return new ExportDeclarationStatement(CreateSourceReference(keyword), declaration);
            }

            if (Check(TokenType.Var))
            {
                Advance();
                var declaration = ParseVariableDeclaration(VariableKind.Var);
                return new ExportDeclarationStatement(CreateSourceReference(keyword), declaration);
            }

            if (Check(TokenType.Async) && CheckAhead(TokenType.Function))
            {
                var asyncToken = Advance();
                var functionToken = Advance();
                var declaration = ParseFunctionDeclaration(true, functionToken);
                return new ExportDeclarationStatement(CreateSourceReference(keyword), declaration);
            }

            if (Match(TokenType.Function))
            {
                var declaration = ParseFunctionDeclaration(false, Previous());
                return new ExportDeclarationStatement(CreateSourceReference(keyword), declaration);
            }

            if (Match(TokenType.Class))
            {
                var declaration = ParseClassDeclaration();
                return new ExportDeclarationStatement(CreateSourceReference(keyword), declaration);
            }

            throw new ParseException("Invalid export statement.", Peek(), _source);
        }

        private ImmutableArray<ExportSpecifier> ParseExportSpecifiers()
        {
            var builder = ImmutableArray.CreateBuilder<ExportSpecifier>();

            do
            {
                var localToken = ConsumeBindingIdentifier("Expected identifier in export list.");
                var local = Symbol.Intern(localToken.Lexeme);
                Symbol exported;

                if (MatchContextualKeyword("as"))
                {
                    var exportedToken = ConsumeBindingIdentifier("Expected identifier after 'as'.");
                    exported = Symbol.Intern(exportedToken.Lexeme);
                }
                else
                {
                    exported = local;
                }

                builder.Add(new ExportSpecifier(CreateSourceReference(localToken), local, exported));
            } while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));

            return builder.ToImmutable();
        }

        private ExportDefaultStatement ParseExportDefaultFunction(SourceReference? exportSource, Token functionToken,
            bool isAsync)
        {
            var isGenerator = Match(TokenType.Star);
            Symbol? name = null;
            if (Check(TokenType.Identifier))
            {
                var nameToken = Advance();
                name = Symbol.Intern(nameToken.Lexeme);
            }

            var function = ParseFunctionTail(name, functionToken, isAsync, isGenerator);
            if (name is not null)
            {
                var declaration = new FunctionDeclaration(function.Source ?? CreateSourceReference(functionToken), name,
                    function);
                return new ExportDefaultStatement(exportSource,
                    new ExportDefaultDeclaration(exportSource, declaration));
            }

            Consume(TokenType.Semicolon, "Expected ';' after export default expression.");
            return new ExportDefaultStatement(exportSource,
                new ExportDefaultExpression(exportSource, function));
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
            var isAwait = false;
            if (Match(TokenType.Await))
            {
                if (!InAsyncContext)
                {
                    throw new ParseException("'for await...of' is only allowed inside async functions.", Previous(), _source);
                }

                isAwait = true;
            }
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
                    // In the for-loop initializer we must not treat `in` as a relational
                    // operator so we can distinguish `for (x in y)` from `for (x; ...)`.
                    var previousAllowIn = _allowInExpressions;
                    _allowInExpressions = false;
                    try
                    {
                        var initExpr = ParseExpression();
                        initializer = new ExpressionStatement(initExpr.Source, initExpr);
                    }
                    finally
                    {
                        _allowInExpressions = previousAllowIn;
                    }
                }
            }

            var isForEach = false;
            var eachKind = ForEachKind.Of;

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

        private ExpressionNode ParseExpression(bool allowSequence = true)
        {
            var expression = ParseAssignment();

            if (!allowSequence)
            {
                return expression;
            }

            while (Match(TokenType.Comma))
            {
                var right = ParseAssignment();
                var source = expression.Source ?? right.Source ?? CreateSourceReference(Previous());
                expression = new SequenceExpression(source, expression, right);
            }

            return expression;
        }

        private ExpressionNode ParseAssignment()
        {
            if (TryParseAsyncArrowFunction(out var asyncArrow))
            {
                return asyncArrow;
            }

            if (TryParseParenthesizedArrowFunction(isAsync: false, out var parenthesizedArrow))
            {
                return parenthesizedArrow;
            }

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

                 if (expr is ArrayExpression arrayPattern)
                 {
                     var binding = ConvertArrayExpressionToBinding(arrayPattern);
                     return new DestructuringAssignmentExpression(expr.Source ?? value.Source, binding, value);
                 }

                 if (expr is ObjectExpression objectPattern)
                 {
                     var binding = ConvertObjectExpressionToBinding(objectPattern);
                     return new DestructuringAssignmentExpression(expr.Source ?? value.Source, binding, value);
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

            if (Match(TokenType.Arrow))
            {
                return FinishArrowFunction(expr, false, Previous());
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

            while (true)
            {
                Token token;
                if (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual,
                          TokenType.Instanceof))
                {
                    token = Previous();
                }
                else if (_allowInExpressions && Match(TokenType.In))
                {
                    token = Previous();
                }
                else
                {
                    break;
                }

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
            var expr = ParseExponentiation();

            while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
            {
                var op = Previous();
                var right = ParseExponentiation();
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

        private ExpressionNode ParseExponentiation()
        {
            var expr = ParseUnary();

            if (Match(TokenType.StarStar))
            {
                var right = ParseExponentiation();
                expr = CreateBinaryExpression("**", expr, right);
            }

            return expr;
        }

        private ExpressionNode ParseUnary()
        {
            if (Match(TokenType.PlusPlus))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "++", ParseUnary(), true);
            }

            if (Match(TokenType.MinusMinus))
            {
                return new UnaryExpression(CreateSourceReference(Previous()), "--", ParseUnary(), true);
            }

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

            if (Check(TokenType.Yield))
            {
                if (IsYieldOrAwaitUsedAsIdentifier())
                {
                    return ParsePostfix();
                }

                var keyword = Advance();
                var isDelegated = Match(TokenType.Star);
                ExpressionNode? value = null;
                if (isDelegated)
                {
                    if (Check(TokenType.Semicolon) || CanInsertSemicolon())
                    {
                        throw new ParseException("yield* requires an expression.", keyword, _source);
                    }

                    value = ParseAssignment();
                }
                else if (!(Check(TokenType.Semicolon) || CanInsertSemicolon()))
                {
                    value = ParseAssignment();
                }

                return new YieldExpression(CreateSourceReference(keyword), value, isDelegated);
            }

            if (Check(TokenType.Await))
            {
                // Outside of an async context, 'await' should behave like an identifier
                // (script semantics). Only async functions may use await-expressions.
                if (!InAsyncContext || IsYieldOrAwaitUsedAsIdentifier())
                {
                    return ParsePostfix();
                }

                var keyword = Advance();
                var operand = ParseUnary();
                return new AwaitExpression(CreateSourceReference(keyword), operand);
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

        private ExpressionNode ParsePrimary(bool allowCallSuffix = true)
        {
            ExpressionNode expr;

            if (Match(TokenType.Number))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), Previous().Literal);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.BigInt))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), Previous().Literal);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.String))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), Previous().Literal);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.RegexLiteral))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), Previous().Literal);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.TemplateLiteral))
            {
                var templateExpr = ParseTemplateLiteralExpression(Previous());
                return ApplyCallSuffix(templateExpr, allowCallSuffix);
            }

            if (Match(TokenType.True))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), true);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.False))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), false);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.Null))
            {
                expr = new LiteralExpression(CreateSourceReference(Previous()), null);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.Undefined))
            {
                var token = Previous();
                var symbol = Symbols.Undefined;
                expr = new IdentifierExpression(CreateSourceReference(token), symbol);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.Identifier))
            {
                var symbol = Symbol.Intern(Previous().Lexeme);
                expr = new IdentifierExpression(CreateSourceReference(Previous()), symbol);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (IsContextualIdentifierToken(Peek()))
            {
                var contextual = Advance();
                var symbol = Symbol.Intern(contextual.Lexeme);
                expr = new IdentifierExpression(CreateSourceReference(contextual), symbol);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.Yield))
            {
                var symbol = Symbol.Intern(Previous().Lexeme);
                expr = new IdentifierExpression(CreateSourceReference(Previous()), symbol);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.LeftParen))
            {
                var grouped = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after expression.");
                return ApplyCallSuffix(grouped, allowCallSuffix);
            }

            if (Match(TokenType.LeftBracket))
            {
                var array = ParseArrayLiteral();
                return ApplyCallSuffix(array, allowCallSuffix);
            }

            if (Match(TokenType.LeftBrace))
            {
                var obj = ParseObjectLiteral();
                return ApplyCallSuffix(obj, allowCallSuffix);
            }

            if (Match(TokenType.Function))
            {
                var function = ParseFunctionExpression();
                return ApplyCallSuffix(function, allowCallSuffix);
            }

            if (Match(TokenType.Class))
            {
                var classExpr = ParseClassExpression();
                return ApplyCallSuffix(classExpr, allowCallSuffix);
            }

            if (Match(TokenType.Import))
            {
                var importToken = Previous();
                var importSymbol = Symbol.Intern(importToken.Lexeme);
                expr = new IdentifierExpression(CreateSourceReference(importToken), importSymbol);
                return ApplyCallSuffix(expr, allowCallSuffix);
            }

            if (Match(TokenType.Async))
            {
                var asyncToken = Previous();
                if (Check(TokenType.Function))
                {
                    Advance(); // function
                    var asyncFunction = ParseFunctionExpression(isAsync: true);
                    return ApplyCallSuffix(asyncFunction, allowCallSuffix);
                }

                var asyncSymbol = Symbol.Intern(asyncToken.Lexeme);
                var asyncIdent = new IdentifierExpression(CreateSourceReference(asyncToken), asyncSymbol);
                return ApplyCallSuffix(asyncIdent, allowCallSuffix);
            }

            if (Match(TokenType.Await))
            {
                var awaitToken = Previous();
                var awaitSymbol = Symbol.Intern(awaitToken.Lexeme);
                var awaitExpr = new IdentifierExpression(CreateSourceReference(awaitToken), awaitSymbol);
                return ApplyCallSuffix(awaitExpr, allowCallSuffix);
            }

            if (Match(TokenType.This))
            {
                var thisExpr = new ThisExpression(CreateSourceReference(Previous()));
                return ApplyCallSuffix(thisExpr, allowCallSuffix);
            }

            if (Match(TokenType.Super))
            {
                var superExpr = new SuperExpression(CreateSourceReference(Previous()));
                return ApplyCallSuffix(superExpr, allowCallSuffix);
            }

            if (Match(TokenType.New))
            {
                var newExpr = ParseNewExpression();
                return ApplyCallSuffix(newExpr, allowCallSuffix);
            }

            if (Check(TokenType.Undefined))
            {
                var token = Advance();
                var symbol = Symbols.Undefined;
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
                return ApplyCallSuffix(unary, allowCallSuffix);
            }

            throw new ParseException($"Unexpected token '{Peek().Lexeme}'.", Peek(), _source);
        }

        private ExpressionNode ParseCallSuffix(ExpressionNode expression)
        {
            while (true)
            {
                if (Match(TokenType.QuestionDot))
                {
                    if (Match(TokenType.LeftParen))
                    {
                        var optionalArguments = ParseArgumentList();
                        expression = new CallExpression(CreateSourceReference(Previous()), expression, optionalArguments,
                            true);
                        continue;
                    }

                    if (Match(TokenType.LeftBracket))
                    {
                        expression = FinishIndexAccess(expression, isOptional: true);
                        continue;
                    }

                    expression = FinishDotAccess(expression, isOptional: true);
                    continue;
                }

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

                if (Check(TokenType.TemplateLiteral))
                {
                    var templateToken = Advance();
                    expression = ParseTaggedTemplateExpression(expression, templateToken);
                    continue;
                }

                break;
            }

            return expression;
        }

        private ExpressionNode ApplyCallSuffix(ExpressionNode expression, bool allowCallSuffix)
        {
            return allowCallSuffix ? ParseCallSuffix(expression) : expression;
        }

        private ImmutableArray<CallArgument> ParseArgumentList()
        {
            var arguments = ImmutableArray.CreateBuilder<CallArgument>();

            // Handle empty argument list: "()"
            if (Check(TokenType.RightParen))
            {
                Consume(TokenType.RightParen, "Expected ')' after arguments.");
                return arguments.ToImmutable();
            }

            // Parse first argument
            var isSpread = Match(TokenType.DotDotDot);
            var expr = ParseExpression(false);
            arguments.Add(new CallArgument(expr.Source, expr, isSpread));

            // Parse subsequent arguments, allowing a trailing comma
            while (Match(TokenType.Comma))
            {
                if (Check(TokenType.RightParen))
                {
                    break;
                }

                isSpread = Match(TokenType.DotDotDot);
                expr = ParseExpression(false);
                arguments.Add(new CallArgument(expr.Source, expr, isSpread));
            }

            Consume(TokenType.RightParen, "Expected ')' after arguments.");
            return arguments.ToImmutable();
        }

        private ExpressionNode FinishDotAccess(ExpressionNode target, bool isOptional = false)
        {
            if (!Check(TokenType.Identifier) && !IsKeyword(Peek()) && !Check(TokenType.PrivateIdentifier) &&
                !IsContextualIdentifierToken(Peek()))
            {
                throw new ParseException("Expected property name after '.'.", Peek(), _source);
            }

            var nameToken = Advance();
            var property = new LiteralExpression(CreateSourceReference(nameToken), nameToken.Lexeme);
            return new MemberExpression(CreateSourceReference(nameToken), target, property, false, isOptional);
        }

        private ExpressionNode FinishIndexAccess(ExpressionNode target, bool isOptional = false)
        {
            var expression = ParseExpression();
            Consume(TokenType.RightBracket, "Expected ']' after index expression.");
            var source = target.Source ?? expression.Source;
            return new MemberExpression(source, target, expression, true, isOptional);
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
                var expr = ParseExpression(false);
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
                var spreadExpr = ParseExpression(false);
                    members.Add(new ObjectMember(spreadExpr.Source, ObjectMemberKind.Spread, string.Empty, spreadExpr,
                        null, false, false, null));
                    continue;
                }

                if (Check(TokenType.Get) && !IsGetOrSetPropertyName())
                {
                    Advance(); // get
                    var (getterKey, getterIsComputed, getterKeySource) = ParseObjectPropertyKey();
                    Consume(TokenType.LeftParen, "Expected '(' after getter name.");
                    Consume(TokenType.RightParen, "Expected ')' after getter parameters.");
                    var body = ParseBlock();
                    var function = new FunctionExpression(body.Source ?? getterKeySource, null,
                        ImmutableArray<FunctionParameter>.Empty, body, false, false);
                    members.Add(new ObjectMember(function.Source ?? getterKeySource, ObjectMemberKind.Getter, getterKey,
                        null, function, getterIsComputed, false, null));
                    continue;
                }

                if (Check(TokenType.Set) && !IsGetOrSetPropertyName())
                {
                    Advance(); // set
                    var (setterKey, setterIsComputed, setterKeySource) = ParseObjectPropertyKey();
                    Consume(TokenType.LeftParen, "Expected '(' after setter name.");
                    var parameterToken = ConsumeParameterIdentifier("Expected parameter name in setter.");
                    var parameterSymbol = Symbol.Intern(parameterToken.Lexeme);
                    var parameter = new FunctionParameter(CreateSourceReference(parameterToken), parameterSymbol, false,
                        null, null);
                    Consume(TokenType.RightParen, "Expected ')' after setter parameter.");
                    var body = ParseBlock();
                    var function = new FunctionExpression(body.Source ?? setterKeySource, null,
                        [parameter], body, false, false);
                    members.Add(new ObjectMember(function.Source ?? setterKeySource, ObjectMemberKind.Setter, setterKey,
                        null, function, setterIsComputed, false, parameterSymbol));
                    continue;
                }

                var isGeneratorMethod = Match(TokenType.Star);
                var (key, isComputed, keySource) = ParseObjectPropertyKey();

                ExpressionNode? value = null;
                FunctionExpression? method = null;
                var kind = ObjectMemberKind.Property;

                if (Match(TokenType.LeftParen))
                {
                    var parameters = ParseParameterList();
                    Consume(TokenType.RightParen, "Expected ')' after method parameters.");
                    using var _ = EnterFunctionContext(false, isGeneratorMethod);
                    var body = ParseBlock();
                    method = new FunctionExpression(body.Source, null, parameters, body, false, isGeneratorMethod);
                    kind = ObjectMemberKind.Method;
                }
                else if (Match(TokenType.Colon))
                {
                    if (isGeneratorMethod)
                    {
                        throw new ParseException("Generator marker '*' must be followed by a method definition.",
                            Peek(), _source);
                    }

                    value = ParseExpression(false);
                }
                else
                {
                    if (isGeneratorMethod)
                    {
                        throw new ParseException("Generator shorthand properties are not supported.", Peek(), _source);
                    }

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

        private TemplateLiteralExpression ParseTemplateLiteralExpression(Token templateToken)
        {
            var parts = templateToken.Literal as List<object?> ?? [];
            var builder = ImmutableArray.CreateBuilder<TemplatePart>();

            foreach (var part in parts)
            {
                if (part is string text)
                {
                    builder.Add(new TemplatePart(null, text, null));
                }
                else if (part is TemplateExpression expression)
                {
                    var parsedExpression = ParseTemplateInterpolation(expression.ExpressionText);
                    builder.Add(new TemplatePart(parsedExpression.Source ?? CreateSourceReference(templateToken), null,
                        parsedExpression));
                }
            }

            return new TemplateLiteralExpression(CreateSourceReference(templateToken), builder.ToImmutable());
        }

        private TaggedTemplateExpression ParseTaggedTemplateExpression(ExpressionNode tagExpression, Token templateToken)
        {
            var parts = templateToken.Literal as List<object?> ?? [];
            var cookedStrings = new List<string>();
            var rawStrings = new List<string>();
            var expressions = ImmutableArray.CreateBuilder<ExpressionNode>();

            foreach (var part in parts)
            {
                if (part is string text)
                {
                    cookedStrings.Add(text);
                    rawStrings.Add(text);
                }
                else if (part is TemplateExpression expression)
                {
                    expressions.Add(ParseTemplateInterpolation(expression.ExpressionText));
                }
            }

            while (cookedStrings.Count <= expressions.Count)
            {
                cookedStrings.Add(string.Empty);
                rawStrings.Add(string.Empty);
            }

            var stringsArray = BuildTemplateStringsArray(cookedStrings, templateToken);
            var rawStringsArray = BuildTemplateStringsArray(rawStrings, templateToken);

            return new TaggedTemplateExpression(CreateSourceReference(templateToken), tagExpression, stringsArray,
                rawStringsArray, expressions.ToImmutable());
        }

        private ArrayExpression BuildTemplateStringsArray(IReadOnlyList<string> values, Token templateToken)
        {
            var elements = ImmutableArray.CreateBuilder<ArrayElement>(values.Count);
            foreach (var text in values)
            {
                var literal = new LiteralExpression(null, text);
                elements.Add(new ArrayElement(null, literal, false));
            }

            return new ArrayExpression(CreateSourceReference(templateToken), elements.ToImmutable());
        }

        private ExpressionNode ParseTemplateInterpolation(string expressionText)
        {
            var trimmed = expressionText.Trim();
            if (trimmed.Length == 0)
            {
                throw new ParseException("Empty expression inside template literal.", Peek(), _source);
            }

            var wrappedSource = $"{trimmed};";
            var lexer = new Lexer(wrappedSource);
            var tokens = lexer.Tokenize();
            var embeddedParser = new TypedAstParser(tokens, wrappedSource);
            var program = embeddedParser.ParseProgram();
            if (program.Body.Length == 0 || program.Body[0] is not ExpressionStatement expressionStatement)
            {
                throw new ParseException("Template literal expressions must be valid expressions.", Peek(), _source);
            }

            return expressionStatement.Expression;
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

            var identifier = ConsumePropertyIdentifierToken("Expected property name.");
            return (identifier.Lexeme, false, CreateSourceReference(identifier));
        }

        private Token ConsumePropertyIdentifierToken(string message)
        {
            if (IsPropertyNameToken(Peek()))
            {
                return Advance();
            }

            throw new ParseException(message, Peek(), _source);
        }

        private ExpressionNode ParseFunctionExpression(Symbol? explicitName = null, bool isAsync = false)
        {
            var functionKeyword = Previous();
            var isGenerator = Match(TokenType.Star);
            var name = explicitName;
            if (name is null && CheckParameterIdentifier())
            {
                var nameToken = Advance();
                name = Symbol.Intern(nameToken.Lexeme);
            }

            return ParseFunctionTail(name, functionKeyword, isAsync, isGenerator);
        }

        private FunctionExpression ParseFunctionTail(Symbol? name, Token startToken, bool isAsync, bool isGenerator)
        {
            Consume(TokenType.LeftParen, "Expected '(' after function name.");
            var parameters = ParseParameterList();
            Consume(TokenType.RightParen, "Expected ')' after parameters.");
            using var _ = EnterFunctionContext(isAsync, isGenerator);
            var body = ParseBlock();
            var source = body.Source ?? CreateSourceReference(startToken);
            return new FunctionExpression(source, name, parameters, body, isAsync, isGenerator);
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
                BindingTarget? pattern = null;
                Symbol? name = null;
                SourceReference? source;

                if (Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace))
                {
                    if (isRest)
                    {
                        throw new ParseException("Rest parameters must be identifiers.", Peek(), _source);
                    }

                    var patternStart = Peek();
                    pattern = ParseBindingTarget("Expected parameter pattern.");
                    source = pattern.Source ?? CreateSourceReference(patternStart);
                }
                else
                {
                    var token = ConsumeParameterIdentifier("Expected parameter name.");
                    name = Symbol.Intern(token.Lexeme);
                    source = CreateSourceReference(token);
                }

                ExpressionNode? defaultValue = null;
                if (!isRest && Match(TokenType.Equal))
                {
                    defaultValue = ParseAssignment();
                }
                else if (isRest && Check(TokenType.Equal))
                {
                    throw new ParseException("Rest parameters cannot have default values.", Peek(), _source);
                }

                builder.Add(new FunctionParameter(source, name, isRest, pattern, defaultValue));
                if (isRest)
                {
                    break;
                }
            } while (Match(TokenType.Comma));

            return builder.ToImmutable();
        }

        private ExpressionNode ParseNewExpression()
        {
            var constructor = ParsePrimary(allowCallSuffix: false);

            while (true)
            {
                if (Match(TokenType.Dot))
                {
                    constructor = FinishDotAccess(constructor);
                    continue;
                }

                if (Match(TokenType.LeftBracket))
                {
                    constructor = FinishIndexAccess(constructor);
                    continue;
                }

                break;
            }

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
                case ExpressionStatement { Expression: ExpressionNode expression }:
                    return TryConvertExpressionToBindingTarget(expression);
                default:
                    return null;
            }
        }

        private BindingTarget? TryConvertExpressionToBindingTarget(ExpressionNode expression)
        {
            return expression switch
            {
                IdentifierExpression identifier => new IdentifierBinding(expression.Source, identifier.Name),
                ArrayExpression array => ConvertArrayExpressionToBinding(array),
                ObjectExpression obj => ConvertObjectExpressionToBinding(obj),
                _ => null
            };
        }

        private ArrayBinding ConvertArrayExpressionToBinding(ArrayExpression array)
        {
            var elements = ImmutableArray.CreateBuilder<ArrayBindingElement>();
            BindingTarget? restTarget = null;

            foreach (var element in array.Elements)
            {
                if (element.Expression is null)
                {
                    elements.Add(new ArrayBindingElement(null, null, null));
                    continue;
                }

                if (element.IsSpread)
                {
                    if (restTarget is not null)
                    {
                        throw new NotSupportedException("Multiple rest elements are not allowed in destructuring patterns.");
                    }

                    var restBinding = ConvertExpressionToBindingTarget(element.Expression)
                                      ?? throw new NotSupportedException("Invalid rest binding target.");
                    if (restBinding is not IdentifierBinding)
                    {
                        throw new NotSupportedException("Rest binding must be an identifier.");
                    }

                    restTarget = restBinding;
                    continue;
                }

                var target = ConvertExpressionToBindingTarget(element.Expression)
                             ?? throw new NotSupportedException("Invalid destructuring target.");
                elements.Add(new ArrayBindingElement(element.Source ?? target.Source, target, null));
            }

            return new ArrayBinding(array.Source, elements.ToImmutable(), restTarget);
        }

        private ObjectBinding ConvertObjectExpressionToBinding(ObjectExpression obj)
        {
            var properties = ImmutableArray.CreateBuilder<ObjectBindingProperty>();
            BindingTarget? restTarget = null;

            foreach (var member in obj.Members)
            {
                if (member.Kind == ObjectMemberKind.Spread)
                {
                    if (restTarget is not null)
                    {
                        throw new NotSupportedException("Multiple rest elements are not allowed in destructuring patterns.");
                    }

                    var restBinding = member.Value is null
                        ? null
                        : ConvertExpressionToBindingTarget(member.Value);
                    if (restBinding is not IdentifierBinding identifierBinding)
                    {
                        throw new NotSupportedException("Rest property must be an identifier.");
                    }

                    restTarget = identifierBinding;
                    continue;
                }

                if (member.Kind != ObjectMemberKind.Property || member.IsComputed || member.Key is not string name ||
                    member.Value is null)
                {
                    throw new NotSupportedException("Invalid object destructuring pattern.");
                }

                var target = ConvertExpressionToBindingTarget(member.Value)
                             ?? throw new NotSupportedException("Invalid object destructuring target.");
                properties.Add(new ObjectBindingProperty(member.Source ?? obj.Source, name, target, null));
            }

            return new ObjectBinding(obj.Source, properties.ToImmutable(), restTarget);
        }

        private BindingTarget? ConvertExpressionToBindingTarget(ExpressionNode expression)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                    return new IdentifierBinding(expression.Source, identifier.Name);
                case ArrayExpression array:
                    return ConvertArrayExpressionToBinding(array);
                case ObjectExpression obj:
                    return ConvertObjectExpressionToBinding(obj);
                default:
                    return null;
            }
        }

        private bool TryParseAsyncArrowFunction(out ExpressionNode arrowFunction)
        {
            arrowFunction = null!;
            if (!Check(TokenType.Async))
            {
                return false;
            }

            var saved = _current;
            var asyncToken = Advance();

            if (HasLineTerminatorBefore())
            {
                _current = saved;
                return false;
            }

            if (Check(TokenType.Function))
            {
                _current = saved;
                return false;
            }

            if (Check(TokenType.LeftParen))
            {
                Advance();
                if (!TryParseArrowParameterList(out var parameters))
                {
                    _current = saved;
                    return false;
                }

                Consume(TokenType.Arrow, "Expected '=>' after async arrow parameters.");
                arrowFunction = ParseArrowFunctionBody(parameters, true, asyncToken);
                return true;
            }

            if (!CheckParameterIdentifier())
            {
                _current = saved;
                return false;
            }

            var parameterToken = ConsumeParameterIdentifier("Expected parameter name.");
            if (!Match(TokenType.Arrow))
            {
                _current = saved;
                return false;
            }

            var parameter = new FunctionParameter(CreateSourceReference(parameterToken),
                Symbol.Intern(parameterToken.Lexeme), false, null, null);
            arrowFunction = ParseArrowFunctionBody([parameter], true, asyncToken);
            return true;
        }

        private bool TryParseParenthesizedArrowFunction(bool isAsync, out ExpressionNode arrowFunction)
        {
            arrowFunction = null!;
            if (!Check(TokenType.LeftParen))
            {
                return false;
            }

            var saved = _current;
            var startToken = Peek();
            Advance(); // consume '('

            if (!TryParseArrowParameterList(out var parameters))
            {
                _current = saved;
                return false;
            }

            Consume(TokenType.Arrow, "Expected '=>' after arrow parameters.");
            arrowFunction = ParseArrowFunctionBody(parameters, isAsync, startToken);
            return true;
        }

        private bool TryParseArrowParameterList(out ImmutableArray<FunctionParameter> parameters)
        {
            var builder = ImmutableArray.CreateBuilder<FunctionParameter>();
            var start = _current;

            if (Check(TokenType.RightParen))
            {
                Advance();
            }
            else
            {
                while (true)
                {
                    var isRest = Match(TokenType.DotDotDot);
                    FunctionParameter parameter;

                    if (Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace))
                    {
                        var bindingStart = _current;
                        BindingTarget pattern;
                        try
                        {
                            pattern = ParseBindingTarget("Expected binding pattern in parameter.");
                        }
                        catch (ParseException)
                        {
                            _current = start;
                            parameters = default;
                            return false;
                        }

                        ExpressionNode? defaultValue = null;
                        if (!isRest && Match(TokenType.Equal))
                        {
                            defaultValue = ParseAssignment();
                        }
                        else if (isRest && Check(TokenType.Equal))
                        {
                            _current = start;
                            parameters = default;
                            return false;
                        }

                        parameter = CreateParameterFromBinding(pattern, isRest, defaultValue);
                    }
                    else
                    {
                        if (!CheckParameterIdentifier())
                        {
                            _current = start;
                            parameters = default;
                            return false;
                        }

                        var nameStart = _current;
                        Token nameToken;
                        try
                        {
                            nameToken = ConsumeParameterIdentifier("Expected parameter name.");
                        }
                        catch (ParseException)
                        {
                            _current = start;
                            parameters = default;
                            return false;
                        }

                        ExpressionNode? defaultValue = null;
                        if (!isRest && Match(TokenType.Equal))
                        {
                            defaultValue = ParseAssignment();
                        }
                        else if (isRest && Check(TokenType.Equal))
                        {
                            _current = start;
                            parameters = default;
                            return false;
                        }

                        parameter = new FunctionParameter(CreateSourceReference(nameToken),
                            Symbol.Intern(nameToken.Lexeme), isRest, null, defaultValue);
                    }

                    builder.Add(parameter);

                    if (!Match(TokenType.Comma))
                    {
                        break;
                    }

                    if (Check(TokenType.RightParen))
                    {
                        break;
                    }
                }

                if (!Check(TokenType.RightParen))
                {
                    _current = start;
                    parameters = default;
                    return false;
                }

                Advance();
            }

            if (!Check(TokenType.Arrow))
            {
                _current = start;
                parameters = default;
                return false;
            }

            parameters = builder.ToImmutable();
            return true;
        }

        private FunctionParameter CreateParameterFromBinding(BindingTarget target, bool isRest,
            ExpressionNode? defaultValue)
        {
            return target switch
            {
                IdentifierBinding identifier =>
                    new FunctionParameter(target.Source, identifier.Name, isRest, null, defaultValue),
                _ => new FunctionParameter(target.Source, null, isRest, target, defaultValue)
            };
        }

        private ExpressionNode ParseArrowFunctionBody(ImmutableArray<FunctionParameter> parameters, bool isAsync,
            Token? headToken, SourceReference? fallbackSource = null)
        {
            using var _ = EnterFunctionContext(isAsync, false);
            BlockStatement body;
            if (Match(TokenType.LeftBrace))
            {
                body = ParseBlock(leftBraceConsumed: true);
            }
            else
            {
                var expression = ParseAssignment();
                var returnStatement = new ReturnStatement(expression.Source, expression);
                body = new BlockStatement(expression.Source, [returnStatement], false);
            }

            var source = body.Source;
            if (source is null && headToken is not null)
            {
                source = CreateSourceReference(headToken);
            }
            else if (source is null)
            {
                source = fallbackSource;
            }

            return new FunctionExpression(source, null, parameters, body, isAsync, false);
        }

        private ExpressionNode FinishArrowFunction(ExpressionNode parameterExpression, bool isAsync, Token arrowToken)
        {
            ImmutableArray<FunctionParameter> parameters;

            if (parameterExpression is IdentifierExpression identifier)
            {
                var parameter = new FunctionParameter(parameterExpression.Source, identifier.Name, false, null, null);
                parameters = [parameter];
            }
            else
            {
                throw new NotSupportedException("Arrow functions require an identifier or parenthesized parameter list.");
            }

            return ParseArrowFunctionBody(parameters, isAsync, null,
                parameterExpression.Source ?? CreateSourceReference(arrowToken));
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

            if (type == TokenType.Semicolon && CanInsertSemicolon())
            {
                var currentToken = Peek();
                return new Token(TokenType.Semicolon, ";", null, currentToken.Line, currentToken.Column,
                    currentToken.StartPosition, currentToken.StartPosition);
            }

            throw new ParseException(message, Peek(), _source);
        }

        private Token ConsumeParameterIdentifier(string message)
        {
            if (CheckIdentifierLike())
            {
                return Advance();
            }

            throw new ParseException(message, Peek(), _source);
        }

        private bool CheckParameterIdentifier()
        {
            return CheckIdentifierLike();
        }

        private Token ConsumeBindingIdentifier(string message)
        {
            return ConsumeParameterIdentifier(message);
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

        private bool CheckIdentifierLike()
        {
            if (Check(TokenType.Identifier))
            {
                return true;
            }

            var token = Peek();
            return token.Type is TokenType.Async or TokenType.Await or TokenType.Yield ||
                   IsContextualIdentifierToken(token);
        }

        private static bool IsContextualIdentifierToken(Token token)
        {
            return token.Type is TokenType.Get or TokenType.Set;
        }

        private bool IsPropertyNameToken(Token token)
        {
            return token.Type == TokenType.Identifier || IsContextualIdentifierToken(token) || IsKeyword(token);
        }

        private bool CheckContextualKeyword(string keyword)
        {
            return Check(TokenType.Identifier) &&
                   string.Equals(Peek().Lexeme, keyword, StringComparison.Ordinal);
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

        private Token ConsumeContextualKeyword(string keyword, string message)
        {
            if (!CheckContextualKeyword(keyword))
            {
                throw new ParseException(message, Peek(), _source);
            }

            return Advance();
        }

        private bool CanInsertSemicolon()
        {
            if (_current > 0 && HasLineTerminatorBefore())
            {
                return true;
            }

            if (Check(TokenType.RightBrace) || Check(TokenType.Eof))
            {
                return true;
            }

            return false;
        }

        private bool IsGetOrSetPropertyName()
        {
            if (_current + 1 >= _tokens.Count)
            {
                return false;
            }

            return _tokens[_current + 1].Type == TokenType.Colon;
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

        private bool IsYieldOrAwaitUsedAsIdentifier()
        {
            var currentToken = Peek();
            if (currentToken.Type == TokenType.Yield && InGeneratorContext)
            {
                return false;
            }

            if (currentToken.Type == TokenType.Await && InAsyncContext)
            {
                return false;
            }

            var nextType = PeekNext().Type;
            return nextType is TokenType.Semicolon or TokenType.Comma or TokenType.RightParen or
                   TokenType.RightBracket or TokenType.RightBrace or TokenType.Colon or
                   TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or
                   TokenType.Percent or TokenType.StarStar or TokenType.Equal or TokenType.PlusEqual or
                   TokenType.MinusEqual or TokenType.StarEqual or TokenType.SlashEqual or
                   TokenType.PercentEqual or TokenType.StarStarEqual or TokenType.EqualEqual or
                   TokenType.EqualEqualEqual or TokenType.BangEqual or TokenType.BangEqualEqual or
                   TokenType.Greater or TokenType.GreaterEqual or TokenType.Less or TokenType.LessEqual or
                   TokenType.AmpAmp or TokenType.PipePipe or TokenType.Amp or TokenType.Pipe or TokenType.Caret or
                   TokenType.LessLess or TokenType.GreaterGreater or TokenType.GreaterGreaterGreater or
                   TokenType.AmpAmpEqual or TokenType.PipePipeEqual or TokenType.AmpEqual or TokenType.PipeEqual or
                   TokenType.CaretEqual or TokenType.LessLessEqual or TokenType.GreaterGreaterEqual or
                   TokenType.GreaterGreaterGreaterEqual or TokenType.QuestionQuestion or
                   TokenType.QuestionQuestionEqual or TokenType.Question or TokenType.Dot or
                   TokenType.QuestionDot or TokenType.LeftBracket or TokenType.PlusPlus or TokenType.MinusMinus;
        }

        private FunctionContextScope EnterFunctionContext(bool isAsync, bool isGenerator)
        {
            _functionContexts.Push(new FunctionContext(isAsync, isGenerator));
            return new FunctionContextScope(this);
        }

        private readonly struct FunctionContext
        {
            public FunctionContext(bool isAsync, bool isGenerator)
            {
                IsAsync = isAsync;
                IsGenerator = isGenerator;
            }

            public bool IsAsync { get; }
            public bool IsGenerator { get; }
        }

        private sealed class FunctionContextScope : IDisposable
        {
            private readonly DirectParser _parser;
            private bool _disposed;

            public FunctionContextScope(DirectParser parser)
            {
                _parser = parser;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                if (_parser._functionContexts.Count > 0)
                {
                    _parser._functionContexts.Pop();
                }

                _disposed = true;
            }
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
