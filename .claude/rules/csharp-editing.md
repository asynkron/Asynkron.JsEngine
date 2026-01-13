# C# File Editing Rules

**MANDATORY: After editing ANY C# file, run diagnostics and fix issues.**

## Required Steps After Editing C# Files

### 1. Run Rider Diagnostics
After editing any `.cs` file, immediately run:
```
mcp__rider__get_file_problems with the file path
```

### 2. Address All Issues
- Review all errors and warnings returned
- Fix any problems found
- Re-run diagnostics to confirm resolution
- **Do NOT leave errors or warnings unaddressed**

### 3. Iterate Until Clean
- If fixes introduce new issues, repeat steps 1-2
- Continue until diagnostics return no problems

## Rules

- Do NOT skip diagnostics check after editing C# files
- Do NOT ignore errors or warnings
- Do NOT move on to other tasks while issues remain
- Fix issues immediately while the context is fresh
- If unsure how to fix an issue, ask the user

## Example Workflow

1. Edit `src/Foo.cs` to add new method
2. Run `mcp__rider__get_file_problems` for `src/Foo.cs`
3. See warning about missing XML documentation
4. Add documentation comment
5. Re-run diagnostics
6. Confirm no issues remain
7. Proceed with next task
