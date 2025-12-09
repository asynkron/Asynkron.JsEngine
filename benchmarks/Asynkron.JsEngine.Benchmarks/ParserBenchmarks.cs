using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;
using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Benchmarks for the Parser (TypedAstParser) phase.
/// Measures the cost of converting tokens into a typed AST.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class ParserBenchmarks
{
    private IReadOnlyList<Token> _simpleExpressionTokens = null!;
    private IReadOnlyList<Token> _functionDefinitionTokens = null!;
    private IReadOnlyList<Token> _classDefinitionTokens = null!;
    private IReadOnlyList<Token> _loopHeavyTokens = null!;
    private IReadOnlyList<Token> _arrowFunctionsTokens = null!;
    private IReadOnlyList<Token> _destructuringTokens = null!;
    private IReadOnlyList<Token> _asyncAwaitTokens = null!;
    private IReadOnlyList<Token> _complexProgramTokens = null!;

    private string _simpleExpression = null!;
    private string _functionDefinition = null!;
    private string _classDefinition = null!;
    private string _loopHeavy = null!;
    private string _arrowFunctions = null!;
    private string _destructuring = null!;
    private string _asyncAwait = null!;
    private string _complexProgram = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simpleExpression = "let x = 1 + 2 * 3 - 4 / 2;";

        _functionDefinition = """
            function fibonacci(n) {
                if (n <= 1) return n;
                return fibonacci(n - 1) + fibonacci(n - 2);
            }

            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }

            function* range(start, end) {
                for (let i = start; i < end; i++) {
                    yield i;
                }
            }
            """;

        _classDefinition = """
            class Animal {
                constructor(name) {
                    this.name = name;
                }
                speak() {
                    console.log(`${this.name} makes a sound.`);
                }
            }

            class Dog extends Animal {
                constructor(name, breed) {
                    super(name);
                    this.breed = breed;
                }
                speak() {
                    console.log(`${this.name} barks.`);
                }
                static isDog(animal) {
                    return animal instanceof Dog;
                }
            }

            class Cat extends Animal {
                #lives = 9;
                speak() {
                    console.log(`${this.name} meows.`);
                }
                get lives() {
                    return this.#lives;
                }
            }
            """;

        _loopHeavy = """
            let result = 0;
            for (let i = 0; i < 100; i++) {
                for (let j = 0; j < 100; j++) {
                    if (i === j) continue;
                    if (i * j > 5000) break;
                    result += i * j;
                }
            }

            let arr = [1, 2, 3, 4, 5];
            for (const item of arr) {
                result += item;
            }

            let obj = { a: 1, b: 2, c: 3 };
            for (const key in obj) {
                result += obj[key];
            }

            while (result > 10000) {
                result = Math.floor(result / 2);
            }

            do {
                result *= 2;
            } while (result < 100);
            """;

        _arrowFunctions = """
            const add = (a, b) => a + b;
            const square = x => x * x;
            const identity = x => x;
            const pair = (a, b) => [a, b];
            const constant = x => () => x;

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
            """;

        _destructuring = """
            const { a, b, c: renamed } = { a: 1, b: 2, c: 3 };
            const [first, second, ...rest] = [1, 2, 3, 4, 5];
            const { x = 10, y = 20 } = { x: 5 };

            const nested = {
                level1: {
                    level2: {
                        value: 42
                    }
                }
            };
            const { level1: { level2: { value: deepValue } } } = nested;

            function process({ name, age = 0, address: { city, country = 'US' } = {} }) {
                return { name, age, city, country };
            }

            const swap = ([a, b]) => [b, a];
            const head = ([first, ...rest]) => first;
            const tail = ([first, ...rest]) => rest;

            const merge = ({ ...obj1 }, { ...obj2 }) => ({ ...obj1, ...obj2 });
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

            const retry = async (fn, attempts = 3) => {
                for (let i = 0; i < attempts; i++) {
                    try {
                        return await fn();
                    } catch (e) {
                        if (i === attempts - 1) throw e;
                    }
                }
            };
            """;

        _complexProgram = """
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

                once(event, handler) {
                    const wrapper = (...args) => {
                        this.off(event, wrapper);
                        handler(...args);
                    };
                    return this.on(event, wrapper);
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

                addReducer(action, reducer) {
                    this.#reducers.set(action, reducer);
                }

                dispatch(action) {
                    const { type, payload } = action;
                    const reducer = this.#reducers.get(type);
                    if (reducer) {
                        const newState = reducer(this.#state, payload);
                        this.#state = { ...this.#state, ...newState };
                        this.emit('change', this.#state);
                    }
                }

                subscribe(handler) {
                    return this.on('change', handler);
                }
            }

            const store = new Store({ count: 0, items: [] });

            store.addReducer('INCREMENT', (state, amount = 1) => ({
                count: state.count + amount
            }));

            store.addReducer('ADD_ITEM', (state, item) => ({
                items: [...state.items, item]
            }));

            const unsubscribe = store.subscribe(state => {
                console.log('State changed:', state);
            });

            store.dispatch({ type: 'INCREMENT', payload: 5 });
            store.dispatch({ type: 'ADD_ITEM', payload: { id: 1, name: 'Test' } });

            const { count, items } = store.getState();
            """;

        // Pre-tokenize all sources
        _simpleExpressionTokens = new Lexer(_simpleExpression).Tokenize();
        _functionDefinitionTokens = new Lexer(_functionDefinition).Tokenize();
        _classDefinitionTokens = new Lexer(_classDefinition).Tokenize();
        _loopHeavyTokens = new Lexer(_loopHeavy).Tokenize();
        _arrowFunctionsTokens = new Lexer(_arrowFunctions).Tokenize();
        _destructuringTokens = new Lexer(_destructuring).Tokenize();
        _asyncAwaitTokens = new Lexer(_asyncAwait).Tokenize();
        _complexProgramTokens = new Lexer(_complexProgram).Tokenize();
    }

    [Benchmark(Baseline = true)]
    public ProgramNode SimpleExpression()
    {
        var parser = new TypedAstParser(_simpleExpressionTokens, _simpleExpression);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode FunctionDefinition()
    {
        var parser = new TypedAstParser(_functionDefinitionTokens, _functionDefinition);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode ClassDefinition()
    {
        var parser = new TypedAstParser(_classDefinitionTokens, _classDefinition);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode LoopHeavy()
    {
        var parser = new TypedAstParser(_loopHeavyTokens, _loopHeavy);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode ArrowFunctions()
    {
        var parser = new TypedAstParser(_arrowFunctionsTokens, _arrowFunctions);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode Destructuring()
    {
        var parser = new TypedAstParser(_destructuringTokens, _destructuring);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode AsyncAwait()
    {
        var parser = new TypedAstParser(_asyncAwaitTokens, _asyncAwait);
        return parser.ParseProgram();
    }

    [Benchmark]
    public ProgramNode ComplexProgram()
    {
        var parser = new TypedAstParser(_complexProgramTokens, _complexProgram);
        return parser.ParseProgram();
    }
}
