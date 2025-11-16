using System;
using System.Collections.Immutable;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Experimental CPS (Continuation-Passing Style) transformer that works directly
/// on the typed AST. The goal is to explore what a typed-first transformation
/// would look like, not to replace the production S-expression implementation.
/// For now only simple async function declarations that immediately <c>return</c>
/// an <c>await</c> expression are supported.
/// </summary>
public sealed class TypedCpsTransformer
{
    private static readonly Symbol PromiseIdentifier = Symbol.Intern("Promise");
    private static readonly Symbol ResolveIdentifier = Symbol.Intern("__resolve");
    private static readonly Symbol RejectIdentifier = Symbol.Intern("__reject");
    private static readonly Symbol AwaitHelperIdentifier = Symbol.Intern("__awaitHelper");
    private static readonly Symbol AwaitValueIdentifier = Symbol.Intern("__value");
    private static readonly Symbol CatchIdentifier = Symbol.Intern("__error");
    private static readonly Symbol ThenIdentifier = Symbol.Intern("then");

    /// <summary>
    /// Returns true when the typed program contains async functions that would
    /// require CPS transformation. The current implementation only looks for
    /// function declarations because that's the only construct the transformer
    /// understands today.
    /// </summary>
    public static bool NeedsTransformation(ProgramNode program)
    {
        foreach (var statement in program.Body)
        {
            if (statement is FunctionDeclaration { Function.IsAsync: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites supported async functions in-place. Unsupported constructs are
    /// left untouched so callers can continue experimenting without risking the
    /// broader pipeline.
    /// </summary>
    public ProgramNode Transform(ProgramNode program)
    {
        var body = TransformImmutableArray(program.Body, TransformStatement, out var changed);
        return changed ? program with { Body = body } : program;
    }

    private StatementNode TransformStatement(StatementNode statement)
    {
        return statement switch
        {
            FunctionDeclaration declaration => TransformFunctionDeclaration(declaration),
            _ => statement
        };
    }

    private StatementNode TransformFunctionDeclaration(FunctionDeclaration declaration)
    {
        var function = TransformFunctionExpression(declaration.Function);
        return ReferenceEquals(function, declaration.Function) ? declaration : declaration with { Function = function };
    }

    private FunctionExpression TransformFunctionExpression(FunctionExpression function)
    {
        if (!function.IsAsync)
        {
            return function;
        }

        if (function.IsGenerator)
        {
            throw new NotSupportedException("Typed CPS transformer does not handle async generators yet.");
        }

        var transformedBody = RewriteAsyncBody(function.Body);
        return function with { IsAsync = false, Body = transformedBody };
    }

    private BlockStatement RewriteAsyncBody(BlockStatement body)
    {
        if (body.Statements.Length != 1 || body.Statements[0] is not ReturnStatement returnStatement)
        {
            throw new NotSupportedException(
                "The typed CPS prototype currently only supports async functions with a single return statement.");
        }

        var resolutionStatements = RewriteReturnExpression(returnStatement.Expression, body.IsStrict);
        var tryBlock = new BlockStatement(null, resolutionStatements, body.IsStrict);
        var catchBodyStatements = ImmutableArray.Create<StatementNode>(
            new ExpressionStatement(null, CreateRejectCall(new IdentifierExpression(null, CatchIdentifier))));
        var catchBody = new BlockStatement(null, catchBodyStatements, body.IsStrict);
        var catchClause = new CatchClause(null, CatchIdentifier, catchBody);
        var tryStatement = new TryStatement(null, tryBlock, catchClause, null);
        var executorStatements = ImmutableArray.Create<StatementNode>(tryStatement);
        var executorBody = new BlockStatement(null, executorStatements, body.IsStrict);
        var executor = new FunctionExpression(null, null,
            [
                new FunctionParameter(null, ResolveIdentifier, false, null, null),
                new FunctionParameter(null, RejectIdentifier, false, null, null)
            ],
            executorBody, false, false);
        var promise = new NewExpression(null, new IdentifierExpression(null, PromiseIdentifier),
            [executor]);
        var returnPromise = new ReturnStatement(null, promise);
        return body with { Statements = [returnPromise] };
    }

    private ImmutableArray<StatementNode> RewriteReturnExpression(ExpressionNode? expression, bool isStrict)
    {
        if (expression is null)
        {
            return
            [
                new ReturnStatement(null, CreateResolveCall(new LiteralExpression(null, null)))
            ];
        }

        if (expression is AwaitExpression awaitExpression)
        {
            var awaited = EnsureSupportedAwaitOperand(awaitExpression.Expression);
            var awaitCall = CreateAwaitHelperCall(awaited);
            var thenInvocation = CreateThenInvocation(awaitCall);
            var expressionStatement = new ExpressionStatement(null, thenInvocation);
            return [expressionStatement];
        }

        return
        [
            new ReturnStatement(null, CreateResolveCall(expression))
        ];
    }

    private ExpressionNode EnsureSupportedAwaitOperand(ExpressionNode expression)
    {
        if (expression is AwaitExpression)
        {
            throw new NotSupportedException("Nested await expressions are not supported by the typed CPS prototype.");
        }

        return expression switch
        {
            FunctionExpression functionExpression => TransformFunctionExpression(functionExpression),
            _ => expression
        };
    }

    private ExpressionNode CreateAwaitHelperCall(ExpressionNode awaited)
    {
        var argument = new CallArgument(awaited.Source, awaited, false);
        return new CallExpression(null, new IdentifierExpression(null, AwaitHelperIdentifier),
            [argument], false);
    }

    private ExpressionNode CreateThenInvocation(ExpressionNode awaitCall)
    {
        var resolveCall = CreateResolveCall(new IdentifierExpression(null, AwaitValueIdentifier));
        var callbackBodyStatements = ImmutableArray.Create<StatementNode>(
            new ReturnStatement(null, resolveCall));
        var callbackBody = new BlockStatement(null, callbackBodyStatements, false);
        var callback = new FunctionExpression(null, null,
            [new FunctionParameter(null, AwaitValueIdentifier, false, null, null)],
            callbackBody, false, false);
        var target = new MemberExpression(null, awaitCall,
            new IdentifierExpression(null, ThenIdentifier), false, false);
        var callbackArgument = new CallArgument(null, callback, false);
        var rejectArgument = new CallArgument(null, new IdentifierExpression(null, RejectIdentifier), false);
        var thenArguments = ImmutableArray.Create(callbackArgument, rejectArgument);
        return new CallExpression(null, target, thenArguments, false);
    }

    private ExpressionNode CreateResolveCall(ExpressionNode value)
    {
        var argument = new CallArgument(value.Source, value, false);
        return new CallExpression(null, new IdentifierExpression(null, ResolveIdentifier),
            [argument], false);
    }

    private ExpressionNode CreateRejectCall(ExpressionNode value)
    {
        var argument = new CallArgument(value.Source, value, false);
        return new CallExpression(null, new IdentifierExpression(null, RejectIdentifier),
            [argument], false);
    }

    private static ImmutableArray<T> TransformImmutableArray<T>(ImmutableArray<T> source, Func<T, T> transformer,
        out bool changed)
    {
        if (source.IsDefaultOrEmpty)
        {
            changed = false;
            return source;
        }

        var builder = ImmutableArray.CreateBuilder<T>(source.Length);
        changed = false;
        foreach (var item in source)
        {
            var transformed = transformer(item);
            builder.Add(transformed);
            if (!ReferenceEquals(item, transformed))
            {
                changed = true;
            }
        }

        return changed ? builder.ToImmutable() : source;
    }
}
