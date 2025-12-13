using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;
using BenchmarkDotNet.Attributes;
using Jint;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Parser comparison benchmarks between Asynkron.JsEngine and Jint.
/// Compares the cost of parsing JavaScript source code into AST.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class JintParserComparisonBenchmarks
{
    // Test sources
    private string _simpleExpression = null!;
    private string _functionDefinitions = null!;
    private string _classDefinitions = null!;
    private string _loopStatements = null!;
    private string _arrowFunctions = null!;
    private string _destructuring = null!;
    private string _asyncAwait = null!;
    private string _complexProgram = null!;

    // Pre-tokenized for Asynkron parser (optional - to isolate parser from lexer)
    private IReadOnlyList<Token> _simpleExpressionTokens = null!;
    private IReadOnlyList<Token> _functionDefinitionsTokens = null!;
    private IReadOnlyList<Token> _classDefinitionsTokens = null!;
    private IReadOnlyList<Token> _loopStatementsTokens = null!;
    private IReadOnlyList<Token> _arrowFunctionsTokens = null!;
    private IReadOnlyList<Token> _destructuringTokens = null!;
    private IReadOnlyList<Token> _asyncAwaitTokens = null!;
    private IReadOnlyList<Token> _complexProgramTokens = null!;

    // Jint parser (uses Esprima internally)
    private Jint.Engine _jintEngine = null!;

    [GlobalSetup]
    public void Setup()
    {
        _jintEngine = new Engine();

        _simpleExpression = """
            let x = 1 + 2 * 3 - 4 / 2;
            let y = x * x + Math.sqrt(16);
            let z = y % 7 + Math.pow(2, 10);
            """;

        _functionDefinitions = """
            function fibonacci(n) {
                if (n <= 1) return n;
                return fibonacci(n - 1) + fibonacci(n - 2);
            }

            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }

            function* range(start, end, step = 1) {
                for (let i = start; i < end; i += step) {
                    yield i;
                }
            }

            function quicksort(arr) {
                if (arr.length <= 1) return arr;
                const pivot = arr[Math.floor(arr.length / 2)];
                const left = arr.filter(x => x < pivot);
                const middle = arr.filter(x => x === pivot);
                const right = arr.filter(x => x > pivot);
                return [...quicksort(left), ...middle, ...quicksort(right)];
            }
            """;

        _classDefinitions = """
            class EventEmitter {
                #events = new Map();

                on(event, handler) {
                    if (!this.#events.has(event)) {
                        this.#events.set(event, new Set());
                    }
                    this.#events.get(event).add(handler);
                    return () => this.off(event, handler);
                }

                off(event, handler) {
                    this.#events.get(event)?.delete(handler);
                }

                emit(event, ...args) {
                    this.#events.get(event)?.forEach(handler => handler(...args));
                }
            }

            class Store extends EventEmitter {
                #state;
                #reducers = new Map();

                constructor(initialState = {}) {
                    super();
                    this.#state = initialState;
                }

                getState() {
                    return { ...this.#state };
                }

                dispatch(action) {
                    const { type, payload } = action;
                    const reducer = this.#reducers.get(type);
                    if (reducer) {
                        this.#state = { ...this.#state, ...reducer(this.#state, payload) };
                        this.emit('change', this.#state);
                    }
                }
            }

            class Animal {
                constructor(name) {
                    this.name = name;
                }
                speak() {
                    return `${this.name} makes a sound`;
                }
                static isAnimal(obj) {
                    return obj instanceof Animal;
                }
            }

            class Dog extends Animal {
                #breed;
                constructor(name, breed) {
                    super(name);
                    this.#breed = breed;
                }
                speak() {
                    return `${this.name} barks`;
                }
                get breed() {
                    return this.#breed;
                }
            }
            """;

        _loopStatements = """
            let result = 0;
            for (let i = 0; i < 100; i++) {
                for (let j = 0; j < 100; j++) {
                    if (i === j) continue;
                    if (i * j > 5000) break;
                    result += i * j;
                }
            }

            const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            for (const item of arr) {
                result += item;
            }

            const obj = { a: 1, b: 2, c: 3, d: 4, e: 5 };
            for (const key in obj) {
                if (obj.hasOwnProperty(key)) {
                    result += obj[key];
                }
            }

            while (result > 10000) {
                result = Math.floor(result / 2);
            }

            do {
                result *= 2;
            } while (result < 1000);

            let count = 0;
            outer: for (let i = 0; i < 10; i++) {
                inner: for (let j = 0; j < 10; j++) {
                    if (i * j > 50) break outer;
                    count++;
                }
            }
            """;

        _arrowFunctions = """
            const add = (a, b) => a + b;
            const square = x => x * x;
            const identity = x => x;
            const pair = (a, b) => [a, b];
            const constant = x => () => x;
            const flip = fn => (a, b) => fn(b, a);

            const compose = (...fns) => x => fns.reduceRight((acc, fn) => fn(acc), x);
            const pipe = (...fns) => x => fns.reduce((acc, fn) => fn(acc), x);

            const curry = fn => {
                const arity = fn.length;
                return function curried(...args) {
                    if (args.length >= arity) {
                        return fn(...args);
                    }
                    return (...moreArgs) => curried(...args, ...moreArgs);
                };
            };

            const memoize = fn => {
                const cache = new Map();
                return (...args) => {
                    const key = JSON.stringify(args);
                    if (!cache.has(key)) {
                        cache.set(key, fn(...args));
                    }
                    return cache.get(key);
                };
            };

            const debounce = (fn, delay) => {
                let timeoutId;
                return (...args) => {
                    clearTimeout(timeoutId);
                    timeoutId = setTimeout(() => fn(...args), delay);
                };
            };

            const throttle = (fn, limit) => {
                let inThrottle;
                return (...args) => {
                    if (!inThrottle) {
                        fn(...args);
                        inThrottle = true;
                        setTimeout(() => inThrottle = false, limit);
                    }
                };
            };
            """;

        _destructuring = """
            const { a, b, c: renamed } = { a: 1, b: 2, c: 3 };
            const [first, second, ...rest] = [1, 2, 3, 4, 5];
            const { x = 10, y = 20 } = { x: 5 };

            const nested = {
                level1: {
                    level2: {
                        value: 42,
                        items: [1, 2, 3]
                    }
                }
            };
            const { level1: { level2: { value: deepValue, items: [firstItem, ...otherItems] } } } = nested;

            function process({ name, age = 0, address: { city, country = 'US' } = {} }) {
                return { name, age, city, country };
            }

            const swap = ([a, b]) => [b, a];
            const head = ([first, ...rest]) => first;
            const tail = ([first, ...rest]) => rest;
            const init = arr => arr.slice(0, -1);
            const last = arr => arr[arr.length - 1];

            const merge = ({ ...obj1 }, { ...obj2 }) => ({ ...obj1, ...obj2 });
            const pick = (obj, ...keys) => keys.reduce((acc, key) => ({ ...acc, [key]: obj[key] }), {});
            const omit = (obj, ...keys) => Object.fromEntries(Object.entries(obj).filter(([k]) => !keys.includes(k)));

            function parseConfig({
                host = 'localhost',
                port = 8080,
                ssl: { enabled = false, cert = null } = {},
                logging: { level = 'info', format = 'json' } = {}
            } = {}) {
                return { host, port, ssl: { enabled, cert }, logging: { level, format } };
            }
            """;

        _asyncAwait = """
            async function fetchData(url) {
                const response = await fetch(url);
                const data = await response.json();
                return data;
            }

            async function processItems(items) {
                const results = [];
                for await (const item of items) {
                    results.push(await processItem(item));
                }
                return results;
            }

            const asyncPipe = (...fns) => async (x) => {
                let result = x;
                for (const fn of fns) {
                    result = await fn(result);
                }
                return result;
            };

            async function* asyncRange(start, end) {
                for (let i = start; i < end; i++) {
                    await new Promise(r => setTimeout(r, 0));
                    yield i;
                }
            }

            const retry = async (fn, attempts = 3, delay = 1000) => {
                for (let i = 0; i < attempts; i++) {
                    try {
                        return await fn();
                    } catch (e) {
                        if (i === attempts - 1) throw e;
                        await new Promise(r => setTimeout(r, delay));
                    }
                }
            };

            const timeout = (promise, ms) => Promise.race([
                promise,
                new Promise((_, reject) => setTimeout(() => reject(new Error('Timeout')), ms))
            ]);

            async function parallel(...promises) {
                return Promise.all(promises);
            }

            async function sequence(fns) {
                const results = [];
                for (const fn of fns) {
                    results.push(await fn());
                }
                return results;
            }

            class AsyncQueue {
                #queue = [];
                #processing = false;

                async enqueue(task) {
                    return new Promise((resolve, reject) => {
                        this.#queue.push({ task, resolve, reject });
                        this.#process();
                    });
                }

                async #process() {
                    if (this.#processing) return;
                    this.#processing = true;
                    while (this.#queue.length > 0) {
                        const { task, resolve, reject } = this.#queue.shift();
                        try {
                            resolve(await task());
                        } catch (e) {
                            reject(e);
                        }
                    }
                    this.#processing = false;
                }
            }
            """;

        _complexProgram = """
            // Observable pattern implementation
            class Observable {
                #subscribers = new Set();

                subscribe(observer) {
                    this.#subscribers.add(observer);
                    return {
                        unsubscribe: () => this.#subscribers.delete(observer)
                    };
                }

                next(value) {
                    this.#subscribers.forEach(obs => obs.next?.(value));
                }

                error(err) {
                    this.#subscribers.forEach(obs => obs.error?.(err));
                }

                complete() {
                    this.#subscribers.forEach(obs => obs.complete?.());
                    this.#subscribers.clear();
                }

                pipe(...operators) {
                    return operators.reduce((obs, op) => op(obs), this);
                }

                static from(iterable) {
                    const obs = new Observable();
                    Promise.resolve().then(() => {
                        try {
                            for (const item of iterable) {
                                obs.next(item);
                            }
                            obs.complete();
                        } catch (e) {
                            obs.error(e);
                        }
                    });
                    return obs;
                }

                static interval(ms) {
                    const obs = new Observable();
                    let count = 0;
                    const id = setInterval(() => obs.next(count++), ms);
                    obs.cancel = () => clearInterval(id);
                    return obs;
                }
            }

            // Operators
            const map = fn => source => {
                const obs = new Observable();
                source.subscribe({
                    next: value => obs.next(fn(value)),
                    error: err => obs.error(err),
                    complete: () => obs.complete()
                });
                return obs;
            };

            const filter = predicate => source => {
                const obs = new Observable();
                source.subscribe({
                    next: value => predicate(value) && obs.next(value),
                    error: err => obs.error(err),
                    complete: () => obs.complete()
                });
                return obs;
            };

            const take = count => source => {
                const obs = new Observable();
                let taken = 0;
                const sub = source.subscribe({
                    next: value => {
                        if (taken++ < count) {
                            obs.next(value);
                        }
                        if (taken >= count) {
                            obs.complete();
                            sub.unsubscribe();
                        }
                    },
                    error: err => obs.error(err),
                    complete: () => obs.complete()
                });
                return obs;
            };

            const scan = (accumulator, seed) => source => {
                const obs = new Observable();
                let acc = seed;
                source.subscribe({
                    next: value => {
                        acc = accumulator(acc, value);
                        obs.next(acc);
                    },
                    error: err => obs.error(err),
                    complete: () => obs.complete()
                });
                return obs;
            };

            // Usage
            const numbers = Observable.from([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
            const result = numbers.pipe(
                filter(x => x % 2 === 0),
                map(x => x * x),
                scan((acc, x) => acc + x, 0),
                take(3)
            );

            result.subscribe({
                next: value => console.log(value),
                complete: () => console.log('Done')
            });
            """;

        // Pre-tokenize for Asynkron
        _simpleExpressionTokens = new Lexer(_simpleExpression).Tokenize();
        _functionDefinitionsTokens = new Lexer(_functionDefinitions).Tokenize();
        _classDefinitionsTokens = new Lexer(_classDefinitions).Tokenize();
        _loopStatementsTokens = new Lexer(_loopStatements).Tokenize();
        _arrowFunctionsTokens = new Lexer(_arrowFunctions).Tokenize();
        _destructuringTokens = new Lexer(_destructuring).Tokenize();
        _asyncAwaitTokens = new Lexer(_asyncAwait).Tokenize();
        _complexProgramTokens = new Lexer(_complexProgram).Tokenize();
    }

    // ==================== Simple Expression ====================
    [Benchmark]
    [BenchmarkCategory("Simple")]
    public ProgramNode Asynkron_Parse_SimpleExpression()
    {
        var parser = new TypedAstParser(_simpleExpressionTokens, _simpleExpression);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public Esprima.Ast.Script Jint_Parse_SimpleExpression()
    {
        return new Esprima.JavaScriptParser().ParseScript(_simpleExpression);
    }

    // ==================== Function Definitions ====================
    [Benchmark]
    [BenchmarkCategory("Functions")]
    public ProgramNode Asynkron_Parse_Functions()
    {
        var parser = new TypedAstParser(_functionDefinitionsTokens, _functionDefinitions);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Functions")]
    public Esprima.Ast.Script Jint_Parse_Functions()
    {
        return new Esprima.JavaScriptParser().ParseScript(_functionDefinitions);
    }

    // ==================== Class Definitions ====================
    [Benchmark]
    [BenchmarkCategory("Classes")]
    public ProgramNode Asynkron_Parse_Classes()
    {
        var parser = new TypedAstParser(_classDefinitionsTokens, _classDefinitions);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Classes")]
    public Esprima.Ast.Script Jint_Parse_Classes()
    {
        return new Esprima.JavaScriptParser().ParseScript(_classDefinitions);
    }

    // ==================== Loop Statements ====================
    [Benchmark]
    [BenchmarkCategory("Loops")]
    public ProgramNode Asynkron_Parse_Loops()
    {
        var parser = new TypedAstParser(_loopStatementsTokens, _loopStatements);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Loops")]
    public Esprima.Ast.Script Jint_Parse_Loops()
    {
        return new Esprima.JavaScriptParser().ParseScript(_loopStatements);
    }

    // ==================== Arrow Functions ====================
    [Benchmark]
    [BenchmarkCategory("ES6")]
    public ProgramNode Asynkron_Parse_ArrowFunctions()
    {
        var parser = new TypedAstParser(_arrowFunctionsTokens, _arrowFunctions);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("ES6")]
    public Esprima.Ast.Script Jint_Parse_ArrowFunctions()
    {
        return new Esprima.JavaScriptParser().ParseScript(_arrowFunctions);
    }

    // ==================== Destructuring ====================
    [Benchmark]
    [BenchmarkCategory("ES6")]
    public ProgramNode Asynkron_Parse_Destructuring()
    {
        var parser = new TypedAstParser(_destructuringTokens, _destructuring);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("ES6")]
    public Esprima.Ast.Script Jint_Parse_Destructuring()
    {
        return new Esprima.JavaScriptParser().ParseScript(_destructuring);
    }

    // ==================== Async/Await ====================
    [Benchmark]
    [BenchmarkCategory("Async")]
    public ProgramNode Asynkron_Parse_AsyncAwait()
    {
        var parser = new TypedAstParser(_asyncAwaitTokens, _asyncAwait);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Async")]
    public Esprima.Ast.Script Jint_Parse_AsyncAwait()
    {
        return new Esprima.JavaScriptParser().ParseScript(_asyncAwait);
    }

    // ==================== Complex Program ====================
    [Benchmark]
    [BenchmarkCategory("Complex")]
    public ProgramNode Asynkron_Parse_ComplexProgram()
    {
        var parser = new TypedAstParser(_complexProgramTokens, _complexProgram);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("Complex")]
    public Esprima.Ast.Script Jint_Parse_ComplexProgram()
    {
        return new Esprima.JavaScriptParser().ParseScript(_complexProgram);
    }

    // ==================== Full Pipeline (Lex + Parse) ====================
    [Benchmark]
    [BenchmarkCategory("FullPipeline")]
    public ProgramNode Asynkron_LexAndParse_ComplexProgram()
    {
        var tokens = new Lexer(_complexProgram).Tokenize();
        var parser = new TypedAstParser(tokens, _complexProgram);
        return parser.ParseProgram();
    }

    [Benchmark]
    [BenchmarkCategory("FullPipeline")]
    public Esprima.Ast.Script Jint_LexAndParse_ComplexProgram()
    {
        return new Esprima.JavaScriptParser().ParseScript(_complexProgram);
    }
}
