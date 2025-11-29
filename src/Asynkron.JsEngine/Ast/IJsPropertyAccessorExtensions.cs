using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(IJsPropertyAccessor target)
    {
        private bool TryInvokeSymbolMethod(object? thisArg, string symbolName,
            out object? result)
        {
            var symbol = TypedAstSymbol.For(symbolName);
            var hashedName = $"@@symbol:{symbol.GetHashCode()}";

            if (TryGetCallable(hashedName, out var callable) ||
                TryGetCallable(symbolName, out callable) ||
                TryGetCallable(symbol.ToString(), out callable))
            {
                result = callable!.Invoke([], thisArg);
                return true;
            }

            result = null;
            return false;

            bool TryGetCallable(string propertyName, out IJsCallable? callable)
            {
                if (target.TryGetProperty(propertyName, out var candidate) && candidate is IJsCallable found)
                {
                    callable = found;
                    return true;
                }

                callable = null;
                return false;
            }
        }
    }

extension(IJsPropertyAccessor accessor)
    {
        private IEnumerable<string> GetEnumerableOwnPropertyKeysInOrder()
        {
            if (accessor is JsObject jsObject)
            {
                foreach (var key in jsObject.GetOwnEnumerablePropertyKeysInOrder())
                {
                    yield return key;
                }

                yield break;
            }

            foreach (var key in accessor.GetEnumerablePropertyNames())
            {
                yield return key;
            }
        }
    }

}
