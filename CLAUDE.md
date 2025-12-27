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

### Test Timeouts
- All tests MUST complete within 20 seconds
- When running tests via CLI, use: `dotnet test -- xUnit.MaxParallelThreads=1 -timeout 20000`
- Tests that exceed 20 seconds indicate a bug (infinite loop, deadlock, or inefficient implementation)

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

## Git Worktree Workflow for Refactoring and Bugfixing

All refactoring or bugfixing work MUST be performed using git worktrees for isolation. Follow this workflow:

1. **Create git worktree for the feature/bug**
   ```bash
   git worktree add ../Asynkron.JsEngine-<short-name> -b feature/<feature-name>
   # or for bugs:
   git worktree add ../Asynkron.JsEngine-<short-name> -b fix/<bug-name>
   ```

2. **Make a plan** - Analyze the issue, identify affected files, and plan the implementation

3. **Implement the fix/refactoring** - Make the necessary code changes

4. **Run filtered tests** - Verify the specific fix works
   ```bash
   dotnet test --filter "FullyQualifiedName~RelevantTestName"
   ```

5. **Run full internal test suite**
   ```bash
   dotnet test tests/Asynkron.JsEngine.Tests
   ```

6. **Run CPU and memory profiler** - If a relevant profiling script exists for the feature
   ```bash
   ./tools/profile <script-name> --cpu
   ./tools/profile <script-name> --memory
   ```

7. **Any problems?** - If tests fail or performance regresses, return to step 2 and iterate

8. **Commit and push**
   ```bash
   git add -A && git commit -m "Description of changes"
   git push -u origin <branch-name>
   ```

9. **Create GitHub PR using gh CLI**
   ```bash
   gh pr create --title "PR Title" --body "Description"
   ```

10. **Merge the PR using gh CLI**
    ```bash
    gh pr merge <pr-number> --squash
    ```

11. **Sync main and delete the worktree**
    ```bash
    # In main repo:
    git fetch origin && git reset --hard origin/main
    git worktree remove ../Asynkron.JsEngine-<short-name>
    git branch -D <branch-name>
    ```

## Using Codex CLI as a Sub-Agent

For complex reasoning tasks, you can delegate to OpenAI Codex CLI. This is useful for tasks requiring deep analysis, alternative perspectives, or when you want to leverage GPT models for specific subtasks.

### Basic Usage

```bash
# Run a task with full automation (no approval prompts, no sandbox)
codex exec --dangerously-bypass-approvals-and-sandbox "your prompt here"

# With web search enabled
codex exec --dangerously-bypass-approvals-and-sandbox --search "research topic"

# Set working directory
codex exec --dangerously-bypass-approvals-and-sandbox -C /path/to/dir "task"

# Output as JSONL for parsing
codex exec --dangerously-bypass-approvals-and-sandbox --json "task"

# Save response to file
codex exec --dangerously-bypass-approvals-and-sandbox -o result.txt "task"
```

### Model and Reasoning Configuration

Set via command-line config overrides:

```bash
# Use a specific model
codex exec -c model="o3" --dangerously-bypass-approvals-and-sandbox "task"

# Set reasoning effort (low, medium, high)
codex exec -c model_reasoning_effort="high" --dangerously-bypass-approvals-and-sandbox "task"

# Combined
codex exec -c model="gpt-5.1-codex-max" -c model_reasoning_effort="high" \
  --dangerously-bypass-approvals-and-sandbox "analyze this complex algorithm"
```

Or set defaults in `~/.codex/config.toml`:

```toml
model = "gpt-5.1-codex-max"
model_reasoning_effort = "high"
```

### Key Options Reference

| Flag | Purpose |
|------|---------|
| `--dangerously-bypass-approvals-and-sandbox` | Skip confirmations, run without sandbox |
| `--search` | Enable web search capability |
| `-m, --model <MODEL>` | Model to use (e.g., `o3`, `gpt-5.1-codex-max`) |
| `-c model_reasoning_effort="high"` | Reasoning level: `low`, `medium`, `high` |
| `-p, --profile <PROFILE>` | Use config profile from `~/.codex/config.toml` |
| `-C, --cd <DIR>` | Set working directory |
| `--json` | Output as JSONL (for parsing) |
| `-o, --output-last-message <FILE>` | Write final response to file |
| `--full-auto` | Alias for `-a on-request --sandbox workspace-write` |

### Running Codex in a Split Pane (Recommended)

When running inside tmux, use a vertical split to show Codex output alongside the main session:

```bash
# Run Codex in a vertical split pane (side-by-side view)
tmux split-window -h 'codex exec --dangerously-bypass-approvals-and-sandbox "your prompt here"; read -p "Press enter to close..."'
```

This allows the user to see both Claude Code and Codex output simultaneously. The `read` command keeps the pane open until dismissed.

Pane navigation:
- `Ctrl-b ←/→` - switch between panes
- `Ctrl-b x` - kill current pane
- `Ctrl-b z` - toggle zoom (fullscreen current pane)

### When to Use Codex

- Complex algorithmic analysis requiring deep reasoning
- Getting alternative implementation approaches
- Research tasks with web search (`--search`)
- Tasks benefiting from GPT's specific strengths
