using System.Threading.Tasks;
using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedAstAdvancedFeaturesTests
{
    [Fact]
    public async Task TaggedTemplateLiteral_runs_through_typed_ast()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function tag(strings, ...values) {
                return values[0] + values[1] + strings.length;
            }
            tag`a${1 + 1}b${2 + 3}`;
        ");

        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task ObjectLiteral_with_spread_and_accessors_behaves_correctly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const base = { seed: 1 };
            const target = {
                ...base,
                value: 4,
                get doubled() { return this.value * 2; },
                set doubled(v) { this.value = v / 2; },
                method() { return this.value + this.seed; }
            };
            const before = target.doubled;
            target.doubled = 14;
            before + target.method();
        ");

        Assert.Equal(16d, result);
    }

    [Fact]
    public async Task ObjectDestructuring_with_rest_properties_supports_nested_members()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const source = { keep: 1, skip: 2, inner: { value: 9, extra: 5 }, other: 3 };
            const { inner: { value, ...innerRest }, keep, ...rest } = source;
            value + innerRest.extra + keep + rest.other;
        ");

        Assert.Equal(18d, result);
    }
}
