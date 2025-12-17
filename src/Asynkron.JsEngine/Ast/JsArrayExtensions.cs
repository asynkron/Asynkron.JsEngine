using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(JsArray stringsArray)
    {
        private object CreateTemplateObject(JsArray rawStringsArray)
        {
            // Create template array object - it needs to be a JsArray for Array.isArray to work
            // Pass the RealmState to ensure the array has Array.prototype as its prototype
            var realmState = stringsArray.RealmState ?? rawStringsArray.RealmState;
            var templateObject = new JsArray(stringsArray.Items, realmState);

            // Define indexed properties with proper descriptors
            // enumerable: true, writable: false, configurable: false
            for (var i = 0; i < stringsArray.Items.Count; i++)
            {
                templateObject.DefineProperty(i.ToString(CultureInfo.InvariantCulture), new PropertyDescriptor
                {
                    Value = stringsArray.Items[i],
                    Writable = false,
                    Enumerable = true,
                    Configurable = false,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
            }

            // Define length property
            // enumerable: false, writable: false, configurable: false
            templateObject.DefineProperty("length", new PropertyDescriptor
            {
                Value = (double)stringsArray.Items.Count,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            });

            // Create and configure the raw array - also needs to be a JsArray
            var rawArray = new JsArray(rawStringsArray.Items, realmState);
            for (var i = 0; i < rawStringsArray.Items.Count; i++)
            {
                rawArray.DefineProperty(i.ToString(CultureInfo.InvariantCulture), new PropertyDescriptor
                {
                    Value = rawStringsArray.Items[i],
                    Writable = false,
                    Enumerable = true,
                    Configurable = false,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
            }

            rawArray.DefineProperty("length", new PropertyDescriptor
            {
                Value = (double)rawStringsArray.Items.Count,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            });

            // Freeze the raw array
            rawArray.Freeze();

            // Define raw property on template object
            // enumerable: false, writable: false, configurable: false
            templateObject.DefineProperty("raw", new PropertyDescriptor
            {
                Value = rawArray,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            });

            // Freeze the template object
            templateObject.Freeze();

            return templateObject;
        }
    }

    extension(JsArray array)
    {
        [MustDisposeResource]
        private IEnumerator<object?> EnumerateArrayElements()
        {
            return Enumerate().GetEnumerator();

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
        }
    }
}
