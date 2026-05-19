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
}
