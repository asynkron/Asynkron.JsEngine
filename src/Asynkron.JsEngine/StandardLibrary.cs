namespace Asynkron.JsEngine;

/// <summary>
/// Provides standard JavaScript library objects and functions (Math, JSON, etc.)
/// </summary>
internal static class StandardLibrary
{
    /// <summary>
    /// Creates a Math object with common mathematical functions and constants.
    /// </summary>
    public static JsObject CreateMathObject()
    {
        var math = new JsObject();

        // Constants
        math["E"] = Math.E;
        math["PI"] = Math.PI;
        math["LN2"] = Math.Log(2);
        math["LN10"] = Math.Log(10);
        math["LOG2E"] = Math.Log2(Math.E);
        math["LOG10E"] = Math.Log10(Math.E);
        math["SQRT1_2"] = Math.Sqrt(0.5);
        math["SQRT2"] = Math.Sqrt(2);

        // Methods
        math["abs"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] switch
            {
                double d => Math.Abs(d),
                int i => Math.Abs(i),
                _ => double.NaN
            };
        });

        math["ceil"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Ceiling(d) : double.NaN;
        });

        math["floor"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Floor(d) : double.NaN;
        });

        math["round"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            
            // JavaScript Math.round uses "round half away from zero"
            // while .NET Math.Round uses "round half to even" by default
            if (d >= 0)
            {
                return Math.Floor(d + 0.5);
            }
            else
            {
                return Math.Ceiling(d - 0.5);
            }
        });

        math["sqrt"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Sqrt(d) : double.NaN;
        });

        math["pow"] = new HostFunction(args =>
        {
            if (args.Count < 2) return double.NaN;
            var baseValue = args[0] as double? ?? double.NaN;
            var exponent = args[1] as double? ?? double.NaN;
            return Math.Pow(baseValue, exponent);
        });

        math["max"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NegativeInfinity;
            var max = double.NegativeInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d)) return double.NaN;
                    if (d > max) max = d;
                }
            }
            return max;
        });

        math["min"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.PositiveInfinity;
            var min = double.PositiveInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d)) return double.NaN;
                    if (d < min) min = d;
                }
            }
            return min;
        });

        math["random"] = new HostFunction(args =>
        {
            return Random.Shared.NextDouble();
        });

        math["sin"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Sin(d) : double.NaN;
        });

        math["cos"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Cos(d) : double.NaN;
        });

        math["tan"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Tan(d) : double.NaN;
        });

        math["asin"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Asin(d) : double.NaN;
        });

        math["acos"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Acos(d) : double.NaN;
        });

        math["atan"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Atan(d) : double.NaN;
        });

        math["atan2"] = new HostFunction(args =>
        {
            if (args.Count < 2) return double.NaN;
            var y = args[0] as double? ?? double.NaN;
            var x = args[1] as double? ?? double.NaN;
            return Math.Atan2(y, x);
        });

        math["exp"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Exp(d) : double.NaN;
        });

        math["log"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Log(d) : double.NaN;
        });

        math["log10"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Log10(d) : double.NaN;
        });

        math["log2"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Log2(d) : double.NaN;
        });

        math["trunc"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Truncate(d) : double.NaN;
        });

        math["sign"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            if (double.IsNaN(d)) return double.NaN;
            return Math.Sign(d);
        });

        return math;
    }

    /// <summary>
    /// Creates a Date object with JavaScript-like date handling.
    /// </summary>
    public static JsObject CreateDateObject()
    {
        var date = new JsObject();

        // Date.now() - returns milliseconds since epoch
        date["now"] = new HostFunction(args =>
        {
            return (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        });

        // Date.parse() - parses a date string
        date["parse"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string dateStr) return double.NaN;
            
            if (DateTimeOffset.TryParse(dateStr, out var parsed))
            {
                return (double)parsed.ToUnixTimeMilliseconds();
            }
            
            return double.NaN;
        });

        return date;
    }

    /// <summary>
    /// Creates a Date instance constructor.
    /// </summary>
    public static IJsCallable CreateDateConstructor()
    {
        return new HostFunction((thisValue, args) =>
        {
            var dateInstance = new JsObject();
            
            DateTimeOffset dateTime;
            
            if (args.Count == 0)
            {
                // No arguments: current date/time
                dateTime = DateTimeOffset.UtcNow;
            }
            else if (args.Count == 1)
            {
                // Single argument: milliseconds since epoch or date string
                if (args[0] is double ms)
                {
                    dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                }
                else if (args[0] is string dateStr && DateTimeOffset.TryParse(dateStr, out var parsed))
                {
                    dateTime = parsed;
                }
                else
                {
                    dateTime = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                // Multiple arguments: year, month, day, hour, minute, second, millisecond
                int year = args[0] is double y ? (int)y : 1970;
                int month = args.Count > 1 && args[1] is double m ? (int)m + 1 : 1; // JS months are 0-indexed
                int day = args.Count > 2 && args[2] is double d ? (int)d : 1;
                int hour = args.Count > 3 && args[3] is double h ? (int)h : 0;
                int minute = args.Count > 4 && args[4] is double min ? (int)min : 0;
                int second = args.Count > 5 && args[5] is double s ? (int)s : 0;
                int millisecond = args.Count > 6 && args[6] is double ms ? (int)ms : 0;
                
                try
                {
                    dateTime = new DateTimeOffset(year, month, day, hour, minute, second, millisecond, TimeSpan.Zero);
                }
                catch
                {
                    dateTime = DateTimeOffset.UtcNow;
                }
            }
            
            // Store the internal date value
            dateInstance["_internalDate"] = (double)dateTime.ToUnixTimeMilliseconds();
            
            // Add instance methods
            dateInstance["getTime"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    return ms;
                }
                return double.NaN;
            });
            
            dateInstance["getFullYear"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.Year;
                }
                return double.NaN;
            });
            
            dateInstance["getMonth"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)(dt.Month - 1); // JS months are 0-indexed
                }
                return double.NaN;
            });
            
            dateInstance["getDate"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.Day;
                }
                return double.NaN;
            });
            
            dateInstance["getDay"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.DayOfWeek;
                }
                return double.NaN;
            });
            
            dateInstance["getHours"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.Hour;
                }
                return double.NaN;
            });
            
            dateInstance["getMinutes"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.Minute;
                }
                return double.NaN;
            });
            
            dateInstance["getSeconds"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.Second;
                }
                return double.NaN;
            });
            
            dateInstance["getMilliseconds"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return (double)dt.Millisecond;
                }
                return double.NaN;
            });
            
            dateInstance["toISOString"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                }
                return "";
            });
            
            dateInstance["toString"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    return dt.ToString();
                }
                return "Invalid Date";
            });
            
            return dateInstance;
        });
    }

    /// <summary>
    /// Creates a JSON object with parse and stringify methods.
    /// </summary>
    public static JsObject CreateJsonObject()
    {
        var json = new JsObject();

        // JSON.parse()
        json["parse"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string jsonStr) return null;
            
            try
            {
                return ParseJsonValue(System.Text.Json.JsonDocument.Parse(jsonStr).RootElement);
            }
            catch
            {
                // In real JavaScript, this would throw a SyntaxError
                return null;
            }
        });

        // JSON.stringify()
        json["stringify"] = new HostFunction(args =>
        {
            if (args.Count == 0) return "undefined";
            
            var value = args[0];
            
            // Handle replacer function and space arguments if needed
            // For now, implement basic stringify
            return StringifyValue(value);
        });

        return json;
    }

    private static object? ParseJsonValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var obj = new JsObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ParseJsonValue(prop.Value);
                }
                return obj;
            
            case System.Text.Json.JsonValueKind.Array:
                var arr = new JsArray();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Push(ParseJsonValue(item));
                }
                AddArrayMethods(arr);
                return arr;
            
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            
            case System.Text.Json.JsonValueKind.Number:
                return element.GetDouble();
            
            case System.Text.Json.JsonValueKind.True:
                return true;
            
            case System.Text.Json.JsonValueKind.False:
                return false;
            
            case System.Text.Json.JsonValueKind.Null:
            default:
                return null;
        }
    }

    private static string StringifyValue(object? value, int depth = 0)
    {
        if (depth > 100) return "null"; // Prevent stack overflow
        
        switch (value)
        {
            case null:
                return "null";
            
            case bool b:
                return b ? "true" : "false";
            
            case double d:
                if (double.IsNaN(d) || double.IsInfinity(d))
                    return "null";
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            
            case string s:
                return System.Text.Json.JsonSerializer.Serialize(s);
            
            case JsArray arr:
                var arrItems = new List<string>();
                foreach (var item in arr.Items)
                {
                    arrItems.Add(StringifyValue(item, depth + 1));
                }
                return "[" + string.Join(",", arrItems) + "]";
            
            case JsObject obj:
                var objProps = new List<string>();
                foreach (var kvp in obj)
                {
                    // Skip functions and internal properties
                    if (kvp.Value is IJsCallable || kvp.Key.StartsWith("_"))
                        continue;
                    
                    var key = System.Text.Json.JsonSerializer.Serialize(kvp.Key);
                    var val = StringifyValue(kvp.Value, depth + 1);
                    objProps.Add($"{key}:{val}");
                }
                return "{" + string.Join(",", objProps) + "}";
            
            case IJsCallable:
                return "undefined";
            
            default:
                return System.Text.Json.JsonSerializer.Serialize(value?.ToString() ?? "");
        }
    }

    /// <summary>
    /// Adds standard array methods to a JsArray instance.
    /// </summary>
    public static void AddArrayMethods(JsArray array)
    {
        // push - already implemented natively
        array.SetProperty("push", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            foreach (var arg in args)
            {
                jsArray.Push(arg);
            }
            return jsArray.Items.Count;
        }));

        // pop
        array.SetProperty("pop", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            return jsArray.Pop();
        }));

        // map
        array.SetProperty("map", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                result.Push(mapped);
            }
            AddArrayMethods(result);
            return result;
        }));

        // filter
        array.SetProperty("filter", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var keep = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(keep))
                {
                    result.Push(element);
                }
            }
            AddArrayMethods(result);
            return result;
        }));

        // reduce
        array.SetProperty("reduce", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            if (jsArray.Items.Count == 0)
            {
                return args.Count > 1 ? args[1] : null;
            }

            int startIndex = 0;
            object? accumulator;

            if (args.Count > 1)
            {
                accumulator = args[1];
            }
            else
            {
                accumulator = jsArray.Items[0];
                startIndex = 1;
            }

            for (int i = startIndex; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                accumulator = callback.Invoke(new object?[] { accumulator, element, (double)i, jsArray }, null);
            }

            return accumulator;
        }));

        // forEach
        array.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
            }

            return null;
        }));

        // find
        array.SetProperty("find", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var match = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(match))
                {
                    return element;
                }
            }

            return null;
        }));

        // findIndex
        array.SetProperty("findIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var match = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(match))
                {
                    return (double)i;
                }
            }

            return -1d;
        }));

        // some
        array.SetProperty("some", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return false;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return false;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var result = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(result))
                {
                    return true;
                }
            }

            return false;
        }));

        // every
        array.SetProperty("every", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return true;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return true;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var result = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (!IsTruthy(result))
                {
                    return false;
                }
            }

            return true;
        }));

        // join
        array.SetProperty("join", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return "";
            var separator = args.Count > 0 && args[0] is string sep ? sep : ",";

            var parts = new List<string>();
            foreach (var item in jsArray.Items)
            {
                parts.Add(item?.ToString() ?? "");
            }

            return string.Join(separator, parts);
        }));

        // includes
        array.SetProperty("includes", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return false;
            if (args.Count == 0) return false;

            var searchElement = args[0];
            foreach (var item in jsArray.Items)
            {
                if (AreStrictlyEqual(item, searchElement))
                {
                    return true;
                }
            }

            return false;
        }));

        // indexOf
        array.SetProperty("indexOf", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return -1d;
            if (args.Count == 0) return -1d;

            var searchElement = args[0];
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                if (AreStrictlyEqual(jsArray.Items[i], searchElement))
                {
                    return (double)i;
                }
            }

            return -1d;
        }));

        // slice
        array.SetProperty("slice", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;

            int start = 0;
            int end = jsArray.Items.Count;

            if (args.Count > 0 && args[0] is double startD)
            {
                start = (int)startD;
                if (start < 0) start = Math.Max(0, jsArray.Items.Count + start);
            }

            if (args.Count > 1 && args[1] is double endD)
            {
                end = (int)endD;
                if (end < 0) end = Math.Max(0, jsArray.Items.Count + end);
            }

            var result = new JsArray();
            for (int i = start; i < Math.Min(end, jsArray.Items.Count); i++)
            {
                result.Push(jsArray.Items[i]);
            }
            AddArrayMethods(result);
            return result;
        }));

        // shift
        array.SetProperty("shift", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            return jsArray.Shift();
        }));

        // unshift
        array.SetProperty("unshift", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return 0;
            jsArray.Unshift(args.ToArray());
            return jsArray.Items.Count;
        }));

        // splice
        array.SetProperty("splice", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            int start = args.Count > 0 && args[0] is double startD ? (int)startD : 0;
            int deleteCount = args.Count > 1 && args[1] is double deleteD ? (int)deleteD : jsArray.Items.Count - start;
            
            object?[] itemsToInsert = args.Count > 2 ? args.Skip(2).ToArray() : Array.Empty<object?>();
            
            var deleted = jsArray.Splice(start, deleteCount, itemsToInsert);
            AddArrayMethods(deleted);
            return deleted;
        }));

        // concat
        array.SetProperty("concat", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            // Add current array items
            foreach (var item in jsArray.Items)
            {
                result.Push(item);
            }
            
            // Add items from arguments
            foreach (var arg in args)
            {
                if (arg is JsArray argArray)
                {
                    foreach (var item in argArray.Items)
                    {
                        result.Push(item);
                    }
                }
                else
                {
                    result.Push(arg);
                }
            }
            
            AddArrayMethods(result);
            return result;
        }));

        // reverse
        array.SetProperty("reverse", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            jsArray.Reverse();
            return jsArray;
        }));

        // sort
        array.SetProperty("sort", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var items = jsArray.Items.ToList();
            
            if (args.Count > 0 && args[0] is IJsCallable compareFn)
            {
                // Sort with custom compare function
                items.Sort((a, b) =>
                {
                    var result = compareFn.Invoke(new object?[] { a, b }, null);
                    if (result is double d)
                    {
                        return d > 0 ? 1 : d < 0 ? -1 : 0;
                    }
                    return 0;
                });
            }
            else
            {
                // Default sort: convert to strings and sort lexicographically
                items.Sort((a, b) =>
                {
                    var aStr = a?.ToString() ?? "";
                    var bStr = b?.ToString() ?? "";
                    return string.Compare(aStr, bStr, StringComparison.Ordinal);
                });
            }
            
            // Replace array items with sorted items
            for (int i = 0; i < items.Count; i++)
            {
                jsArray.SetElement(i, items[i]);
            }
            
            return jsArray;
        }));
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            double d => Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    private static bool AreStrictlyEqual(object? left, object? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        var leftType = left.GetType();
        var rightType = right.GetType();

        if (leftType != rightType) return false;

        return Equals(left, right);
    }
}
