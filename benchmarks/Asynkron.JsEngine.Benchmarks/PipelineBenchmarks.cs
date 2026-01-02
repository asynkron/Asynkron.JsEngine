using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;
using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Benchmarks that measure the full pipeline and help identify which phase
/// (Lexing, Parsing, or Evaluation) is the performance bottleneck.
/// Each benchmark tests the same code but isolates different phases.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class PipelineBenchmarks
{
    private JsEngine _engine = null!;

    // Test programs of varying complexity
    private string _smallProgram = null!;
    private string _mediumProgram = null!;
    private string _largeProgram = null!;

    // Pre-processed artifacts for isolated benchmarks
    private IReadOnlyList<Token> _smallTokens = null!;
    private IReadOnlyList<Token> _mediumTokens = null!;
    private IReadOnlyList<Token> _largeTokens = null!;

    private ProgramNode _smallAst = null!;
    private ProgramNode _mediumAst = null!;
    private ProgramNode _largeAst = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        // Small: Simple expressions and basic operations
        _smallProgram = """
            let x = 10;
            let y = 20;
            let z = x + y * 2;
            z;
            """;

        // Medium: Functions, loops, and object manipulation
        _mediumProgram = """
            function processItems(items) {
                let result = [];
                for (let i = 0; i < items.length; i++) {
                    let item = items[i];
                    if (item.active) {
                        result.push({
                            id: item.id,
                            value: item.value * 2,
                            name: item.name.toUpperCase()
                        });
                    }
                }
                return result;
            }

            let data = [];
            for (let i = 0; i < 100; i++) {
                data.push({
                    id: i,
                    value: i * 10,
                    name: "item" + i,
                    active: i % 2 === 0
                });
            }

            let processed = processItems(data);
            processed.length;
            """;

        // Large: Complex class hierarchy, closures, and heavy computation
        _largeProgram = """
            class Collection {
                #items = [];

                add(item) {
                    this.#items.push(item);
                    return this;
                }

                remove(predicate) {
                    this.#items = this.#items.filter(x => !predicate(x));
                    return this;
                }

                map(fn) {
                    const result = new Collection();
                    this.#items.forEach(x => result.add(fn(x)));
                    return result;
                }

                filter(predicate) {
                    const result = new Collection();
                    this.#items.forEach(x => {
                        if (predicate(x)) result.add(x);
                    });
                    return result;
                }

                reduce(fn, initial) {
                    let acc = initial;
                    this.#items.forEach(x => { acc = fn(acc, x); });
                    return acc;
                }

                get length() {
                    return this.#items.length;
                }

                toArray() {
                    return [...this.#items];
                }
            }

            function createCounter(start = 0) {
                let count = start;
                return {
                    increment() { return ++count; },
                    decrement() { return --count; },
                    get value() { return count; },
                    reset() { count = start; }
                };
            }

            function fibonacci(n) {
                if (n <= 1) return n;
                return fibonacci(n - 1) + fibonacci(n - 2);
            }

            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }

            function quicksort(arr) {
                if (arr.length <= 1) return arr;
                const pivot = arr[Math.floor(arr.length / 2)];
                const left = arr.filter(x => x < pivot);
                const middle = arr.filter(x => x === pivot);
                const right = arr.filter(x => x > pivot);
                return [...quicksort(left), ...middle, ...quicksort(right)];
            }

            // Execute various operations
            const col = new Collection();
            for (let i = 0; i < 500; i++) {
                col.add({ id: i, value: i * 2, category: i % 5 });
            }

            const filtered = col
                .filter(x => x.category === 0)
                .map(x => ({ ...x, doubled: x.value * 2 }));

            const sum = filtered.reduce((acc, x) => acc + x.value, 0);

            const counter = createCounter(100);
            for (let i = 0; i < 50; i++) {
                counter.increment();
            }

            const fib15 = fibonacci(15);
            const fact10 = factorial(10);

            const unsorted = [];
            for (let i = 0; i < 100; i++) {
                unsorted.push(Math.floor(Math.random() * 1000));
            }
            const sorted = quicksort(unsorted);

            ({
                collectionLength: filtered.length,
                sum: sum,
                counterValue: counter.value,
                fibonacci: fib15,
                factorial: fact10,
                sortedLength: sorted.length
            });
            """;

        // Pre-tokenize
        _smallTokens = new JsLexer(_smallProgram).Tokenize();
        _mediumTokens = new JsLexer(_mediumProgram).Tokenize();
        _largeTokens = new JsLexer(_largeProgram).Tokenize();

        // Pre-parse
        _smallAst = new JsAstParser(_smallTokens, _smallProgram).ParseProgram();
        _mediumAst = new JsAstParser(_mediumTokens, _mediumProgram).ParseProgram();
        _largeAst = new JsAstParser(_largeTokens, _largeProgram).ParseProgram();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _engine.DisposeAsync();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _engine.DisposeAsync().AsTask().Wait();
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);
    }

    // ============ SMALL PROGRAM PHASE BENCHMARKS ============

    [Benchmark]
    [BenchmarkCategory("Small", "Lexing")]
    public IReadOnlyList<Token> Small_LexOnly()
    {
        return new JsLexer(_smallProgram).Tokenize();
    }

    [Benchmark]
    [BenchmarkCategory("Small", "Parsing")]
    public ProgramNode Small_ParseOnly()
    {
        return new JsAstParser(_smallTokens, _smallProgram).ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Small", "Full")]
    public async Task<object?> Small_Full()
    {
        return await _engine.Evaluate(_smallProgram);
    }

    // ============ MEDIUM PROGRAM PHASE BENCHMARKS ============

    [Benchmark]
    [BenchmarkCategory("Medium", "Lexing")]
    public IReadOnlyList<Token> Medium_LexOnly()
    {
        return new JsLexer(_mediumProgram).Tokenize();
    }

    [Benchmark]
    [BenchmarkCategory("Medium", "Parsing")]
    public ProgramNode Medium_ParseOnly()
    {
        return new JsAstParser(_mediumTokens, _mediumProgram).ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Medium", "Full")]
    public async Task<object?> Medium_Full()
    {
        return await _engine.Evaluate(_mediumProgram);
    }

    // ============ LARGE PROGRAM PHASE BENCHMARKS ============

    [Benchmark]
    [BenchmarkCategory("Large", "Lexing")]
    public IReadOnlyList<Token> Large_LexOnly()
    {
        return new JsLexer(_largeProgram).Tokenize();
    }

    [Benchmark]
    [BenchmarkCategory("Large", "Parsing")]
    public ProgramNode Large_ParseOnly()
    {
        return new JsAstParser(_largeTokens, _largeProgram).ParseProgram();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Large", "Full")]
    public async Task<object?> Large_Full()
    {
        return await _engine.Evaluate(_largeProgram);
    }
}
