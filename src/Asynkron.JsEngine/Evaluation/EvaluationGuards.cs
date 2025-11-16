using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine;

internal static class EvaluationGuards
{
    internal static (IReadOnlyList<object> regularParams, Symbol? restParam) ParseParameterList(Cons list, EvaluationContext context)
    {
        var regularParams = new List<object>();
        Symbol? restParam = null;

        foreach (var item in list)
        {
            // Check if this is a rest parameter (rest symbol paramName)
            if (item is Cons { Head: Symbol head } restCons && ReferenceEquals(head, JsSymbols.Rest))
            {
                restParam = ExpectSymbol(restCons.Rest.Head, "Expected rest parameter name.", context);
                break; // Rest parameter must be last
            }

            // Check if this is a destructuring pattern (array or object pattern)
            if (item is Cons { Head: Symbol patternType } pattern &&
                (ReferenceEquals(patternType, JsSymbols.ArrayPattern) ||
                 ReferenceEquals(patternType, JsSymbols.ObjectPattern)))
            {
                regularParams.Add(pattern);
            }
            else
            {
                regularParams.Add(ExpectSymbol(item, "Expected symbol or pattern in parameter list.", context));
            }
        }

        return (regularParams, restParam);
    }

    internal static Symbol ExpectSymbol(object? value, string message, EvaluationContext context)
    {
        return value as Symbol ?? throw new InvalidOperationException($"{message}{GetSourceInfo(context)}");
    }

    internal static Cons ExpectCons(object? value, string message, EvaluationContext context)
    {
        return value as Cons ?? throw new InvalidOperationException($"{message}{GetSourceInfo(context)}");
    }

    internal static SuperBinding ExpectSuperBinding(JsEnvironment environment, EvaluationContext context)
    {
        object? value;
        try
        {
            value = environment.Get(JsSymbols.Super);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Super is not available in this context.{GetSourceInfo(context)}", ex);
        }

        return value as SuperBinding ?? throw new InvalidOperationException($"Super is not available in this context.{GetSourceInfo(context)}");
    }

    internal static string GetSourceInfo(EvaluationContext context)
    {
        if (context.SourceReference is null)
        {
            return " (no source reference)";
        }

        var src = context.SourceReference;
        var snippet = src.GetText();
        if (snippet.Length > 50)
        {
            snippet = snippet[..47] + "...";
        }

        return $" at {src} (snippet: '{snippet}') Source: '{src.Source}' Start: {src.StartPosition} End: {src.EndPosition}";
    }

    internal static SourceReference? GetSourceReference(Cons? cons)
    {
        if (cons == null)
        {
            return null;
        }

        if (cons.SourceReference != null)
        {
            return cons.SourceReference;
        }

        var current = cons.Origin;
        while (current != null)
        {
            if (current.SourceReference != null)
            {
                return current.SourceReference;
            }

            current = current.Origin;
        }

        return null;
    }

    internal static string FormatErrorMessage(string message, Cons? cons)
    {
        var sourceRef = GetSourceReference(cons);
        if (sourceRef == null)
        {
            return message;
        }

        message += $" at {sourceRef}";
        var sourceText = sourceRef.GetText();
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            message += $": {sourceText}";
        }

        return message;
    }
}
