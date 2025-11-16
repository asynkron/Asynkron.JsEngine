using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;
using static Asynkron.JsEngine.DestructuringEvaluator;
using static Asynkron.JsEngine.EvaluationGuards;

namespace Asynkron.JsEngine;

internal static class ExpressionEvaluator
{
    internal static object? EvaluateExpression(object? expression, JsEnvironment environment, EvaluationContext context)
    {
        switch (expression)
        {
            case null:
                return null;
            case bool b:
                return b;
            case string s:
                return s;
            case double d:
                return d;
            case Symbol symbol:
                // Special case: undefined is a reserved symbol that evaluates to itself
                if (ReferenceEquals(symbol, JsSymbols.Undefined))
                {
                    return symbol;
                }

                return environment.Get(symbol);
            case Cons cons:
                return EvaluateCompositeExpression(cons, environment, context);
            default:
                return expression;
        }
    }

    private static object? EvaluateCompositeExpression(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // Set source reference for error reporting
        context.SourceReference = cons.SourceReference;
        // Debug: Console.WriteLine($"Setting SourceReference: {context.SourceReference} for {cons.Head}");

        if (cons.Head is not Symbol symbol)
        {
            throw new InvalidOperationException($"Composite expression must begin with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(symbol, JsSymbols.Assign))
        {
            var target = ExpectSymbol(cons.Rest.Head, "Expected assignment target.", context);
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            environment.Assign(target, value);
            return value;
        }

        if (ReferenceEquals(symbol, JsSymbols.DestructuringAssignment))
        {
            var pattern = ExpectCons(cons.Rest.Head, "Expected destructuring pattern.", context);
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            DestructureAssignment(pattern, value, environment, context);
            return value;
        }

        if (ReferenceEquals(symbol, JsSymbols.Call))
        {
            return EvaluateCall(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.OptionalCall))
        {
            return EvaluateOptionalCall(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ArrayLiteral))
        {
            return EvaluateArrayLiteral(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.TemplateLiteral))
        {
            return EvaluateTemplateLiteral(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.TaggedTemplate))
        {
            return EvaluateTaggedTemplate(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ObjectLiteral))
        {
            return EvaluateObjectLiteral(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.GetIndex))
        {
            return EvaluateGetIndex(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.OptionalGetIndex))
        {
            return EvaluateOptionalGetIndex(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetIndex))
        {
            return EvaluateSetIndex(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.GetProperty))
        {
            return EvaluateGetProperty(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.OptionalGetProperty))
        {
            return EvaluateOptionalGetProperty(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetProperty))
        {
            return EvaluateSetProperty(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.New))
        {
            return EvaluateNew(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Negate))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment, context);
            // Handle BigInt negation
            if (operand is JsBigInt bigInt)
            {
                return -bigInt;
            }

            return -ToNumber(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.UnaryPlus))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment, context);
            // Handle BigInt conversion
            if (operand is JsBigInt)
            {
                // Unary plus on BigInt throws TypeError in JavaScript
                throw new Exception("Cannot convert a BigInt value to a number");
            }

            return ToNumber(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Not))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment, context);
            return !IsTruthy(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Typeof))
        {
            // Special case: typeof can be used on undeclared variables without throwing
            // Check if the operand is a simple identifier (Symbol) that doesn't exist
            var operandExpression = cons.Rest.Head;
            if (operandExpression is Symbol operandSymbol &&
                !ReferenceEquals(operandSymbol, JsSymbols.Undefined))
            {
                // Try to get the value without throwing
                return !environment.TryGet(operandSymbol, out var value) ?
                    // Symbol doesn't exist, return "undefined" for typeof
                    "undefined" :
                    // Symbol exists, return its typeof
                    GetTypeofString(value);
            }

            // For non-symbol operands (e.g., typeof (x + y)), evaluate normally
            var operand = EvaluateExpression(operandExpression, environment, context);
            return GetTypeofString(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Void))
        {
            // The void operator evaluates its operand and returns undefined
            var operandExpression = cons.Rest.Head;
            EvaluateExpression(operandExpression, environment, context);
            return JsSymbols.Undefined;
        }

        if (ReferenceEquals(symbol, JsSymbols.Delete))
        {
            // The delete operator deletes a property from an object
            var operandExpression = cons.Rest.Head;

            // Check if operand is a property access or index access
            if (operandExpression is Cons { Head: Symbol operandSymbol } operandCons)
            {
                // delete obj.prop or delete obj[key]
                if (ReferenceEquals(operandSymbol, JsSymbols.GetProperty))
                {
                    // delete obj.prop
                    var target = EvaluateExpression(operandCons.Rest.Head, environment, context);
                    var propertyNameObj = operandCons.Rest.Rest.Head;
                    if (target is not JsObject jsObj)
                    {
                        return true;
                    }

                    // Property name can be a string or Symbol
                    var propertyName = propertyNameObj is Symbol sym ? sym.Name : propertyNameObj?.ToString() ?? "";
                    jsObj.Remove(propertyName);
                    return true;
                }

                if (ReferenceEquals(operandSymbol, JsSymbols.GetIndex))
                {
                    // delete obj[key]
                    var target = EvaluateExpression(operandCons.Rest.Head, environment, context);
                    var key = EvaluateExpression(operandCons.Rest.Rest.Head, environment, context);

                    switch (target)
                    {
                        // Handle array deletion - set element to undefined to create a hole
                        case JsArray jsArray when TryConvertToIndex(key, out var arrayIndex):
                            jsArray.SetElement(arrayIndex, JsSymbols.Undefined);
                            return true;
                        case JsObject jsObj:
                        {
                            var keyStr = ToJsString(key);
                            jsObj.Remove(keyStr);
                            return true;
                        }
                        default:
                            return true;
                    }
                }
            }

            // For other cases (like delete of a variable or non-property access), evaluate and return true
            // In non-strict mode, delete always returns true for non-property-references
            EvaluateExpression(operandExpression, environment, context);
            return true;
        }

        if (ReferenceEquals(symbol, JsSymbols.Lambda))
        {
            var maybeName = cons.Rest.Head as Symbol;
            var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected lambda parameters list.", context);
            var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected lambda body block.", context);
        var (regularParams, restParam) = ParseParameterList(parameters, context);
            return new JsFunction(maybeName, regularParams, restParam, body, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Generator))
        {
            // Handle generator expressions like: function*() { yield 1; }
            var maybeName = cons.Rest.Head as Symbol;
            var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected generator parameters list.", context);
            var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected generator body block.", context);
            return new GeneratorFactory(maybeName, parameters, body, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Yield))
        {
            // Evaluate the value to yield
            var value = EvaluateExpression(cons.Rest.Head, environment, context);

            // Check if we have a yield tracker (only present in generator context)
            try
            {
                var trackerObj = environment.Get(Symbol.Intern("__yieldTracker__"));
                if (trackerObj is not YieldTracker tracker || !tracker.ShouldYield())
                {
                    return null;
                }

                // This is the yield we should stop at
                context.SetYield(value);
                return value;

                // Otherwise, this yield was already processed - skip it and return null
                // (the value is not meaningful when skipping)
            }
            catch (InvalidOperationException)
            {
                // No tracker found - yield is outside a generator (shouldn't happen)
                throw new InvalidOperationException(FormatErrorMessage("yield can only be used inside a generator function", cons) + ".");
            }
        }

        if (!ReferenceEquals(symbol, JsSymbols.Ternary))
        {
            return EvaluateBinary(cons, environment, symbol, context);
        }

        var condition = EvaluateExpression(cons.Rest.Head, environment, context);
        var thenBranch = cons.Rest.Rest.Head;
        var elseBranch = cons.Rest.Rest.Rest.Head;
        return IsTruthy(condition)
            ? EvaluateExpression(thenBranch, environment, context)
            : EvaluateExpression(elseBranch, environment, context);

    }

    private static object? EvaluateCall(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var calleeExpression = cons.Rest.Head;
        var (callee, thisValue) = ResolveCallee(calleeExpression, environment, context);
        if (callee is not IJsCallable callable)
        {
            var calleeType = callee?.GetType().Name ?? "null";
            var calleeValue = callee?.ToString() ?? "null";
            var errorMessage = FormatErrorMessage($"Attempted to call a non-callable value of type {calleeType}: {calleeValue}", cons);
            throw new InvalidOperationException(errorMessage + ".");
        }

        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
            // Check if this is a spread argument
        {
            if (argumentExpression is Cons { Head: Symbol head } spreadCons && ReferenceEquals(head, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(spreadCons.Rest.Head, environment, context);
                // Spread arrays
                if (spreadValue is JsArray array)
                {
                    foreach (var element in array.Items)
                    {
                        arguments.Add(element);
                    }
                }
                else
                {
                    throw new InvalidOperationException(FormatErrorMessage("Spread operator can only be applied to arrays", spreadCons) + ".");
                }
            }
            else
            {
                arguments.Add(EvaluateExpression(argumentExpression, environment, context));
            }
        }

        try
        {
            // If this is an environment-aware callable, set the calling environment
            if (callable is IJsEnvironmentAwareCallable envAware)
            {
                envAware.CallingJsEnvironment = environment;
            }

            // If this is a debug-aware function, set the environment and context
            if (callable is not DebugAwareHostFunction debugFunc)
            {
                return callable.Invoke(arguments, thisValue);
            }

            debugFunc.CurrentJsEnvironment = environment;
            debugFunc.CurrentContext = context;

            return callable.Invoke(arguments, thisValue);
        }
        catch (ThrowSignal signal)
        {
            // Propagate the throw to the calling context
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static (object? Callee, object? ThisValue) ResolveCallee(object? calleeExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        switch (calleeExpression)
        {
            case Symbol superSymbol when ReferenceEquals(superSymbol, JsSymbols.Super):
            {
                var binding = ExpectSuperBinding(environment, context);
                if (binding.Constructor is null)
                {
                    throw new InvalidOperationException(FormatErrorMessage("Super constructor is not available in this context",
                        calleeExpression as Cons) + ".");
                }

                return (binding.Constructor, binding.ThisValue);
            }
            case Cons { Head: Symbol head } propertyCons when ReferenceEquals(head, JsSymbols.GetProperty):
            {
                var targetExpression = propertyCons.Rest.Head;
                var propertyName = propertyCons.Rest.Rest.Head as string
                                   ?? throw new InvalidOperationException(
                                       $"Property access requires a string name.{GetSourceInfo(context)}");

                if (targetExpression is Symbol targetSymbol && ReferenceEquals(targetSymbol, JsSymbols.Super))
                {
                    var binding = ExpectSuperBinding(environment, context);
                    return binding.TryGetProperty(propertyName, out var superValue)
                        ? (superValue, binding.ThisValue)
                        : (null, binding.ThisValue);
                }

                var target = EvaluateExpression(targetExpression, environment, context);
                return TryGetPropertyValue(target, propertyName, out var value)
                    ? (value, target)
                    : (null, target);
            }
            case Cons { Head: Symbol indexHead } indexCons when
                ReferenceEquals(indexHead, JsSymbols.GetIndex):
            {
                var targetExpression = indexCons.Rest.Head;
                var indexExpression = indexCons.Rest.Rest.Head;

                if (targetExpression is Symbol indexTargetSymbol && ReferenceEquals(indexTargetSymbol, JsSymbols.Super))
                {
                    var binding = ExpectSuperBinding(environment, context);
                    var superIndex = EvaluateExpression(indexExpression, environment, context);
                    var superPropertyName = ToPropertyName(superIndex)
                                            ?? throw new InvalidOperationException(
                                                $"Unsupported index value '{superIndex}'.{GetSourceInfo(context)}");

                    return binding.TryGetProperty(superPropertyName, out var superValue)
                        ? (superValue, binding.ThisValue)
                        : (null, binding.ThisValue);
                }

                var target = EvaluateExpression(targetExpression, environment, context);
                var index = EvaluateExpression(indexExpression, environment, context);

                if (target is JsArray jsArray && TryConvertToIndex(index, out var arrayIndex))
                {
                    return (jsArray.GetElement(arrayIndex), target);
                }

                var propertyName = ToPropertyName(index);
                if (propertyName is not null && TryGetPropertyValue(target, propertyName, out var value))
                {
                    return (value, target);
                }

                return (null, target);
            }
            default:
                return (EvaluateExpression(calleeExpression, environment, context), null);
        }
    }

    private static object EvaluateArrayLiteral(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var array = new JsArray();
        foreach (var elementExpression in cons.Rest)
            // Check if this is a spread element
        {
            if (elementExpression is Cons { Head: Symbol head } spreadCons && ReferenceEquals(head, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(spreadCons.Rest.Head, environment, context);
                // Spread arrays
                if (spreadValue is JsArray spreadArray)
                {
                    foreach (var arrayElement in spreadArray.Items)
                    {
                        array.Push(arrayElement);
                    }
                }
                else
                {
                    throw new InvalidOperationException(FormatErrorMessage("Spread operator can only be applied to arrays", spreadCons) + ".");
                }
            }
            else
            {
                array.Push(EvaluateExpression(elementExpression, environment, context));
            }
        }

        // Add standard array methods
        StandardLibrary.AddArrayMethods(array);

        return array;
    }

    private static object EvaluateTemplateLiteral(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var result = new System.Text.StringBuilder();

        foreach (var part in cons.Rest)
        {
            if (part is string str)
            {
                result.Append(str);
            }
            else
            {
                // Evaluate the expression and convert to string
                var value = EvaluateExpression(part, environment, context);
                result.Append(ConvertToString(value));
            }
        }

        return result.ToString();
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            //TODO: [object, Object] ??
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value.ToString() ?? ""
        };
    }

    private static object? EvaluateTaggedTemplate(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // Format: (taggedTemplate tag stringsArray rawStringsArray expr1 expr2 ...)
        var rest = cons.Rest;

        // Get the tag function
        var tagExpr = rest.Head;
        var tagFunction = EvaluateExpression(tagExpr, environment, context);

        if (tagFunction is not IJsCallable callable)
        {
            throw new InvalidOperationException(FormatErrorMessage("Tag in tagged template must be a function", cons) + ".");
        }

        rest = rest.Rest;

        // Get the strings array expression
        var stringsArrayExpr = rest.Head;
        if (EvaluateExpression(stringsArrayExpr, environment, context) is not JsArray stringsArray)
        {
            throw new InvalidOperationException(FormatErrorMessage("Tagged template strings array is invalid", cons) + ".");
        }

        rest = rest.Rest;

        // Get the raw strings array expression
        var rawStringsArrayExpr = rest.Head;
        if (EvaluateExpression(rawStringsArrayExpr, environment, context) is not JsArray rawStringsArray)
        {
            throw new InvalidOperationException(FormatErrorMessage("Tagged template raw strings array is invalid", cons) + ".");
        }

        rest = rest.Rest;

        // Create a template object with a 'raw' property
        var templateObj = new JsObject();

        // Copy strings array properties
        for (var i = 0; i < stringsArray.Items.Count; i++) templateObj[i.ToString(CultureInfo.InvariantCulture)] = stringsArray.Items[i];
        templateObj["length"] = (double)stringsArray.Items.Count;

        // Add raw property
        templateObj["raw"] = rawStringsArray;

        // Evaluate the substitution expressions
        var substitutions = new List<object?> { templateObj };
        foreach (var exprNode in rest)
        {
            var value = EvaluateExpression(exprNode, environment, context);
            substitutions.Add(value);
        }

        // Call the tag function with the template object and substitutions
        try
        {
            return callable.Invoke(substitutions, null);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static object EvaluateObjectLiteral(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var result = new JsObject();
        foreach (var propertyExpression in cons.Rest)
        {
            var propertyCons = ExpectCons(propertyExpression, "Expected property description in object literal.", context);
            var propertyTag = propertyCons.Head as Symbol
                              ?? throw new InvalidOperationException(
                                  $"Object literal entries must start with a symbol.{GetSourceInfo(context)}");

            // Handle spread operator (future feature for object rest/spread)
            if (ReferenceEquals(propertyTag, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(propertyCons.Rest.Head, environment, context);
                if (spreadValue is JsObject spreadObj)
                {
                    foreach (var kvp in spreadObj)
                    {
                        result.SetProperty(kvp.Key, kvp.Value);
                    }
                }

                continue;
            }

            // Property name can be a string literal or an expression (for computed properties)
            var propertyNameOrExpression = propertyCons.Rest.Head;
            string propertyName;

            if (propertyNameOrExpression is string str)
            {
                propertyName = str;
            }
            else
            {
                // Computed property name - evaluate the expression
                var propertyNameValue = EvaluateExpression(propertyNameOrExpression, environment, context);
                propertyName = ToPropertyName(propertyNameValue)
                               ?? throw new InvalidOperationException(
                                   $"Cannot convert '{propertyNameValue}' to property name.{GetSourceInfo(context)}");
            }

            if (ReferenceEquals(propertyTag, JsSymbols.Property))
            {
                var valueExpression = propertyCons.Rest.Rest.Head;
                var value = EvaluateExpression(valueExpression, environment, context);
                result.SetProperty(propertyName, value);
            }
            else if (ReferenceEquals(propertyTag, JsSymbols.Getter))
            {
                // (getter "name" (block ...))
                var body = ExpectCons(propertyCons.Rest.Rest.Head, "Expected getter body.", context);
                var getter = new JsFunction(null, [], null, body, environment);
                result.SetGetter(propertyName, getter);
            }
            else if (ReferenceEquals(propertyTag, JsSymbols.Setter))
            {
                // (setter "name" param (block ...))
                var param = ExpectSymbol(propertyCons.Rest.Rest.Head, "Expected setter parameter.", context);
                var body = ExpectCons(propertyCons.Rest.Rest.Rest.Head, "Expected setter body.", context);
                var paramList = new[] { param };
                var setter = new JsFunction(null, paramList, null, body, environment);
                result.SetSetter(propertyName, setter);
            }
            else
            {
                throw new InvalidOperationException($"Unknown property type: {propertyTag}{GetSourceInfo(context)}");
            }
        }

        return result;
    }

    private static object? EvaluateGetProperty(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property access requires a string name.{GetSourceInfo(context)}");

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment, context);
            if (binding.TryGetProperty(propertyName, out var superValue))
            {
                return superValue;
            }

            throw new InvalidOperationException($"Cannot read property '{propertyName}' from super prototype.{GetSourceInfo(context)}");
        }

        var target = EvaluateExpression(targetExpression, environment, context);
        if (TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        // Return undefined for non-existent properties (JavaScript behavior)
        return JsSymbols.Undefined;
    }

    private static object? EvaluateSetProperty(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property assignment requires a string name.{GetSourceInfo(context)}");

         if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
         {
             throw new InvalidOperationException($"Assigning through super is not supported in this interpreter.{GetSourceInfo(context)}");
         }

         var valueExpression = cons.Rest.Rest.Rest.Head;
        var target = EvaluateExpression(targetExpression, environment, context);
        var value = EvaluateExpression(valueExpression, environment, context);
        AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static object? EvaluateOptionalGetProperty(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property access requires a string name.{GetSourceInfo(context)}");

        var target = EvaluateExpression(targetExpression, environment, context);

        // If target is null or undefined, return undefined
        if (IsNullish(target))
        {
            return JsSymbols.Undefined;
        }

        if (TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        return JsSymbols.Undefined;
    }

    private static object? EvaluateOptionalGetIndex(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;

        var target = EvaluateExpression(targetExpression, environment, context);

        // If target is null or undefined, return undefined
        if (IsNullish(target))
        {
            return JsSymbols.Undefined;
        }

        var indexValue = EvaluateExpression(indexExpression, environment, context);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            return jsArray.GetElement(arrayIndex);
        }

        if (target is TypedArrayBase typedArray && TryConvertToIndex(indexValue, out var typedIndex))
        {
            return typedArray.GetElement(typedIndex);
        }

        var propertyName = ToPropertyName(indexValue);
        if (propertyName is not null && TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        return JsSymbols.Undefined;
    }

    private static object? EvaluateOptionalCall(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var calleeExpression = cons.Rest.Head;

        var callee = EvaluateExpression(calleeExpression, environment, context);

        // If callee is null or undefined, return undefined
        if (IsNullish(callee))
        {
            return JsSymbols.Undefined;
        }

        // Evaluate arguments
        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
        {
            if (argumentExpression is Cons { Head: Symbol sym } spreadCons && ReferenceEquals(sym, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(spreadCons.Rest.Head, environment, context);
                if (spreadValue is JsArray array)
                {
                    foreach (var element in array.Items)
                    {
                        arguments.Add(element);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Spread operator can only be applied to arrays.{GetSourceInfo(context)}");
                }
            }
            else
            {
                arguments.Add(EvaluateExpression(argumentExpression, environment, context));
            }
        }

        if (callee is not IJsCallable callable)
        {
            return JsSymbols.Undefined;
        }

        try
        {
            return callable.Invoke(arguments, null);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static bool IsNullish(object? value)
    {
        return value is null || (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined));
    }

    private static object? EvaluateGetIndex(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;

        if (targetExpression is Symbol superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment, context);
            var superIndexValue = EvaluateExpression(indexExpression, environment, context);
            var superPropertyName = ToPropertyName(superIndexValue)
                                    ?? throw new InvalidOperationException(
                                        $"Unsupported index value '{superIndexValue}'.{GetSourceInfo(context)}");

            if (binding.TryGetProperty(superPropertyName, out var superPropertyValue))
            {
                return superPropertyValue;
            }

            throw new InvalidOperationException($"Cannot read property '{superPropertyName}' from super prototype.{GetSourceInfo(context)}");
        }

        var target = EvaluateExpression(targetExpression, environment, context);
        var indexValue = EvaluateExpression(indexExpression, environment, context);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            return jsArray.GetElement(arrayIndex);
        }

        if (target is TypedArrayBase typedArray && TryConvertToIndex(indexValue, out var typedIndex))
        {
            return typedArray.GetElement(typedIndex);
        }

        var propertyName = ToPropertyName(indexValue)
                           ?? throw new InvalidOperationException($"Unsupported index value '{indexValue}'.{GetSourceInfo(context)}");

        if (TryGetPropertyValue(target, propertyName, out var propertyValue))
        {
            return propertyValue;
        }

        // Return undefined for non-existent properties (JavaScript behavior)
        return JsSymbols.Undefined;
    }

    private static object? EvaluateSetIndex(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;
        var valueExpression = cons.Rest.Rest.Rest.Head;

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            throw new InvalidOperationException($"Assigning through super is not supported in this interpreter.{GetSourceInfo(context)}");
        }

        var target = EvaluateExpression(targetExpression, environment, context);
        var indexValue = EvaluateExpression(indexExpression, environment, context);
        var value = EvaluateExpression(valueExpression, environment, context);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            jsArray.SetElement(arrayIndex, value);
            return value;
        }

        if (target is TypedArrayBase typedArray && TryConvertToIndex(indexValue, out var typedIndex))
        {
            var numValue = value switch
            {
                double d => d,
                int i => (double)i,
                _ => 0.0
            };
            typedArray.SetElement(typedIndex, numValue);
            return value;
        }

        var propertyName = ToPropertyName(indexValue)
                           ?? throw new InvalidOperationException($"Unsupported index value '{indexValue}'.{GetSourceInfo(context)}");

        AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static object? EvaluateNew(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var constructorExpression = cons.Rest.Head;
        var constructor = EvaluateExpression(constructorExpression, environment, context);
        if (constructor is not IJsCallable callable)
        {
            var constructorType = constructor?.GetType().Name ?? "null";
            var constructorValue = constructor?.ToString() ?? "null";
            var expressionStr = constructorExpression?.ToString() ?? "null";
            throw new InvalidOperationException(
                FormatErrorMessage($"Attempted to construct with a non-callable value (expression: {expressionStr}, type: {constructorType}, value: {constructorValue})", cons) + ".");
        }

        var instance = new JsObject();
        if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject prototypeObject)
        {
            instance.SetPrototype(prototypeObject);
        }

        // Initialize private fields from this class and all parent classes
        InitializePrivateFields(constructor, instance, environment, context);

        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
        {
            arguments.Add(EvaluateExpression(argumentExpression, environment, context));
        }

        try
        {
            var result = callable.Invoke(arguments, instance);
            return result switch
            {
                JsArray jsArray => jsArray,
                JsObject jsObject => jsObject,
                JsMap jsMap => jsMap,
                JsSet jsSet => jsSet,
                JsWeakMap jsWeakMap => jsWeakMap,
                JsWeakSet jsWeakSet => jsWeakSet,
                JsArrayBuffer buffer => buffer,
                JsDataView dataView => dataView,
                TypedArrayBase typedArray => typedArray,
                IDictionary<string, object?> dictionary => dictionary,
                _ => instance
            };
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static void InitializePrivateFields(object? constructor, JsObject instance, JsEnvironment environment,
        EvaluationContext context)
    {
        // First, initialize parent class private and public fields (if any)
        if (constructor is JsFunction jsFunc && TryGetPropertyValue(constructor, "__proto__", out var parent) &&
            parent is not null)
        {
            InitializePrivateFields(parent, instance, environment, context);
        }

        // Then initialize this class's private and public fields
        if (!TryGetPropertyValue(constructor, "__privateFields__", out var privateFieldsValue) ||
            privateFieldsValue is not Cons privateFieldsList)
        {
            return;
        }

        foreach (var fieldExpression in privateFieldsList)
        {
            var fieldCons = ExpectCons(fieldExpression, "Expected field definition.", context);
            var tag = ExpectSymbol(fieldCons.Head, "Expected field tag.", context);

            if (!ReferenceEquals(tag, JsSymbols.PrivateField) && !ReferenceEquals(tag, JsSymbols.PublicField))
            {
                continue;
            }

            var fieldName = fieldCons.Rest.Head as string
                            ?? throw new InvalidOperationException($"Expected field name.{GetSourceInfo(context)}");
            var initializer = fieldCons.Rest.Rest.Head;

            object? initialValue = null;
            if (initializer is not null)
            {
                // Create a temporary environment with 'this' bound to the instance
                var initEnv = new JsEnvironment(environment);
                initEnv.Define(JsSymbols.This, instance);
                initialValue = EvaluateExpression(initializer, initEnv, context);
            }

            instance.SetProperty(fieldName, initialValue);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? EvaluateBinary(Cons cons, JsEnvironment environment, Symbol operatorSymbol,
        EvaluationContext context)
    {
        var leftExpression = cons.Rest.Head;
        var operatorName = operatorSymbol.Name;

        // Handle unary operators (only have left operand)
        switch (operatorName)
        {
            case "~":
            {
                var operand = EvaluateExpression(leftExpression, environment, context);
                try
                {
                    return BitwiseNot(operand);
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(ex.Message + GetSourceInfo(context), ex);
                }
            }
            case "++prefix":
                return IncrementPrefix(leftExpression, environment, context);
            case "--prefix":
                return DecrementPrefix(leftExpression, environment, context);
            case "++postfix":
                return IncrementPostfix(leftExpression, environment, context);
            case "--postfix":
                return DecrementPostfix(leftExpression, environment, context);
        }

        // Binary operators have both left and right
        var rightExpression = cons.Rest.Rest.Head;
        var left = EvaluateExpression(leftExpression, environment, context);

        switch (operatorName)
        {
            case "&&":
                return IsTruthy(left) ? EvaluateExpression(rightExpression, environment, context) : left;
            case "||":
                return IsTruthy(left) ? left : EvaluateExpression(rightExpression, environment, context);
            case "??":
            {
                var leftIsNullish = left is null || (left is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined));
                return leftIsNullish ? EvaluateExpression(rightExpression, environment, context) : left;
            }
            case ",":
                // Comma operator: evaluate left (discard result), then evaluate and return right
                return EvaluateExpression(rightExpression, environment, context);
            case "===":
            {
                var rightStrict = EvaluateExpression(rightExpression, environment, context);
                return StrictEquals(left, rightStrict);
            }
            case "!==":
            {
                var rightStrict = EvaluateExpression(rightExpression, environment, context);
                return !StrictEquals(left, rightStrict);
            }
        }

        var right = EvaluateExpression(rightExpression, environment, context);

        try
        {
            return operatorName switch
            {
                "+" => Add(left, right),
                "-" => Subtract(left, right),
                "*" => Multiply(left, right),
                "**" => Power(left, right),
                "/" => Divide(left, right),
                "%" => Modulo(left, right),
                "&" => BitwiseAnd(left, right),
                "|" => BitwiseOr(left, right),
                "^" => BitwiseXor(left, right),
                "<<" => LeftShift(left, right),
                ">>" => RightShift(left, right),
                ">>>" => UnsignedRightShift(left, right),
                "==" => LooseEquals(left, right),
                "!=" => !LooseEquals(left, right),
                ">" => GreaterThan(left, right),
                ">=" => GreaterThanOrEqual(left, right),
                "<" => LessThan(left, right),
                "<=" => LessThanOrEqual(left, right),
                "in" => InOperator(left, right),
                "instanceof" => InstanceofOperator(left, right),
                _ => throw new InvalidOperationException($"Unsupported operator '{operatorName}'.{GetSourceInfo(context)}")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(ex.Message + GetSourceInfo(context), ex);
        }
    }

    internal static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    internal static double ToNumber(this object? value)
    {
        return value switch
        {
            null => 0,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => double.NaN,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            short s => s,
            ushort us => us,
            byte b => b,
            sbyte sb => sb,
            bool flag => flag ? 1 : 0,
            string str => StringToNumber(str),
            JsArray arr => ArrayToNumber(arr),
            JsObject => double.NaN, // Objects convert to NaN
            _ => throw new InvalidOperationException($"Cannot convert value '{value}' to a number.")
        };
    }

    private static double StringToNumber(string str)
    {
        // Empty string converts to 0
        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        // Trim whitespace
        var trimmed = str.Trim();

        // Whitespace-only string converts to 0
        if (string.IsNullOrEmpty(trimmed))
        {
            return 0;
        }

        // Try to parse the trimmed string
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Invalid number format converts to NaN
        return double.NaN;
    }

    private static double ArrayToNumber(JsArray arr)
    {
        return arr.Items.Count switch
        {
            // Empty array converts to 0
            0 => 0,
            // Single element array converts to the number representation of that element
            1 => ToNumber(arr.Items[0]),
            _ => double.NaN
        };

        // Multi-element array converts to NaN
    }

    // Helper method for converting values to strings in array context (join/toString)
    // where null and undefined become empty strings
    internal static string ToJsStringForArray(object? value)
    {
        // null and undefined convert to empty string in array toString/join
        if (value is null || (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined)))
        {
            return "";
        }

        return ToJsString(value);
    }

    internal static string ToJsString(object? value)
    {
        return value switch
        {
            null => "null",
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => "undefined",
            bool b => b ? "true" : "false",
            JsBigInt bigInt => bigInt.ToString(),
            JsArray arr => ArrayToString(arr),
            JsObject => "[object Object]",
            IJsCallable => "function() { [native code] }",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string ArrayToString(JsArray arr)
    {
        // Convert each element to string and join with comma
        // Per ECMAScript spec: null and undefined are converted to empty strings
        var elements = arr.Items.Select(ToJsStringForArray).ToList();
        return string.Join(",", elements);
    }

    private static string GetTypeofString(object? value)
    {
        // JavaScript oddity: typeof null === "object" (historical bug)
        if (value is null)
        {
            return "object";
        }

        // Check for undefined symbol
        if (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined))
        {
            return "undefined";
        }

        // Check for JavaScript Symbol (primitive type)
        if (value is JsSymbol)
        {
            return "symbol";
        }

        // Check for BigInt
        if (value is JsBigInt)
        {
            return "bigint";
        }

        return value switch
        {
            bool => "boolean",
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => "number",
            string => "string",
            JsFunction or HostFunction => "function",
            _ => "object"
        };
    }

    // Helper method to handle common BigInt binary operation pattern
    private static object PerformBigIntOrNumericOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<double, double, object> numericOp)
    {
        // Handle BigInt op BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return numericOp(ToNumber(left), ToNumber(right));
    }

    private static object Add(object? left, object? right)
    {
        // If either operand is a string, perform string concatenation
        if (left is string || right is string)
        {
            return ToJsString(left) + ToJsString(right);
        }

        // If either operand is an object or array, convert to string (ToPrimitive preference is string for +)
        if (left is JsObject || left is JsArray || right is JsObject || right is JsArray)
        {
            return ToJsString(left) + ToJsString(right);
        }

        // Handle BigInt + BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        // Otherwise, perform numeric addition
        return ToNumber(left) + ToNumber(right);
    }

    private static object Subtract(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l - r,
            (l, r) => l - r);
    }

    private static object Multiply(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l * r,
            (l, r) => l * r);
    }

    private static object Power(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => JsBigInt.Pow(l, r),
            (l, r) => Math.Pow(l, r));
    }

    private static object Divide(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l / r,
            (l, r) => l / r);
    }

    private static object Modulo(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l % r,
            (l, r) => l % r);
    }

    // Helper for comparison operations with BigInt/Number mixed comparisons
    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, bool> bigIntOp,
        Func<System.Numerics.BigInteger, System.Numerics.BigInteger, bool> mixedOp,
        Func<double, double, bool> numericOp)
    {
        switch (left)
        {
            // Handle BigInt comparisons
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return bigIntOp(leftBigInt, rightBigInt);
            // BigInt can be compared with Number in relational operators
            case JsBigInt lbi:
            {
                var rightNum = ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return mixedOp(lbi.Value, new System.Numerics.BigInteger(rightNum));
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return mixedOp(new System.Numerics.BigInteger(leftNum), rbi.Value);
            }
            default:
                return numericOp(ToNumber(left), ToNumber(right));
        }
    }

    private static bool GreaterThan(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l > r,
            (l, r) => l > r,
            (l, r) => l > r);
    }

    private static bool GreaterThanOrEqual(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l >= r,
            (l, r) => l >= r,
            (l, r) => l >= r);
    }

    private static bool LessThan(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l < r,
            (l, r) => l < r,
            (l, r) => l < r);
    }

    private static bool LessThanOrEqual(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l <= r,
            (l, r) => l <= r,
            (l, r) => l <= r);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return left is not Double.NaN;
            // mirror JavaScript's NaN behaviour
        }

        if (left is null || right is null)
        {
            return false;
        }

        // BigInt strict equality
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt == rightBigInt;
        }

        // BigInt and Number are never strictly equal
        if ((left is JsBigInt && IsNumeric(right)) || (IsNumeric(left) && right is JsBigInt))
        {
            return false;
        }

        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return left.GetType() == right.GetType() && Equals(left, right);
        }

        var leftNumber = ToNumber(left);
        var rightNumber = ToNumber(right);
        if (double.IsNaN(leftNumber) || double.IsNaN(rightNumber))
        {
            return false;
        }

        return leftNumber.Equals(rightNumber);

    }

    private static bool LooseEquals(object? left, object? right)
    {
        while (true)
        {
            // JavaScript oddity: null == undefined (but null !== undefined)
            var leftIsNullish = left is null || (left is Symbol symL && ReferenceEquals(symL, JsSymbols.Undefined));
            var rightIsNullish = right is null || (right is Symbol symR && ReferenceEquals(symR, JsSymbols.Undefined));

            if (leftIsNullish && rightIsNullish)
            {
                return true;
            }

            if (leftIsNullish || rightIsNullish)
            {
                return false;
            }

            // If types are the same, use strict equality
            if (left?.GetType() == right?.GetType())
            {
                return StrictEquals(left, right);
            }

            // BigInt == Number: compare numerically (allowed in loose equality)
            if (left is JsBigInt leftBigInt && IsNumeric(right))
            {
                var rightNum = ToNumber(right);
                if (double.IsNaN(rightNum) || double.IsInfinity(rightNum))
                {
                    return false;
                }

                //TODO: Check for fractional part, how does this work in JS?
                // Check if right is an integer and compare
                if (rightNum == Math.Floor(rightNum))
                {
                    return leftBigInt.Value == new System.Numerics.BigInteger(rightNum);
                }

                return false;
            }

            if (IsNumeric(left) && right is JsBigInt rightBigInt)
            {
                var leftNum = ToNumber(left);
                if (double.IsNaN(leftNum) || double.IsInfinity(leftNum))
                {
                    return false;
                }

                // Check if left is an integer and compare
                //TODO: Check for fractional part, how does this work in JS?
                if (leftNum == Math.Floor(leftNum))
                {
                    return new System.Numerics.BigInteger(leftNum) == rightBigInt.Value;
                }

                return false;
            }

            switch (left)
            {
                // BigInt == String: convert string to BigInt if possible
                case JsBigInt lbi when right is string str:
                    try
                    {
                        var rightBigInt2 = new JsBigInt(str.Trim());
                        return lbi == rightBigInt2;
                    }
                    catch
                    {
                        return false;
                    }

                case string str2 when right is JsBigInt rbi:
                    try
                    {
                        var leftBigInt2 = new JsBigInt(str2.Trim());
                        return leftBigInt2 == rbi;
                    }
                    catch
                    {
                        return false;
                    }
            }

            // Type coercion for loose equality
            // Number == String: convert string to number
            if (IsNumeric(left) && right is string)
            {
                return ToNumber(left).Equals(ToNumber(right));
            }

            switch (left)
            {
                case string when IsNumeric(right):
                    return ToNumber(left).Equals(ToNumber(right));
                // Boolean == anything: convert boolean to number
                case bool:
                    left = ToNumber(left);
                    continue;
            }

            if (right is bool)
            {
                right = ToNumber(right);
                continue;
            }

            // Object/Array == Primitive: convert object/array to primitive
            if (left is JsObject or JsArray && (IsNumeric(right) || right is string))
            {
                // Try converting to primitive (via toString then toNumber if comparing to number)
                return IsNumeric(right)
                    ? ToNumber(left).Equals(ToNumber(right))
                    : ToJsString(left).Equals(right);
            }

            if (right is JsObject or JsArray && (IsNumeric(left) || left is string))
            {
                // Try converting to primitive
                return IsNumeric(left)
                    ? ToNumber(left).Equals(ToNumber(right))
                    : left.Equals(ToJsString(right));
            }

            // For other cases, use strict equality
            return StrictEquals(left, right);
            break;
        }
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        // First, try the common interface for types with TryGetProperty
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            return propertyAccessor.TryGetProperty(propertyName, out value);
        }

        // Handle common primitives and host collections that expose properties without the interface
        switch (target)
        {
            // case IDictionary<string, object?> dictionary when dictionary.TryGetValue(propertyName, out value):
            //     return true;
            case double num:
                // Handle number properties (Number.prototype methods)
                var numberWrapper = StandardLibrary.CreateNumberWrapper(num);
                if (numberWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
            case string str:
                // Handle string properties
                if (propertyName == "length")
                {
                    value = (double)str.Length;
                    return true;
                }

                // Handle numeric indices (bracket notation: str[0], str[1], etc.)
                if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < str.Length)
                {
                    value = str[index].ToString();
                    return true;
                }

                // For string methods, create a wrapper object with methods
                var stringWrapper = StandardLibrary.CreateStringWrapper(str);
                if (stringWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
        }

        value = null;
        return false;
    }

    private static void AssignPropertyValue(object? target, string propertyName, object? value)
    {
        // First, try the common interface for types with SetProperty
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            propertyAccessor.SetProperty(propertyName, value);
            return;
        }

        throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
    }

    private static bool TryConvertToIndex(object? value, out int index)
    {
        switch (value)
        {
            case int i and >= 0:
                index = i;
                return true;
            case long l and >= 0 and <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                var truncated = Math.Truncate(d);
                if (Math.Abs(d - truncated) < double.Epsilon && truncated is >= 0 and <= int.MaxValue)
                {
                    index = (int)truncated;
                    return true;
                }

                break;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                               parsed >= 0:
                index = parsed;
                return true;
        }

        index = 0;
        return false;
    }

    private static string? ToPropertyName(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            Symbol symbol => symbol.Name,
            JsSymbol jsSymbol => $"@@symbol:{jsSymbol.GetHashCode()}", // Special prefix for Symbol keys
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    // Bitwise operations
    // Helper for bitwise operations that work on int32
    private static object PerformBigIntOrInt32Operation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op)
    {
        // Handle BigInt op BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right);
        return (double)int32Op(leftInt, rightInt);
    }

    private static object BitwiseAnd(object? left, object? right)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l & r,
            (l, r) => l & r);
    }

    private static object BitwiseOr(object? left, object? right)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l | r,
            (l, r) => l | r);
    }

    private static object BitwiseXor(object? left, object? right)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l ^ r,
            (l, r) => l ^ r);
    }

    private static object BitwiseNot(object? operand)
    {
        // Handle ~BigInt
        if (operand is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        var operandInt = ToInt32(operand);
        return (double)~operandInt;
    }

    private static object LeftShift(object? left, object? right)
    {
        // Handle BigInt << BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            // BigInt shift requires int, so check range
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftInt << rightInt);
    }

    private static object RightShift(object? left, object? right)
    {
        // Handle BigInt >> BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            // BigInt shift requires int, so check range
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftInt >> rightInt);
    }

    private static object UnsignedRightShift(object? left, object? right)
    {
        // BigInt does not support >>> operator (unsigned right shift)
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("BigInts have no unsigned right shift, use >> instead");
        }

        var leftUInt = ToUInt32(left);
        var rightInt = ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftUInt >> rightInt);
    }

    private static int ToInt32(object? value)
    {
        var num = ToNumber(value);
        return JsNumericConversions.ToInt32(num);
    }

    private static uint ToUInt32(object? value)
    {
        var num = ToNumber(value);
        return JsNumericConversions.ToUInt32(num);
    }

    // Increment/Decrement operations
    private static object IncrementPrefix(object? operandExpression, JsEnvironment environment, EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt increment
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt + JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return newValue;
        }

        var numValue = ToNumber(currentValue) + 1;
        UpdateVariable(operandExpression, numValue, environment, context);
        return numValue;
    }

    private static object DecrementPrefix(object? operandExpression, JsEnvironment environment, EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt decrement
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt - JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return newValue;
        }

        var numValue = ToNumber(currentValue) - 1;
        UpdateVariable(operandExpression, numValue, environment, context);
        return numValue;
    }

    private static object IncrementPostfix(object? operandExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt increment
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt + JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return bigInt; // Return the old value
        }

        var oldValue = ToNumber(currentValue);
        var newValue2 = oldValue + 1;
        UpdateVariable(operandExpression, newValue2, environment, context);
        return oldValue; // Return the old value
    }

    private static object DecrementPostfix(object? operandExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt decrement
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt - JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return bigInt; // Return the old value
        }

        var oldValue = ToNumber(currentValue);
        var newValue2 = oldValue - 1;
        UpdateVariable(operandExpression, newValue2, environment, context);
        return oldValue; // Return the old value
    }

    private static void UpdateVariable(object? operandExpression, object? newValue, JsEnvironment environment,
        EvaluationContext context)
    {
        if (operandExpression is Symbol symbol)
        {
            environment.Assign(symbol, newValue);
        }
        else if (operandExpression is Cons { Head: Symbol head } cons)
        {
            if (ReferenceEquals(head, JsSymbols.GetProperty))
            {
                var target = EvaluateExpression(cons.Rest.Head, environment, context);
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property access requires a string name.{GetSourceInfo(context)}");
                AssignPropertyValue(target, propertyName, newValue);
            }
            else if (ReferenceEquals(head, JsSymbols.GetIndex))
            {
                var target = EvaluateExpression(cons.Rest.Head, environment, context);
                var index = EvaluateExpression(cons.Rest.Rest.Head, environment, context);

                if (target is JsArray jsArray && TryConvertToIndex(index, out var arrayIndex))
                {
                    jsArray.SetElement(arrayIndex, newValue);
                }
                else if (target is TypedArrayBase typedArray && TryConvertToIndex(index, out var typedIndex))
                {
                    var numericValue = ToNumber(newValue);
                    typedArray.SetElement(typedIndex, numericValue);
                }
                else if (target is JsObject jsObject)
                {
                    var propertyName = ToPropertyName(index)
                                       ?? throw new InvalidOperationException($"Invalid property name: {index}{GetSourceInfo(context)}");
                    jsObject.SetProperty(propertyName, newValue);
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"Invalid operand for increment/decrement operator.{GetSourceInfo(context)}");
        }
    }


    private static bool InOperator(object? left, object? right)
    {
        var propertyName = left?.ToString() ?? string.Empty;

        if (right is JsObject jsObj)
        {
            return jsObj.ContainsKey(propertyName);
        }

        return false;
    }

    private static bool InstanceofOperator(object? left, object? right)
    {
        if (left is not JsObject leftObj)
        {
            return false;
        }

        if (right is not IJsCallable)
        {
            return false;
        }

        object? constructorPrototype = null;
        if (right is JsFunction jsFunc)
        {
            TryGetPropertyValue(jsFunc, "prototype", out constructorPrototype);
        }
        else if (right is JsObject rightObj)
        {
            TryGetPropertyValue(rightObj, "prototype", out constructorPrototype);
        }

        if (constructorPrototype is not JsObject prototypeObj)
        {
            return false;
        }

        var current = leftObj.Prototype;
        while (current != null)
        {
            if (ReferenceEquals(current, prototypeObj))
            {
                return true;
            }

            current = current.Prototype;
        }

        return false;
    }

}
