---
description: Build and optionally run demos
allowed-tools: Bash(dotnet:*)
argument-hint: [demo-name]
---

Build the project and optionally run a demo.

For full details, see [agents/how-to-build-and-test.md](../../agents/how-to-build-and-test.md).

## Usage

### Build everything

```bash
dotnet restore && dotnet build
```

### Run a demo

If an argument is provided, run that demo:

```bash
dotnet run --project examples/$ARGUMENTS
```

Available demos:
- `Demo` - General demonstration
- `PromiseDemo` - Promise functionality
- `NpmPackageDemo` - NPM package loading

## Examples

Build only:
```
/build
```

Build and run Demo:
```
/build Demo
```

Build and run PromiseDemo:
```
/build PromiseDemo
```

## Important

Never use `--no-build` flag - always keep code compiled with latest changes.
