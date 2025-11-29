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

extension(JsArray stringsArray)
    {
        private JsObject CreateTemplateObject(JsArray rawStringsArray)
        {
            var templateObject = new JsObject();
            for (var i = 0; i < stringsArray.Items.Count; i++)
            {
                templateObject[i.ToString(CultureInfo.InvariantCulture)] = stringsArray.Items[i];
            }

            templateObject["length"] = (double)stringsArray.Items.Count;
            templateObject["raw"] = rawStringsArray;
            return templateObject;
        }
    }

extension(JsArray array)
    {
        [MustDisposeResource]
        private IEnumerator<object?> EnumerateArrayElements()
        {
            IEnumerable<object?> Enumerate()
            {
                var length = array.Length;
                var truncated = Math.Truncate(length);
                var clamped = truncated > int.MaxValue ? int.MaxValue : truncated;
                var count = clamped < 0 ? 0 : (int)clamped;
                for (var i = 0; i < count; i++)
                {
                    yield return array.GetElement(i);
                }
            }

            return Enumerate().GetEnumerator();
        }
    }

}
