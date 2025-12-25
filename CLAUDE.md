# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See @AGENTS.md for additional coding standards, profiling guidelines, and optimization patterns.

## Build and Test Commands

```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run unit tests only
dotnet test tests/Asynkron.JsEngine.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests by category
dotnet test --filter Category=AsyncForOfGlobalKnownFailure

# Run demos
dotnet run --project examples/Demo
dotnet run --project examples/PromiseDemo
dotnet run --project examples/NpmPackageDemo

# Profiling
./tools/profile forofiteration --cpu
./tools/profile forofiteration --memory
./tools/profile forofiteration --exception
Where "forofiteration" is the name of one of the /tools/Scripts examples

```

**Important**: Never use `--no-build` - always ensure you are working with the latest compiled code.

## Architecture Overview

This is a JavaScript interpreter written in C# targeting .NET 10. The execution pipeline is:

**JavaScript Source → Lexer → TypedAstParser → Typed AST → TypedAstEvaluator → Result**

### Core Components (in `src/Asynkron.JsEngine/`)

- **Parser/** - `Lexer` tokenizes source, `TypedAstParser` produces typed AST nodes (`ProgramNode`, `StatementNode`, `ExpressionNode`)
- **Ast/** - AST node definitions and the `TypedAstEvaluator` which executes the AST. Many `*Extensions.cs` files contain evaluation logic for specific node types
- **JsTypes/** - JavaScript value types: `JsObject`, `JsArray`, `JsFunction`, `JsPromise`, `JsBigInt`, typed arrays, etc.
- **JsEnvironment.cs** - Lexical environment/scope chain management
- **JsEngine.cs** - Public API façade, registers globals (Object, Array, Promise, Symbol, Map, Set, etc.), integrates event queue
- **Execution/** - Generator IR interpreter for `yield`/`yield*`, async iteration support
- **StdLib/** - Standard library implementations (Math, Date, JSON, RegExp, console, etc.)

### Key Design Patterns

- **Generator IR**: Synchronous generators compile to `GeneratorPlan` and execute via IR interpreter (not AST replay)
- **CPS Transformation**: Async/await lowered to Promise/continuation-passing style before evaluation
- **Prototype Chains**: `JsObject` tracks prototype chain for property lookup traversal

## Development Rules

### Thread Safety
- **Never** use `Task.Wait()`, `Task.Result`, or `Thread.Sleep()` - these block threads
- **Never** use `ThreadStatic`, `AsyncLocal<T>`, or shared state between async calls
- Pass all context explicitly via `JsEnvironment` or similar parameters

### ECMAScript Compliance
- Follow ECMAScript specification behavior as closely as practical
- Do not introduce non-standard language extensions
- Support both strict and sloppy mode with spec-defined differences

### Error Handling
- Throw `NotSupportedException` with clear reason for unsupported features - never silently degrade
- Use `realm.Logger?.LogInformation(...)` for diagnostics, never `Console.WriteLine`

### Code Generation
- Never edit files with `.generated.` in their names - they are produced by tooling
- Edit non-generated partials/helpers instead

### Debugging
- Use `System.Diagnostics.Activity` for tracing (see `ActivityTracingTests.EvaluatorActivitiesAttachToTestRoot`)

## Project Structure

```
src/
  Asynkron.JsEngine/           # Main engine library
  Asynkron.JsEngine.Generators/ # Source generators
tests/
  Asynkron.JsEngine.Tests/     # Unit tests (xUnit)
  Asynkron.JsEngine.Tests.Test262/ # ECMAScript Test262 conformance tests
examples/                      # Demo console applications
docs/                          # Detailed documentation
```

## Workflow

The `continue.md` file at repo root contains rolling next steps. When completing a task, remove it from `continue.md` and update with new steps.
