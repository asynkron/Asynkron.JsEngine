using System.Diagnostics;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class IntlLocaleDebugTests
{
    [Fact]
    public async Task Constructor_CanonicalizesCalendarAlias()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const locale = new Intl.Locale('en', { calendar: 'islamicc' });
            locale.calendar;
        ");

        Assert.Equal("islamic-civil", result?.ToString());
    }

    [Fact]
    public async Task Constructor_ReadsNumericBeforeNumberingSystem()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const order = [];
            const options = {
              get caseFirst() {
                order.push('caseFirst');
                return 'false';
              },
              get numeric() {
                order.push('numeric');
                return true;
              },
              get numberingSystem() {
                order.push('numberingSystem');
                return 'latn';
              },
            };

            new Intl.Locale('en', options);
            order.join(',');
        ");

        Assert.Equal("caseFirst,numeric,numberingSystem", result?.ToString());
    }

    [Fact]
    public async Task Constructor_UndefinedNumericDoesNotOverrideLocale()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const options = { numeric: undefined };
            const a = new Intl.Locale('en', options).toString();
            const b = new Intl.Locale('en-u-kn-true', options).toString();
            const c = new Intl.Locale('en-u-kf-lower', options).numeric;
            [a, b, String(c)].join('|');
        ");

        Assert.Equal("en|en-u-kn|false", result?.ToString());
    }

    [Fact]
    public async Task GetCanonicalLocales_UsesIntlLocaleInternalSlot()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const loc = new Intl.Locale('ar');
            class PatchedLocale extends Intl.Locale {
              constructor(tag, options) { super(tag, options); }
              toString() { throw new Error('toString should not be called'); }
            }
            const ploc = new PatchedLocale('fa');
            const values = Intl.getCanonicalLocales([loc, 'zh', ploc]);
            values.join('|');
        ");

        Assert.Equal("ar|zh|fa", result?.ToString());
    }

    [Fact]
    public async Task GrandfatheredTagsAreCanonicalizedInGetters()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const loc = new Intl.Locale('cel-gaulish');
            const loc2 = new Intl.Locale('cel', { variants: 'gaulish' });
            [
              loc.baseName,
              loc.language,
              String(loc.script),
              String(loc.region),
              String(loc.variants),
              loc2.baseName,
              loc2.language,
              String(loc2.variants)
            ].join('|');
        ");

        Assert.Equal("xtg|xtg|undefined|undefined|undefined|xtg|xtg|undefined", result?.ToString());
    }

    [Fact]
    public async Task Constructor_VariantsOptionRejectsDashAndDuplicateForms()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const values = ['', '-spanglis', 'spanglis-', 'spanglis--oxendict', 'fonipa-Fonipa'];
            values.map(value => {
              try {
                new Intl.Locale('en', { variants: value });
                return 'accepted';
              } catch (error) {
                return error instanceof RangeError ? 'range' : error.name;
              }
            }).join('|');
        ");

        Assert.Equal("range|range|range|range|range", result?.ToString());
    }

    [Fact]
    public async Task Constructor_VariantsOptionSortsCanonicalVariants()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            [
              new Intl.Locale('xx', { variants: '1xyz-1234-abcde-12345678' }).toString(),
              new Intl.Locale('en-fonipa-u-ca-gregory', { variants: 'spanglis-oxendict' }).toString()
            ].join('|');
        ");

        Assert.Equal("xx-1234-12345678-1xyz-abcde|en-oxendict-spanglis-u-ca-gregory", result?.ToString());
    }

    [Fact]
    public async Task LocaleGetters_TreatDigitFourSubtagAsVariant()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const loc = new Intl.Locale('de-1901');
            [loc.baseName, String(loc.script), String(loc.region), loc.variants].join('|');
        ");

        Assert.Equal("de-1901|undefined|undefined|1901", result?.ToString());
    }

    [Fact]
    public async Task LocaleLikelySubtags_PreserveGenericExtensions()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            [
              new Intl.Locale('en-a-not-assigned').maximize().toString(),
              new Intl.Locale('en-Latn-US-a-not-assigned').minimize().toString()
            ].join('|');
        ");

        Assert.Equal("en-Latn-US-a-not-assigned|en-a-not-assigned", result?.ToString());
    }

    [Fact]
    public async Task Constructor_UnicodeExtensionKeepsFirstDuplicateKeyword()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("new Intl.Locale('da-u-ca-gregory-ca-buddhist').toString();");

        Assert.Equal("da-u-ca-gregory", result?.ToString());
    }

    [Fact]
    public async Task CanonicalizesLanguageTagsWithTransformAndIncompleteUnicodeExtension()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const tag = 'cmn-hans-cn-u-ca-t-ca-x-t-u';
            const canonical = Intl.getCanonicalLocales(tag)[0];
            const supported = Intl.PluralRules.supportedLocalesOf([tag])[0];
            const resolved = new Intl.PluralRules([tag], { localeMatcher: 'lookup' }).resolvedOptions().locale;
            [canonical, supported, resolved].join('|');
        ");

        Assert.Equal("zh-Hans-CN-t-ca-u-ca-x-t-u|zh-Hans-CN-t-ca-u-ca-x-t-u|zh-Hans-CN", result?.ToString());
    }

    [Fact]
    public async Task IntlConstructors_TolerateUnsupportedUnicodeExtensionValuesInLocale()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const constructors = [
              Intl.Collator,
              Intl.NumberFormat,
              Intl.DateTimeFormat,
              Intl.PluralRules,
              Intl.RelativeTimeFormat,
              Intl.ListFormat,
            ].filter(Boolean);

            const requested = 'en-u-co-phonebk-nu-invalid';
            const outcomes = constructors.map(Ctor => {
              const supported = Ctor.supportedLocalesOf([requested], { localeMatcher: 'lookup' })[0];
              let resolved;
              try {
                resolved = new Ctor([requested], { localeMatcher: 'lookup' }).resolvedOptions().locale;
              } catch (error) {
                resolved = `THREW:${error?.name ?? 'Error'}`;
              }

              return `${Ctor.name}:${supported}:${resolved}`;
            });

            outcomes.join('|');
        ");

        var summary = result?.ToString();
        Assert.NotNull(summary);
        Assert.DoesNotContain("THREW:", summary!, StringComparison.Ordinal);
        Assert.DoesNotContain(":undefined:", summary!, StringComparison.Ordinal);
        Assert.Contains("Collator:en-u-co-phonebk-nu-invalid:", summary!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCanonicalLocales_CanonicalizesRepresentativeLanguageTags()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            Intl.getCanonicalLocales([
              'iw-il',
              'sgn-GR',
              'cmn-hans-cn-u-ca-t-ca-x-t-u',
              'en-a-not-assigned-x-private',
              'de-1901'
            ]).join('|');
        ");

        Assert.Equal(
            "he-IL|sfb|zh-Hans-CN-t-ca-u-ca-x-t-u|en-a-not-assigned-x-private|de-1901",
            result?.ToString());
    }

    [Fact]
    public async Task LocaleCanonicalization_RejectsInvalidDuplicateTags()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            [
              'en-u-ca-gregory-u-nu-latn',
              'de-1901-1901',
              'en-a-value-a-other',
              'en-u',
              'en-t-a0',
              'en-x'
            ].map(tag => {
              let canonical;
              try {
                Intl.getCanonicalLocales(tag);
                canonical = 'accepted';
              } catch (error) {
                canonical = error instanceof RangeError ? 'range' : error.name;
              }

              let locale;
              try {
                new Intl.Locale(tag);
                locale = 'accepted';
              } catch (error) {
                locale = error instanceof RangeError ? 'range' : error.name;
              }

              return canonical + ':' + locale;
            }).join('|');
        ");

        Assert.Equal(
            "range:range|range:range|range:range|range:range|range:range|range:range",
            result?.ToString());
    }

    [Fact(Timeout = 10000)]
    public async Task GetCanonicalLocales_LongInvalidTagRejectsWithinTimeoutBudget()
    {
        var engine = new JsEngine();
        var stopwatch = Stopwatch.StartNew();
        var result = await engine.Evaluate(@"
            const tag = 'en-' + 'a-'.repeat(5000) + 'a';
            try {
              Intl.getCanonicalLocales(tag);
              'accepted';
            } catch (error) {
              error instanceof RangeError ? 'range' : error.name;
            }
        ");
        stopwatch.Stop();

        Assert.Equal("range", result?.ToString());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Elapsed: {stopwatch.Elapsed}");
    }
}
