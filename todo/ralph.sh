#!/bin/bash
# ralph.sh - Fix failing tests using Codex
# Processes all .md files in todo/, continues on error, console output only
# Only processes files marked with "** DONE **" (analysis complete, ready to fix)

REPO_ROOT="$HOME/git/asynkron/Asynkron.JsEngine-failing-tests"
TODO_DIR="$REPO_ROOT/todo"
FAILED=0
PROCESSED=0
SKIPPED=0

for file in "$TODO_DIR"/*.md; do
    # Skip non-files
    [[ -f "$file" ]] || continue

    filename=$(basename "$file")

    # Skip files without diagnosis (not yet analyzed)
    if ! grep -q '\*\* DONE \*\*' "$file"; then
        echo "SKIPPED (not yet analyzed): $filename"
        ((SKIPPED++))
        continue
    fi

    echo ""
    echo "========================================="
    echo "Processing: $filename"
    echo "========================================="

    # Extract method name from filename (remove .md extension)
    method="${filename%.md}"
    today=$(date +%Y-%m-%d)

    if codex -C "$REPO_ROOT" exec "$(cat <<EOF
# Fix Failing Test: $filename

Fix the ECMAScript Test262 failing tests for method \`$method\`.

## Steps

1. Read \`todo/$filename\` to see the failing test list AND the existing diagnosis
   Note: The file is marked "** DONE **" which means ANALYSIS is complete, NOT the fix. Your job is to implement the fix.
2. Read the CLAUDE.md and the referenced how-to files to understand the codebase patterns
3. Run the failing tests to confirm they still fail:
   \`\`\`bash
   dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=$method" -v n
   \`\`\`
4. Based on the diagnosis "Fix Direction", locate and modify the relevant .cs files to fix the issue
5. Run the tests again to verify the fix works
6. If tests pass, commit and push:
   \`\`\`bash
   git add -A
   git commit -m "Fix: $method - [brief description of fix]"
   git push
   \`\`\`
7. If tests still fail, iterate on the fix until they pass

## Rules
- DO modify .cs files to fix the issue
- Follow the coding standards in agents/how-to-coding-standards.md
- Run tests after each change to verify progress
- Do NOT create a PR
- Exit when tests pass and changes are pushed
EOF
)" --dangerously-bypass-approvals-and-sandbox; then
        # Success - fix was applied
        echo "SUCCESS: $filename (fixed)"
    else
        echo "FAILED: $filename"
        ((FAILED++))
    fi

    ((PROCESSED++))
    echo "Progress: $PROCESSED processed, $SKIPPED skipped, $FAILED failed"
done

echo ""
echo "========================================="
echo "Complete: $PROCESSED processed, $SKIPPED skipped, $FAILED failed"
echo "========================================="
