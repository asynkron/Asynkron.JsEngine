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

        // ES6+ Math methods
        math["cbrt"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Cbrt(d) : double.NaN;
        });

        math["clz32"] = new HostFunction(args =>
        {
            if (args.Count == 0) return 32d;
            if (args[0] is not double d) return 32d;
            var n = (int)d;
            if (n == 0) return 32d;
            return (double)System.Numerics.BitOperations.LeadingZeroCount((uint)n);
        });

        math["imul"] = new HostFunction(args =>
        {
            if (args.Count < 2) return 0d;
            var a = args[0] is double d1 ? (int)d1 : 0;
            var b = args[1] is double d2 ? (int)d2 : 0;
            return (double)(a * b);
        });

        math["fround"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            return (double)(float)d;
        });

        math["hypot"] = new HostFunction(args =>
        {
            if (args.Count == 0) return 0d;
            double sumOfSquares = 0;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d)) return double.NaN;
                    if (double.IsInfinity(d)) return double.PositiveInfinity;
                    sumOfSquares += d * d;
                }
            }
            return Math.Sqrt(sumOfSquares);
        });

        math["acosh"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Acosh(d) : double.NaN;
        });

        math["asinh"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Asinh(d) : double.NaN;
        });

        math["atanh"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Atanh(d) : double.NaN;
        });

        math["cosh"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Cosh(d) : double.NaN;
        });

        math["sinh"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Sinh(d) : double.NaN;
        });

        math["tanh"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Tanh(d) : double.NaN;
        });

        math["expm1"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            // e^x - 1 with better precision for small x
            return Math.Exp(d) - 1;
        });

        math["log1p"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            // log(1 + x) with better precision for small x
            return Math.Log(1 + d);
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
    /// Creates a RegExp constructor function.
    /// </summary>
    public static IJsCallable CreateRegExpConstructor()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                var emptyRegex = new JsRegExp("(?:)", "");
                AddRegExpMethods(emptyRegex);
                return emptyRegex.JsObject;
            }
            
            var pattern = args[0]?.ToString() ?? "";
            var flags = args.Count > 1 ? (args[1]?.ToString() ?? "") : "";
            
            var regex = new JsRegExp(pattern, flags);
            regex.JsObject["__regex__"] = regex; // Store reference for internal use
            AddRegExpMethods(regex);
            return regex.JsObject;
        });
    }

    /// <summary>
    /// Adds RegExp instance methods to a JsRegExp object.
    /// </summary>
    private static void AddRegExpMethods(JsRegExp regex)
    {
        // test(string) - returns boolean
        regex.SetProperty("test", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0) return false;
            var input = args[0]?.ToString() ?? "";
            return regex.Test(input);
        }));

        // exec(string) - returns array with match details or null
        regex.SetProperty("exec", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0) return null;
            var input = args[0]?.ToString() ?? "";
            return regex.Exec(input);
        }));
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
                var mapped = callback.Invoke([element, (double)i, jsArray], null);
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
                var keep = callback.Invoke([element, (double)i, jsArray], null);
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
                accumulator = callback.Invoke([accumulator, element, (double)i, jsArray], null);
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
                callback.Invoke([element, (double)i, jsArray], null);
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
                var match = callback.Invoke([element, (double)i, jsArray], null);
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
                var match = callback.Invoke([element, (double)i, jsArray], null);
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
                var result = callback.Invoke([element, (double)i, jsArray], null);
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
                var result = callback.Invoke([element, (double)i, jsArray], null);
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
            
            object?[] itemsToInsert = args.Count > 2 ? args.Skip(2).ToArray() : [];
            
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
                    var result = compareFn.Invoke([a, b], null);
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

        // at(index)
        array.SetProperty("at", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not double d) return null;
            var index = (int)d;
            // Handle negative indices
            if (index < 0) index = jsArray.Items.Count + index;
            if (index < 0 || index >= jsArray.Items.Count) return null;
            return jsArray.GetElement(index);
        }));

        // flat(depth = 1)
        array.SetProperty("flat", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            var depth = args.Count > 0 && args[0] is double d ? (int)d : 1;
            
            var result = new JsArray();
            FlattenArray(jsArray, result, depth);
            AddArrayMethods(result);
            return result;
        }));

        // flatMap(callback)
        array.SetProperty("flatMap", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke([element, (double)i, jsArray], null);
                
                // Flatten one level
                if (mapped is JsArray mappedArray)
                {
                    for (int j = 0; j < mappedArray.Items.Count; j++)
                    {
                        result.Push(mappedArray.GetElement(j));
                    }
                }
                else
                {
                    result.Push(mapped);
                }
            }
            AddArrayMethods(result);
            return result;
        }));

        // findLast(callback)
        array.SetProperty("findLast", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = jsArray.Items.Count - 1; i >= 0; i--)
            {
                var element = jsArray.Items[i];
                var matches = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(matches))
                {
                    return element;
                }
            }
            return null;
        }));

        // findLastIndex(callback)
        array.SetProperty("findLastIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return -1d;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return -1d;

            for (int i = jsArray.Items.Count - 1; i >= 0; i--)
            {
                var element = jsArray.Items[i];
                var matches = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(matches))
                {
                    return (double)i;
                }
            }
            return -1d;
        }));

        // fill(value, start = 0, end = length)
        array.SetProperty("fill", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0) return jsArray;
            
            var value = args[0];
            var start = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            var end = args.Count > 2 && args[2] is double d2 ? (int)d2 : jsArray.Items.Count;
            
            // Handle negative indices
            if (start < 0) start = Math.Max(0, jsArray.Items.Count + start);
            if (end < 0) end = Math.Max(0, jsArray.Items.Count + end);
            
            // Clamp to array bounds
            start = Math.Max(0, Math.Min(start, jsArray.Items.Count));
            end = Math.Max(start, Math.Min(end, jsArray.Items.Count));
            
            for (int i = start; i < end; i++)
            {
                jsArray.SetElement(i, value);
            }
            
            return jsArray;
        }));

        // copyWithin(target, start = 0, end = length)
        array.SetProperty("copyWithin", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0) return jsArray;
            
            var target = args[0] is double dt ? (int)dt : 0;
            var start = args.Count > 1 && args[1] is double ds ? (int)ds : 0;
            var end = args.Count > 2 && args[2] is double de ? (int)de : jsArray.Items.Count;
            
            var len = jsArray.Items.Count;
            
            // Handle negative indices
            if (target < 0) target = Math.Max(0, len + target);
            else target = Math.Min(target, len);
            
            if (start < 0) start = Math.Max(0, len + start);
            else start = Math.Min(start, len);
            
            if (end < 0) end = Math.Max(0, len + end);
            else end = Math.Min(end, len);
            
            var count = Math.Min(end - start, len - target);
            if (count <= 0) return jsArray;
            
            // Copy to temporary array to handle overlapping ranges
            var temp = new object?[count];
            for (int i = 0; i < count; i++)
            {
                temp[i] = jsArray.GetElement(start + i);
            }
            
            for (int i = 0; i < count; i++)
            {
                jsArray.SetElement(target + i, temp[i]);
            }
            
            return jsArray;
        }));

        // toSorted(compareFn) - non-mutating sort
        array.SetProperty("toSorted", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                result.Push(jsArray.GetElement(i));
            }
            AddArrayMethods(result);
            
            var items = result.Items.ToList();
            
            if (args.Count > 0 && args[0] is IJsCallable compareFn)
            {
                // Sort with custom compare function
                items.Sort((a, b) =>
                {
                    var cmp = compareFn.Invoke([a, b], null);
                    if (cmp is double d)
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
            
            for (int i = 0; i < items.Count; i++)
            {
                result.SetElement(i, items[i]);
            }
            
            return result;
        }));

        // toReversed() - non-mutating reverse
        array.SetProperty("toReversed", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            for (int i = jsArray.Items.Count - 1; i >= 0; i--)
            {
                result.Push(jsArray.GetElement(i));
            }
            AddArrayMethods(result);
            return result;
        }));

        // toSpliced(start, deleteCount, ...items) - non-mutating splice
        array.SetProperty("toSpliced", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            var len = jsArray.Items.Count;
            
            if (args.Count == 0)
            {
                // No arguments, return copy
                for (int i = 0; i < len; i++)
                {
                    result.Push(jsArray.GetElement(i));
                }
            }
            else
            {
                var start = args[0] is double ds ? (int)ds : 0;
                var deleteCount = args.Count > 1 && args[1] is double dc ? (int)dc : len - start;
                
                // Handle negative start
                if (start < 0) start = Math.Max(0, len + start);
                else start = Math.Min(start, len);
                
                // Clamp deleteCount
                deleteCount = Math.Max(0, Math.Min(deleteCount, len - start));
                
                // Copy elements before start
                for (int i = 0; i < start; i++)
                {
                    result.Push(jsArray.GetElement(i));
                }
                
                // Insert new items
                for (int i = 2; i < args.Count; i++)
                {
                    result.Push(args[i]);
                }
                
                // Copy elements after deleted section
                for (int i = start + deleteCount; i < len; i++)
                {
                    result.Push(jsArray.GetElement(i));
                }
            }
            
            AddArrayMethods(result);
            return result;
        }));

        // with(index, value) - non-mutating element replacement
        array.SetProperty("with", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count < 2) return null;
            if (args[0] is not double d) return null;
            
            var index = (int)d;
            var value = args[1];
            
            // Handle negative indices
            if (index < 0) index = jsArray.Items.Count + index;
            
            // Index out of bounds throws RangeError in JavaScript
            if (index < 0 || index >= jsArray.Items.Count) return null;
            
            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                result.Push(i == index ? value : jsArray.GetElement(i));
            }
            AddArrayMethods(result);
            return result;
        }));

        // entries() - returns an array of [index, value] pairs
        array.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var entry = new JsArray([i, jsArray.GetElement(i)]);
                AddArrayMethods(entry);
                result.Push(entry);
            }
            AddArrayMethods(result);
            return result;
        }));

        // keys() - returns an array of indices
        array.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                result.Push((double)i);
            }
            AddArrayMethods(result);
            return result;
        }));

        // values() - returns an array of values
        array.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            
            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                result.Push(jsArray.GetElement(i));
            }
            AddArrayMethods(result);
            return result;
        }));
    }

    private static void FlattenArray(JsArray source, JsArray target, int depth)
    {
        foreach (var item in source.Items)
        {
            if (depth > 0 && item is JsArray nestedArray)
            {
                FlattenArray(nestedArray, target, depth - 1);
            }
            else
            {
                target.Push(item);
            }
        }
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

    /// <summary>
    /// Creates a Promise constructor with static methods.
    /// </summary>
    public static IJsCallable CreatePromiseConstructor(JsEngine engine)
    {
        var promiseConstructor = new HostFunction((thisValue, args) =>
        {
            // Promise constructor takes an executor function: function(resolve, reject) { ... }
            if (args.Count == 0 || args[0] is not IJsCallable executor)
                throw new InvalidOperationException("Promise constructor requires an executor function");

            var promise = new JsPromise(engine);
            var promiseObj = promise.JsObject;

            // Create resolve and reject callbacks
            var resolve = new HostFunction(resolveArgs =>
            {
                promise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);
                return null;
            });

            var reject = new HostFunction(rejectArgs =>
            {
                promise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                return null;
            });

            // Add then, catch, and finally methods
            promiseObj["then"] = new HostFunction((promiseThis, thenArgs) =>
            {
                var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
                var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
                var resultPromise = promise.Then(onFulfilled, onRejected);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            promiseObj["catch"] = new HostFunction((promiseThis, catchArgs) =>
            {
                var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
                var resultPromise = promise.Then(null, onRejected);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            promiseObj["finally"] = new HostFunction((promiseThis, finallyArgs) =>
            {
                var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
                if (onFinally == null)
                    return promiseObj;

                var finallyWrapper = new HostFunction(wrapperArgs =>
                {
                    onFinally.Invoke([], null);
                    return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
                });

                var resultPromise = promise.Then(finallyWrapper, finallyWrapper);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            // Execute the executor function immediately
            try
            {
                executor.Invoke([resolve, reject], null);
            }
            catch (Exception ex)
            {
                promise.Reject(ex.Message);
            }

            return promiseObj;
        });

        // Add static methods to Promise constructor
        if (promiseConstructor is HostFunction hf)
        {
            // Promise.resolve(value)
            hf.SetProperty("resolve", new HostFunction(args =>
            {
                var value = args.Count > 0 ? args[0] : null;
                var promise = new JsPromise(engine);
                
                // Add instance methods
                AddPromiseInstanceMethods(promise.JsObject, promise, engine);
                
                promise.Resolve(value);
                return promise.JsObject;
            }));

            // Promise.reject(reason)
            hf.SetProperty("reject", new HostFunction(args =>
            {
                var reason = args.Count > 0 ? args[0] : null;
                var promise = new JsPromise(engine);
                
                // Add instance methods
                AddPromiseInstanceMethods(promise.JsObject, promise, engine);
                
                promise.Reject(reason);
                return promise.JsObject;
            }));

            // Promise.all(iterable)
            hf.SetProperty("all", new HostFunction(args =>
            {
                if (args.Count == 0 || args[0] is not JsArray array)
                    return null;

                var resultPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

                var results = new object?[array.Items.Count];
                var remaining = array.Items.Count;

                if (remaining == 0)
                {
                    var emptyArray = new JsArray();
                    AddArrayMethods(emptyArray);
                    resultPromise.Resolve(emptyArray);
                    return resultPromise.JsObject;
                }

                for (int i = 0; i < array.Items.Count; i++)
                {
                    var index = i;
                    var item = array.Items[i];

                    // Check if item is a promise (JsObject with "then" method)
                    if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable thenCallable)
                    {
                        thenCallable.Invoke([
                            new HostFunction(resolveArgs =>
                            {
                                results[index] = resolveArgs.Count > 0 ? resolveArgs[0] : null;
                                remaining--;
                                
                                if (remaining == 0)
                                {
                                    var resultArray = new JsArray();
                                    foreach (var result in results)
                                    {
                                        resultArray.Push(result);
                                    }
                                    AddArrayMethods(resultArray);
                                    resultPromise.Resolve(resultArray);
                                }
                                return null;
                            }),
                            new HostFunction(rejectArgs =>
                            {
                                resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                                return null;
                            })
                        ], itemObj);
                    }
                    else
                    {
                        results[index] = item;
                        remaining--;
                        
                        if (remaining == 0)
                        {
                            var resultArray = new JsArray();
                            foreach (var result in results)
                            {
                                resultArray.Push(result);
                            }
                            AddArrayMethods(resultArray);
                            resultPromise.Resolve(resultArray);
                        }
                    }
                }

                return resultPromise.JsObject;
            }));

            // Promise.race(iterable)
            hf.SetProperty("race", new HostFunction(args =>
            {
                if (args.Count == 0 || args[0] is not JsArray array)
                    return null;

                var resultPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

                var settled = false;

                foreach (var item in array.Items)
                {
                    // Check if item is a promise (JsObject with "then" method)
                    if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable thenCallable)
                    {
                        thenCallable.Invoke([
                            new HostFunction(resolveArgs =>
                            {
                                if (!settled)
                                {
                                    settled = true;
                                    resultPromise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);
                                }
                                return null;
                            }),
                            new HostFunction(rejectArgs =>
                            {
                                if (!settled)
                                {
                                    settled = true;
                                    resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                                }
                                return null;
                            })
                        ], itemObj);
                    }
                    else if (!settled)
                    {
                        settled = true;
                        resultPromise.Resolve(item);
                    }
                }

                return resultPromise.JsObject;
            }));
        }

        return promiseConstructor;
    }

    /// <summary>
    /// Helper method to add instance methods to a promise.
    /// </summary>
    internal static void AddPromiseInstanceMethods(JsObject promiseObj, JsPromise promise, JsEngine engine)
    {
        promiseObj["then"] = new HostFunction((promiseThis, thenArgs) =>
        {
            var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
            var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
            var result = promise.Then(onFulfilled, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });

        promiseObj["catch"] = new HostFunction((promiseThis, catchArgs) =>
        {
            var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
            var result = promise.Then(null, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });

        promiseObj["finally"] = new HostFunction((promiseThis, finallyArgs) =>
        {
            var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
            if (onFinally == null)
                return promiseObj;

            var finallyWrapper = new HostFunction(wrapperArgs =>
            {
                onFinally.Invoke([], null);
                return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
            });

            var result = promise.Then(finallyWrapper, finallyWrapper);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });
    }

    /// <summary>
    /// Creates a string wrapper object with string methods attached.
    /// This allows string primitives to have methods like toLowerCase(), substring(), etc.
    /// </summary>
    public static JsObject CreateStringWrapper(string str)
    {
        var stringObj = new JsObject();
        stringObj["__value__"] = str;
        stringObj["length"] = (double)str.Length;
        AddStringMethods(stringObj, str);
        return stringObj;
    }

    /// <summary>
    /// Adds string methods to a string wrapper object.
    /// </summary>
    private static void AddStringMethods(JsObject stringObj, string str)
    {
        // charAt(index)
        stringObj.SetProperty("charAt", new HostFunction(args =>
        {
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= str.Length) return "";
            return str[index].ToString();
        }));

        // charCodeAt(index)
        stringObj.SetProperty("charCodeAt", new HostFunction(args =>
        {
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= str.Length) return double.NaN;
            return (double)str[index];
        }));

        // indexOf(searchString, position?)
        stringObj.SetProperty("indexOf", new HostFunction(args =>
        {
            if (args.Count == 0) return -1d;
            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            var result = str.IndexOf(searchStr, position, StringComparison.Ordinal);
            return (double)result;
        }));

        // lastIndexOf(searchString, position?)
        stringObj.SetProperty("lastIndexOf", new HostFunction(args =>
        {
            if (args.Count == 0) return -1d;
            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? 
                Math.Min((int)d, str.Length - 1) : str.Length - 1;
            var result = position >= 0 ? str.LastIndexOf(searchStr, position, StringComparison.Ordinal) : -1;
            return (double)result;
        }));

        // substring(start, end?)
        stringObj.SetProperty("substring", new HostFunction(args =>
        {
            if (args.Count == 0) return str;
            var start = args[0] is double d1 ? Math.Max(0, Math.Min((int)d1, str.Length)) : 0;
            var end = args.Count > 1 && args[1] is double d2 ? 
                Math.Max(0, Math.Min((int)d2, str.Length)) : str.Length;
            
            // JavaScript substring swaps if start > end
            if (start > end)
            {
                (start, end) = (end, start);
            }
            
            return str.Substring(start, end - start);
        }));

        // slice(start, end?)
        stringObj.SetProperty("slice", new HostFunction(args =>
        {
            if (args.Count == 0) return str;
            var start = args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : str.Length;
            
            // Handle negative indices
            if (start < 0) start = Math.Max(0, str.Length + start);
            else start = Math.Min(start, str.Length);
            
            if (end < 0) end = Math.Max(0, str.Length + end);
            else end = Math.Min(end, str.Length);
            
            if (start >= end) return "";
            return str.Substring(start, end - start);
        }));

        // toLowerCase()
        stringObj.SetProperty("toLowerCase", new HostFunction(args => str.ToLowerInvariant()));

        // toUpperCase()
        stringObj.SetProperty("toUpperCase", new HostFunction(args => str.ToUpperInvariant()));

        // trim()
        stringObj.SetProperty("trim", new HostFunction(args => str.Trim()));

        // trimStart() / trimLeft()
        stringObj.SetProperty("trimStart", new HostFunction(args => str.TrimStart()));
        stringObj.SetProperty("trimLeft", new HostFunction(args => str.TrimStart()));

        // trimEnd() / trimRight()
        stringObj.SetProperty("trimEnd", new HostFunction(args => str.TrimEnd()));
        stringObj.SetProperty("trimRight", new HostFunction(args => str.TrimEnd()));

        // split(separator, limit?)
        stringObj.SetProperty("split", new HostFunction(args =>
        {
            if (args.Count == 0) return CreateArrayFromStrings([str]);
            
            var separator = args[0]?.ToString();
            var limit = args.Count > 1 && args[1] is double d ? (int)d : int.MaxValue;
            
            if (separator == null || separator == "")
            {
                // Split into individual characters
                var chars = str.Select(c => c.ToString()).Take(limit).ToArray();
                return CreateArrayFromStrings(chars);
            }
            
            var parts = str.Split([separator], StringSplitOptions.None);
            if (limit < parts.Length)
            {
                parts = parts.Take(limit).ToArray();
            }
            return CreateArrayFromStrings(parts);
        }));

        // replace(searchValue, replaceValue)
        stringObj.SetProperty("replace", new HostFunction(args =>
        {
            if (args.Count < 2) return str;
            
            // Check if first argument is a RegExp (JsObject with __regex__ property)
            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) && regexValue is JsRegExp regex)
            {
                var replaceValue = args[1]?.ToString() ?? "";
                if (regex.Global)
                {
                    return System.Text.RegularExpressions.Regex.Replace(str, regex.Pattern, replaceValue);
                }
                else
                {
                    var match = System.Text.RegularExpressions.Regex.Match(str, regex.Pattern);
                    if (match.Success)
                    {
                        return str.Substring(0, match.Index) + replaceValue + str.Substring(match.Index + match.Length);
                    }
                    return str;
                }
            }
            
            // String replacement (only first occurrence)
            var searchValue = args[0]?.ToString() ?? "";
            var replaceStr = args[1]?.ToString() ?? "";
            var index = str.IndexOf(searchValue, StringComparison.Ordinal);
            if (index == -1) return str;
            
            return str.Substring(0, index) + replaceStr + str.Substring(index + searchValue.Length);
        }));

        // match(regexp)
        stringObj.SetProperty("match", new HostFunction(args =>
        {
            if (args.Count == 0) return null;
            
            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) && regexValue is JsRegExp regex)
            {
                if (regex.Global)
                {
                    return regex.MatchAll(str);
                }
                else
                {
                    return regex.Exec(str);
                }
            }
            
            return null;
        }));

        // search(regexp)
        stringObj.SetProperty("search", new HostFunction(args =>
        {
            if (args.Count == 0) return -1d;
            
            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) && regexValue is JsRegExp regex)
            {
                var result = regex.Exec(str);
                if (result is JsArray arr && arr.TryGetProperty("index", out var indexObj) && indexObj is double d)
                {
                    return d;
                }
                return -1d;
            }
            
            return -1d;
        }));

        // startsWith(searchString, position?)
        stringObj.SetProperty("startsWith", new HostFunction(args =>
        {
            if (args.Count == 0) return true;
            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? (int)d : 0;
            if (position < 0 || position >= str.Length) return false;
            return str.Substring(position).StartsWith(searchStr, StringComparison.Ordinal);
        }));

        // endsWith(searchString, length?)
        stringObj.SetProperty("endsWith", new HostFunction(args =>
        {
            if (args.Count == 0) return true;
            var searchStr = args[0]?.ToString() ?? "";
            var length = args.Count > 1 && args[1] is double d ? (int)d : str.Length;
            if (length < 0) return false;
            length = Math.Min(length, str.Length);
            return str.Substring(0, length).EndsWith(searchStr, StringComparison.Ordinal);
        }));

        // includes(searchString, position?)
        stringObj.SetProperty("includes", new HostFunction(args =>
        {
            if (args.Count == 0) return true;
            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            if (position >= str.Length) return searchStr == "";
            return str.IndexOf(searchStr, position, StringComparison.Ordinal) >= 0;
        }));

        // repeat(count)
        stringObj.SetProperty("repeat", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d) return "";
            var count = (int)d;
            if (count < 0 || count == int.MaxValue) return ""; // JavaScript throws RangeError, we return empty
            if (count == 0) return "";
            return string.Concat(Enumerable.Repeat(str, count));
        }));

        // padStart(targetLength, padString?)
        stringObj.SetProperty("padStart", new HostFunction(args =>
        {
            if (args.Count == 0) return str;
            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= str.Length) return str;
            var padString = args.Count > 1 ? (args[1]?.ToString() ?? " ") : " ";
            if (padString == "") return str;
            
            var padLength = targetLength - str.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return padding.Substring(0, padLength) + str;
        }));

        // padEnd(targetLength, padString?)
        stringObj.SetProperty("padEnd", new HostFunction(args =>
        {
            if (args.Count == 0) return str;
            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= str.Length) return str;
            var padString = args.Count > 1 ? (args[1]?.ToString() ?? " ") : " ";
            if (padString == "") return str;
            
            var padLength = targetLength - str.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return str + padding.Substring(0, padLength);
        }));

        // replaceAll(searchValue, replaceValue)
        stringObj.SetProperty("replaceAll", new HostFunction(args =>
        {
            if (args.Count < 2) return str;
            var searchValue = args[0]?.ToString() ?? "";
            var replaceValue = args[1]?.ToString() ?? "";
            return str.Replace(searchValue, replaceValue);
        }));

        // at(index)
        stringObj.SetProperty("at", new HostFunction(args =>
        {
            if (args.Count == 0) return null;
            if (args[0] is not double d) return null;
            var index = (int)d;
            // Handle negative indices
            if (index < 0) index = str.Length + index;
            if (index < 0 || index >= str.Length) return null;
            return str[index].ToString();
        }));

        // trimStart() / trimLeft()
        stringObj.SetProperty("trimStart", new HostFunction(args => str.TrimStart()));
        stringObj.SetProperty("trimLeft", new HostFunction(args => str.TrimStart()));

        // trimEnd() / trimRight()
        stringObj.SetProperty("trimEnd", new HostFunction(args => str.TrimEnd()));
        stringObj.SetProperty("trimRight", new HostFunction(args => str.TrimEnd()));

        // codePointAt(index)
        stringObj.SetProperty("codePointAt", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d) return null;
            var index = (int)d;
            if (index < 0 || index >= str.Length) return null;
            
            // Get the code point at the given position
            // Handle surrogate pairs for characters outside the BMP (Basic Multilingual Plane)
            var c = str[index];
            if (char.IsHighSurrogate(c) && index + 1 < str.Length)
            {
                var low = str[index + 1];
                if (char.IsLowSurrogate(low))
                {
                    // Calculate the code point from the surrogate pair
                    var high = (int)c;
                    var lowInt = (int)low;
                    var codePoint = ((high - 0xD800) << 10) + (lowInt - 0xDC00) + 0x10000;
                    return (double)codePoint;
                }
            }
            return (double)c;
        }));

        // localeCompare(compareString)
        stringObj.SetProperty("localeCompare", new HostFunction(args =>
        {
            if (args.Count == 0) return 0d;
            var compareString = args[0]?.ToString() ?? "";
            var result = string.Compare(str, compareString, StringComparison.CurrentCulture);
            return (double)result;
        }));

        // normalize(form) - Unicode normalization
        stringObj.SetProperty("normalize", new HostFunction(args =>
        {
            var form = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "NFC";
            
            try
            {
                return form switch
                {
                    "NFC" => str.Normalize(System.Text.NormalizationForm.FormC),
                    "NFD" => str.Normalize(System.Text.NormalizationForm.FormD),
                    "NFKC" => str.Normalize(System.Text.NormalizationForm.FormKC),
                    "NFKD" => str.Normalize(System.Text.NormalizationForm.FormKD),
                    _ => throw new Exception("RangeError: The normalization form should be one of NFC, NFD, NFKC, NFKD.")
                };
            }
            catch
            {
                return str;
            }
        }));

        // matchAll(regexp) - returns an array of all matches
        stringObj.SetProperty("matchAll", new HostFunction(args =>
        {
            if (args.Count == 0) return null;
            
            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) && regexValue is JsRegExp regex)
            {
                return regex.MatchAll(str);
            }
            
            // If not a RegExp, convert to one
            var pattern = args[0]?.ToString() ?? "";
            var tempRegex = new JsRegExp(pattern, "g");
            return tempRegex.MatchAll(str);
        }));

        // anchor(name) - deprecated HTML wrapper method
        stringObj.SetProperty("anchor", new HostFunction(args =>
        {
            var name = args.Count > 0 ? (args[0]?.ToString() ?? "") : "";
            // Escape quotes in name
            name = name.Replace("\"", "&quot;");
            return $"<a name=\"{name}\">{str}</a>";
        }));

        // link(url) - deprecated HTML wrapper method
        stringObj.SetProperty("link", new HostFunction(args =>
        {
            var url = args.Count > 0 ? (args[0]?.ToString() ?? "") : "";
            // Escape quotes in url
            url = url.Replace("\"", "&quot;");
            return $"<a href=\"{url}\">{str}</a>";
        }));
        
        // Set up Symbol.iterator for string
        var iteratorSymbol = JsSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
        
        // Create iterator function that returns an iterator object
        var iteratorFunction = new HostFunction((thisValue, args) =>
        {
            // Use array to hold index so it can be mutated in closure
            var indexHolder = new int[] { 0 };
            var iterator = new JsObject();
            
            // Add next() method to iterator
            iterator.SetProperty("next", new HostFunction((nextThisValue, nextArgs) =>
            {
                var result = new JsObject();
                if (indexHolder[0] < str.Length)
                {
                    result.SetProperty("value", str[indexHolder[0]].ToString());
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", JsSymbols.Undefined);
                    result.SetProperty("done", true);
                }
                return result;
            }));
            
            return iterator;
        });
        
        stringObj.SetProperty(iteratorKey, iteratorFunction);
    }

    private static JsArray CreateArrayFromStrings(string[] strings)
    {
        var array = new JsArray();
        foreach (var s in strings)
        {
            array.Push(s);
        }
        AddArrayMethods(array);
        return array;
    }

    /// <summary>
    /// Creates the Object constructor with static methods.
    /// </summary>
    public static JsObject CreateObjectConstructor()
    {
        var objectConstructor = new JsObject();

        // Object.keys(obj)
        objectConstructor["keys"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return new JsArray();
            
            var keys = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                keys.Push(key);
            }
            AddArrayMethods(keys);
            return keys;
        });

        // Object.values(obj)
        objectConstructor["values"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return new JsArray();
            
            var values = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (obj.TryGetValue(key, out var value))
                {
                    values.Push(value);
                }
            }
            AddArrayMethods(values);
            return values;
        });

        // Object.entries(obj)
        objectConstructor["entries"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return new JsArray();
            
            var entries = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (obj.TryGetValue(key, out var value))
                {
                    var entry = new JsArray([key, value]);
                    AddArrayMethods(entry);
                    entries.Push(entry);
                }
            }
            AddArrayMethods(entries);
            return entries;
        });

        // Object.assign(target, ...sources)
        objectConstructor["assign"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject target) return null;
            
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] is JsObject source)
                {
                    foreach (var key in source.GetOwnPropertyNames())
                    {
                        if (source.TryGetValue(key, out var value))
                        {
                            target[key] = value;
                        }
                    }
                }
            }
            
            return target;
        });

        // Object.fromEntries(entries)
        objectConstructor["fromEntries"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsArray entries) return new JsObject();
            
            var result = new JsObject();
            foreach (var entry in entries.Items)
            {
                if (entry is JsArray entryArray && entryArray.Items.Count >= 2)
                {
                    var key = entryArray.GetElement(0)?.ToString() ?? "";
                    var value = entryArray.GetElement(1);
                    result[key] = value;
                }
            }
            
            return result;
        });

        // Object.hasOwn(obj, prop)
        objectConstructor["hasOwn"] = new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not JsObject obj) return false;
            var propName = args[1]?.ToString() ?? "";
            return obj.ContainsKey(propName);
        });

        // Object.freeze(obj)
        objectConstructor["freeze"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return args.Count > 0 ? args[0] : null;
            obj.Freeze();
            return obj;
        });

        // Object.seal(obj)
        objectConstructor["seal"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return args.Count > 0 ? args[0] : null;
            obj.Seal();
            return obj;
        });

        // Object.isFrozen(obj)
        objectConstructor["isFrozen"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return true; // Non-objects are considered frozen
            return obj.IsFrozen;
        });

        // Object.isSealed(obj)
        objectConstructor["isSealed"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return true; // Non-objects are considered sealed
            return obj.IsSealed;
        });

        // Object.create(proto, propertiesObject)
        objectConstructor["create"] = new HostFunction(args =>
        {
            var obj = new JsObject();
            if (args.Count > 0 && args[0] != null)
            {
                obj.SetPrototype(args[0]);
            }
            
            // Handle second parameter: property descriptors
            if (args.Count > 1 && args[1] is JsObject propsObj)
            {
                foreach (var propName in propsObj.GetOwnPropertyNames())
                {
                    if (propsObj.TryGetValue(propName, out var descriptorObj) && descriptorObj is JsObject descObj)
                    {
                        var descriptor = new PropertyDescriptor();
                        
                        // Check if this is an accessor descriptor
                        var hasGet = descObj.TryGetValue("get", out var getVal);
                        var hasSet = descObj.TryGetValue("set", out var setVal);
                        
                        if (hasGet || hasSet)
                        {
                            // Accessor descriptor
                            if (hasGet && getVal is IJsCallable getter)
                            {
                                descriptor.Get = getter;
                            }
                            if (hasSet && setVal is IJsCallable setter)
                            {
                                descriptor.Set = setter;
                            }
                        }
                        else
                        {
                            // Data descriptor
                            if (descObj.TryGetValue("value", out var value))
                            {
                                descriptor.Value = value;
                            }
                            
                            if (descObj.TryGetValue("writable", out var writableVal))
                            {
                                descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                            }
                        }
                        
                        // Common properties
                        if (descObj.TryGetValue("enumerable", out var enumerableVal))
                        {
                            descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
                        }
                        else
                        {
                            descriptor.Enumerable = false; // Default is false for Object.create
                        }
                        
                        if (descObj.TryGetValue("configurable", out var configurableVal))
                        {
                            descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
                        }
                        else
                        {
                            descriptor.Configurable = false; // Default is false for Object.create
                        }
                        
                        obj.DefineProperty(propName, descriptor);
                    }
                }
            }
            
            return obj;
        });

        // Object.getOwnPropertyNames(obj)
        objectConstructor["getOwnPropertyNames"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj) return new JsArray();
            
            var names = new JsArray();
            foreach (var name in obj.GetOwnPropertyNames())
            {
                names.Push(name);
            }
            AddArrayMethods(names);
            return names;
        });

        // Object.getOwnPropertyDescriptor(obj, prop)
        objectConstructor["getOwnPropertyDescriptor"] = new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not JsObject obj) return JsSymbols.Undefined;
            var propName = args[1]?.ToString() ?? "";
            
            var desc = obj.GetOwnPropertyDescriptor(propName);
            if (desc == null) return JsSymbols.Undefined;
            
            var resultDesc = new JsObject();
            if (desc.IsAccessorDescriptor)
            {
                if (desc.Get != null)
                {
                    resultDesc["get"] = desc.Get;
                }
                if (desc.Set != null)
                {
                    resultDesc["set"] = desc.Set;
                }
            }
            else
            {
                resultDesc["value"] = desc.Value;
                resultDesc["writable"] = desc.Writable;
            }
            
            resultDesc["enumerable"] = desc.Enumerable;
            resultDesc["configurable"] = desc.Configurable;
            
            return resultDesc;
        });

        // Object.defineProperty(obj, prop, descriptor)
        objectConstructor["defineProperty"] = new HostFunction(args =>
        {
            if (args.Count < 3 || args[0] is not JsObject obj) return args.Count > 0 ? args[0] : null;
            var propName = args[1]?.ToString() ?? "";
            
            if (args[2] is JsObject descriptorObj)
            {
                var descriptor = new PropertyDescriptor();
                
                // Check if this is an accessor descriptor
                var hasGet = descriptorObj.TryGetValue("get", out var getVal);
                var hasSet = descriptorObj.TryGetValue("set", out var setVal);
                
                if (hasGet || hasSet)
                {
                    // Accessor descriptor
                    if (hasGet && getVal is IJsCallable getter)
                    {
                        descriptor.Get = getter;
                    }
                    if (hasSet && setVal is IJsCallable setter)
                    {
                        descriptor.Set = setter;
                    }
                }
                else
                {
                    // Data descriptor
                    if (descriptorObj.TryGetValue("value", out var value))
                    {
                        descriptor.Value = value;
                    }
                    
                    if (descriptorObj.TryGetValue("writable", out var writableVal))
                    {
                        descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                    }
                }
                
                // Common properties
                if (descriptorObj.TryGetValue("enumerable", out var enumerableVal))
                {
                    descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
                }
                
                if (descriptorObj.TryGetValue("configurable", out var configurableVal))
                {
                    descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
                }
                
                obj.DefineProperty(propName, descriptor);
            }
            
            return obj;
        });

        return objectConstructor;
    }

    /// <summary>
    /// Creates the Array constructor with static methods.
    /// </summary>
    public static HostFunction CreateArrayConstructor()
    {
        // Array constructor
        var arrayConstructor = new HostFunction(args =>
        {
            // Array(length) or Array(element0, element1, ...)
            if (args.Count == 1 && args[0] is double length)
            {
                var arr = new JsArray();
                var len = (int)length;
                for (int i = 0; i < len; i++)
                {
                    arr.Push(null);
                }
                AddArrayMethods(arr);
                return arr;
            }
            else
            {
                var arr = new JsArray(args);
                AddArrayMethods(arr);
                return arr;
            }
        });

        // Array.isArray(value)
        arrayConstructor.SetProperty("isArray", new HostFunction(args =>
        {
            if (args.Count == 0) return false;
            return args[0] is JsArray;
        }));

        // Array.from(arrayLike)
        arrayConstructor.SetProperty("from", new HostFunction(args =>
        {
            if (args.Count == 0) return new JsArray();
            
            var items = new List<object?>();
            
            if (args[0] is JsArray jsArray)
            {
                for (int i = 0; i < jsArray.Items.Count; i++)
                {
                    items.Add(jsArray.GetElement(i));
                }
            }
            else if (args[0] is string str)
            {
                foreach (char c in str)
                {
                    items.Add(c.ToString());
                }
            }
            else
            {
                return new JsArray();
            }
            
            var result = new JsArray(items);
            AddArrayMethods(result);
            return result;
        }));

        // Array.of(...elements)
        arrayConstructor.SetProperty("of", new HostFunction(args =>
        {
            var arr = new JsArray(args);
            AddArrayMethods(arr);
            return arr;
        }));

        return arrayConstructor;
    }

    /// <summary>
    /// Creates the Symbol constructor function with static methods.
    /// </summary>
    public static HostFunction CreateSymbolConstructor()
    {
        // Symbol cannot be used with 'new' in JavaScript
        var symbolConstructor = new HostFunction(args =>
        {
            var description = args.Count > 0 && args[0] != null && !ReferenceEquals(args[0], JsSymbols.Undefined)
                ? args[0]!.ToString()
                : null;
            return JsSymbol.Create(description);
        });

        // Symbol.for(key) - creates/retrieves a global symbol
        symbolConstructor.SetProperty("for", new HostFunction(args =>
        {
            if (args.Count == 0) return JsSymbols.Undefined;
            var key = args[0]?.ToString() ?? "";
            return JsSymbol.For(key);
        }));

        // Symbol.keyFor(symbol) - gets the key for a global symbol
        symbolConstructor.SetProperty("keyFor", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsSymbol sym)
            {
                return JsSymbols.Undefined;
            }
            var key = JsSymbol.KeyFor(sym);
            return key ?? (object)JsSymbols.Undefined;
        }));

        // Well-known symbols
        symbolConstructor.SetProperty("iterator", JsSymbol.For("Symbol.iterator"));
        symbolConstructor.SetProperty("asyncIterator", JsSymbol.For("Symbol.asyncIterator"));

        return symbolConstructor;
    }

    /// <summary>
    /// Creates the Map constructor function.
    /// </summary>
    public static IJsCallable CreateMapConstructor()
    {
        var mapConstructor = new HostFunction(args =>
        {
            var map = new JsMap();
            
            // If an iterable is provided, populate the map
            if (args.Count > 0 && args[0] is JsArray entries)
            {
                foreach (var entry in entries.Items)
                {
                    if (entry is JsArray pair && pair.Items.Count >= 2)
                    {
                        map.Set(pair.GetElement(0), pair.GetElement(1));
                    }
                }
            }
            
            AddMapMethods(map);
            return map;
        });

        return mapConstructor;
    }

    /// <summary>
    /// Adds instance methods to a Map object.
    /// </summary>
    private static void AddMapMethods(JsMap map)
    {
        // Note: size needs special handling as a getter - for now we'll just access it dynamically in the methods
        
        // set(key, value)
        map.SetProperty("set", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return JsSymbols.Undefined;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            var value = args.Count > 1 ? args[1] : JsSymbols.Undefined;
            return m.Set(key, value);
        }));

        // get(key)
        map.SetProperty("get", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return JsSymbols.Undefined;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return m.Get(key);
        }));

        // has(key)
        map.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return false;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return m.Has(key);
        }));

        // delete(key)
        map.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return false;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return m.Delete(key);
        }));

        // clear()
        map.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsMap m) m.Clear();
            return JsSymbols.Undefined;
        }));

        // forEach(callback, thisArg)
        map.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return JsSymbols.Undefined;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return JsSymbols.Undefined;
            var thisArg = args.Count > 1 ? args[1] : null;
            m.ForEach(callback, thisArg);
            return JsSymbols.Undefined;
        }));

        // entries()
        map.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return JsSymbols.Undefined;
            return m.Entries();
        }));

        // keys()
        map.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return JsSymbols.Undefined;
            return m.Keys();
        }));

        // values()
        map.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m) return JsSymbols.Undefined;
            return m.Values();
        }));
    }

    /// <summary>
    /// Creates the Set constructor function.
    /// </summary>
    public static IJsCallable CreateSetConstructor()
    {
        var setConstructor = new HostFunction(args =>
        {
            var set = new JsSet();
            
            // If an iterable is provided, populate the set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    set.Add(value);
                }
            }
            
            AddSetMethods(set);
            return set;
        });

        return setConstructor;
    }

    /// <summary>
    /// Adds instance methods to a Set object.
    /// </summary>
    private static void AddSetMethods(JsSet set)
    {
        // Note: size needs special handling as a getter - handled in Evaluator.TryGetPropertyValue
        
        // add(value)
        set.SetProperty("add", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return JsSymbols.Undefined;
            var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return s.Add(value);
        }));

        // has(value)
        set.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return false;
            var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return s.Has(value);
        }));

        // delete(value)
        set.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return false;
            var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return s.Delete(value);
        }));

        // clear()
        set.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsSet s) s.Clear();
            return JsSymbols.Undefined;
        }));

        // forEach(callback, thisArg)
        set.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return JsSymbols.Undefined;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return JsSymbols.Undefined;
            var thisArg = args.Count > 1 ? args[1] : null;
            s.ForEach(callback, thisArg);
            return JsSymbols.Undefined;
        }));

        // entries()
        set.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return JsSymbols.Undefined;
            return s.Entries();
        }));

        // keys()
        set.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return JsSymbols.Undefined;
            return s.Keys();
        }));

        // values()
        set.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s) return JsSymbols.Undefined;
            return s.Values();
        }));
    }

    /// <summary>
    /// Creates the WeakMap constructor function.
    /// </summary>
    public static IJsCallable CreateWeakMapConstructor()
    {
        var weakMapConstructor = new HostFunction(args =>
        {
            var weakMap = new JsWeakMap();
            
            // Note: WeakMap constructor can accept an iterable, but we'll start with basic support
            // If an iterable is provided, populate the weak map
            if (args.Count > 0 && args[0] is JsArray entries)
            {
                foreach (var entry in entries.Items)
                {
                    if (entry is JsArray pair && pair.Items.Count >= 2)
                    {
                        try
                        {
                            weakMap.Set(pair.GetElement(0), pair.GetElement(1));
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(ex.Message);
                        }
                    }
                }
            }
            
            AddWeakMapMethods(weakMap);
            return weakMap;
        });

        return weakMapConstructor;
    }

    /// <summary>
    /// Adds instance methods to a WeakMap object.
    /// </summary>
    private static void AddWeakMapMethods(JsWeakMap weakMap)
    {
        // set(key, value)
        weakMap.SetProperty("set", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm) return JsSymbols.Undefined;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            var value = args.Count > 1 ? args[1] : JsSymbols.Undefined;
            try
            {
                return wm.Set(key, value);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }));

        // get(key)
        weakMap.SetProperty("get", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm) return JsSymbols.Undefined;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return wm.Get(key);
        }));

        // has(key)
        weakMap.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm) return false;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return wm.Has(key);
        }));

        // delete(key)
        weakMap.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm) return false;
            var key = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return wm.Delete(key);
        }));
    }

    /// <summary>
    /// Creates the WeakSet constructor function.
    /// </summary>
    public static IJsCallable CreateWeakSetConstructor()
    {
        var weakSetConstructor = new HostFunction(args =>
        {
            var weakSet = new JsWeakSet();
            
            // If an iterable is provided, populate the weak set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    try
                    {
                        weakSet.Add(value);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message);
                    }
                }
            }
            
            AddWeakSetMethods(weakSet);
            return weakSet;
        });

        return weakSetConstructor;
    }

    /// <summary>
    /// Adds instance methods to a WeakSet object.
    /// </summary>
    private static void AddWeakSetMethods(JsWeakSet weakSet)
    {
        // add(value)
        weakSet.SetProperty("add", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws) return JsSymbols.Undefined;
            var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            try
            {
                return ws.Add(value);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }));

        // has(value)
        weakSet.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws) return false;
            var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return ws.Has(value);
        }));

        // delete(value)
        weakSet.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws) return false;
            var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
            return ws.Delete(value);
        }));
    }

    /// <summary>
    /// Creates the Number constructor with static methods.
    /// </summary>
    public static HostFunction CreateNumberConstructor()
    {
        // Number constructor
        var numberConstructor = new HostFunction(args =>
        {
            if (args.Count == 0) return 0d;
            
            var value = args[0];
            // Convert to number
            if (value is double d) return d;
            if (value is string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0d;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
                return double.NaN;
            }
            if (value is bool b) return b ? 1d : 0d;
            if (value == null) return 0d;
            if (ReferenceEquals(value, JsSymbols.Undefined)) return double.NaN;
            
            return double.NaN;
        });

        // Number.isInteger(value)
        numberConstructor.SetProperty("isInteger", new HostFunction(args =>
        {
            if (args.Count == 0) return false;
            if (args[0] is not double d) return false;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            return Math.Abs(d % 1) < double.Epsilon;
        }));

        // Number.isFinite(value)
        numberConstructor.SetProperty("isFinite", new HostFunction(args =>
        {
            if (args.Count == 0) return false;
            if (args[0] is not double d) return false;
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }));

        // Number.isNaN(value)
        numberConstructor.SetProperty("isNaN", new HostFunction(args =>
        {
            if (args.Count == 0) return false;
            if (args[0] is not double d) return false;
            return double.IsNaN(d);
        }));

        // Number.isSafeInteger(value)
        numberConstructor.SetProperty("isSafeInteger", new HostFunction(args =>
        {
            if (args.Count == 0) return false;
            if (args[0] is not double d) return false;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            if (Math.Abs(d % 1) >= double.Epsilon) return false; // Not an integer
            return Math.Abs(d) <= 9007199254740991; // MAX_SAFE_INTEGER
        }));

        // Number.parseFloat(string)
        numberConstructor.SetProperty("parseFloat", new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str == "") return double.NaN;
            
            // Try to parse, taking as much as possible from the start
            var match = System.Text.RegularExpressions.Regex.Match(str, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
            if (match.Success)
            {
                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
                    return result;
            }
            
            if (str.StartsWith("Infinity")) return double.PositiveInfinity;
            if (str.StartsWith("+Infinity")) return double.PositiveInfinity;
            if (str.StartsWith("-Infinity")) return double.NegativeInfinity;
            
            return double.NaN;
        }));

        // Number.parseInt(string, radix)
        numberConstructor.SetProperty("parseInt", new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str == "") return double.NaN;
            
            var radix = args.Count > 1 && args[1] is double r ? (int)r : 10;
            if (radix < 2 || radix > 36) return double.NaN;
            
            // Handle sign
            var sign = 1;
            if (str.StartsWith("-"))
            {
                sign = -1;
                str = str.Substring(1).TrimStart();
            }
            else if (str.StartsWith("+"))
            {
                str = str.Substring(1).TrimStart();
            }
            
            // Parse until we hit invalid character
            double result = 0;
            var hasDigits = false;
            foreach (var c in str)
            {
                int digit;
                if (char.IsDigit(c))
                {
                    digit = c - '0';
                }
                else if (char.IsLetter(c))
                {
                    var upper = char.ToUpperInvariant(c);
                    digit = upper - 'A' + 10;
                }
                else
                {
                    break; // Stop at first invalid character
                }
                
                if (digit >= radix) break;
                
                result = result * radix + digit;
                hasDigits = true;
            }
            
            return hasDigits ? result * sign : double.NaN;
        }));

        // Number.EPSILON
        numberConstructor.SetProperty("EPSILON", double.Epsilon);
        
        // Number.MAX_SAFE_INTEGER
        numberConstructor.SetProperty("MAX_SAFE_INTEGER", 9007199254740991d);
        
        // Number.MIN_SAFE_INTEGER
        numberConstructor.SetProperty("MIN_SAFE_INTEGER", -9007199254740991d);
        
        // Number.MAX_VALUE
        numberConstructor.SetProperty("MAX_VALUE", double.MaxValue);
        
        // Number.MIN_VALUE
        numberConstructor.SetProperty("MIN_VALUE", double.MinValue);
        
        // Number.POSITIVE_INFINITY
        numberConstructor.SetProperty("POSITIVE_INFINITY", double.PositiveInfinity);
        
        // Number.NEGATIVE_INFINITY
        numberConstructor.SetProperty("NEGATIVE_INFINITY", double.NegativeInfinity);
        
        // Number.NaN
        numberConstructor.SetProperty("NaN", double.NaN);

        return numberConstructor;
    }

    /// <summary>
    /// Creates the String constructor with static methods.
    /// </summary>
    public static HostFunction CreateStringConstructor()
    {
        // String constructor
        var stringConstructor = new HostFunction(args =>
        {
            if (args.Count == 0) return "";
            
            var value = args[0];
            // Convert to string
            if (value is string s) return s;
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is bool b) return b ? "true" : "false";
            if (value == null) return "null";
            if (ReferenceEquals(value, JsSymbols.Undefined)) return "undefined";
            
            return value.ToString() ?? "";
        });

        // String.fromCodePoint(...codePoints)
        stringConstructor.SetProperty("fromCodePoint", new HostFunction(args =>
        {
            if (args.Count == 0) return "";
            
            var result = new System.Text.StringBuilder();
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    var codePoint = (int)d;
                    // Validate code point range
                    if (codePoint < 0 || codePoint > 0x10FFFF)
                    {
                        throw new Exception("RangeError: Invalid code point " + codePoint);
                    }
                    
                    // Handle surrogate pairs for code points > 0xFFFF
                    if (codePoint <= 0xFFFF)
                    {
                        result.Append((char)codePoint);
                    }
                    else
                    {
                        // Convert to surrogate pair
                        codePoint -= 0x10000;
                        result.Append((char)(0xD800 + (codePoint >> 10)));
                        result.Append((char)(0xDC00 + (codePoint & 0x3FF)));
                    }
                }
            }
            return result.ToString();
        }));

        // String.fromCharCode(...charCodes) - for compatibility
        stringConstructor.SetProperty("fromCharCode", new HostFunction(args =>
        {
            if (args.Count == 0) return "";
            
            var result = new System.Text.StringBuilder();
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    var charCode = (int)d & 0xFFFF; // Limit to 16-bit range
                    result.Append((char)charCode);
                }
            }
            return result.ToString();
        }));

        // String.raw(template, ...substitutions)
        // This is a special method used with tagged templates
        stringConstructor.SetProperty("raw", new HostFunction(args =>
        {
            if (args.Count == 0) return "";
            
            // First argument should be a template object with 'raw' property
            if (args[0] is not JsObject template)
            {
                return "";
            }
            
            // Get the raw strings array
            if (!template.TryGetProperty("raw", out var rawValue) || rawValue is not JsArray rawStrings)
            {
                return "";
            }
            
            var result = new System.Text.StringBuilder();
            var rawCount = rawStrings.Items.Count;
            
            for (int i = 0; i < rawCount; i++)
            {
                // Append the raw string part
                var rawPart = rawStrings.GetElement(i)?.ToString() ?? "";
                result.Append(rawPart);
                
                // Append the substitution if there is one
                if (i < args.Count - 1)
                {
                    var substitution = args[i + 1];
                    if (substitution != null)
                    {
                        result.Append(substitution.ToString());
                    }
                }
            }
            
            return result.ToString();
        }));

        return stringConstructor;
    }

    /// <summary>
    /// Creates error constructor functions for standard JavaScript error types.
    /// </summary>
    public static HostFunction CreateErrorConstructor(string errorType = "Error")
    {
        var errorConstructor = new HostFunction((thisValue, args) =>
        {
            var errorObj = new JsObject();
            var message = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "";
            
            errorObj["name"] = errorType;
            errorObj["message"] = message;
            errorObj["toString"] = new HostFunction((errThis, toStringArgs) =>
            {
                if (errThis is JsObject err)
                {
                    var name = err.TryGetValue("name", out var n) ? n?.ToString() : errorType;
                    var msg = err.TryGetValue("message", out var m) ? m?.ToString() : "";
                    return string.IsNullOrEmpty(msg) ? name : $"{name}: {msg}";
                }
                return errorType;
            });
            
            return errorObj;
        });
        
        return errorConstructor;
    }

    /// <summary>
    /// Creates the eval function that evaluates JavaScript code in the current context.
    /// </summary>
    public static HostFunction CreateEvalFunction(JsEngine engine)
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string code)
            {
                return args.Count > 0 ? args[0] : JsSymbols.Undefined;
            }
            
            // Evaluate the code string using the engine
            return engine.Evaluate(code);
        });
    }
    
    /// <summary>
    /// Converts a value to a boolean following JavaScript truthiness rules.
    /// </summary>
    private static bool ToBoolean(object? value) => value switch
    {
        null => false,
        Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => false,
        bool b => b,
        double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
        string s => s.Length > 0,
        _ => true
    };

    /// <summary>
    /// Creates the ArrayBuffer constructor.
    /// </summary>
    public static HostFunction CreateArrayBufferConstructor()
    {
        var constructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return new JsArrayBuffer(0);
            }
            
            var length = args[0] switch
            {
                double d => (int)d,
                int i => i,
                _ => 0
            };
            
            return new JsArrayBuffer(length);
        });
        
        constructor.SetProperty("isView", new HostFunction(args =>
        {
            if (args.Count == 0) return false;
            return args[0] is TypedArrayBase || args[0] is JsDataView;
        }));
        
        return constructor;
    }

    /// <summary>
    /// Creates the DataView constructor.
    /// </summary>
    public static HostFunction CreateDataViewConstructor()
    {
        return new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0 || args[0] is not JsArrayBuffer buffer)
            {
                throw new InvalidOperationException("DataView requires an ArrayBuffer");
            }
            
            var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            int? byteLength = args.Count > 2 && args[2] is double d2 ? (int)d2 : null;
            
            return new JsDataView(buffer, byteOffset, byteLength);
        });
    }

    /// <summary>
    /// Creates a typed array constructor.
    /// </summary>
    private static HostFunction CreateTypedArrayConstructor<T>(
        Func<int, T> fromLength,
        Func<JsArray, T> fromArray,
        Func<JsArrayBuffer, int, int, T> fromBuffer,
        int bytesPerElement) where T : TypedArrayBase
    {
        var constructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return fromLength(0);
            }
            
            var firstArg = args[0];
            
            // TypedArray(length)
            if (firstArg is double d)
            {
                return fromLength((int)d);
            }
            
            // TypedArray(array)
            if (firstArg is JsArray array)
            {
                return fromArray(array);
            }
            
            // TypedArray(buffer, byteOffset, length)
            if (firstArg is JsArrayBuffer buffer)
            {
                var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
                
                int length;
                if (args.Count > 2 && args[2] is double d2)
                {
                    length = (int)d2;
                }
                else
                {
                    // Calculate length from remaining buffer
                    var remainingBytes = buffer.ByteLength - byteOffset;
                    length = remainingBytes / bytesPerElement;
                }
                
                return fromBuffer(buffer, byteOffset, length);
            }
            
            return fromLength(0);
        });
        
        constructor.SetProperty("BYTES_PER_ELEMENT", (double)bytesPerElement);
        
        return constructor;
    }

    public static HostFunction CreateInt8ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsInt8Array.FromLength,
            JsInt8Array.FromArray,
            (buffer, offset, length) => new JsInt8Array(buffer, offset, length),
            JsInt8Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateUint8ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsUint8Array.FromLength,
            JsUint8Array.FromArray,
            (buffer, offset, length) => new JsUint8Array(buffer, offset, length),
            JsUint8Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateUint8ClampedArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsUint8ClampedArray.FromLength,
            JsUint8ClampedArray.FromArray,
            (buffer, offset, length) => new JsUint8ClampedArray(buffer, offset, length),
            JsUint8ClampedArray.BYTES_PER_ELEMENT);

    public static HostFunction CreateInt16ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsInt16Array.FromLength,
            JsInt16Array.FromArray,
            (buffer, offset, length) => new JsInt16Array(buffer, offset, length),
            JsInt16Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateUint16ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsUint16Array.FromLength,
            JsUint16Array.FromArray,
            (buffer, offset, length) => new JsUint16Array(buffer, offset, length),
            JsUint16Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateInt32ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsInt32Array.FromLength,
            JsInt32Array.FromArray,
            (buffer, offset, length) => new JsInt32Array(buffer, offset, length),
            JsInt32Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateUint32ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsUint32Array.FromLength,
            JsUint32Array.FromArray,
            (buffer, offset, length) => new JsUint32Array(buffer, offset, length),
            JsUint32Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateFloat32ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsFloat32Array.FromLength,
            JsFloat32Array.FromArray,
            (buffer, offset, length) => new JsFloat32Array(buffer, offset, length),
            JsFloat32Array.BYTES_PER_ELEMENT);

    public static HostFunction CreateFloat64ArrayConstructor() =>
        CreateTypedArrayConstructor(
            JsFloat64Array.FromLength,
            JsFloat64Array.FromArray,
            (buffer, offset, length) => new JsFloat64Array(buffer, offset, length),
            JsFloat64Array.BYTES_PER_ELEMENT);

    /// <summary>
    /// Helper method for async iteration: gets an async iterator from an iterable.
    /// For for-await-of: tries Symbol.asyncIterator first, falls back to Symbol.iterator.
    /// </summary>
    public static HostFunction CreateGetAsyncIteratorHelper()
    {
        return new HostFunction(args =>
        {
            // args[0] should be the iterable
            if (args.Count == 0)
                throw new InvalidOperationException("__getAsyncIterator requires an iterable");

            var iterable = args[0];
            
            // Handle generators - they are already iterators
            if (iterable is JsGenerator generator)
            {
                // Wrap the generator in a JsObject that exposes the Next method
                var iteratorObj = new JsObject();
                iteratorObj.SetProperty("next", new HostFunction(_ =>
                {
                    return generator.Next();
                }));
                iteratorObj.SetProperty("return", new HostFunction(args =>
                {
                    var value = args.Count > 0 ? args[0] : null;
                    return generator.Return(value);
                }));
                iteratorObj.SetProperty("throw", new HostFunction(args =>
                {
                    var error = args.Count > 0 ? args[0] : null;
                    return generator.Throw(error);
                }));
                return iteratorObj;
            }
            
            // Handle strings specially - they need Symbol.iterator
            if (iterable is string str)
            {
                // Create a string iterator manually
                var index = 0;
                var iteratorObj = new JsObject();
                iteratorObj.SetProperty("next", new HostFunction(_ =>
                {
                    if (index < str.Length)
                    {
                        var result = new JsObject();
                        result.SetProperty("value", str[index].ToString());
                        result.SetProperty("done", false);
                        index++;
                        return result;
                    }
                    else
                    {
                        var result = new JsObject();
                        result.SetProperty("done", true);
                        return result;
                    }
                }));
                return iteratorObj;
            }
            
            // For objects, check if it's already an iterator (has a "next" method)
            // This handles generator objects which are returned from generator functions
            if (iterable is JsObject jsObj)
            {
                // Check if it's already an iterator (has a "next" method)
                if (jsObj.TryGetProperty("next", out var nextMethod) && nextMethod is IJsCallable)
                {
                    // It's already an iterator, return it as-is
                    return jsObj;
                }
                
                // Try Symbol.asyncIterator
                var asyncIteratorSymbol = JsSymbol.For("Symbol.asyncIterator");
                var asyncIteratorKey = $"@@symbol:{asyncIteratorSymbol.GetHashCode()}";
                if (jsObj.TryGetProperty(asyncIteratorKey, out var asyncIteratorMethod) && asyncIteratorMethod is IJsCallable asyncIteratorCallable)
                {
                    return asyncIteratorCallable.Invoke([], jsObj);
                }
                
                // Fall back to Symbol.iterator
                var iteratorSymbol = JsSymbol.For("Symbol.iterator");
                var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
                if (jsObj.TryGetProperty(iteratorKey, out var iteratorMethod) && iteratorMethod is IJsCallable iteratorCallable)
                {
                    return iteratorCallable.Invoke([], jsObj);
                }
                
                throw new InvalidOperationException("Object is not iterable (no Symbol.asyncIterator or Symbol.iterator method)");
            }
            
            // For arrays, get the iterator
            if (iterable is JsArray jsArray)
            {
                var iteratorSymbol = JsSymbol.For("Symbol.iterator");
                var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
                if (jsArray.TryGetProperty(iteratorKey, out var iteratorMethod) && iteratorMethod is IJsCallable iteratorCallable)
                {
                    return iteratorCallable.Invoke([], jsArray);
                }
            }
            
            throw new InvalidOperationException($"Value is not iterable: {iterable?.GetType().Name}");
        });
    }

    /// <summary>
    /// Helper method for async iteration: gets next value from iterator and wraps in Promise if needed.
    /// This handles both sync and async iterators uniformly.
    /// </summary>
    public static HostFunction CreateIteratorNextHelper(JsEngine engine)
    {
        return new HostFunction(args =>
        {
            // args[0] should be the iterator object
            if (args.Count == 0 || args[0] is not JsObject iterator)
                throw new InvalidOperationException("__iteratorNext requires an iterator object");

            // Call iterator.next()
            if (!iterator.TryGetProperty("next", out var nextMethod) || nextMethod is not IJsCallable nextCallable)
                throw new InvalidOperationException("Iterator must have a 'next' method");

            var result = nextCallable.Invoke([], iterator);

            // Check if result is already a promise (has a "then" method)
            if (result is JsObject resultObj && resultObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable)
            {
                // Already a promise, return as-is
                return result;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(result);
            return promise.JsObject;
        });
    }
    
    /// <summary>
    /// Helper function for await expressions: wraps value in Promise if needed.
    /// Checks if the value is already a promise (has a "then" method) before wrapping.
    /// </summary>
    public static HostFunction CreateAwaitHelper(JsEngine engine)
    {
        return new HostFunction(args =>
        {
            // args[0] should be the value to await
            var value = args.Count > 0 ? args[0] : null;

            // Check if value is already a promise (has a "then" method)
            if (value is JsObject valueObj && valueObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable)
            {
                // Already a promise, return as-is
                return value;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(value);
            return promise.JsObject;
        });
    }
}
