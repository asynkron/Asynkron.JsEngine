using System.Collections;
using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Tests.Helpers;

/// <summary>
/// Utility helpers for parsing, analyzing, and traversing typed ASTs in tests.
/// </summary>
public static class AstTestHelpers
{
    private const BindingFlags ReflectionFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Runs the standard lexer/parser/constant-folding/scope-analysis pipeline used in tests.
    /// Optionally applies CPS transformation when required.
    /// </summary>
    public static AstPipelineResult ParseAndAnalyze(string source, bool applyCps = true)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new TypedAstParser(tokens, source);
        var parsed = parser.ParseProgram();

        var constantFolded = new TypedConstantExpressionTransformer().Transform(parsed);
        var analyzed = new ScopeAnalyzer().Analyze(constantFolded);
        var afterCps = applyCps && TypedCpsTransformer.NeedsTransformation(analyzed)
            ? new TypedCpsTransformer().Transform(analyzed)
            : analyzed;

        return new AstPipelineResult(parsed, analyzed, afterCps);
    }

    /// <summary>
    /// Returns the first node of the requested type in a depth-first walk.
    /// </summary>
    public static T FindFirst<T>(AstNode root, Func<T, bool>? predicate = null) where T : class
    {
        foreach (var node in Walk(root, includeSelf: true))
        {
            if (node is T typed && (predicate is null || predicate(typed)))
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"No node of type {typeof(T).Name} found in AST.");
    }

    /// <summary>
    /// Depth-first traversal of all descendant AST nodes.
    /// </summary>
    public static IEnumerable<AstNode> Walk(AstNode node, bool includeSelf = false)
    {
        if (includeSelf)
        {
            yield return node;
        }

        var stack = new Stack<AstNode>();
        PushChildren(stack, node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            PushChildren(stack, current);
        }
    }

    /// <summary>
    /// Collects child AST nodes for traversal using reflection. This keeps tests resilient to new node types.
    /// </summary>
    private static void PushChildren(Stack<AstNode> stack, AstNode node)
    {
        foreach (var property in node.GetType().GetProperties(ReflectionFlags))
        {
            var value = property.GetValue(node);
            switch (value)
            {
                case null:
                    continue;
                case AstNode child:
                    stack.Push(child);
                    break;
                case IEnumerable enumerable when value is not string:
                    foreach (var element in enumerable)
                    {
                        if (element is AstNode elementNode)
                        {
                            stack.Push(elementNode);
                        }
                    }
                    break;
            }
        }
    }
}

public readonly record struct AstPipelineResult(
    ProgramNode Parsed,
    ProgramNode Analyzed,
    ProgramNode AfterCps);
