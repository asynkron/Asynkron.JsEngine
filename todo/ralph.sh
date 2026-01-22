#!/bin/bash
# ralph.sh - Diagnose failing tests using Codex
# Processes all .md files in todo/, continues on error, console output only
# Skips files already marked with "** DONE **"

REPO_ROOT="$HOME/git/asynkron/Asynkron.JsEngine-failing-tests"
TODO_DIR="$REPO_ROOT/todo"
FAILED=0
PROCESSED=0
SKIPPED=0

for file in "$TODO_DIR"/*.md; do
    # Skip non-files
    [[ -f "$file" ]] || continue

    filename=$(basename "$file")

    # Check if already done
    if grep -q '\*\* DONE \*\*' "$file"; then
        echo "SKIPPED (already done): $filename"
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
# Failing Test Diagnosis: $filename

Diagnose why ECMAScript Test262 tests are failing for method \`$method\`.

## Steps

1. Read \`todo/$filename\` to see the failing test list
2. Run these specific tests only:
   \`\`\`bash
   dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=$method" -v n
   \`\`\`
3. Analyze the output - identify:
   - The actual error messages/exceptions
   - What ECMAScript behavior is broken or missing
   - Common pattern across failures
4. Append your diagnosis to \`todo/$filename\`:
   \`\`\`markdown

   ---
   ## Diagnosis ($today)

   **Summary:** [One-line root cause]

   **Error Pattern:**
   \`\`\`
   [Paste key error messages here]
   \`\`\`

   **Analysis:**
   [Why tests fail, what spec behavior is wrong/missing]

   **Fix Direction:**
   [What code changes would fix this]
   \`\`\`
5. Commit and push:
   \`\`\`bash
   git add "todo/$filename"
   git commit -m "Diagnose: $method"
   git push
   \`\`\`

## Rules
- Do NOT modify .cs files - diagnosis only
- Do NOT run other tests
- Do NOT create a PR
- Exit when done
EOF
)" --dangerously-bypass-approvals-and-sandbox; then
        # Success - mark as done
        echo "" >> "$file"
        echo "** DONE **" >> "$file"
        git add "$file"
        git commit -m "Mark done: $method"
        git push
        echo "SUCCESS: $filename (marked done)"
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
