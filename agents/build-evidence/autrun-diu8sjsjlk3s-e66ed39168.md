# Build Evidence: autrun-diu8sjsjlk3s-e66ed39168

Baseline timestamp: 2026-05-28T10:45:00Z
Baseline signal: ModuleNamespace.cs line count = 393
Final timestamp: 2026-05-28T10:51:33Z
Final signal: ModuleNamespace.cs line count = 389
Signal delta: -4 lines (reduction)

- Slice check: only `src/Asynkron.JsEngine/Ast/Modules/ModuleNamespace.cs` changed in this PR slice; change is deletion-only in `SetPrototype` and preserves null-return/immutable-prototype throw behavior.
- Scope note: recurring-child run stayed inside the module namespace cleanup slice and did not widen into unrelated Roslynator findings.
- CLOC evidence: `cloc --git --diff origin/main HEAD --include-lang=C#` reports removed `3` code lines and `1` blank line.
- QuickDup (.cs) evidence: `quickdup -path src -ext .cs -top 20` completed successfully; no source edits required from duplicate scan.
- Roslynator discovery: `roslynator analyze src/Asynkron.JsEngine/Asynkron.JsEngine.csproj --output Roslynator.xml` ran and reported existing cross-repo diagnostics (226 total), not scoped to this cleanup slice.
- Focused module proof: prior review evidence confirms the targeted `ModuleTests` namespace/prototype tests passed for the three named module namespace checks.
