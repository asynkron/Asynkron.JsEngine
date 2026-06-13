using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibTemporal)]
public sealed class IntlDateTimeFormatTemporalTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task FormatRange_FormatsTemporalPlainDateOperands()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'UTC',
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
            formatter.formatRange(
                new Temporal.PlainDate(2024, 5, 1),
                new Temporal.PlainDate(2024, 5, 2));
            """);

        Assert.Equal("05/01/2024 \u2013 05/02/2024", result);
    }

    [Fact]
    public async Task FormatRange_CollapsesMatchingTemporalPlainDateOperands()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'UTC',
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
            const date = new Temporal.PlainDate(2024, 5, 1);
            formatter.formatRange(date, date) === formatter.format(date);
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task FormatToPartsAndRangeToParts_CreateOrdinaryPartObjects()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function compare(actual, expected, path) {
                if (actual === expected) return "ok";
                if (actual === null || actual === undefined || expected === null || expected === undefined) {
                    return path + ": optional mismatch";
                }

                const actualType = typeof actual;
                const expectedType = typeof expected;
                if (actualType !== expectedType) {
                    return path + ": type " + actualType + " != " + expectedType;
                }

                if (actualType !== "object" && actualType !== "function") {
                    return path + ": primitive " + String(actual) + " != " + String(expected);
                }

                const actualTag = Symbol.toStringTag in actual ? actual[Symbol.toStringTag] : undefined;
                const expectedTag = Symbol.toStringTag in expected ? expected[Symbol.toStringTag] : undefined;
                if (actualTag !== expectedTag) {
                    return path + ": tag " + String(actualTag) + " != " + String(expectedTag);
                }

                if (Array.isArray(actual) || Array.isArray(expected)) {
                    if (!Array.isArray(actual) || !Array.isArray(expected)) {
                        return path + ": array-like mismatch";
                    }

                    if (actual.length !== expected.length) {
                        return path + ": length " + actual.length + " != " + expected.length;
                    }

                    for (let i = 0; i < actual.length; i++) {
                        const item = compare(actual[i], expected[i], path + "[" + i + "]");
                        if (item !== "ok") return item;
                    }

                    return "ok";
                }

                const actualKeys = [];
                for (const key in actual) actualKeys.push(key);
                const expectedKeys = [];
                for (const key in expected) expectedKeys.push(key);
                if (actualKeys.length !== expectedKeys.length) {
                    return path + ": keys " + actualKeys.join(",") + " != " + expectedKeys.join(",");
                }

                actualKeys.sort();
                expectedKeys.sort();
                for (let i = 0; i < actualKeys.length; i++) {
                    if (actualKeys[i] !== expectedKeys[i]) {
                        return path + ": key " + actualKeys[i] + " != " + expectedKeys[i];
                    }

                    const item = compare(actual[actualKeys[i]], expected[expectedKeys[i]],
                        path + "." + actualKeys[i]);
                    if (item !== "ok") return item;
                }

                return "ok";
            }

            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'Pacific/Apia',
                year: 'numeric',
                month: 'numeric',
                day: 'numeric'
            });
            const start = new Temporal.PlainDate(2021, 8, 4);
            const end = new Temporal.PlainDate(2021, 8, 5);
            const parts = formatter.formatToParts(start);
            const rangeParts = formatter.formatRangeToParts(start, end);
            const partsExpected = [
                { type: "month", value: "8" },
                { type: "literal", value: "/" },
                { type: "day", value: "4" },
                { type: "literal", value: "/" },
                { type: "year", value: "2021" },
            ];
            const rangeExpected = [
                { type: "month", value: "8", source: "startRange" },
                { type: "literal", value: "/", source: "startRange" },
                { type: "day", value: "4", source: "startRange" },
                { type: "literal", value: "/", source: "startRange" },
                { type: "year", value: "2021", source: "startRange" },
                { type: "literal", value: " \u2013 ", source: "shared" },
                { type: "month", value: "8", source: "endRange" },
                { type: "literal", value: "/", source: "endRange" },
                { type: "day", value: "5", source: "endRange" },
                { type: "literal", value: "/", source: "endRange" },
                { type: "year", value: "2021", source: "endRange" },
            ];

            const protoCheck = Object.getPrototypeOf(parts[0]) === Object.prototype &&
                Object.getPrototypeOf(rangeParts[0]) === Object.prototype;
            const partsCheck = compare(parts, partsExpected, "parts");
            const rangeCheck = compare(rangeParts, rangeExpected, "rangeParts");
            protoCheck && partsCheck === "ok" && rangeCheck === "ok"
                ? "ok"
                : JSON.stringify({ protoCheck, partsCheck, rangeCheck });
            """);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task FormatRange_RejectsDistinctTemporalOperandKinds()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            new Intl.DateTimeFormat('en-US').formatRange(
                new Temporal.PlainDate(2024, 5, 1),
                new Temporal.PlainTime(12, 30));
            """));
    }

    [Fact]
    public async Task FormatRange_RejectsTemporalOperandsWithNonOverlappingOptions()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            new Intl.DateTimeFormat('en-US', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            }).formatRange(
                new Temporal.PlainTime(12, 30),
                new Temporal.PlainTime(13, 30));
            """));
    }

    [Fact]
    public async Task PlainDateTimeToLocaleString_PadsNumericH23HourWithResolvedTimeZone()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            new Temporal.PlainDateTime(2021, 8, 4, 0, 30, 45, 123, 456, 789)
                .toLocaleString("en", {
                    timeZone: "Pacific/Apia",
                    year: "numeric",
                    month: "numeric",
                    day: "numeric",
                    hour: "numeric",
                    minute: "numeric",
                    second: "numeric",
                    hourCycle: "h23"
                });
            """);

        Assert.Equal("8/4/2021, 00:30:45", result);
    }

    [Fact]
    public async Task PlainYearMonthToLocaleString_UsesCalendarAwareDateStyleMonthNames()
    {
        await using var engine = CreateEngine();

        var gregoryLong = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 2024, monthCode: "M03", calendar: "gregory" })
                .toLocaleString("en-u-ca-gregory", { dateStyle: "long" });
            """);
        var gregoryShort = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 2024, monthCode: "M03", calendar: "gregory" })
                .toLocaleString("en-u-ca-gregory", { dateStyle: "short" });
            """);
        var islamicLong = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 1445, monthCode: "M09", calendar: "islamic-tbla" })
                .toLocaleString("en-u-ca-islamic-tbla", { dateStyle: "long" });
            """);
        var islamicShort = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 1445, monthCode: "M09", calendar: "islamic-tbla" })
                .toLocaleString("en-u-ca-islamic-tbla", { dateStyle: "short" });
            """);

        Assert.Contains("March", Assert.IsType<string>(gregoryLong));
        Assert.DoesNotContain("March", Assert.IsType<string>(gregoryShort));
        Assert.Contains("Ramadan", Assert.IsType<string>(islamicLong));
        Assert.DoesNotContain("Ramadan", Assert.IsType<string>(islamicShort));
    }

    [Fact]
    public async Task PlainYearMonthToLocaleString_DateStyleDoesNotFormatReferenceDay()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            new Temporal.PlainYearMonth(2024, 5, "gregory", 31)
                .toLocaleString("en", { dateStyle: "full" });
            """);

        Assert.DoesNotContain("31", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task PlainYearMonthToLocaleString_UndefinedDateStyleMatchesOmittedDateStyle()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const yearMonth = Temporal.PlainYearMonth.from({
                year: 2024,
                monthCode: "M03",
                calendar: "gregory"
            });
            yearMonth.toLocaleString("en-u-ca-gregory", { dateStyle: undefined }) ===
                yearMonth.toLocaleString("en-u-ca-gregory", {});
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task PlainTimeToLocaleString_IgnoresResolvedTimeZoneOffset()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const options = {
                timeZone: "Pacific/Apia",
                hour: "numeric",
                minute: "numeric",
                second: "numeric",
                hourCycle: "h23"
            };

            [
                new Temporal.PlainTime(0, 30, 45, 123, 456, 789).toLocaleString("en", options),
                new Temporal.PlainTime(23, 30, 45, 123, 456, 789).toLocaleString("en", options)
            ].join("|");
            """);

        Assert.Equal("00:30:45|23:30:45", result);
    }
}
