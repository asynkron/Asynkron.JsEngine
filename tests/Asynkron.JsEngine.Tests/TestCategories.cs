using System.Collections.Generic;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Trait helper so tests can declare intent-focused categories without repeating the trait key.
/// </summary>
[TraitDiscoverer("Asynkron.JsEngine.Tests.CategoryDiscoverer", "Asynkron.JsEngine.Tests")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class CategoryAttribute(string category) : Attribute, ITraitAttribute
{
    public string Category { get; } = category;
}

public sealed class CategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var args = traitAttribute.GetConstructorArguments();
        foreach (var arg in args)
        {
            if (arg is string category && !string.IsNullOrWhiteSpace(category))
            {
                yield return new KeyValuePair<string, string>("Category", category);
            }
        }
    }
}

public static class TestCategories
{
    // Cross-cutting buckets
    public const string RuntimeSemantics = "Runtime.Semantics";
    public const string Parser = "Parser";
    public const string ScopeAnalysis = "Scope.Analysis";
    public const string Transformations = "Transformations";
    public const string AsyncRuntime = "Async.Runtime";
    public const string IteratorRuntime = "Iterator.Runtime";
    public const string ModuleSystem = "ModuleSystem";
    public const string Debugging = "Debugging";
    public const string Diagnostics = "Diagnostics";
    public const string CompletionValues = "CompletionValues";
    public const string Performance = "Performance";
    public const string Regression = "Regression";
    public const string Integration = "Integration";
    public const string Eval = "Eval";
    public const string StrictMode = "StrictMode";
    public const string TypeSystem = "TypeSystem";
    public const string Memory = "Memory";

    // Standard library coverage
    public const string StdLibArray = "StdLib.Array";
    public const string StdLibObject = "StdLib.Object";
    public const string StdLibMapSet = "StdLib.MapSet";
    public const string StdLibWeakCollections = "StdLib.WeakCollections";
    public const string StdLibTypedArray = "StdLib.TypedArray";
    public const string StdLibDataView = "StdLib.DataView";
    public const string StdLibNumber = "StdLib.Number";
    public const string StdLibBigInt = "StdLib.BigInt";
    public const string StdLibMath = "StdLib.Math";
    public const string StdLibString = "StdLib.String";
    public const string StdLibRegExp = "StdLib.RegExp";
    public const string StdLibDate = "StdLib.Date";
    public const string StdLibTemporal = "StdLib.Temporal";
    public const string StdLibSymbol = "StdLib.Symbol";
    public const string StdLibPromise = "StdLib.Promise";
    public const string StdLibIterator = "StdLib.Iterator";
    public const string StdLibFunction = "StdLib.Function";
    public const string StdLibConsole = "StdLib.Console";
    public const string StdLibIntl = "StdLib.Intl";
    public const string StdLibCrypto = "StdLib.Crypto";

    // Preserve legacy filters that were already in use
    public const string SlotOptimization = "SlotOptimization";
    public const string AsyncForOfGlobalKnownFailure = "AsyncForOfGlobalKnownFailure";
}
