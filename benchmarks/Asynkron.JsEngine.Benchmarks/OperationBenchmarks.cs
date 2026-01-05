using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Micro-benchmarks for specific JavaScript operations.
/// These help identify which specific operations within the evaluator are slowest.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class OperationBenchmarks
{
    private JsEngine _engine = null!;

    // Arithmetic operations
    private string _intArithmetic = null!;
    private string _floatArithmetic = null!;
    private string _bigIntArithmetic = null!;

    // Property operations
    private string _dotPropertyRead = null!;
    private string _bracketPropertyRead = null!;
    private string _propertyWrite = null!;
    private string _computedProperty = null!;

    // Function operations
    private string _directCall = null!;
    private string _methodCall = null!;
    private string _constructorCall = null!;
    private string _applyCall = null!;

    // Loop operations
    private string _forLoop = null!;
    private string _forOfLoop = null!;
    private string _forInLoop = null!;
    private string _whileLoop = null!;

    // Object operations
    private string _objectLiteral = null!;
    private string _objectSpread = null!;
    private string _objectDestructure = null!;

    // Array operations
    private string _arrayPush = null!;
    private string _arrayMap = null!;
    private string _arrayFilter = null!;
    private string _arrayReduce = null!;
    private string _arraySpread = null!;

    // String operations
    private string _stringConcat = null!;
    private string _templateLiteral = null!;
    private string _stringSplit = null!;

    // Comparison operations
    private string _strictEquality = null!;
    private string _looseEquality = null!;
    private string _typeofCheck = null!;
    private string _instanceofCheck = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        // Arithmetic
        _intArithmetic = """
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                sum = sum + i - 1 + 2 * 3 / 2;
            }
            sum;
            """;

        _floatArithmetic = """
            let sum = 0.0;
            for (let i = 0; i < 10000; i++) {
                sum = sum + i * 0.1 - 0.05 + 0.001 * i;
            }
            sum;
            """;

        _bigIntArithmetic = """
            let sum = 0n;
            for (let i = 0n; i < 5000n; i++) {
                sum = sum + i * 2n - 1n;
            }
            sum;
            """;

        // Property access
        _dotPropertyRead = """
            let obj = { a: 1, b: 2, c: 3, d: 4, e: 5 };
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                sum += obj.a + obj.b + obj.c + obj.d + obj.e;
            }
            sum;
            """;

        _bracketPropertyRead = """
            let obj = { a: 1, b: 2, c: 3, d: 4, e: 5 };
            let keys = ['a', 'b', 'c', 'd', 'e'];
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                for (let k of keys) {
                    sum += obj[k];
                }
            }
            sum;
            """;

        _propertyWrite = """
            let obj = {};
            for (let i = 0; i < 5000; i++) {
                obj.a = i;
                obj.b = i * 2;
                obj.c = i * 3;
            }
            obj.c;
            """;

        _computedProperty = """
            let results = [];
            for (let i = 0; i < 1000; i++) {
                results.push({ ["key" + i]: i, ["value" + i]: i * 2 });
            }
            results.length;
            """;

        // Function calls
        _directCall = """
            function add(a, b) { return a + b; }
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                sum = add(sum, 1);
            }
            sum;
            """;

        _methodCall = """
            let obj = {
                value: 0,
                add(n) { this.value += n; return this.value; }
            };
            for (let i = 0; i < 10000; i++) {
                obj.add(1);
            }
            obj.value;
            """;

        _constructorCall = """
            class Point {
                constructor(x, y) {
                    this.x = x;
                    this.y = y;
                }
            }
            let points = [];
            for (let i = 0; i < 5000; i++) {
                points.push(new Point(i, i * 2));
            }
            points.length;
            """;

        _applyCall = """
            function sum(...args) {
                return args.reduce((a, b) => a + b, 0);
            }
            let total = 0;
            let args = [1, 2, 3, 4, 5];
            for (let i = 0; i < 5000; i++) {
                total += sum.apply(null, args);
            }
            total;
            """;

        // Loops
        _forLoop = """
            let sum = 0;
            for (let i = 0; i < 100000; i++) {
                sum += i;
            }
            sum;
            """;

        // Note: Array construction is included in the benchmark (unavoidable overhead)
        // Both loops iterate 100k times for fair comparison
        _forOfLoop = """
            let arr = [];
            for (let i = 0; i < 100000; i++) arr.push(i);
            let sum = 0;
            for (const x of arr) {
                sum += x;
            }
            sum;
            """;

        _forInLoop = """
            let obj = {};
            for (let i = 0; i < 1000; i++) obj["key" + i] = i;
            let sum = 0;
            for (const key in obj) {
                sum += obj[key];
            }
            sum;
            """;

        _whileLoop = """
            let i = 0;
            let sum = 0;
            while (i < 50000) {
                sum += i;
                i++;
            }
            sum;
            """;

        // Object operations
        _objectLiteral = """
            let objects = [];
            for (let i = 0; i < 2000; i++) {
                objects.push({ a: i, b: i * 2, c: i * 3 });
            }
            objects.length;
            """;

        _objectSpread = """
            let base = { a: 1, b: 2, c: 3 };
            let objects = [];
            for (let i = 0; i < 2000; i++) {
                objects.push({ ...base, d: i });
            }
            objects.length;
            """;

        _objectDestructure = """
            let objects = [];
            for (let i = 0; i < 1000; i++) {
                objects.push({ a: i, b: i * 2, c: i * 3, d: i * 4 });
            }
            let sum = 0;
            for (const { a, b, c, d } of objects) {
                sum += a + b + c + d;
            }
            sum;
            """;

        // Array operations
        _arrayPush = """
            let arr = [];
            for (let i = 0; i < 20000; i++) {
                arr.push(i);
            }
            arr.length;
            """;

        _arrayMap = """
            let arr = [];
            for (let i = 0; i < 5000; i++) arr.push(i);
            let mapped = arr.map(x => x * 2);
            mapped.length;
            """;

        _arrayFilter = """
            let arr = [];
            for (let i = 0; i < 10000; i++) arr.push(i);
            let filtered = arr.filter(x => x % 2 === 0);
            filtered.length;
            """;

        _arrayReduce = """
            let arr = [];
            for (let i = 0; i < 10000; i++) arr.push(i);
            let sum = arr.reduce((acc, x) => acc + x, 0);
            sum;
            """;

        _arraySpread = """
            let arr1 = [1, 2, 3, 4, 5];
            let results = [];
            for (let i = 0; i < 2000; i++) {
                results.push([...arr1, i]);
            }
            results.length;
            """;

        // String operations
        _stringConcat = """
            let str = "";
            for (let i = 0; i < 2000; i++) {
                str = str + "x";
            }
            str.length;
            """;

        _templateLiteral = """
            let parts = [];
            for (let i = 0; i < 2000; i++) {
                parts.push(`value-${i}-end`);
            }
            parts.length;
            """;

        _stringSplit = """
            let str = "";
            for (let i = 0; i < 1000; i++) str += "x,";
            let parts = str.split(",");
            parts.length;
            """;

        // Comparison operations
        _strictEquality = """
            let count = 0;
            for (let i = 0; i < 20000; i++) {
                if (i === 10000) count++;
                if (i !== 5000) count++;
            }
            count;
            """;

        _looseEquality = """
            let count = 0;
            let val = "100";
            for (let i = 0; i < 20000; i++) {
                if (i == val) count++;
                if (i != val) count++;
            }
            count;
            """;

        _typeofCheck = """
            let values = [1, "str", true, null, undefined, {}, [], () => {}];
            let count = 0;
            for (let i = 0; i < 5000; i++) {
                for (const v of values) {
                    if (typeof v === "number") count++;
                    if (typeof v === "string") count++;
                    if (typeof v === "object") count++;
                }
            }
            count;
            """;

        _instanceofCheck = """
            class A {}
            class B extends A {}
            let a = new A();
            let b = new B();
            let count = 0;
            for (let i = 0; i < 10000; i++) {
                if (a instanceof A) count++;
                if (b instanceof A) count++;
                if (b instanceof B) count++;
            }
            count;
            """;
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

    // ============ ARITHMETIC ============
    [Benchmark]
    [BenchmarkCategory("Arithmetic")]
    public async Task<object?> IntArithmetic() => await _engine.Evaluate(_intArithmetic);

    [Benchmark]
    [BenchmarkCategory("Arithmetic")]
    public async Task<object?> FloatArithmetic() => await _engine.Evaluate(_floatArithmetic);

    [Benchmark]
    [BenchmarkCategory("Arithmetic")]
    public async Task<object?> BigIntArithmetic() => await _engine.Evaluate(_bigIntArithmetic);

    // ============ PROPERTY ACCESS ============
    [Benchmark]
    [BenchmarkCategory("Property")]
    public async Task<object?> DotPropertyRead() => await _engine.Evaluate(_dotPropertyRead);

    [Benchmark]
    [BenchmarkCategory("Property")]
    public async Task<object?> BracketPropertyRead() => await _engine.Evaluate(_bracketPropertyRead);

    [Benchmark]
    [BenchmarkCategory("Property")]
    public async Task<object?> PropertyWrite() => await _engine.Evaluate(_propertyWrite);

    [Benchmark]
    [BenchmarkCategory("Property")]
    public async Task<object?> ComputedProperty() => await _engine.Evaluate(_computedProperty);

    // ============ FUNCTION CALLS ============
    [Benchmark]
    [BenchmarkCategory("FunctionCall")]
    public async Task<object?> DirectCall() => await _engine.Evaluate(_directCall);

    [Benchmark]
    [BenchmarkCategory("FunctionCall")]
    public async Task<object?> MethodCall() => await _engine.Evaluate(_methodCall);

    [Benchmark]
    [BenchmarkCategory("FunctionCall")]
    public async Task<object?> ConstructorCall() => await _engine.Evaluate(_constructorCall);

    [Benchmark]
    [BenchmarkCategory("FunctionCall")]
    public async Task<object?> ApplyCall() => await _engine.Evaluate(_applyCall);

    // ============ LOOPS ============
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Loop")]
    public async Task<object?> ForLoop() => await _engine.Evaluate(_forLoop);

    [Benchmark]
    [BenchmarkCategory("Loop")]
    public async Task<object?> ForOfLoop() => await _engine.Evaluate(_forOfLoop);

    [Benchmark]
    [BenchmarkCategory("Loop")]
    public async Task<object?> ForInLoop() => await _engine.Evaluate(_forInLoop);

    [Benchmark]
    [BenchmarkCategory("Loop")]
    public async Task<object?> WhileLoop() => await _engine.Evaluate(_whileLoop);

    // ============ OBJECTS ============
    [Benchmark]
    [BenchmarkCategory("Object")]
    public async Task<object?> ObjectLiteral() => await _engine.Evaluate(_objectLiteral);

    [Benchmark]
    [BenchmarkCategory("Object")]
    public async Task<object?> ObjectSpread() => await _engine.Evaluate(_objectSpread);

    [Benchmark]
    [BenchmarkCategory("Object")]
    public async Task<object?> ObjectDestructure() => await _engine.Evaluate(_objectDestructure);

    // ============ ARRAYS ============
    [Benchmark]
    [BenchmarkCategory("Array")]
    public async Task<object?> ArrayPush() => await _engine.Evaluate(_arrayPush);

    [Benchmark]
    [BenchmarkCategory("Array")]
    public async Task<object?> ArrayMap() => await _engine.Evaluate(_arrayMap);

    [Benchmark]
    [BenchmarkCategory("Array")]
    public async Task<object?> ArrayFilter() => await _engine.Evaluate(_arrayFilter);

    [Benchmark]
    [BenchmarkCategory("Array")]
    public async Task<object?> ArrayReduce() => await _engine.Evaluate(_arrayReduce);

    [Benchmark]
    [BenchmarkCategory("Array")]
    public async Task<object?> ArraySpread() => await _engine.Evaluate(_arraySpread);

    // ============ STRINGS ============
    [Benchmark]
    [BenchmarkCategory("String")]
    public async Task<object?> StringConcat() => await _engine.Evaluate(_stringConcat);

    [Benchmark]
    [BenchmarkCategory("String")]
    public async Task<object?> TemplateLiteral() => await _engine.Evaluate(_templateLiteral);

    [Benchmark]
    [BenchmarkCategory("String")]
    public async Task<object?> StringSplit() => await _engine.Evaluate(_stringSplit);

    // ============ COMPARISONS ============
    [Benchmark]
    [BenchmarkCategory("Comparison")]
    public async Task<object?> StrictEquality() => await _engine.Evaluate(_strictEquality);

    [Benchmark]
    [BenchmarkCategory("Comparison")]
    public async Task<object?> LooseEquality() => await _engine.Evaluate(_looseEquality);

    [Benchmark]
    [BenchmarkCategory("Comparison")]
    public async Task<object?> TypeofCheck() => await _engine.Evaluate(_typeofCheck);

    [Benchmark]
    [BenchmarkCategory("Comparison")]
    public async Task<object?> InstanceofCheck() => await _engine.Evaluate(_instanceofCheck);
}
