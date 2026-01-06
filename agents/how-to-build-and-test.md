# Build and Test

## Standard Commands
```bash
# Restore dependencies
dotnet restore

# Build everything
dotnet build

# Main test suite
dotnet test tests/Asynkron.JsEngine.Tests
```
Never use `--no-build`; keep code compiled with latest changes.

## Narrow Test Runs
```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SomeTestName"
```

## ECMAScript Test262 Suite

Run the full LanguageTests class (43,000+ tests):
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings
```

Run the BuiltInsTests class:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings
```

## Demos
```bash
dotnet run --project examples/Demo
dotnet run --project examples/PromiseDemo
dotnet run --project examples/NpmPackageDemo
```
