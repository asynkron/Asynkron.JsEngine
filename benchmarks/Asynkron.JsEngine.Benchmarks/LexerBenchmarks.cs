using Asynkron.JsEngine.Parser;
using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Benchmarks for the Lexer (tokenization) phase.
/// Measures the cost of converting JavaScript source into tokens.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class LexerBenchmarks
{
    private string _simpleExpression = null!;
    private string _functionDefinition = null!;
    private string _classDefinition = null!;
    private string _loopHeavy = null!;
    private string _objectLiteral = null!;
    private string _arrayOperations = null!;
    private string _stringHeavy = null!;
    private string _complexProgram = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simpleExpression = "let x = 1 + 2 * 3;";

        _functionDefinition = """
            function fibonacci(n) {
                if (n <= 1) return n;
                return fibonacci(n - 1) + fibonacci(n - 2);
            }
            """;

        _classDefinition = """
            class Person {
                constructor(name, age) {
                    this.name = name;
                    this.age = age;
                }

                greet() {
                    return `Hello, my name is ${this.name}`;
                }

                static create(name, age) {
                    return new Person(name, age);
                }

                get info() {
                    return { name: this.name, age: this.age };
                }

                set info(value) {
                    this.name = value.name;
                    this.age = value.age;
                }
            }
            """;

        _loopHeavy = """
            let sum = 0;
            for (let i = 0; i < 1000; i++) {
                for (let j = 0; j < 100; j++) {
                    sum += i * j;
                }
            }
            while (sum > 0) {
                sum--;
            }
            do {
                sum++;
            } while (sum < 10);
            """;

        _objectLiteral = """
            const obj = {
                name: "test",
                value: 42,
                nested: {
                    a: 1,
                    b: 2,
                    c: {
                        d: 3,
                        e: [1, 2, 3]
                    }
                },
                method() {
                    return this.value;
                },
                get prop() { return this.name; },
                ["computed" + "Key"]: "dynamic"
            };
            """;

        _arrayOperations = """
            const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            const mapped = arr.map(x => x * 2);
            const filtered = arr.filter(x => x > 5);
            const reduced = arr.reduce((acc, x) => acc + x, 0);
            const spread = [...arr, ...mapped, ...filtered];
            const destructured = [first, second, ...rest] = arr;
            """;

        _stringHeavy = """
            const str1 = "Hello, World!";
            const str2 = 'Single quotes';
            const template = `Template literal with ${str1} interpolation`;
            const multiline = `
                This is a
                multiline
                template literal
                with ${1 + 2} expression
            `;
            const escaped = "Line1\nLine2\tTabbed\"Quoted\"";
            """;

        _complexProgram = """
            // Complex program combining multiple features
            class Calculator {
                #history = [];

                constructor(initialValue = 0) {
                    this.value = initialValue;
                }

                add(n) {
                    this.#history.push({ op: 'add', n });
                    this.value += n;
                    return this;
                }

                subtract(n) {
                    this.#history.push({ op: 'subtract', n });
                    this.value -= n;
                    return this;
                }

                multiply(n) {
                    this.#history.push({ op: 'multiply', n });
                    this.value *= n;
                    return this;
                }

                divide(n) {
                    if (n === 0) throw new Error('Division by zero');
                    this.#history.push({ op: 'divide', n });
                    this.value /= n;
                    return this;
                }

                async compute(operations) {
                    for (const { op, value } of operations) {
                        await new Promise(resolve => setTimeout(resolve, 0));
                        switch (op) {
                            case 'add': this.add(value); break;
                            case 'subtract': this.subtract(value); break;
                            case 'multiply': this.multiply(value); break;
                            case 'divide': this.divide(value); break;
                            default: throw new Error(`Unknown operation: ${op}`);
                        }
                    }
                    return this.value;
                }

                getHistory() {
                    return [...this.#history];
                }

                static fromOperations(ops) {
                    const calc = new Calculator();
                    ops.forEach(({ op, value }) => calc[op]?.(value));
                    return calc;
                }
            }

            const calc = new Calculator(10);
            calc.add(5).multiply(2).subtract(3).divide(2);

            const operations = [
                { op: 'add', value: 10 },
                { op: 'multiply', value: 2 },
                { op: 'subtract', value: 5 }
            ];

            const { value, getHistory } = calc;
            const history = calc.getHistory();

            try {
                calc.divide(0);
            } catch (e) {
                console.log(`Error: ${e.message}`);
            }
            """;
    }

    [Benchmark(Baseline = true)]
    public IReadOnlyList<Token> SimpleExpression()
    {
        var lexer = new Lexer(_simpleExpression);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> FunctionDefinition()
    {
        var lexer = new Lexer(_functionDefinition);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> ClassDefinition()
    {
        var lexer = new Lexer(_classDefinition);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> LoopHeavy()
    {
        var lexer = new Lexer(_loopHeavy);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> ObjectLiteral()
    {
        var lexer = new Lexer(_objectLiteral);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> ArrayOperations()
    {
        var lexer = new Lexer(_arrayOperations);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> StringHeavy()
    {
        var lexer = new Lexer(_stringHeavy);
        return lexer.Tokenize();
    }

    [Benchmark]
    public IReadOnlyList<Token> ComplexProgram()
    {
        var lexer = new Lexer(_complexProgram);
        return lexer.Tokenize();
    }
}
