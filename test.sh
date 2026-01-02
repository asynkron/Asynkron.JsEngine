#!/usr/bin/env bash
set -uo pipefail

FAILED=0

echo "=== Running internal tests ==="
dotnet test tests/Asynkron.JsEngine.Tests || FAILED=1

echo ""
echo "=== Running Test262 ForOf tests ==="
dotnet test tests/Asynkron.JsEngine.Tests.Test262 --filter "FullyQualifiedName~forOf" || FAILED=1

exit $FAILED
