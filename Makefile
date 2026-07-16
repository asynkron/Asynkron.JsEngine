.PHONY: help quality quality-evidence-contract build-internal test-internal test-internal-no-build slo-gate

CONFIGURATION ?= Debug
DOTNET_BUILD_ARGS ?= /p:RunAnalyzers=false
DOTNET_BUILD_STABILITY_ARGS ?= /m:1 /nr:false
DOTNET_TEST_ARGS ?= --logger "console;verbosity=minimal"
XUNIT_ARGS ?= xUnit.MaxParallelThreads=1 -timeout 20000
DOTNET ?= dotnet
GIT ?= git
PYTHON ?= python3

help:
	@printf "%s\n" "Available targets:" \
		"  help                   Show available repo maintenance targets" \
		"  quality                Check diff, build internal projects, run internal tests (no Test262)" \
		"  quality-evidence-contract Validate Faktorial quality evidence adapter contract" \
		"  slo-gate               Check SLO timing baseline and directional target status" \
		"  build-internal         Build internal projects used by the quality gate" \
		"  test-internal          Run internal tests with build" \
		"  test-internal-no-build Run internal tests without rebuilding" \
		"" \
		"Variable overrides:" \
		"  CONFIGURATION=Debug|Release          Default: Debug" \
		"  DOTNET=dotnet                        Dotnet executable override" \
		"  GIT=git                              Git executable override" \
		"  DOTNET_BUILD_ARGS='<args>'           Extra args passed to dotnet build commands" \
		"  DOTNET_BUILD_STABILITY_ARGS='<args>' Extra build stability args (default: /m:1 /nr:false)" \
		"  DOTNET_TEST_ARGS='<args>'            Args passed to dotnet test commands (default: --logger \"console;verbosity=minimal\")" \
		"  XUNIT_ARGS='<args>'                  xUnit args after '--' (default: xUnit.MaxParallelThreads=1 -timeout 20000)" \
		"  PYTHON=python3                       Python executable override"

quality:
	$(GIT) diff --check
	$(MAKE) build-internal
	$(MAKE) test-internal-no-build

quality-evidence-contract:
	$(PYTHON) -m unittest tools/faktorial_quality_evidence_test.py

slo-gate:
	./tools/check-slo-gate

build-internal:
	$(DOTNET) build tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) $(DOTNET_BUILD_ARGS) $(DOTNET_BUILD_STABILITY_ARGS)

test-internal:
	$(DOTNET) test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) $(DOTNET_TEST_ARGS) -- $(XUNIT_ARGS)

test-internal-no-build:
	$(DOTNET) test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) --no-build $(DOTNET_TEST_ARGS) -- $(XUNIT_ARGS)
