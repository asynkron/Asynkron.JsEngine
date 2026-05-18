.PHONY: quality build-internal test-internal test-internal-no-build

CONFIGURATION ?= Debug
DOTNET_TEST_ARGS ?=
XUNIT_ARGS ?= xUnit.MaxParallelThreads=1 -timeout 20000

quality:
	rtk git diff --check
	$(MAKE) build-internal
	$(MAKE) test-internal-no-build

build-internal:
	rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c $(CONFIGURATION)
	rtk dotnet build src/Asynkron.JsEngine.Generators/Asynkron.JsEngine.Generators.csproj -c $(CONFIGURATION)
	rtk dotnet build tests/Asynkron.JsEngine.Tests.Helpers/Asynkron.JsEngine.Tests.Helpers.csproj -c $(CONFIGURATION)
	rtk dotnet build tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION)

test-internal:
	rtk proxy dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) $(DOTNET_TEST_ARGS) -- $(XUNIT_ARGS)

test-internal-no-build:
	rtk proxy dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c $(CONFIGURATION) --no-build $(DOTNET_TEST_ARGS) -- $(XUNIT_ARGS)
