#!/usr/bin/env bash
set -euo pipefail

# Run targeted Test262 tests for the failing categories related to slot stamping
# Categories: ArgumentsObject, BlockScope_shadowing, Comments_hashbang, Destructuring_binding, EvalCode_direct

echo "Running targeted Test262 tests for slot stamping failures..."

dotnet test tests/Asynkron.JsEngine.Tests.Test262 \
  --filter "FullyQualifiedName~ArgumentsObject|FullyQualifiedName~BlockScope_shadowing|FullyQualifiedName~Comments_hashbang|FullyQualifiedName~Destructuring_binding|FullyQualifiedName~EvalCode_direct" \
  -- xUnit.MaxParallelThreads=1

echo "Test run complete."
