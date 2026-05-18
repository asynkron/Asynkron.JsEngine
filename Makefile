.PHONY: quality build-internal test-internal test-internal-no-build

CONFIGURATION ?= Debug
DOTNET_TEST_ARGS ?=
XUNIT_ARGS ?= xUnit.MaxParallelThreads=1 -timeout 20000
DOTNET ?= dotnet
GIT ?= git

quality:
	$(GIT) diff --check
	$(MAKE) build-internal
	$(MAKE) test-internal-no-build

build-internal:
	$(DOTNET) build tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION)

test-internal:
	$(DOTNET) test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) $(DOTNET_TEST_ARGS) -- $(XUNIT_ARGS)

test-internal-no-build:
	$(DOTNET) test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) --no-build $(DOTNET_TEST_ARGS) -- $(XUNIT_ARGS)
