using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Converts the existing S-expression representation into a typed AST.
/// The builder intentionally keeps a few escape hatches (Unknown* records)
/// so we can enable translation gradually without blocking the pipeline.
/// </summary>
public sealed class SExpressionAstBuilder
{
    private static readonly HashSet<string> KnownOperators = new(StringComparer.Ordinal)
    {
        "++prefix",
        "--prefix",
        "++postfix",
        "--postfix",
        "~",
        ",",
        "+",
        "-",
        "*",
        "/",
        "%",
        "**",
        "==",
        "!=",
        "===",
        "!==",
        "<",
        "<=",
        ">",
        ">=",
        "in",
        "instanceof",
        "<<",
        ">>",
        ">>>",
        "&",
        "|",
        "^",
        "&&",
        "||",
        "??"
    };

    /// <summary>
    /// Translates the S-expression program returned by the parser into a typed AST.
    /// </summary>
    public ProgramNode BuildProgram(Cons program)
    {
        if (program.IsEmpty || program.Head is not Symbol head || !ReferenceEquals(head, JsSymbols.Program))
        {
            throw new ArgumentException("Expected a program S-expression.", nameof(program));
        }

        var statements = program.Rest;
        var isStrict = TryConsumeUseStrict(ref statements);
        var builder = ImmutableArray.CreateBuilder<StatementNode>();
        foreach (var statement in statements)
        {
            builder.Add(BuildStatement(statement));
        }

        return new ProgramNode(program.SourceReference, builder.ToImmutable(), isStrict);
    }

    private StatementNode BuildStatement(object? node)
    {
        switch (node)
        {
            case null:
                return new EmptyStatement(null);
            case MultipleDeclarations multi:
            {
                var statements = ImmutableArray.CreateBuilder<StatementNode>(multi.Declarations.Count);
                foreach (var decl in multi.Declarations)
                {
                    statements.Add(BuildStatement(decl));
                }

                return new BlockStatement(null, statements.ToImmutable(), false);
            }
            case Cons cons when cons.Head is Symbol symbol:
                return BuildStatementFromSymbol(cons, symbol);
            case Cons cons:
                return new UnknownStatement(cons.SourceReference, cons);
            default:
                return new ExpressionStatement(null, BuildExpression(node));
        }
    }

    private StatementNode BuildStatementFromSymbol(Cons cons, Symbol symbol)
    {
        if (ReferenceEquals(symbol, JsSymbols.Block))
        {
            return BuildBlock(cons);
        }

        if (ReferenceEquals(symbol, JsSymbols.ExpressionStatement))
        {
            var expression = BuildExpression(cons.Rest.Head);
            return new ExpressionStatement(cons.SourceReference, expression);
        }

        if (ReferenceEquals(symbol, JsSymbols.Return))
        {
            var hasValue = !ReferenceEquals(cons.Rest, Cons.Empty);
            var expression = hasValue ? BuildExpression(cons.Rest.Head) : null;
            return new ReturnStatement(cons.SourceReference, expression);
        }

        if (ReferenceEquals(symbol, JsSymbols.Throw))
        {
            var expression = BuildExpression(cons.Rest.Head);
            return new ThrowStatement(cons.SourceReference, expression);
        }

        if (ReferenceEquals(symbol, JsSymbols.Let))
        {
            return BuildVariableDeclaration(cons, VariableKind.Let);
        }

        if (ReferenceEquals(symbol, JsSymbols.Var))
        {
            return BuildVariableDeclaration(cons, VariableKind.Var);
        }

        if (ReferenceEquals(symbol, JsSymbols.Const))
        {
            return BuildVariableDeclaration(cons, VariableKind.Const);
        }

        if (ReferenceEquals(symbol, JsSymbols.If))
        {
            var condition = BuildExpression(cons.Rest.Head);
            var thenStatement = BuildStatement(cons.Rest.Rest.Head);
            var elseBranch = cons.Rest.Rest.Rest.Head;
            var elseStatement = elseBranch is null ? null : BuildStatement(elseBranch);
            return new IfStatement(cons.SourceReference, condition, thenStatement, elseStatement);
        }

        if (ReferenceEquals(symbol, JsSymbols.While))
        {
            var condition = BuildExpression(cons.Rest.Head);
            var body = BuildStatement(cons.Rest.Rest.Head);
            return new WhileStatement(cons.SourceReference, condition, body);
        }

        if (ReferenceEquals(symbol, JsSymbols.DoWhile))
        {
            var condition = BuildExpression(cons.Rest.Head);
            var body = BuildStatement(cons.Rest.Rest.Head);
            return new DoWhileStatement(cons.SourceReference, body, condition);
        }

        if (ReferenceEquals(symbol, JsSymbols.For))
        {
            var initializer = BuildForInitializer(cons.Rest.Head);
            var conditionExpr = cons.Rest.Rest.Head;
            var incrementExpr = cons.Rest.Rest.Rest.Head;
            var body = BuildStatement(cons.Rest.Rest.Rest.Rest.Head);
            var condition = conditionExpr is null ? null : BuildExpression(conditionExpr);
            var increment = incrementExpr is null ? null : BuildExpression(incrementExpr);
            return new ForStatement(cons.SourceReference, initializer, condition, increment, body);
        }

        if (ReferenceEquals(symbol, JsSymbols.ForIn) ||
            ReferenceEquals(symbol, JsSymbols.ForOf) ||
            ReferenceEquals(symbol, JsSymbols.ForAwaitOf))
        {
            var firstArg = cons.Rest.Head;
            VariableKind? declarationKind = null;
            BindingTarget target;

            if (firstArg is Cons { Head: Symbol declHead } declaration &&
                (ReferenceEquals(declHead, JsSymbols.Let) ||
                 ReferenceEquals(declHead, JsSymbols.Var) ||
                 ReferenceEquals(declHead, JsSymbols.Const)))
            {
                declarationKind = ReferenceEquals(declHead, JsSymbols.Const)
                    ? VariableKind.Const
                    : ReferenceEquals(declHead, JsSymbols.Var)
                        ? VariableKind.Var
                        : VariableKind.Let;

                target = BuildBindingTarget(declaration.Rest.Head, declaration.SourceReference);
            }
            else
            {
                target = BuildBindingTarget(firstArg, cons.SourceReference);
            }

            var iterable = BuildExpression(cons.Rest.Rest.Head);
            var body = BuildStatement(cons.Rest.Rest.Rest.Head);
            var kind = ReferenceEquals(symbol, JsSymbols.ForIn)
                ? ForEachKind.In
                : ReferenceEquals(symbol, JsSymbols.ForOf)
                    ? ForEachKind.Of
                    : ForEachKind.AwaitOf;
            return new ForEachStatement(cons.SourceReference, target, iterable, body, kind, declarationKind);
        }

        if (ReferenceEquals(symbol, JsSymbols.Switch))
        {
            return BuildSwitch(cons);
        }

        if (ReferenceEquals(symbol, JsSymbols.Try))
        {
            return BuildTry(cons);
        }

        if (ReferenceEquals(symbol, JsSymbols.Break))
        {
            var label = cons.Rest is { IsEmpty: false } rest ? rest.Head as Symbol : null;
            return new BreakStatement(cons.SourceReference, label);
        }

        if (ReferenceEquals(symbol, JsSymbols.Continue))
        {
            var label = cons.Rest is { IsEmpty: false } rest ? rest.Head as Symbol : null;
            return new ContinueStatement(cons.SourceReference, label);
        }

        if (ReferenceEquals(symbol, JsSymbols.EmptyStatement))
        {
            return new EmptyStatement(cons.SourceReference);
        }

        if (ReferenceEquals(symbol, JsSymbols.Label))
        {
            var label = cons.Rest.Head as Symbol;
            var statement = BuildStatement(cons.Rest.Rest.Head);
            return label is null
                ? new UnknownStatement(cons.SourceReference, cons)
                : new LabeledStatement(cons.SourceReference, label, statement);
        }

        if (ReferenceEquals(symbol, JsSymbols.Function) ||
            ReferenceEquals(symbol, JsSymbols.Async) ||
            ReferenceEquals(symbol, JsSymbols.Generator))
        {
            return BuildFunctionDeclaration(cons, symbol);
        }

        if (ReferenceEquals(symbol, JsSymbols.Class))
        {
            var name = cons.Rest.Head as Symbol;
            var extendsClause = cons.Rest.Rest.Head as Cons;
            var constructor = cons.Rest.Rest.Rest.Head as Cons ?? Cons.Empty;
            var methods = cons.Rest.Rest.Rest.Rest.Head as Cons ?? Cons.Empty;
            var fields = cons.Rest.Rest.Rest.Rest.Rest.Head as Cons ?? Cons.Empty;
            return name is null
                ? new UnknownStatement(cons.SourceReference, cons)
                : new ClassDeclaration(cons.SourceReference, name, extendsClause, constructor, methods, fields);
        }

        if (ReferenceEquals(symbol, JsSymbols.Import) ||
            ReferenceEquals(symbol, JsSymbols.Export) ||
            ReferenceEquals(symbol, JsSymbols.ExportDefault) ||
            ReferenceEquals(symbol, JsSymbols.ExportNamed))
        {
            return new ModuleStatement(cons.SourceReference, cons);
        }

        return new UnknownStatement(cons.SourceReference, cons);
    }

    private BlockStatement BuildBlock(Cons cons)
    {
        var statements = cons.Rest;
        var isStrict = TryConsumeUseStrict(ref statements);
        var builder = ImmutableArray.CreateBuilder<StatementNode>();
        foreach (var statement in statements)
        {
            builder.Add(BuildStatement(statement));
        }

        return new BlockStatement(cons.SourceReference, builder.ToImmutable(), isStrict);
    }

    private static bool TryConsumeUseStrict(ref Cons list)
    {
        if (list is { IsEmpty: false, Head: Cons { Head: Symbol head } } &&
            ReferenceEquals(head, JsSymbols.UseStrict))
        {
            list = list.Rest;
            return true;
        }

        return false;
    }

    private StatementNode? BuildForInitializer(object? initializer)
    {
        if (initializer is null)
        {
            return null;
        }

        if (initializer is Cons { Head: Symbol symbol } cons &&
            (ReferenceEquals(symbol, JsSymbols.Let) ||
             ReferenceEquals(symbol, JsSymbols.Var) ||
             ReferenceEquals(symbol, JsSymbols.Const)))
        {
            var kind = ReferenceEquals(symbol, JsSymbols.Const)
                ? VariableKind.Const
                : ReferenceEquals(symbol, JsSymbols.Var)
                    ? VariableKind.Var
                    : VariableKind.Let;
            return BuildVariableDeclaration(cons, kind);
        }

        var expression = BuildExpression(initializer);
        return new ExpressionStatement((initializer as Cons)?.SourceReference, expression);
    }

    private StatementNode BuildSwitch(Cons cons)
    {
        var discriminant = BuildExpression(cons.Rest.Head);
        var clauses = cons.Rest.Rest.Head as Cons ?? Cons.Empty;
        var casesBuilder = ImmutableArray.CreateBuilder<SwitchCase>();

        foreach (var clause in clauses)
        {
            if (clause is not Cons { Head: Symbol clauseSymbol } clauseCons)
            {
                continue;
            }

            if (ReferenceEquals(clauseSymbol, JsSymbols.Case))
            {
                var test = BuildExpression(clauseCons.Rest.Head);
                var bodyCons = ExpectCons(clauseCons.Rest.Rest.Head, clauseCons.SourceReference);
                casesBuilder.Add(new SwitchCase(clauseCons.SourceReference, test, BuildBlock(bodyCons)));
                continue;
            }

            if (ReferenceEquals(clauseSymbol, JsSymbols.Default))
            {
                var bodyCons = ExpectCons(clauseCons.Rest.Head, clauseCons.SourceReference);
                casesBuilder.Add(new SwitchCase(clauseCons.SourceReference, null, BuildBlock(bodyCons)));
            }
        }

        return new SwitchStatement(cons.SourceReference, discriminant, casesBuilder.ToImmutable());
    }

    private StatementNode BuildTry(Cons cons)
    {
        var tryBlock = BuildBlock(ExpectCons(cons.Rest.Head, cons.SourceReference));
        CatchClause? catchClause = null;
        BlockStatement? finallyBlock = null;

        var catchNode = cons.Rest.Rest.Head;
        if (catchNode is Cons { Head: Symbol catchSymbol } catchCons && ReferenceEquals(catchSymbol, JsSymbols.Catch))
        {
            var binding = catchCons.Rest.Head as Symbol;
            var body = BuildBlock(ExpectCons(catchCons.Rest.Rest.Head, catchCons.SourceReference));
            if (binding != null)
            {
                catchClause = new CatchClause(catchCons.SourceReference, binding, body);
            }
        }

        var finallyNode = cons.Rest.Rest.Rest.Head;
        if (finallyNode is Cons finallyCons)
        {
            finallyBlock = BuildBlock(finallyCons);
        }

        return new TryStatement(cons.SourceReference, tryBlock, catchClause, finallyBlock);
    }

    private StatementNode BuildFunctionDeclaration(Cons cons, Symbol symbol)
    {
        var name = cons.Rest.Head as Symbol;
        var parametersCons = ExpectCons(cons.Rest.Rest.Head, cons.SourceReference);
        var bodyCons = ExpectCons(cons.Rest.Rest.Rest.Head, cons.SourceReference);
        var isAsync = ReferenceEquals(symbol, JsSymbols.Async);
        var isGenerator = ReferenceEquals(symbol, JsSymbols.Generator);

        var function = new FunctionExpression(cons.SourceReference, name,
            BuildFunctionParameters(parametersCons),
            BuildBlock(bodyCons), isAsync, isGenerator);

        return name is null
            ? new UnknownStatement(cons.SourceReference, cons)
            : new FunctionDeclaration(cons.SourceReference, name, function);
    }

    private VariableDeclaration BuildVariableDeclaration(Cons cons, VariableKind kind)
    {
        var target = BuildBindingTarget(cons.Rest.Head, cons.SourceReference);
        var initializerValue = cons.Rest.Rest.Head;
        ExpressionNode? initializer = null;
        if (!ReferenceEquals(initializerValue, JsSymbols.Uninitialized))
        {
            initializer = BuildExpression(initializerValue);
        }

        var declarator = new VariableDeclarator(cons.SourceReference, target, initializer);
        return new VariableDeclaration(cons.SourceReference, kind, ImmutableArray.Create(declarator));
    }

    private static BindingTarget BuildBindingTarget(object? target, SourceReference? source)
    {
        return target switch
        {
            Symbol symbol => new IdentifierBinding(source, symbol),
            Cons cons => new DestructuringBinding(cons.SourceReference ?? source, cons),
            _ => new DestructuringBinding(source, Cons.Cell(target))
        };
    }

    private ImmutableArray<FunctionParameter> BuildFunctionParameters(Cons parameters)
    {
        var builder = ImmutableArray.CreateBuilder<FunctionParameter>();
        foreach (var parameter in parameters)
        {
            builder.Add(BuildFunctionParameter(parameter));
        }

        return builder.ToImmutable();
    }

    private FunctionParameter BuildFunctionParameter(object? parameter)
    {
        if (parameter is Cons { Head: Symbol head } cons)
        {
            if (ReferenceEquals(head, JsSymbols.Rest))
            {
                var symbol = cons.Rest.Head as Symbol;
                return new FunctionParameter(cons.SourceReference, symbol, true, null, null);
            }

            if (ReferenceEquals(head, JsSymbols.PatternElement) ||
                ReferenceEquals(head, JsSymbols.PatternProperty) ||
                ReferenceEquals(head, JsSymbols.ArrayPattern) ||
                ReferenceEquals(head, JsSymbols.ObjectPattern))
            {
                return new FunctionParameter(cons.SourceReference, null, false, cons, null);
            }

            if (ReferenceEquals(head, JsSymbols.PatternDefault))
            {
                var pattern = cons.Rest.Head as Cons;
                var defaultValue = BuildExpression(cons.Rest.Rest.Head);
                return new FunctionParameter(cons.SourceReference, null, false, pattern, defaultValue);
            }
        }

        var name = parameter as Symbol;
        return new FunctionParameter(null, name, false, null, null);
    }

    private ExpressionNode BuildExpression(object? expression)
    {
        return expression switch
        {
            null => new LiteralExpression(null, null),
            bool b => new LiteralExpression(null, b),
            string s => new LiteralExpression(null, s),
            double d => new LiteralExpression(null, d),
            int i => new LiteralExpression(null, (double)i),
            Symbol symbol => BuildSymbolExpression(symbol),
            Cons { Head: Symbol symbol } cons => BuildCompositeExpression(cons, symbol),
            Cons cons => new UnknownExpression(cons.SourceReference, cons),
            _ => new LiteralExpression(null, expression)
        };
    }

    private static ExpressionNode BuildSymbolExpression(Symbol symbol)
    {
        if (ReferenceEquals(symbol, JsSymbols.This))
        {
            return new ThisExpression(null);
        }

        if (ReferenceEquals(symbol, JsSymbols.Super))
        {
            return new SuperExpression(null);
        }

        return new IdentifierExpression(null, symbol);
    }

    private ExpressionNode BuildCompositeExpression(Cons cons, Symbol symbol)
    {
        if (ReferenceEquals(symbol, JsSymbols.Assign))
        {
            if (cons.Rest.Head is Symbol targetSymbol)
            {
                return new AssignmentExpression(cons.SourceReference, targetSymbol, BuildExpression(cons.Rest.Rest.Head));
            }

            return new UnknownExpression(cons.SourceReference, cons);
        }

        if (ReferenceEquals(symbol, JsSymbols.DestructuringAssignment))
        {
            var pattern = cons.Rest.Head as Cons ?? Cons.Empty;
            var value = BuildExpression(cons.Rest.Rest.Head);
            return new DestructuringAssignmentExpression(cons.SourceReference, pattern, value);
        }

        if (ReferenceEquals(symbol, JsSymbols.Call) || ReferenceEquals(symbol, JsSymbols.OptionalCall))
        {
            var callee = BuildExpression(cons.Rest.Head);
            var argumentsBuilder = ImmutableArray.CreateBuilder<CallArgument>();
            foreach (var arg in cons.Rest.Rest)
            {
                if (arg is Cons { Head: Symbol argHead } spreadCons && ReferenceEquals(argHead, JsSymbols.Spread))
                {
                    var spreadExpr = BuildExpression(spreadCons.Rest.Head);
                    argumentsBuilder.Add(new CallArgument(spreadCons.SourceReference, spreadExpr, true));
                }
                else
                {
                    argumentsBuilder.Add(new CallArgument((arg as Cons)?.SourceReference, BuildExpression(arg), false));
                }
            }

            return new CallExpression(cons.SourceReference, callee, argumentsBuilder.ToImmutable(),
                ReferenceEquals(symbol, JsSymbols.OptionalCall));
        }

        if (ReferenceEquals(symbol, JsSymbols.New))
        {
            var constructor = BuildExpression(cons.Rest.Head);
            var argsBuilder = ImmutableArray.CreateBuilder<ExpressionNode>();
            foreach (var arg in cons.Rest.Rest)
            {
                argsBuilder.Add(BuildExpression(arg));
            }

            return new NewExpression(cons.SourceReference, constructor, argsBuilder.ToImmutable());
        }

        if (ReferenceEquals(symbol, JsSymbols.ArrayLiteral))
        {
            var elementsBuilder = ImmutableArray.CreateBuilder<ArrayElement>();
            foreach (var element in cons.Rest)
            {
                switch (element)
                {
                    case Cons { Head: Symbol head } elementCons when ReferenceEquals(head, JsSymbols.Spread):
                    {
                        var spreadValue = BuildExpression(elementCons.Rest.Head);
                        elementsBuilder.Add(new ArrayElement(elementCons.SourceReference, spreadValue, true));
                        break;
                    }
                    case null:
                        elementsBuilder.Add(new ArrayElement(null, null, false));
                        break;
                    default:
                        elementsBuilder.Add(new ArrayElement((element as Cons)?.SourceReference, BuildExpression(element), false));
                        break;
                }
            }

            return new ArrayExpression(cons.SourceReference, elementsBuilder.ToImmutable());
        }

        if (ReferenceEquals(symbol, JsSymbols.ObjectLiteral))
        {
            return BuildObjectExpression(cons);
        }

        if (ReferenceEquals(symbol, JsSymbols.TemplateLiteral))
        {
            var partsBuilder = ImmutableArray.CreateBuilder<TemplatePart>();
            foreach (var part in cons.Rest)
            {
                if (part is string s)
                {
                    partsBuilder.Add(new TemplatePart(null, s, null));
                }
                else
                {
                    partsBuilder.Add(new TemplatePart((part as Cons)?.SourceReference, null, BuildExpression(part)));
                }
            }

            return new TemplateLiteralExpression(cons.SourceReference, partsBuilder.ToImmutable());
        }

        if (ReferenceEquals(symbol, JsSymbols.TaggedTemplate))
        {
            var rest = cons.Rest;
            var tag = BuildExpression(rest.Head);
            rest = rest.Rest;
            var stringsArray = BuildExpression(rest.Head);
            rest = rest.Rest;
            var rawStringsArray = BuildExpression(rest.Head);
            rest = rest.Rest;

            var expressionsBuilder = ImmutableArray.CreateBuilder<ExpressionNode>();
            foreach (var expr in rest)
            {
                expressionsBuilder.Add(BuildExpression(expr));
            }

            return new TaggedTemplateExpression(cons.SourceReference, tag, stringsArray, rawStringsArray,
                expressionsBuilder.ToImmutable());
        }

        if (ReferenceEquals(symbol, JsSymbols.GetProperty) ||
            ReferenceEquals(symbol, JsSymbols.OptionalGetProperty))
        {
            var target = BuildExpression(cons.Rest.Head);
            var property = BuildExpression(cons.Rest.Rest.Head);
            return new MemberExpression(cons.SourceReference, target, property, false,
                ReferenceEquals(symbol, JsSymbols.OptionalGetProperty));
        }

        if (ReferenceEquals(symbol, JsSymbols.GetIndex) ||
            ReferenceEquals(symbol, JsSymbols.OptionalGetIndex))
        {
            var target = BuildExpression(cons.Rest.Head);
            var index = BuildExpression(cons.Rest.Rest.Head);
            return new MemberExpression(cons.SourceReference, target, index, true,
                ReferenceEquals(symbol, JsSymbols.OptionalGetIndex));
        }

        if (ReferenceEquals(symbol, JsSymbols.SetProperty))
        {
            var target = BuildExpression(cons.Rest.Head);
            var propertyNode = cons.Rest.Rest.Head;
            var property = BuildExpression(propertyNode);
            var isComputed = propertyNode is Cons;
            var value = BuildExpression(cons.Rest.Rest.Rest.Head);
            return new PropertyAssignmentExpression(cons.SourceReference, target, property, value, isComputed);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetIndex))
        {
            var target = BuildExpression(cons.Rest.Head);
            var index = BuildExpression(cons.Rest.Rest.Head);
            var value = BuildExpression(cons.Rest.Rest.Rest.Head);
            return new IndexAssignmentExpression(cons.SourceReference, target, index, value);
        }

        if (ReferenceEquals(symbol, JsSymbols.Negate))
        {
            return new UnaryExpression(cons.SourceReference, "-", BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.UnaryPlus))
        {
            return new UnaryExpression(cons.SourceReference, "+", BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.Not))
        {
            return new UnaryExpression(cons.SourceReference, "!", BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.Typeof))
        {
            return new UnaryExpression(cons.SourceReference, "typeof", BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.Void))
        {
            return new UnaryExpression(cons.SourceReference, "void", BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.Delete))
        {
            return new UnaryExpression(cons.SourceReference, "delete", BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.Await))
        {
            return new AwaitExpression(cons.SourceReference, BuildExpression(cons.Rest.Head));
        }

        if (ReferenceEquals(symbol, JsSymbols.Yield))
        {
            return new YieldExpression(cons.SourceReference, BuildExpression(cons.Rest.Head), false);
        }

        if (ReferenceEquals(symbol, JsSymbols.YieldStar))
        {
            return new YieldExpression(cons.SourceReference, BuildExpression(cons.Rest.Head), true);
        }

        if (ReferenceEquals(symbol, JsSymbols.Ternary))
        {
            var test = BuildExpression(cons.Rest.Head);
            var thenExpr = BuildExpression(cons.Rest.Rest.Head);
            var elseExpr = BuildExpression(cons.Rest.Rest.Rest.Head);
            return new ConditionalExpression(cons.SourceReference, test, thenExpr, elseExpr);
        }

        if (ReferenceEquals(symbol, JsSymbols.Lambda) || ReferenceEquals(symbol, JsSymbols.Generator) ||
            ReferenceEquals(symbol, JsSymbols.AsyncExpr))
        {
            var name = cons.Rest.Head as Symbol;
            var parameters = BuildFunctionParameters(ExpectCons(cons.Rest.Rest.Head, cons.SourceReference));
            var body = BuildBlock(ExpectCons(cons.Rest.Rest.Rest.Head, cons.SourceReference));
            var isAsync = ReferenceEquals(symbol, JsSymbols.AsyncExpr);
            var isGenerator = ReferenceEquals(symbol, JsSymbols.Generator);
            return new FunctionExpression(cons.SourceReference, name, parameters, body, isAsync, isGenerator);
        }

        if (KnownOperators.Contains(symbol.Name))
        {
            return BuildOperatorExpression(cons, symbol.Name);
        }

        return new UnknownExpression(cons.SourceReference, cons);
    }

    private ExpressionNode BuildOperatorExpression(Cons cons, string opName)
    {
        return opName switch
        {
            "++prefix" => new UnaryExpression(cons.SourceReference, "++", BuildExpression(cons.Rest.Head), true),
            "--prefix" => new UnaryExpression(cons.SourceReference, "--", BuildExpression(cons.Rest.Head), true),
            "++postfix" => new UnaryExpression(cons.SourceReference, "++", BuildExpression(cons.Rest.Head), false),
            "--postfix" => new UnaryExpression(cons.SourceReference, "--", BuildExpression(cons.Rest.Head), false),
            "~" => new UnaryExpression(cons.SourceReference, "~", BuildExpression(cons.Rest.Head), true),
            "," => new SequenceExpression(cons.SourceReference, BuildExpression(cons.Rest.Head),
                BuildExpression(cons.Rest.Rest.Head)),
            _ => new BinaryExpression(cons.SourceReference, opName, BuildExpression(cons.Rest.Head),
                BuildExpression(cons.Rest.Rest.Head))
        };
    }

    private ObjectExpression BuildObjectExpression(Cons cons)
    {
        var membersBuilder = ImmutableArray.CreateBuilder<ObjectMember>();
        foreach (var item in cons.Rest)
        {
            if (item is Cons { Head: Symbol head } memberCons)
            {
                if (ReferenceEquals(head, JsSymbols.Property))
                {
                    var key = BuildObjectKey(memberCons.Rest.Head, out var isComputed);
                    var valueExpr = BuildExpression(memberCons.Rest.Rest.Head);
                    if (valueExpr is FunctionExpression fn)
                    {
                        membersBuilder.Add(new ObjectMember(memberCons.SourceReference, ObjectMemberKind.Method, key, null, fn,
                            isComputed, false, null));
                    }
                    else
                    {
                        membersBuilder.Add(new ObjectMember(memberCons.SourceReference, ObjectMemberKind.Property, key,
                            valueExpr, null, isComputed, false, null));
                    }

                    continue;
                }

                if (ReferenceEquals(head, JsSymbols.Getter))
                {
                    var key = BuildObjectKey(memberCons.Rest.Head, out var isComputed);
                    var body = BuildBlock(ExpectCons(memberCons.Rest.Rest.Head, memberCons.SourceReference));
                    var fn = new FunctionExpression(memberCons.SourceReference, null, ImmutableArray<FunctionParameter>.Empty,
                        body, false, false);
                    membersBuilder.Add(new ObjectMember(memberCons.SourceReference, ObjectMemberKind.Getter, key, null, fn,
                        isComputed, false, null));
                    continue;
                }

                if (ReferenceEquals(head, JsSymbols.Setter))
                {
                    var key = BuildObjectKey(memberCons.Rest.Head, out var isComputed);
                    var parameter = memberCons.Rest.Rest.Head as Symbol;
                    var body = BuildBlock(ExpectCons(memberCons.Rest.Rest.Rest.Head, memberCons.SourceReference));
                    var parameters = ImmutableArray.Create(new FunctionParameter(null, parameter, false, null, null));
                    var fn = new FunctionExpression(memberCons.SourceReference, null, parameters, body, false, false);
                    membersBuilder.Add(new ObjectMember(memberCons.SourceReference, ObjectMemberKind.Setter, key, null, fn,
                        isComputed, false, parameter));
                    continue;
                }

                if (ReferenceEquals(head, JsSymbols.Spread))
                {
                    var value = BuildExpression(memberCons.Rest.Head);
                    membersBuilder.Add(new ObjectMember(memberCons.SourceReference, ObjectMemberKind.Spread, string.Empty,
                        value, null, false, false, null));
                    continue;
                }
            }

            membersBuilder.Add(new ObjectMember((item as Cons)?.SourceReference ?? cons.SourceReference,
                ObjectMemberKind.Unknown, item ?? string.Empty, null, null, false, false, null));
        }

        return new ObjectExpression(cons.SourceReference, membersBuilder.ToImmutable());
    }

    private object BuildObjectKey(object? keyNode, out bool isComputed)
    {
        if (keyNode is Cons cons)
        {
            isComputed = true;
            return BuildExpression(cons);
        }

        if (keyNode is Symbol symbol)
        {
            isComputed = true;
            return BuildSymbolExpression(symbol);
        }

        isComputed = false;
        return keyNode ?? string.Empty;
    }

    private static Cons ExpectCons(object? value, SourceReference? fallbackSource)
    {
        if (value is Cons cons)
        {
            return cons;
        }

        var location = fallbackSource?.ToString() ?? "unknown location";
        throw new InvalidOperationException($"Expected list S-expression near {location}.");
    }
}
