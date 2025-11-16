using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;
using static Asynkron.JsEngine.EvaluationGuards;
using static Asynkron.JsEngine.ExpressionEvaluator;

namespace Asynkron.JsEngine;

internal static class DestructuringEvaluator
{
    internal static void DestructureAndDefine(Cons pattern, object? value, JsEnvironment environment, bool isConst,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArray(pattern, value, environment, isConst, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObject(pattern, value, environment, isConst, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    internal static void DestructureAndDefineFunctionScoped(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArrayFunctionScoped(pattern, value, environment, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObjectFunctionScoped(pattern, value, environment, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    internal static void DestructureArray(Cons pattern, object? value, JsEnvironment environment, bool isConst,
        EvaluationContext context)
    {
        if (value is not JsArray array)
        {
            throw new InvalidOperationException($"Cannot destructure non-array value in array pattern.{GetSourceInfo(context)}");
        }

        var index = 0;
        foreach (var element in pattern.Rest)
        {
            // Skip holes (null elements)
            if (element is null)
            {
                index++;
                continue;
            }

            if (element is not Cons elementCons)
            {
                throw new InvalidOperationException($"Expected pattern element to be a cons.{GetSourceInfo(context)}");
            }

            if (elementCons.Head is not Symbol elementType)
            {
                throw new InvalidOperationException($"Pattern element must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest element
            if (ReferenceEquals(elementType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(elementCons.Rest.Head, "Expected identifier for rest element.", context);
                var restArray = new JsArray();
                for (var i = index; i < array.Items.Count; i++) restArray.Push(array.Items[i]);
                environment.Define(restName, restArray, isConst);
                break;
            }

            // Handle pattern element
            if (!ReferenceEquals(elementType, JsSymbols.PatternElement))
            {
                continue;
            }

            var target = elementCons.Rest.Head;
            var defaultValue = elementCons.Rest.Rest.Head;
            var elementValue = index < array.Items.Count ? array.Items[index] : null;

            // Apply default value if element is undefined
            if (elementValue is null && defaultValue is not null)
            {
                elementValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefine(nestedPattern, elementValue, environment, isConst, context);
                    break;
                case Symbol identifier:
                    environment.Define(identifier, elementValue, isConst);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in array pattern element.{GetSourceInfo(context)}");
            }

            index++;
        }
    }

    internal static void DestructureArrayFunctionScoped(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (value is not JsArray array)
        {
            throw new InvalidOperationException($"Cannot destructure non-array value in array pattern.{GetSourceInfo(context)}");
        }

        var index = 0;
        foreach (var element in pattern.Rest)
        {
            // Skip holes (null elements)
            if (element is null)
            {
                index++;
                continue;
            }

            if (element is not Cons elementCons)
            {
                throw new InvalidOperationException($"Expected pattern element to be a cons.{GetSourceInfo(context)}");
            }

            if (elementCons.Head is not Symbol elementType)
            {
                throw new InvalidOperationException($"Pattern element must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest element
            if (ReferenceEquals(elementType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(elementCons.Rest.Head, "Expected identifier for rest element.", context);
                var restArray = new JsArray();
                for (var i = index; i < array.Items.Count; i++) restArray.Push(array.Items[i]);
                environment.DefineFunctionScoped(restName, restArray, true);
                break;
            }

            // Handle pattern element
            if (!ReferenceEquals(elementType, JsSymbols.PatternElement))
            {
                continue;
            }

            var target = elementCons.Rest.Head;
            var defaultValue = elementCons.Rest.Rest.Head;
            var elementValue = index < array.Items.Count ? array.Items[index] : null;

            // Apply default value if element is undefined
            if (elementValue is null && defaultValue is not null)
            {
                elementValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefineFunctionScoped(nestedPattern, elementValue, environment, context);
                    break;
                case Symbol identifier:
                    environment.DefineFunctionScoped(identifier, elementValue, true);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in array pattern element.{GetSourceInfo(context)}");
            }

            index++;
        }
    }

    internal static void DestructureObject(Cons pattern, object? value, JsEnvironment environment, bool isConst,
        EvaluationContext context)
    {
        if (value is not JsObject obj)
        {
            throw new InvalidOperationException($"Cannot destructure non-object value in object pattern.{GetSourceInfo(context)}");
        }

        var usedKeys = new HashSet<string>();

        foreach (var property in pattern.Rest)
        {
            if (property is not Cons propertyCons)
            {
                throw new InvalidOperationException($"Expected pattern property to be a cons.{GetSourceInfo(context)}");
            }

            if (propertyCons.Head is not Symbol propertyType)
            {
                throw new InvalidOperationException($"Pattern property must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest property
            if (ReferenceEquals(propertyType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(propertyCons.Rest.Head, "Expected identifier for rest property.", context);
                var restObject = new JsObject();
                foreach (var kvp in obj)
                {
                    if (!usedKeys.Contains(kvp.Key))
                    {
                        restObject[kvp.Key] = kvp.Value;
                    }
                }

                environment.Define(restName, restObject, isConst);
                break;
            }

            // Handle pattern property
            if (!ReferenceEquals(propertyType, JsSymbols.PatternProperty))
            {
                continue;
            }

            var sourceName = propertyCons.Rest.Head as string ??
                             throw new InvalidOperationException($"Expected property name in object pattern.{GetSourceInfo(context)}");
            var target = propertyCons.Rest.Rest.Head;
            var defaultValue = propertyCons.Rest.Rest.Rest.Head;

            usedKeys.Add(sourceName);

            var propertyValue = obj.TryGetProperty(sourceName, out var val) ? val : null;

            // Apply default value if property is undefined
            if (propertyValue is null && defaultValue is not null)
            {
                propertyValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefine(nestedPattern, propertyValue, environment, isConst, context);
                    break;
                case Symbol identifier:
                    environment.Define(identifier, propertyValue, isConst);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in object pattern property.{GetSourceInfo(context)}");
            }
        }
    }

    internal static void DestructureObjectFunctionScoped(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (value is not JsObject obj)
        {
            throw new InvalidOperationException($"Cannot destructure non-object value in object pattern.{GetSourceInfo(context)}");
        }

        var usedKeys = new HashSet<string>();

        foreach (var property in pattern.Rest)
        {
            if (property is not Cons propertyCons)
            {
                throw new InvalidOperationException($"Expected pattern property to be a cons.{GetSourceInfo(context)}");
            }

            if (propertyCons.Head is not Symbol propertyType)
            {
                throw new InvalidOperationException($"Pattern property must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest property
            if (ReferenceEquals(propertyType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(propertyCons.Rest.Head, "Expected identifier for rest property.", context);
                var restObject = new JsObject();
                foreach (var kvp in obj)
                {
                    if (!usedKeys.Contains(kvp.Key))
                    {
                        restObject[kvp.Key] = kvp.Value;
                    }
                }

                environment.DefineFunctionScoped(restName, restObject, true);
                break;
            }

            // Handle pattern property
            if (!ReferenceEquals(propertyType, JsSymbols.PatternProperty))
            {
                continue;
            }

            var sourceName = propertyCons.Rest.Head as string ??
                             throw new InvalidOperationException($"Expected property name in object pattern.{GetSourceInfo(context)}");
            var target = propertyCons.Rest.Rest.Head;
            var defaultValue = propertyCons.Rest.Rest.Rest.Head;

            usedKeys.Add(sourceName);

            var propertyValue = obj.TryGetProperty(sourceName, out var val) ? val : null;

            // Apply default value if property is undefined
            if (propertyValue is null && defaultValue is not null)
            {
                propertyValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefineFunctionScoped(nestedPattern, propertyValue, environment, context);
                    break;
                case Symbol identifier:
                    environment.DefineFunctionScoped(identifier, propertyValue, true);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in object pattern property.{GetSourceInfo(context)}");
            }
        }
    }

    // Public method to destructure function parameters (called from JsFunction)
    public static void DestructureParameter(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArrayFunctionScoped(pattern, value, environment, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObjectFunctionScoped(pattern, value, environment, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    internal static void DestructureAssignment(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArrayFunctionScoped(pattern, value, environment, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObjectFunctionScoped(pattern, value, environment, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    // Helper for exceptions with source info

}
