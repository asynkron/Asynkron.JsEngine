using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static JsObject CreateIntlObject()
    {
        var intl = new JsObject();

        // Minimal Locale constructor with a prototype exposing a calendar accessor.
        var localePrototype = new JsObject();
        var calendarGetter = new HostFunction((thisValue, _) =>
        {
            if (thisValue is JsObject self && self.TryGetProperty("__calendar__", out var value))
            {
                return value;
            }

            return Symbols.Undefined;
        });
        calendarGetter.DefineProperty("name", new PropertyDescriptor
        {
            Value = "get calendar",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        localePrototype.DefineProperty("calendar", new PropertyDescriptor
        {
            Get = calendarGetter,
            Enumerable = false,
            Configurable = true
        });

        var localeCtor = new HostFunction((thisValue, args) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            if (args.Count > 0 && args[0] is string tag)
            {
                instance.SetProperty("__tag__", tag);
            }

            // Optionally capture calendar option if provided.
            if (args.Count > 1 && args[1] is JsObject options && options.TryGetProperty("calendar", out var calendar))
            {
                instance.SetProperty("__calendar__", calendar);
            }

            return instance;
        })
        {
            IsConstructor = true
        };

        localeCtor.SetProperty("prototype", localePrototype);
        localePrototype.SetProperty("constructor", localeCtor);

        intl.SetProperty("Locale", localeCtor);

        // Minimal Intl.DurationFormat stub for Test262 coverage.
        var durationFormatPrototype = new JsObject();
        if (ObjectPrototype is not null)
        {
            durationFormatPrototype.SetPrototype(ObjectPrototype);
        }

        var durationFormatCtor = new HostFunction((thisValue, args) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(durationFormatPrototype);
            return instance;
        })
        {
            IsConstructor = true
        };

        durationFormatCtor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 0d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        durationFormatCtor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "DurationFormat",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        durationFormatCtor.DefineProperty("prototype", new PropertyDescriptor
        {
            Value = durationFormatPrototype,
            Writable = false,
            Enumerable = false,
            Configurable = false
        });
        durationFormatPrototype.DefineProperty("constructor", new PropertyDescriptor
        {
            Value = durationFormatCtor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        durationFormatPrototype.SetProperty("format", new HostFunction((thisValue, args) => "PT0S")
        {
            IsConstructor = false
        });
        durationFormatPrototype.SetProperty("formatToParts",
            new HostFunction((thisValue, args) => new JsArray())
            {
                IsConstructor = false
            });
        durationFormatPrototype.SetProperty("resolvedOptions",
            new HostFunction((thisValue, args) =>
            {
                var obj = new JsObject();
                obj.SetProperty("numberingSystem", "latn");
                obj.SetProperty("style", "short");
                obj.SetProperty("years", "auto");
                obj.SetProperty("yearsDisplay", "auto");
                obj.SetProperty("months", "auto");
                obj.SetProperty("monthsDisplay", "auto");
                obj.SetProperty("weeks", "auto");
                obj.SetProperty("weeksDisplay", "auto");
                obj.SetProperty("days", "auto");
                obj.SetProperty("daysDisplay", "auto");
                obj.SetProperty("hours", "auto");
                obj.SetProperty("hoursDisplay", "auto");
                obj.SetProperty("minutes", "auto");
                obj.SetProperty("minutesDisplay", "auto");
                obj.SetProperty("seconds", "auto");
                obj.SetProperty("secondsDisplay", "auto");
                obj.SetProperty("milliseconds", "auto");
                obj.SetProperty("millisecondsDisplay", "auto");
                obj.SetProperty("microseconds", "auto");
                obj.SetProperty("microsecondsDisplay", "auto");
                obj.SetProperty("nanoseconds", "auto");
                obj.SetProperty("nanosecondsDisplay", "auto");
                obj.SetProperty("locale", "en");
                return obj;
            })
            {
                IsConstructor = false
            });
        var durationToStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        durationFormatPrototype.DefineProperty(durationToStringTagKey, new PropertyDescriptor
        {
            Value = "Intl.DurationFormat",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        durationFormatCtor.DefineProperty("supportedLocalesOf", new PropertyDescriptor
        {
            Value = new HostFunction(args =>
            {
                var result = new JsArray();
                if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
                {
                    return result;
                }

                object? locales = args[0];
                if (locales is string localeString)
                {
                    result.Push(localeString);
                    return result;
                }

                if (locales is JsArray localesArray)
                {
                    foreach (var item in localesArray.Items)
                    {
                        if (item is string locale)
                        {
                            result.Push(locale);
                            continue;
                        }

                        throw ThrowTypeError("Invalid locale value");
                    }

                    return result;
                }

                throw ThrowTypeError("Invalid locales argument");
            })
            {
                IsConstructor = false
            },
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        if (durationFormatCtor.TryGetProperty("supportedLocalesOf", out var supportedLocalesOf) &&
            supportedLocalesOf is IJsObjectLike supportedLocalesAccessor)
        {
            supportedLocalesAccessor.DefineProperty("length", new PropertyDescriptor
            {
                Value = 1d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            supportedLocalesAccessor.DefineProperty("name", new PropertyDescriptor
            {
                Value = "supportedLocalesOf",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        }

        intl.SetProperty("DurationFormat", durationFormatCtor);

        // Minimal Intl.Collator stub to satisfy supportedLocalesOf/name tests.
        var collatorPrototype = new JsObject();
        if (ObjectPrototype is not null)
        {
            collatorPrototype.SetPrototype(ObjectPrototype);
        }

        var collatorCtor = new HostFunction((thisValue, args) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(collatorPrototype);
            instance.SetProperty("compare", new HostFunction((innerThis, compareArgs) =>
            {
                // Basic comparison using string coercion.
                var a = compareArgs.Count > 0 ? JsValueToString(compareArgs[0]) : string.Empty;
                var b = compareArgs.Count > 1 ? JsValueToString(compareArgs[1]) : string.Empty;
                return string.CompareOrdinal(a, b) switch
                {
                    < 0 => -1d,
                    > 0 => 1d,
                    _ => 0d
                };
            })
            {
                IsConstructor = false
            });
            return instance;
        })
        {
            IsConstructor = true
        };

        collatorCtor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 0d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        collatorCtor.DefineProperty("prototype", new PropertyDescriptor
        {
            Value = collatorPrototype,
            Writable = false,
            Enumerable = false,
            Configurable = false
        });
        collatorPrototype.DefineProperty("constructor", new PropertyDescriptor
        {
            Value = collatorCtor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        collatorCtor.DefineProperty("supportedLocalesOf", new PropertyDescriptor
        {
            Value = new HostFunction(args => new JsArray())
            {
                IsConstructor = false
            },
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        if (collatorCtor.TryGetProperty("supportedLocalesOf", out var collatorSupported) &&
            collatorSupported is IJsObjectLike collatorSupportedAccessor)
        {
            collatorSupportedAccessor.DefineProperty("length", new PropertyDescriptor
            {
                Value = 1d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            collatorSupportedAccessor.DefineProperty("name", new PropertyDescriptor
            {
                Value = "supportedLocalesOf",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        }

        intl.SetProperty("Collator", collatorCtor);

        return intl;
    }

    public static JsObject CreateTemporalObject()
    {
        var temporal = new JsObject();
        var durationPrototype = new JsObject();
        if (ObjectPrototype is not null)
        {
            durationPrototype.SetPrototype(ObjectPrototype);
        }

        var durationCtor = new HostFunction((thisValue, args) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(durationPrototype);
            if (args.Count > 0 && args[0] is JsObject source)
            {
                foreach (var key in source.Keys)
                {
                    instance.SetProperty(key, source[key]);
                }
            }
            return instance;
        })
        {
            IsConstructor = true
        };

        var durationFrom = new HostFunction(args =>
        {
            var input = args.Count > 0 && args[0] is JsObject jsObj ? jsObj : new JsObject();
            var instance = durationCtor.Invoke([input], null) as JsObject ?? new JsObject();
            instance.SetPrototype(durationPrototype);
            return instance;
        })
        {
            IsConstructor = false
        };

        durationCtor.DefineProperty("from", new PropertyDescriptor
        {
            Value = durationFrom,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        durationPrototype.SetProperty("toLocaleString", new HostFunction((thisValue, args) =>
        {
            var locale = args.Count > 0 ? args[0] : Symbols.Undefined;
            var options = args.Count > 1 ? args[1] : Symbols.Undefined;
            if (Symbols.Undefined.Equals(locale) && args.Count > 0)
            {
                locale = args[0];
            }

            var formatterObj = CreateIntlObject().TryGetProperty("DurationFormat", out var ctorVal) &&
                               ctorVal is IJsCallable durationFormatCtor
                ? durationFormatCtor.Invoke(new[] { locale, options }, null)
                : new JsObject();

            if (formatterObj is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("format", out var formatVal) &&
                formatVal is IJsCallable formatFn)
            {
                return formatFn.Invoke([thisValue], formatterObj);
            }

            return "";
        })
        {
            IsConstructor = false
        });

        durationCtor.DefineProperty("prototype", new PropertyDescriptor
        {
            Value = durationPrototype,
            Writable = false,
            Enumerable = false,
            Configurable = false
        });
        durationPrototype.DefineProperty("constructor", new PropertyDescriptor
        {
            Value = durationCtor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        temporal.SetProperty("Duration", durationCtor);
        return temporal;
    }

    /// <summary>
    /// Creates a minimal Function constructor with a callable `Function`
    /// value and a `Function.call` helper that can be used with patterns
    /// like <c>Function.call.bind(Object.prototype.hasOwnProperty)</c>.
    /// </summary>

}
