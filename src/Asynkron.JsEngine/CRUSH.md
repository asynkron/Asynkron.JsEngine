CRUSH.md – Fast reference for agents working in this repo

Project type
- .NET SDK-style C# library targeting net10.0. Nullable enabled, implicit usings on, unsafe allowed. Roslyn source generators referenced via Analyzer.

Build / restore / pack
- Restore: dotnet restore
- Build (Debug): dotnet build -c Debug
- Build (Release): dotnet build -c Release
- Pack (Release NuGet): dotnet pack -c Release

Test & single-test
- Run all tests: dotnet test -c Debug
- Filter by class: dotnet test -c Debug --filter FullyQualifiedName~Namespace.ClassName
- Filter by single test: dotnet test -c Debug --filter "FullyQualifiedName=Namespace.ClassName.MethodName"
- Repeat until failure: dotnet test -c Debug -- --blame-crash

Lint / analyzers / formatting
- Analyzers: EnableNETAnalyzers is disabled in csproj; rely on IDE or add .editorconfig/StyleCop if needed.
- Format C#: dotnet format
- Code cleanup before commits: dotnet format whitespace; dotnet format style; dotnet format analyzers

Debug & run samples
- This project is a library; to run code, add or use an existing test project or a console host in the solution root.

Code style guidelines
- Imports/usings: use implicit global usings; prefer file-scoped namespaces; order: System.*, then third-party, then internal.
- Formatting: 4-space indents; brace on new line; avoid trailing whitespace; keep lines <= 120 chars.
- Types/nullability: nullable enabled; avoid ! operator; prefer Try- methods; return ReadOnlySpan/Span when perf-critical.
- Naming: PascalCase for public types/members; camelCase for locals/parameters; _camelCase for private fields; suffix Async for async.
- Error handling: never swallow exceptions; throw specific exceptions; use ArgumentException/ArgumentNullException guards; no exceptions for expected control flow.
- Performance: avoid allocations on hot paths; use pooled arrays and Span<T>; prefer readonly struct where appropriate; use ArrayPool/object pools provided in repo.
- Threading/async: use ValueTask where appropriate; avoid async void; schedule microtasks via PromiseMicrotasks when bridging JS promises.
- API design: keep public surface minimal; internal where possible; avoid exposing mutable collections.

Repository conventions
- No Cursor or Copilot rule files found. If added later (.cursor/rules/, .cursorrules, .github/copilot-instructions.md), mirror key constraints here.
- Do not commit secrets; do not add comments with keys. Keep CR/LF consistent (LF).
