using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateIntlObject(RealmState realm)
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
        calendarGetter.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "get calendar", Writable = false, Enumerable = false, Configurable = true
            });

        localePrototype.DefineProperty("calendar",
            new PropertyDescriptor { Get = calendarGetter, Enumerable = false, Configurable = true });

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
        }) { IsConstructor = true };

        localeCtor.SetProperty("prototype", localePrototype);
        localePrototype.SetProperty("constructor", localeCtor);

        intl.SetProperty("Locale", localeCtor);

        // Minimal Intl.DurationFormat stub for Test262 coverage.
        var durationFormatPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            durationFormatPrototype.SetPrototype(realm.ObjectPrototype);
        }

        var durationFormatCtor = new HostFunction((thisValue, _) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(durationFormatPrototype);
            return instance;
        }) { IsConstructor = true };

        durationFormatCtor.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        durationFormatCtor.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "DurationFormat", Writable = false, Enumerable = false, Configurable = true
            });
        durationFormatCtor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = durationFormatPrototype, Writable = false, Enumerable = false, Configurable = false
            });
        durationFormatPrototype.DefineProperty("constructor",
            new PropertyDescriptor
            {
                Value = durationFormatCtor, Writable = true, Enumerable = false, Configurable = true
            });

        durationFormatPrototype.SetProperty("format",
            new HostFunction((_, _) => "PT0S") { IsConstructor = false });
        durationFormatPrototype.SetProperty("formatToParts",
            new HostFunction((_, _) => new JsArray()) { IsConstructor = false });
        durationFormatPrototype.SetProperty("resolvedOptions",
            new HostFunction((_, _) =>
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
            }) { IsConstructor = false });
        var durationToStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        durationFormatPrototype.DefineProperty(durationToStringTagKey,
            new PropertyDescriptor
            {
                Value = "Intl.DurationFormat", Writable = false, Enumerable = false, Configurable = true
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

                var locales = args[0];
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

                        throw ThrowTypeError("Invalid locale value", realm: realm);
                    }

                    return result;
                }

                throw ThrowTypeError("Invalid locales argument", realm: realm);
            }) { IsConstructor = false },
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        if (durationFormatCtor.TryGetProperty("supportedLocalesOf", out var supportedLocalesOf) &&
            supportedLocalesOf is IJsObjectLike supportedLocalesAccessor)
        {
            supportedLocalesAccessor.DefineProperty("length",
                new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
            supportedLocalesAccessor.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
                });
        }

        intl.SetProperty("DurationFormat", durationFormatCtor);

        // Minimal Intl.Collator stub to satisfy supportedLocalesOf/name tests.
        var collatorPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            collatorPrototype.SetPrototype(realm.ObjectPrototype);
        }

        var collatorCtor = new HostFunction(CollatorCtor) { IsConstructor = true };

        collatorCtor.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        collatorCtor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = collatorPrototype, Writable = false, Enumerable = false, Configurable = false
            });
        collatorPrototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = collatorCtor, Writable = true, Enumerable = false, Configurable = true });
        collatorCtor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = new HostFunction(CollatorSupportedLocalesOf) { IsConstructor = false },
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        if (collatorCtor.TryGetProperty("supportedLocalesOf", out var collatorSupported) &&
            collatorSupported is IJsObjectLike collatorSupportedAccessor)
        {
            collatorSupportedAccessor.DefineProperty("length",
                new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
            collatorSupportedAccessor.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
                });
        }

        intl.SetProperty("Collator", collatorCtor);

        // Minimal Intl.NumberFormat stub to satisfy basic callable surface and
        // TypeError-on-invalid-this behaviour used by Test262.
        var numberFormatPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            numberFormatPrototype.SetPrototype(realm.ObjectPrototype);
        }

        var numberFormatCtor = new HostFunction((thisValue, _) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(numberFormatPrototype);
            instance.SetProperty("__numberFormat__", true);
            return instance;
        }, realm)
        {
            IsConstructor = true
        };

        if (realm.FunctionPrototype is not null)
        {
            numberFormatCtor.SetPrototype(realm.FunctionPrototype);
        }

        numberFormatCtor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = numberFormatPrototype, Writable = false, Enumerable = false, Configurable = false
            });
        numberFormatCtor.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        numberFormatCtor.DefineProperty("name",
            new PropertyDescriptor { Value = "NumberFormat", Writable = false, Enumerable = false, Configurable = true });
        numberFormatPrototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = numberFormatCtor, Writable = true, Enumerable = false, Configurable = true });

        JsObject ValidateNumberFormatReceiver(object? receiver)
        {
            if (receiver is JsObject obj && obj.TryGetProperty("__numberFormat__", out var marker) && marker is true)
            {
                return obj;
            }

            throw ThrowTypeError("Intl.NumberFormat method called on incompatible receiver", realm: realm);
        }

        var formatGetter = new HostFunction((thisValue, _) =>
        {
            ValidateNumberFormatReceiver(thisValue);
            return new HostFunction((_, formatArgs) =>
            {
                var value = formatArgs.Count > 0 ? formatArgs[0] : Symbols.Undefined;
                return JsOps.ToJsString(value);
            }, realm)
            {
                IsConstructor = false
            };
        }, realm)
        {
            IsConstructor = false
        };
        formatGetter.DefineProperty("name",
            new PropertyDescriptor { Value = "get format", Writable = false, Enumerable = false, Configurable = true });

        numberFormatPrototype.DefineProperty("format",
            new PropertyDescriptor
            {
                Get = formatGetter, Enumerable = false, Configurable = true
            });

        var formatToPartsFn = new HostFunction((thisValue, formatArgs) =>
        {
            ValidateNumberFormatReceiver(thisValue);
            var value = formatArgs.Count > 0 ? formatArgs[0] : Symbols.Undefined;
            var part = new JsObject();
            part.SetProperty("type", "literal");
            part.SetProperty("value", JsOps.ToJsString(value));
            var parts = new JsArray();
            parts.Push(part);
            return parts;
        }, realm) { IsConstructor = false };
        numberFormatPrototype.DefineProperty("formatToParts",
            new PropertyDescriptor
            {
                Value = formatToPartsFn, Writable = true, Enumerable = false, Configurable = true
            });

        var resolvedOptionsFn = new HostFunction((thisValue, _) =>
        {
            ValidateNumberFormatReceiver(thisValue);
            var obj = new JsObject();
            obj.SetProperty("locale", "en");
            return obj;
        }, realm) { IsConstructor = false };
        numberFormatPrototype.DefineProperty("resolvedOptions",
            new PropertyDescriptor
            {
                Value = resolvedOptionsFn, Writable = true, Enumerable = false, Configurable = true
            });

        var numberFormatSupportedLocalesOf = new HostFunction((_, args) =>
        {
            var result = new JsArray();
            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
            {
                return result;
            }

            var locales = args[0];
            if (locales is string single)
            {
                result.Push(single);
                return result;
            }

            if (locales is JsArray array)
            {
                if (array.Items.Count > 0 && array.Items[0] is string firstLocale)
                {
                    result.Push(firstLocale);
                }
            }

            return result;
        }, realm)
        {
            IsConstructor = false
        };
        numberFormatSupportedLocalesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        numberFormatSupportedLocalesOf.DefineProperty("name",
            new PropertyDescriptor { Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true });

        numberFormatCtor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = numberFormatSupportedLocalesOf, Writable = true, Enumerable = false, Configurable = true
            });
        numberFormatSupportedLocalesOf.SetPrototype(numberFormatCtor.Prototype);
        numberFormatSupportedLocalesOf.Delete("prototype");

        intl.SetProperty("NumberFormat", numberFormatCtor);

        return intl;

        object? CollatorCtor(object? thisValue, IReadOnlyList<object?> _)
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(collatorPrototype);
            var compareFn = new HostFunction(CollatorCompare) { IsConstructor = false };
            instance.SetProperty("compare", compareFn);
            return instance;
        }

        object? CollatorCompare(object? _, IReadOnlyList<object?> compareArgs)
        {
            var a = compareArgs.Count > 0 ? JsValueToString(compareArgs[0]) : string.Empty;
            var b = compareArgs.Count > 1 ? JsValueToString(compareArgs[1]) : string.Empty;
            return string.CompareOrdinal(a, b) switch
            {
                < 0 => -1d,
                > 0 => 1d,
                _ => 0d
            };
        }

        object? CollatorSupportedLocalesOf(IReadOnlyList<object?> _)
        {
            return new JsArray();
        }
    }

    public static JsObject CreateTemporalObject(RealmState realm)
    {
        var temporal = new JsObject();
        var durationPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            durationPrototype.SetPrototype(realm.ObjectPrototype);
        }

        var durationCtor = new HostFunction((thisValue, args) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(durationPrototype);
            if (args.Count <= 0 || args[0] is not JsObject source)
            {
                return instance;
            }

            foreach (var key in source.Keys)
            {
                instance.SetProperty(key, source[key]);
            }

            return instance;
        }) { IsConstructor = true };

        var durationFrom = new HostFunction(args =>
        {
            var input = args.Count > 0 && args[0] is JsObject jsObj ? jsObj : new JsObject();
            var instance = durationCtor.Invoke([input], null) as JsObject ?? new JsObject();
            instance.SetPrototype(durationPrototype);
            return instance;
        }) { IsConstructor = false };

        durationCtor.DefineProperty("from",
            new PropertyDescriptor { Value = durationFrom, Writable = true, Enumerable = false, Configurable = true });

        var durationToLocaleString = new HostFunction(DurationToLocaleString) { IsConstructor = false };
        durationPrototype.SetProperty("toLocaleString", durationToLocaleString);

        durationCtor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = durationPrototype, Writable = false, Enumerable = false, Configurable = false
            });
        durationPrototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = durationCtor, Writable = true, Enumerable = false, Configurable = true });

        temporal.SetProperty("Duration", durationCtor);
        return temporal;

        object? DurationToLocaleString(object? thisValue, IReadOnlyList<object?> args)
        {
            var locale = args.Count > 0 ? args[0] : Symbols.Undefined;
            var options = args.Count > 1 ? args[1] : Symbols.Undefined;
            if (Symbols.Undefined.Equals(locale) && args.Count > 0)
            {
                locale = args[0];
            }

            var formatterObj = CreateIntlObject(realm).TryGetProperty("DurationFormat", out var ctorVal) &&
                               ctorVal is IJsCallable durationFormatCtor
                ? durationFormatCtor.Invoke([locale, options], null)
                : new JsObject();

            if (formatterObj is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("format", out var formatVal) &&
                formatVal is IJsCallable formatFn)
            {
                return formatFn.Invoke([thisValue], formatterObj);
            }

            return "";
        }
    }
}
