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
    // Asynkron engine
    private JsEngine _asynkronEngine = null!;

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
            function fib(n) {
                if (n <= 1) return n;
                return fib(n - 1) + fib(n - 2);
            }
            fib(25);
            """;

        // For loop intensive
        _forLoop = """
            let sum = 0;
            for (let i = 0; i < 100000; i++) {
                sum += i;
            }
            sum;
            """;

        // While loop
        _whileLoop = """
            let sum = 0;
            let i = 0;
            while (i < 100000) {
                sum += i;
                i++;
            }
            sum;
            """;

        // Object creation
        _objectCreation = """
            let objects = [];
            for (let i = 0; i < 5000; i++) {
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
            for (let i = 0; i < 5000; i++) {
                arr.push(i);
            }
            let mapped = arr.map(x => x * 2);
            let filtered = mapped.filter(x => x > 2500);
            let sum = filtered.reduce((a, b) => a + b, 0);
            sum;
            """;

        // String operations
        _stringOperations = """
            let result = "";
            for (let i = 0; i < 1000; i++) {
                result += "x";
            }
            let upper = result.toUpperCase();
            let split = result.split("");
            let joined = split.join("-");
            joined.length;
            """;

        // Function calls
        _functionCalls = """
            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }
            function sub(a, b) { return a - b; }
            function div(a, b) { return a / b; }

            let result = 0;
            for (let i = 0; i < 10000; i++) {
                result = add(result, mul(i, 2));
                result = sub(result, div(i, 2));
            }
            result;
            """;

        // Closures
        _closures = """
            function makeCounter() {
                let count = 0;
                return function() {
                    return ++count;
                };
            }

            let counters = [];
            for (let i = 0; i < 100; i++) {
                counters.push(makeCounter());
            }

            let sum = 0;
            for (let i = 0; i < 100; i++) {
                for (let j = 0; j < 50; j++) {
                    sum += counters[i]();
                }
            }
            sum;
            """;

        // Recursion (non-fibonacci)
        _recursion = """
            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }

            function sumTo(n) {
                if (n <= 0) return 0;
                return n + sumTo(n - 1);
            }

            let result = 0;
            for (let i = 0; i < 500; i++) {
                result += factorial(12);
                result += sumTo(100);
            }
            result;
            """;

        // Property access (deep nesting)
        _propertyAccess = """
            let obj = {
                a: { b: { c: { d: { e: 1 } } } },
                x: 10,
                y: 20,
                z: 30
            };
            let sum = 0;
            for (let i = 0; i < 50000; i++) {
                sum += obj.a.b.c.d.e;
                sum += obj.x + obj.y + obj.z;
            }
            sum;
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
            for (let i = 0; i < 1000; i++) {
                dogs.push(new Dog("Dog" + i, "Breed" + (i % 10)));
            }
            let sounds = dogs.map(d => d.speak());
            sounds.length;
            """;

        // Destructuring
        _destructuring = """
            let results = [];
            for (let i = 0; i < 5000; i++) {
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
            for (let i = 0; i < 2000; i++) {
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
            for (let i = 0; i < 5000; i++) {
                map.set("key" + i, i);
                set.add(i);
            }
            let sum = 0;
            for (let i = 0; i < 5000; i++) {
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
            for (let i = 0; i < 2000; i++) {
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
            for (let i = 0; i < 5000; i++) {
                let matches = text.match(/[a-z]+/gi);
                count += matches ? matches.length : 0;
                let replaced = text.replace(/[aeiou]/g, '*');
                count += replaced.length;
            }
            count;
            """;

        // Basic Promise
        _promiseBasic = """
            let result = 0;
            for (let i = 0; i < 1000; i++) {
                let p = new Promise((resolve) => resolve(i));
                p.then(v => { result += v; });
            }
            result;
            """;

        // Async/await (synchronous resolution)
        _asyncAwait = """
            async function asyncAdd(a, b) {
                return a + b;
            }

            async function compute() {
                let sum = 0;
                for (let i = 0; i < 500; i++) {
                    sum = await asyncAdd(sum, i);
                }
                return sum;
            }

            compute();
            """;

        // Generator function
        _generatorFunction = """
            function* range(start, end) {
                for (let i = start; i < end; i++) {
                    yield i;
                }
            }

            let sum = 0;
            for (let i = 0; i < 100; i++) {
                for (const n of range(0, 100)) {
                    sum += n;
                }
            }
            sum;
            """;
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
        return await _asynkronEngine.Evaluate(_simpleArithmetic);
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
        return await _asynkronEngine.Evaluate(_fibonacci);
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
        return await _asynkronEngine.Evaluate(_forLoop);
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
        return await _asynkronEngine.Evaluate(_whileLoop);
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
        return await _asynkronEngine.Evaluate(_objectCreation);
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
        return await _asynkronEngine.Evaluate(_arrayOperations);
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
        return await _asynkronEngine.Evaluate(_stringOperations);
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
        return await _asynkronEngine.Evaluate(_functionCalls);
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
        return await _asynkronEngine.Evaluate(_closures);
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
        return await _asynkronEngine.Evaluate(_recursion);
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
        return await _asynkronEngine.Evaluate(_propertyAccess);
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
        return await _asynkronEngine.Evaluate(_classDefinition);
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
        return await _asynkronEngine.Evaluate(_destructuring);
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
        return await _asynkronEngine.Evaluate(_spreadOperator);
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
        return await _asynkronEngine.Evaluate(_mapSet);
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
        return await _asynkronEngine.Evaluate(_jsonOperations);
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
        return await _asynkronEngine.Evaluate(_regexOperations);
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
        return await _asynkronEngine.Evaluate(_promiseBasic);
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
        return await _asynkronEngine.Evaluate(_asyncAwait);
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
        return await _asynkronEngine.Evaluate(_generatorFunction);
    }

    [Benchmark]
    [BenchmarkCategory("Generator")]
    public object Jint_GeneratorFunction()
    {
        return _jintEngine.Evaluate(_generatorFunction).ToObject()!;
    }
}
