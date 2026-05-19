using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlSupportedValuesTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task SupportedValuesRejectSymbolKeys()
    {
        await using var engine = CreateEngine();
        await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("Intl.supportedValuesOf(Symbol('k'));"));
    }

    [Fact]
    public async Task CurrencyValuesAlignWithDisplayNamesAndNumberFormat()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
                var currencies = Intl.supportedValuesOf("currency");
                if (currencies.length === 0) {
                    return [0];
                }

                var currency = currencies[0];
                var dn = new Intl.DisplayNames("en", { type: "currency", fallback: "none" });
                var name = dn.of(currency);
                var nf = new Intl.NumberFormat("en", { style: "currency", currency });
                var resolved = nf.resolvedOptions();
                var fallback = dn.of("ZZZ");
                return [currencies.length, currency, typeof name, resolved.currency, fallback === undefined];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.True(array.Items.Count >= 1, "Result array should expose at least the length slot.");
        var length = Assert.IsType<double>(array.Items[0].ToObject()!);
        Assert.True(length >= 0, "Currency list length should be non-negative.");
        if (length == 0)
        {
            return;
        }

        Assert.Equal(5, array.Items.Count);
        var currencyCode = Assert.IsType<string>(array.Items[1].ToObject()!);
        Assert.Equal(3, currencyCode.Length);
        var displayType = Assert.IsType<string>(array.Items[2].ToObject()!);
        Assert.Equal("string", displayType);
        var resolvedCurrency = Assert.IsType<string>(array.Items[3].ToObject()!);
        Assert.Equal(currencyCode, resolvedCurrency);
        var fallbackUndefined = Assert.IsType<bool>(array.Items[4].ToObject()!);
        Assert.True(fallbackUndefined);
    }

    [Fact]
    public async Task CollationValuesAlignWithCollatorResolvedOptions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
                var collations = Intl.supportedValuesOf("collation");
                var locales = ["en", "ar", "de", "es", "hi", "ko", "ln", "si", "sv", "zh"];

                for (var i = 0; i < collations.length; i++) {
                    var collation = collations[i];
                    var supported = false;
                    for (var j = 0; j < locales.length; j++) {
                        var collator = new Intl.Collator(locales[j], { collation });
                        if (collator.resolvedOptions().collation === collation) {
                            supported = true;
                            break;
                        }
                    }

                    if (!supported) {
                        return collation;
                    }
                }

                return true;
            })();
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task KnownButUnsupportedCollationsResolveToDefault()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
                var direct = new Intl.Collator("hi", { collation: "direct" }).resolvedOptions().collation;
                var searchjl = new Intl.Collator("ko", { collation: "searchjl" }).resolvedOptions().collation;
                var collations = Intl.supportedValuesOf("collation");
                return [direct, searchjl, collations.includes("direct"), collations.includes("searchjl")];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("default", array.Items[0].ToObject());
        Assert.Equal("default", array.Items[1].ToObject());
        Assert.Equal(false, array.Items[2].ToObject());
        Assert.Equal(false, array.Items[3].ToObject());
    }

    [Fact]
    public async Task CollatorOptionFromOuterForOfBindingSurvivesInnerLocaleLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";
            (function () {
                for (let collation of Intl.supportedValuesOf("collation")) {
                    if (collation !== "phonebk") {
                        continue;
                    }

                    for (let locale of ["en", "ar", "de"]) {
                        new Intl.Collator(locale, { collation });
                    }
                }

                return true;
            })();
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task CollatorSupportedValuesMirrorTest262KnownCollationProbe()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";
            (function () {
                function allCollations() {
                    return [
                        "big5han", "compat", "dict", "direct", "ducet", "emoji",
                        "eor", "gb2312", "phonebk", "phonetic", "pinyin",
                        "reformed", "search", "searchjl", "standard", "stroke",
                        "trad", "unihan", "zhuyin",
                    ];
                }

                const collations = Intl.supportedValuesOf("collation");
                const locales = ["en", "ar", "de", "es", "hi", "ko", "ln", "si", "sv", "zh"];

                for (let collation of collations) {
                    let supported = false;
                    for (let locale of locales) {
                        let obj;
                        try {
                            obj = new Intl.Collator(locale, { collation });
                        } catch (e) {
                            return "throw:" + collation + ":" + locale + ":" + e.message;
                        }
                        if (obj.resolvedOptions().collation === collation) {
                            supported = true;
                            break;
                        }
                    }

                    if (!supported) {
                        return "unsupported:" + collation;
                    }
                }

                for (let collation of allCollations()) {
                    let supported = false;
                    for (let locale of locales) {
                        let obj;
                        try {
                            obj = new Intl.Collator(locale, { collation });
                        } catch (e) {
                            return "throw:" + collation + ":" + locale + ":" + e.message;
                        }
                        if (obj.resolvedOptions().collation === collation) {
                            supported = true;
                            break;
                        }
                    }

                    if (supported && !collations.includes(collation)) {
                        return "missing:" + collation;
                    }

                    if (!supported && collations.includes(collation)) {
                        return "extra:" + collation;
                    }
                }

                return true;
            })();
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task SupportedValuesCoerceKeysWithToString()
    {
        await using var engine = CreateEngine();
        var baseline = Assert.IsType<JsArray>(await engine.Evaluate("Intl.supportedValuesOf('calendar');"));
        var viaStringObject =
            Assert.IsType<JsArray>(await engine.Evaluate("Intl.supportedValuesOf(new String('calendar'));"));
        var viaPlainObject = Assert.IsType<JsArray>(await engine.Evaluate("""
            Intl.supportedValuesOf({
                toString() {
                    return 'calendar';
                }
            });
            """));

        Assert.Equal(baseline.Items, viaStringObject.Items);
        Assert.Equal(baseline.Items, viaPlainObject.Items);
    }
}
