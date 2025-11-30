using System.Reflection;
using System.Text.Json;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlLocaleData
{
    private static readonly LocaleData Data = Load();

    public static IReadOnlyDictionary<string, string> TagMappings => Data.TagMappings;
    public static IReadOnlyDictionary<string, string> LanguageMappings => Data.LanguageMappings;
    public static IReadOnlyDictionary<string, ComplexLanguageMapping> ComplexLanguageMappings =>
        Data.ComplexLanguageMappings;
    public static IReadOnlyDictionary<string, string> RegionMappings => Data.RegionMappings;
    public static IReadOnlyDictionary<string, Dictionary<string, string>> ComplexRegionMappings =>
        Data.ComplexRegionMappings;
    public static IReadOnlyDictionary<string, VariantMapping> VariantMappings => Data.VariantMappings;
    public static IReadOnlyDictionary<string, Dictionary<string, string>> UnicodeMappings =>
        Data.UnicodeMappings;
    public static IReadOnlyDictionary<string, Dictionary<string, string>> TransformMappings =>
        Data.TransformMappings;

    private static LocaleData Load()
    {
        var assembly = typeof(IntlLocaleData).Assembly;
        var resourceName = "Asynkron.JsEngine.StdLib.Intl.IntlLocaleData.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                            ?? throw new InvalidOperationException(
                                "Could not load embedded Intl locale data.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var data = JsonSerializer.Deserialize<LocaleData>(stream, options)
                   ?? throw new InvalidOperationException("Intl locale data is missing.");

        // Manual additions for tags required by Test262 but not present in the trimmed dataset.
        data.TagMappings["sgn-gr"] = "gss";

        return data;
    }

    private sealed class LocaleData
    {
        public Dictionary<string, string> TagMappings { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> LanguageMappings { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, ComplexLanguageMapping> ComplexLanguageMappings { get; init; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, string> RegionMappings { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> ComplexRegionMappings { get; init; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, VariantMapping> VariantMappings { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> UnicodeMappings { get; init; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> TransformMappings { get; init; } =
            new(StringComparer.Ordinal);
    }
}
