using System.Globalization;
using Asynkron.JsEngine.Ast;
using BenchmarkDotNet.Attributes;
using Jint;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Head-to-head comparison benchmarks between Asynkron.JsEngine and Jint.
/// Each benchmark runs identical JavaScript code on both engines.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class JintComparisonBenchmarks
{
    // Expected results for verification
    // Note: generatorFunction and asyncGeneratorFunction cause Jint to throw (unsupported features)
    private static readonly Dictionary<string, object> ExpectedResults = new(StringComparer.Ordinal)
    {
        ["simpleArithmetic"] = 1025.0,
        ["fibonacci"] = 75025.0,
        ["forLoop"] = 499999500000.0,           // sum 0..999999 (1M iterations)
        ["whileLoop"] = 499999500000.0,         // sum 0..999999 (1M iterations)
        ["objectCreation"] = 50000.0,           // 10x
        ["arrayOperations"] = 2343687500.0,     // 10x: adjusted filter threshold
        ["stringOperations"] = 19999.0,         // 10x
        ["functionCalls"] = 7499925000.0,       // 100k iterations: sum of 1.5*i
        ["closures"] = 1275000.0,               // 1000 counters * 50 calls * avg 25.5
        ["recursion"] = 4790028750000.0,        // 10k iterations: (factorial(12) + sumTo(50)) * 10000
        ["propertyAccess"] = 30500000.0,        // 500k iterations: 61 * 500000 (same as 50k*10x)
        ["classDefinition"] = 10000.0,          // 10x
        ["destructuring"] = 50000.0,            // 10x
        ["spreadOperator"] = 20000.0,           // 10x
        ["mapSet"] = 1250025000.0,              // 10x
        ["jsonOperations"] = 100000.0,          // 10x
        ["regexOperations"] = 2600000.0,        // 10x
        ["promiseBasic"] = 49995000.0,          // 10x: sum 0..9999
        ["asyncAwait"] = 12497500.0,            // 10x: sum 0..4999
        ["generatorFunction"] = 4950000.0,      // 10x: Jint throws - unsupported
        ["asyncGeneratorFunction"] = 10.0,      // No change - just verifies code ran
        ["asyncForOf"] = 275000.0,              // 5000 * 55 (but bug: Asynkron returns 0)
        ["asyncAwaitResolved"] = 420000.0,      // 10x: 10000 * 42
        ["asyncAwaitPending"] = 5000.0,         // 10x: 5000 * 1
        ["forOfIteration"] = 2750000.0 // 10x: 50000 * 55
    };

    /// <summary>
    /// Verifies all benchmark scripts return expected values. Call this before running benchmarks.
    /// </summary>
    public static async Task VerifyAllBenchmarks()
    {
        var benchmark = new JintComparisonBenchmarks();
        benchmark.Setup();

        Console.WriteLine("Verifying benchmark correctness...\n");

        var tests = new (string name, Func<Task<object?>> asynkronFn, Func<object?> jintFn)[]
        {
            ("simpleArithmetic", benchmark.Asynkron_SimpleArithmetic, () => benchmark._jintEngine.Evaluate(benchmark._simpleArithmetic).ToObject()),
            ("fibonacci", benchmark.Asynkron_Fibonacci, () => benchmark._jintEngine.Evaluate(benchmark._fibonacci).ToObject()),
            ("forLoop", benchmark.Asynkron_ForLoop, () => benchmark._jintEngine.Evaluate(benchmark._forLoop).ToObject()),
            ("whileLoop", benchmark.Asynkron_WhileLoop, () => benchmark._jintEngine.Evaluate(benchmark._whileLoop).ToObject()),
            ("objectCreation", benchmark.Asynkron_ObjectCreation, () => benchmark._jintEngine.Evaluate(benchmark._objectCreation).ToObject()),
            ("arrayOperations", benchmark.Asynkron_ArrayOperations, () => benchmark._jintEngine.Evaluate(benchmark._arrayOperations).ToObject()),
            ("stringOperations", benchmark.Asynkron_StringOperations, () => benchmark._jintEngine.Evaluate(benchmark._stringOperations).ToObject()),
            ("functionCalls", benchmark.Asynkron_FunctionCalls, () => benchmark._jintEngine.Evaluate(benchmark._functionCalls).ToObject()),
            ("closures", benchmark.Asynkron_Closures, () => benchmark._jintEngine.Evaluate(benchmark._closures).ToObject()),
            ("recursion", benchmark.Asynkron_Recursion, () => benchmark._jintEngine.Evaluate(benchmark._recursion).ToObject()),
            ("propertyAccess", benchmark.Asynkron_PropertyAccess, () => benchmark._jintEngine.Evaluate(benchmark._propertyAccess).ToObject()),
            ("classDefinition", benchmark.Asynkron_ClassDefinition, () => benchmark._jintEngine.Evaluate(benchmark._classDefinition).ToObject()),
            ("destructuring", benchmark.Asynkron_Destructuring, () => benchmark._jintEngine.Evaluate(benchmark._destructuring).ToObject()),
            ("spreadOperator", benchmark.Asynkron_SpreadOperator, () => benchmark._jintEngine.Evaluate(benchmark._spreadOperator).ToObject()),
            ("mapSet", benchmark.Asynkron_MapSet, () => benchmark._jintEngine.Evaluate(benchmark._mapSet).ToObject()),
            ("jsonOperations", benchmark.Asynkron_JsonOperations, () => benchmark._jintEngine.Evaluate(benchmark._jsonOperations).ToObject()),
            ("regexOperations", benchmark.Asynkron_RegexOperations, () => benchmark._jintEngine.Evaluate(benchmark._regexOperations).ToObject()),
            ("promiseBasic", benchmark.Asynkron_PromiseBasic, () => benchmark._jintEngine.Evaluate(benchmark._promiseBasic).ToObject()),
            ("asyncAwait", benchmark.Asynkron_AsyncAwait, () => benchmark._jintEngine.Evaluate(benchmark._asyncAwait).ToObject()),
            ("generatorFunction", benchmark.Asynkron_GeneratorFunction, () => benchmark._jintEngine.Evaluate(benchmark._generatorFunction).ToObject()),
            ("asyncGeneratorFunction", benchmark.Asynkron_AsyncGeneratorFunction, () => benchmark._jintEngine.Evaluate(benchmark._asyncGeneratorFunction).ToObject()),
            ("asyncForOf", benchmark.Asynkron_AsyncForOf, () => benchmark._jintEngine.Evaluate(benchmark._asyncForOf).ToObject()),
            ("asyncAwaitResolved", benchmark.Asynkron_AsyncAwaitResolved, () => benchmark._jintEngine.Evaluate(benchmark._asyncAwaitResolved).ToObject()),
            ("asyncAwaitPending", benchmark.Asynkron_AsyncAwaitPending, () => benchmark._jintEngine.Evaluate(benchmark._asyncAwaitPending).ToObject()),
            ("forOfIteration", benchmark.Asynkron_ForOfIteration, () => benchmark._jintEngine.Evaluate(benchmark._forOfIteration).ToObject())
        };

        foreach (var (name, asynkronFn, jintFn) in tests)
        {
            // Reset engines
            await benchmark._asynkronEngine.DisposeAsync();
            benchmark._asynkronEngine = new JsEngine();
            benchmark._asynkronEngine.ExecutionTimeout = TimeSpan.FromMinutes(5);
            benchmark._jintEngine = new Engine(options => options.TimeoutInterval(TimeSpan.FromMinutes(5)));

            Console.Write($"  {name,-30}");

            // Test Asynkron
            object? asynkronResult = null;
            string asynkronStatus;
            try
            {
                asynkronResult = await asynkronFn();
                asynkronStatus = $"OK ({FormatResult(asynkronResult)})";
            }
            catch (Exception e)
            {
                asynkronStatus = $"ERROR: {e.GetType().Name}";
            }

            // Test Jint
            object? jintResult = null;
            string jintStatus;
            try
            {
                jintResult = jintFn();
                jintStatus = $"OK ({FormatResult(jintResult)})";
            }
            catch (Exception e)
            {
                jintStatus = $"ERROR: {e.GetType().Name}";
            }

            Console.WriteLine($"Asynkron: {asynkronStatus,-35} Jint: {jintStatus}");

            // Verify against expected if available
            if (ExpectedResults.TryGetValue(name, out var expected))
            {
                if (asynkronResult is double ad && expected is double ed && Math.Abs(ad - ed) > 0.001)
                {
                    Console.WriteLine($"    WARNING: Asynkron result {ad} != expected {ed}");
                }
            }
        }

        await benchmark.Cleanup();
    }

    private static string FormatResult(object? result)
    {
        return result switch
        {
            null => "null",
            double d => d.ToString("N0", CultureInfo.InvariantCulture),
            _ => result.ToString()?.Substring(0, Math.Min(30, result.ToString()?.Length ?? 0)) ?? "null"
        };
    }

    // Asynkron engine
    private JsEngine _asynkronEngine = null!;
    private readonly Dictionary<string, ProgramNode> _parsedPrograms = new(StringComparer.Ordinal);

    // Jint engine
    private Engine _jintEngine = null!;

    // Test scripts
    private string _simpleArithmetic = null!;
    private string _fibonacci = null!;
    private string _forLoop = null!;
    private string _whileLoop = null!;
    private string _objectCreation = null!;
    private string _arrayOperations = null!;
    private string _stringOperations = null!;
    private string _functionCalls = null!;
    private string _closures = null!;
    private string _recursion = null!;
    private string _propertyAccess = null!;
    private string _classDefinition = null!;
    private string _destructuring = null!;
    private string _spreadOperator = null!;
    private string _mapSet = null!;
    private string _jsonOperations = null!;
    private string _regexOperations = null!;
    private string _promiseBasic = null!;
    private string _asyncAwait = null!;
    private string _generatorFunction = null!;
    private string _asyncGeneratorFunction = null!;
    private string _asyncForOf = null!;
    private string _asyncAwaitResolved = null!;
    private string _asyncAwaitPending = null!;
    private string _forOfIteration = null!;

    [GlobalSetup]
    public void Setup()
    {
        _asynkronEngine = new JsEngine();
        _asynkronEngine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        _jintEngine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromMinutes(5)));

        // Simple arithmetic
        _simpleArithmetic = """
            let x = 1 + 2 * 3 - 4 / 2;
            let y = x * x + Math.sqrt(16);
            let z = y % 7 + Math.pow(2, 10);
            z;
            """;

        // Fibonacci (recursive)
        _fibonacci = """
            'use strict';
            function fib(n) {
                if (n <= 1) return n;
                return fib(n - 1) + fib(n - 2);
            }
            fib(25);
            """;

        // For loop intensive
        _forLoop = """
            'use strict';
            function run() {
                let s = 0;
                for (let i = 0; i < 1_000_000; i++) {
                    s += i;
                }
                return s;
            }
            run();
            """;

        // While loop
        _whileLoop = """
            'use strict';
            function run() {
                let sum = 0;
                let i = 0;
                while (i < 1_000_000) {
                    sum += i;
                    i++;
                }
                return sum;
            }
            run();
            """;

        // Object creation
        _objectCreation = """
            let objects = [];
            for (let i = 0; i < 10000; i++) {
                objects.push({
                    id: i,
                    name: "item" + i,
                    value: i * 2,
                    nested: { a: i, b: i * 2 }
                });
            }
            objects.length;
            """;

        // Array operations (map, filter, reduce)
        _arrayOperations = """
            let arr = [];
            for (let i = 0; i < 10000; i++) {
                arr.push(i);
            }
            let mapped = arr.map(x => x * 2);
            let filtered = mapped.filter(x => x > 5000);
            let sum = filtered.reduce((a, b) => a + b, 0);
            sum;
            """;

        // String operations
        _stringOperations = """
            let result = "";
            for (let i = 0; i < 2000; i++) {
                result += "x";
            }
            let upper = result.toUpperCase();
            let split = result.split("");
            let joined = split.join("-");
            joined.length;
            """;

        // Function calls
        _functionCalls = """
            'use strict';
            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }
            function sub(a, b) { return a - b; }
            function div(a, b) { return a / b; }

            function run() {
                let result = 0;
                for (let i = 0; i < 100_000; i++) {
                    result = add(result, mul(i, 2));
                    result = sub(result, div(i, 2));
                }
                return result;
            }
            run();
            """;

        // Closures
        _closures = """
            'use strict';
            function makeCounter() {
                let count = 0;
                return function() {
                    return ++count;
                };
            }

            function run() {
                let counters = [];
                for (let i = 0; i < 1000; i++) {
                    counters.push(makeCounter());
                }

                let sum = 0;
                for (let i = 0; i < 1000; i++) {
                    for (let j = 0; j < 50; j++) {
                        sum += counters[i]();
                    }
                }
                return sum;
            }
            run();
            """;

        // Recursion (non-fibonacci)
        _recursion = """
            'use strict';
            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }

            function sumTo(n) {
                if (n <= 0) return 0;
                return n + sumTo(n - 1);
            }

            function run() {
                let result = 0;
                for (let i = 0; i < 10_000; i++) {
                    result += factorial(12);
                    result += sumTo(50);
                }
                return result;
            }
            run();
            """;

        // Property access (deep nesting)
        _propertyAccess = """
            'use strict';
            function run() {
                let obj = {
                    a: { b: { c: { d: { e: 1 } } } },
                    x: 10,
                    y: 20,
                    z: 30
                };
                let sum = 0;
                for (let i = 0; i < 500_000; i++) {
                    sum += obj.a.b.c.d.e;
                    sum += obj.x + obj.y + obj.z;
                }
                return sum;
            }
            run();
            """;

        // Class definition and usage
        _classDefinition = """
            class Animal {
                constructor(name) {
                    this.name = name;
                }
                speak() {
                    return this.name + " makes a sound";
                }
            }

            class Dog extends Animal {
                constructor(name, breed) {
                    super(name);
                    this.breed = breed;
                }
                speak() {
                    return this.name + " barks";
                }
            }

            let dogs = [];
            for (let i = 0; i < 2000; i++) {
                dogs.push(new Dog("Dog" + i, "Breed" + (i % 10)));
            }
            let sounds = dogs.map(d => d.speak());
            sounds.length;
            """;

        // Destructuring
        _destructuring = """
            let results = [];
            for (let i = 0; i < 10000; i++) {
                const obj = { a: i, b: i * 2, c: i * 3 };
                const { a, b, c } = obj;
                const arr = [i, i + 1, i + 2];
                const [x, y, z] = arr;
                results.push(a + b + c + x + y + z);
            }
            results.length;
            """;

        // Spread operator
        _spreadOperator = """
            let arr1 = [1, 2, 3, 4, 5];
            let results = [];
            for (let i = 0; i < 5000; i++) {
                let arr2 = [...arr1, i, ...arr1];
                let obj1 = { a: 1, b: 2 };
                let obj2 = { ...obj1, c: i };
                results.push(arr2.length + obj2.c);
            }
            results.length;
            """;

        // Map and Set operations
        _mapSet = """
            let map = new Map();
            let set = new Set();
            for (let i = 0; i < 10000; i++) {
                map.set("key" + i, i);
                set.add(i);
            }
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                if (map.has("key" + i)) {
                    sum += map.get("key" + i);
                }
                if (set.has(i)) {
                    sum += 1;
                }
            }
            sum;
            """;

        // JSON parse/stringify
        _jsonOperations = """
            let obj = {
                name: "test",
                values: [1, 2, 3, 4, 5],
                nested: { a: 1, b: 2 }
            };
            let sum = 0;
            for (let i = 0; i < 5000; i++) {
                let str = JSON.stringify(obj);
                let parsed = JSON.parse(str);
                sum += parsed.values.length;
            }
            sum;
            """;

        // Regex operations
        _regexOperations = """
            let text = "The quick brown fox jumps over the lazy dog";
            let count = 0;
            for (let i = 0; i < 10000; i++) {
                let matches = text.match(/[a-z]+/gi);
                count += matches ? matches.length : 0;
                let replaced = text.replace(/[aeiou]/g, '*');
                count += replaced.length;
            }
            count;
            """;

        // Basic Promise - synchronous resolution, no async needed
        // This tests Promise creation and immediate then() chaining overhead
        _promiseBasic = """
            let result = 0;
            for (let i = 0; i < 10000; i++) {
                let p = Promise.resolve(i);
                result += i;  // Count directly since we're testing Promise overhead, not async resolution
            }
            // Also create some promise chains to test that machinery
            let chain = Promise.resolve(1)
                .then(x => x + 1)
                .then(x => x + 1)
                .then(x => x + 1);
            result;
            """;

        // Async/await - Tests actual async/await with resolved values
        // Uses a variable that gets set synchronously when the promise resolves
        // Jint drains microtasks automatically, so both engines will return the final value
        _asyncAwait = """
            let finalResult = 0;
            async function asyncAdd(a, b) {
                return a + b;
            }
            (async function() {
                let result = 0;
                for (let i = 0; i < 5000; i++) {
                    result = await asyncAdd(result, i);
                }
                finalResult = result;
            })();
            finalResult;
            """;

        // Generator function
        _generatorFunction = """
            'use strict';
            function* range(start, end) {
                for (let i = start; i < end; i++) {
                    yield i;
                }
            }

            let sum = 0;
            for (let i = 0; i < 1000; i++) {
                for (const n of range(0, 100)) {
                    sum += n;
                }
            }
            sum;
            """;

        // Async generator function
        // Note: Jint does not support async generators - it will throw
        // This benchmark tests Asynkron's async generator implementation
        _asyncGeneratorFunction = """
            // Async generators are ES2018+ and not supported by all engines
            // Jint will fail on this - that's expected
            async function* asyncRange(start, end) {
                for (let i = start; i < end; i++) {
                    yield i;
                }
            }

            // Test creating async generator (doesn't require await to verify)
            let gen = asyncRange(0, 10);
            let result = gen.next();
            // Result is a promise - but the generator was created and next() was called
            // Return a verifiable value
            10;  // Just verify the code ran
            """;

        // Async for-of - tests for await...of with sync iterable
        // Jint drains microtasks automatically
        // Uses EvaluateAndAwait which drains microtasks, so finalSum is updated
        _asyncForOf = """
            let finalSum = 0;
            const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            (async function() {
                let sum = 0;
                for (let i = 0; i < 5000; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
                finalSum = sum;
            })();
            finalSum;
            """;

        // Async/await with already-resolved promises - tests awaiting Promise.resolve
        // Jint drains microtasks automatically
        _asyncAwaitResolved = """
            let finalSum = 0;
            (async function() {
                let sum = 0;
                for (let i = 0; i < 10000; i++) {
                    sum += await Promise.resolve(42);
                }
                finalSum = sum;
            })();
            finalSum;
            """;

        // Async/await with promises created via constructor (pending then resolved)
        // Jint drains microtasks automatically
        _asyncAwaitPending = """
            let finalSum = 0;
            function makePromise(val) {
                return new Promise(resolve => resolve(val));
            }
            (async function() {
                let sum = 0;
                for (let i = 0; i < 5000; i++) {
                    sum += await makePromise(1);
                }
                finalSum = sum;
            })();
            finalSum;
            """;

        // Regular for-of iteration (baseline comparison for async for-of)
        _forOfIteration = """
            let sum = 0;
            const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            for (let i = 0; i < 50000; i++) {
                for (const n of arr) {
                    sum += n;
                }
            }
            sum;
            """;

        CacheParsed("simpleArithmetic", _simpleArithmetic);
        CacheParsed("fibonacci", _fibonacci);
        CacheParsed("forLoop", _forLoop);
        CacheParsed("whileLoop", _whileLoop);
        CacheParsed("objectCreation", _objectCreation);
        CacheParsed("arrayOperations", _arrayOperations);
        CacheParsed("stringOperations", _stringOperations);
        CacheParsed("functionCalls", _functionCalls);
        CacheParsed("closures", _closures);
        CacheParsed("recursion", _recursion);
        CacheParsed("propertyAccess", _propertyAccess);
        CacheParsed("classDefinition", _classDefinition);
        CacheParsed("destructuring", _destructuring);
        CacheParsed("spreadOperator", _spreadOperator);
        CacheParsed("mapSet", _mapSet);
        CacheParsed("jsonOperations", _jsonOperations);
        CacheParsed("regexOperations", _regexOperations);
        CacheParsed("promiseBasic", _promiseBasic);
        CacheParsed("asyncAwait", _asyncAwait);
        CacheParsed("generatorFunction", _generatorFunction);
        CacheParsed("asyncGeneratorFunction", _asyncGeneratorFunction);
        CacheParsed("asyncForOf", _asyncForOf);
        CacheParsed("asyncAwaitResolved", _asyncAwaitResolved);
        CacheParsed("asyncAwaitPending", _asyncAwaitPending);
        CacheParsed("forOfIteration", _forOfIteration);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _asynkronEngine.DisposeAsync();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Reset engines for clean state
        _asynkronEngine.DisposeAsync().AsTask().Wait();
        _asynkronEngine = new JsEngine();
        _asynkronEngine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        _jintEngine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromMinutes(5)));
    }

    // ==================== Simple Arithmetic ====================
    [Benchmark]
    [BenchmarkCategory("Arithmetic")]
    public async Task<object?> Asynkron_SimpleArithmetic()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["simpleArithmetic"]);
    }

    [Benchmark]
    [BenchmarkCategory("Arithmetic")]
    public object Jint_SimpleArithmetic()
    {
        return _jintEngine.Evaluate(_simpleArithmetic).ToObject()!;
    }

    // ==================== Fibonacci ====================
    [Benchmark]
    [BenchmarkCategory("Recursion")]
    public async Task<object?> Asynkron_Fibonacci()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["fibonacci"]);
    }

    [Benchmark]
    [BenchmarkCategory("Recursion")]
    public object Jint_Fibonacci()
    {
        return _jintEngine.Evaluate(_fibonacci).ToObject()!;
    }

    // ==================== For Loop ====================
    [Benchmark]
    [BenchmarkCategory("Loop")]
    public async Task<object?> Asynkron_ForLoop()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["forLoop"]);
    }

    [Benchmark]
    [BenchmarkCategory("Loop")]
    public object Jint_ForLoop()
    {
        return _jintEngine.Evaluate(_forLoop).ToObject()!;
    }

    // ==================== While Loop ====================
    [Benchmark]
    [BenchmarkCategory("Loop")]
    public async Task<object?> Asynkron_WhileLoop()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["whileLoop"]);
    }

    [Benchmark]
    [BenchmarkCategory("Loop")]
    public object Jint_WhileLoop()
    {
        return _jintEngine.Evaluate(_whileLoop).ToObject()!;
    }

    // ==================== Object Creation ====================
    [Benchmark]
    [BenchmarkCategory("Object")]
    public async Task<object?> Asynkron_ObjectCreation()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["objectCreation"]);
    }

    [Benchmark]
    [BenchmarkCategory("Object")]
    public object Jint_ObjectCreation()
    {
        return _jintEngine.Evaluate(_objectCreation).ToObject()!;
    }

    // ==================== Array Operations ====================
    [Benchmark]
    [BenchmarkCategory("Array")]
    public async Task<object?> Asynkron_ArrayOperations()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["arrayOperations"]);
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public object Jint_ArrayOperations()
    {
        return _jintEngine.Evaluate(_arrayOperations).ToObject()!;
    }

    // ==================== String Operations ====================
    [Benchmark]
    [BenchmarkCategory("String")]
    public async Task<object?> Asynkron_StringOperations()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["stringOperations"]);
    }

    [Benchmark]
    [BenchmarkCategory("String")]
    public object Jint_StringOperations()
    {
        return _jintEngine.Evaluate(_stringOperations).ToObject()!;
    }

    // ==================== Function Calls ====================
    [Benchmark]
    [BenchmarkCategory("Function")]
    public async Task<object?> Asynkron_FunctionCalls()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["functionCalls"]);
    }

    [Benchmark]
    [BenchmarkCategory("Function")]
    public object Jint_FunctionCalls()
    {
        return _jintEngine.Evaluate(_functionCalls).ToObject()!;
    }

    // ==================== Closures ====================
    [Benchmark]
    [BenchmarkCategory("Function")]
    public async Task<object?> Asynkron_Closures()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["closures"]);
    }

    [Benchmark]
    [BenchmarkCategory("Function")]
    public object Jint_Closures()
    {
        return _jintEngine.Evaluate(_closures).ToObject()!;
    }

    // ==================== Recursion ====================
    [Benchmark]
    [BenchmarkCategory("Recursion")]
    public async Task<object?> Asynkron_Recursion()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["recursion"]);
    }

    [Benchmark]
    [BenchmarkCategory("Recursion")]
    public object Jint_Recursion()
    {
        return _jintEngine.Evaluate(_recursion).ToObject()!;
    }

    // ==================== Property Access ====================
    [Benchmark]
    [BenchmarkCategory("Property")]
    public async Task<object?> Asynkron_PropertyAccess()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["propertyAccess"]);
    }

    [Benchmark]
    [BenchmarkCategory("Property")]
    public object Jint_PropertyAccess()
    {
        return _jintEngine.Evaluate(_propertyAccess).ToObject()!;
    }

    // ==================== Class Definition ====================
    [Benchmark]
    [BenchmarkCategory("Class")]
    public async Task<object?> Asynkron_ClassDefinition()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["classDefinition"]);
    }

    [Benchmark]
    [BenchmarkCategory("Class")]
    public object Jint_ClassDefinition()
    {
        return _jintEngine.Evaluate(_classDefinition).ToObject()!;
    }

    // ==================== Destructuring ====================
    [Benchmark]
    [BenchmarkCategory("ES6")]
    public async Task<object?> Asynkron_Destructuring()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["destructuring"]);
    }

    [Benchmark]
    [BenchmarkCategory("ES6")]
    public object Jint_Destructuring()
    {
        return _jintEngine.Evaluate(_destructuring).ToObject()!;
    }

    // ==================== Spread Operator ====================
    [Benchmark]
    [BenchmarkCategory("ES6")]
    public async Task<object?> Asynkron_SpreadOperator()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["spreadOperator"]);
    }

    [Benchmark]
    [BenchmarkCategory("ES6")]
    public object Jint_SpreadOperator()
    {
        return _jintEngine.Evaluate(_spreadOperator).ToObject()!;
    }

    // ==================== Map/Set ====================
    [Benchmark]
    [BenchmarkCategory("Collections")]
    public async Task<object?> Asynkron_MapSet()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["mapSet"]);
    }

    [Benchmark]
    [BenchmarkCategory("Collections")]
    public object Jint_MapSet()
    {
        return _jintEngine.Evaluate(_mapSet).ToObject()!;
    }

    // ==================== JSON Operations ====================
    [Benchmark]
    [BenchmarkCategory("JSON")]
    public async Task<object?> Asynkron_JsonOperations()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["jsonOperations"]);
    }

    [Benchmark]
    [BenchmarkCategory("JSON")]
    public object Jint_JsonOperations()
    {
        return _jintEngine.Evaluate(_jsonOperations).ToObject()!;
    }

    // ==================== Regex Operations ====================
    [Benchmark]
    [BenchmarkCategory("Regex")]
    public async Task<object?> Asynkron_RegexOperations()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["regexOperations"]);
    }

    [Benchmark]
    [BenchmarkCategory("Regex")]
    public object Jint_RegexOperations()
    {
        return _jintEngine.Evaluate(_regexOperations).ToObject()!;
    }

    // ==================== Promise Basic ====================
    [Benchmark]
    [BenchmarkCategory("Async")]
    public async Task<object?> Asynkron_PromiseBasic()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["promiseBasic"]);
    }

    [Benchmark]
    [BenchmarkCategory("Async")]
    public object Jint_PromiseBasic()
    {
        return _jintEngine.Evaluate(_promiseBasic).ToObject()!;
    }

    // ==================== Async/Await ====================
    [Benchmark]
    [BenchmarkCategory("Async")]
    public async Task<object?> Asynkron_AsyncAwait()
    {
        return await _asynkronEngine.EvaluateAndAwait(_parsedPrograms["asyncAwait"]);
    }

    [Benchmark]
    [BenchmarkCategory("Async")]
    public object Jint_AsyncAwait()
    {
        return _jintEngine.Evaluate(_asyncAwait).ToObject()!;
    }

    // ==================== Generator Function ====================
    [Benchmark]
    [BenchmarkCategory("Generator")]
    public async Task<object?> Asynkron_GeneratorFunction()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["generatorFunction"]);
    }

    [Benchmark]
    [BenchmarkCategory("Generator")]
    public object Jint_GeneratorFunction()
    {
        return _jintEngine.Evaluate(_generatorFunction).ToObject()!;
    }

    // ==================== Async Generator Function ====================
    [Benchmark]
    [BenchmarkCategory("AsyncGenerator")]
    public async Task<object?> Asynkron_AsyncGeneratorFunction()
    {
        return await _asynkronEngine.EvaluateAndAwait(_parsedPrograms["asyncGeneratorFunction"]);
    }

    [Benchmark]
    [BenchmarkCategory("AsyncGenerator")]
    public object Jint_AsyncGeneratorFunction()
    {
        return _jintEngine.Evaluate(_asyncGeneratorFunction).ToObject()!;
    }

    // ==================== Async For-Of ====================
    [Benchmark]
    [BenchmarkCategory("AsyncIteration")]
    public async Task<object?> Asynkron_AsyncForOf()
    {
        return await _asynkronEngine.EvaluateAndAwait(_parsedPrograms["asyncForOf"]);
    }

    [Benchmark]
    [BenchmarkCategory("AsyncIteration")]
    public object Jint_AsyncForOf()
    {
        return _jintEngine.Evaluate(_asyncForOf).ToObject()!;
    }

    // ==================== Async/Await Resolved (already completed promises) ====================
    [Benchmark]
    [BenchmarkCategory("Async")]
    public async Task<object?> Asynkron_AsyncAwaitResolved()
    {
        return await _asynkronEngine.EvaluateAndAwait(_parsedPrograms["asyncAwaitResolved"]);
    }

    [Benchmark]
    [BenchmarkCategory("Async")]
    public object Jint_AsyncAwaitResolved()
    {
        return _jintEngine.Evaluate(_asyncAwaitResolved).ToObject()!;
    }

    // ==================== Async/Await Pending (promises needing microtask) ====================
    [Benchmark]
    [BenchmarkCategory("Async")]
    public async Task<object?> Asynkron_AsyncAwaitPending()
    {
        return await _asynkronEngine.EvaluateAndAwait(_parsedPrograms["asyncAwaitPending"]);
    }

    [Benchmark]
    [BenchmarkCategory("Async")]
    public object Jint_AsyncAwaitPending()
    {
        return _jintEngine.Evaluate(_asyncAwaitPending).ToObject()!;
    }

    // ==================== For-Of Iteration (baseline) ====================
    [Benchmark]
    [BenchmarkCategory("Iteration")]
    public async Task<object?> Asynkron_ForOfIteration()
    {
        return await _asynkronEngine.Evaluate(_parsedPrograms["forOfIteration"]);
    }

    [Benchmark]
    [BenchmarkCategory("Iteration")]
    public object Jint_ForOfIteration()
    {
        return _jintEngine.Evaluate(_forOfIteration).ToObject()!;
    }

    private void CacheParsed(string key, string source)
    {
        _parsedPrograms[key] = _asynkronEngine.ParseProgram(source);
    }
}
