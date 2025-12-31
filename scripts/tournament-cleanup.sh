#!/bin/bash
# Tournament Cleanup Script
# Run this BEFORE merging a winner to ensure no rogue agents are still running

set -e

echo "=== Tournament Cleanup ==="
echo ""

# Check for any worktrees
echo "Current worktrees:"
git worktree list
echo ""

# Check for uncommitted changes in main
echo "Checking main for uncommitted changes..."
if ! git diff --quiet || ! git diff --staged --quiet; then
    echo "WARNING: Uncommitted changes detected in main!"
    echo ""
    git status --short
    echo ""
    read -p "Discard these changes? [y/N] " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        git restore --staged .
        git restore .
        git clean -fd
        echo "Changes discarded."
    else
        echo "Keeping changes. Be careful!"
    fi
else
    echo "Main is clean."
fi
echo ""

# Remove tournament worktrees
echo "Removing tournament worktrees..."
for wt in ../Asynkron.JsEngine-t{1,2,3,4}; do
    if [ -d "$wt" ]; then
        echo "  Removing $wt"
        git worktree remove "$wt" --force 2>/dev/null || true
    fi
done

# Clean up tournament branches
echo ""
echo "Cleaning up tournament branches..."
for branch in $(git branch | grep tournament); do
    echo "  Deleting $branch"
    git branch -D "$branch" 2>/dev/null || true
done

echo ""
echo "=== Cleanup Complete ==="
echo ""
echo "IMPORTANT: If agents are still running in Claude Code, use:"
echo "  /tasks     - to see running tasks"
echo "  Then ask Claude to stop them before proceeding"
