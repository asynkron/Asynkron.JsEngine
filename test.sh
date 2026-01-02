#!/usr/bin/env bash
set -euo pipefail

echo "=== Running Internal Tests ==="
dotnet test tests/Asynkron.JsEngine.Tests --no-restore --verbosity minimal

echo ""
echo "=== Running Test262 ForOf Tests ==="
dotnet test tests/Asynkron.JsEngine.Tests.Test262 --no-restore --verbosity minimal --filter "FullyQualifiedName~Statements_forOf"
