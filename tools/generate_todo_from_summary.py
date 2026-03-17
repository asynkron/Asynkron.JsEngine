#!/usr/bin/env python3
"""
Wipe the /todo directory and regenerate todo files from the testrunner summary.

Each failing/crashed test method gets one markdown file with all its parameter variations listed.

Usage:
    python3 tools/generate_todo_from_summary.py [summary_file]

Default summary file: .testrunner/summary.md
Output directory: todo/
"""

import sys
import shutil
from collections import defaultdict
from pathlib import Path

PROJECT_ROOT = Path(__file__).parent.parent
DEFAULT_SUMMARY = PROJECT_ROOT / ".testrunner" / "summary.md"
TODO_DIR = PROJECT_ROOT / "todo"


def parse_summary(filepath):
    """Parse summary.md and extract test entries from Failed and Crashed sections."""
    tests = []
    current_section = None

    with open(filepath, "r") as f:
        for line in f:
            line = line.strip()

            if line.startswith("## Failed"):
                current_section = "failed"
                continue
            elif line.startswith("## Skipped"):
                current_section = "skipped"
                continue
            elif line.startswith("## Crashed"):
                current_section = "crashed"
                continue
            elif line.startswith("## "):
                current_section = None
                continue

            if current_section in ("failed", "crashed"):
                if line.startswith("- "):
                    test_name = line[2:]
                    tests.append((test_name, current_section))

    return tests


def extract_method_info(full_test_name):
    """Extract method name and FQN from a full test name."""
    paren_idx = full_test_name.find("(")
    if paren_idx == -1:
        return None, None

    fqn = full_test_name[:paren_idx]
    last_dot_idx = fqn.rfind(".")
    method_name = fqn[last_dot_idx + 1:] if last_dot_idx != -1 else fqn

    return method_name, fqn


def group_by_method(tests):
    """Group tests by method name."""
    grouped = defaultdict(lambda: {"fqn": None, "tests": [], "sections": set()})

    for full_test_name, section in tests:
        method_name, fqn = extract_method_info(full_test_name)
        if method_name is None:
            continue

        grouped[method_name]["fqn"] = fqn
        grouped[method_name]["tests"].append(full_test_name)
        grouped[method_name]["sections"].add(section)

    return grouped


def generate_markdown(method_name, data):
    """Generate markdown content for a method."""
    lines = [
        f"# {method_name}",
        "",
        "FQN:",
        f"`{data['fqn']}`",
        "",
        "Full test name:",
        "",
    ]

    for test in sorted(data["tests"]):
        lines.append(f"- {test}")

    lines.append("")
    return "\n".join(lines)


def main():
    summary_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SUMMARY

    if not summary_path.exists():
        print(f"Error: {summary_path} not found")
        sys.exit(1)

    # Parse summary
    print(f"Parsing {summary_path}...")
    tests = parse_summary(summary_path)
    print(f"  Found {len(tests)} failing/crashed test entries")

    # Group by method
    grouped = group_by_method(tests)
    print(f"  Found {len(grouped)} unique methods")

    # Wipe todo directory
    if TODO_DIR.exists():
        old_count = len(list(TODO_DIR.glob("*.md")))
        shutil.rmtree(TODO_DIR)
        print(f"  Wiped {old_count} old todo files")

    TODO_DIR.mkdir(parents=True, exist_ok=True)

    # Write new todo files
    for method_name, data in sorted(grouped.items()):
        content = generate_markdown(method_name, data)
        output_path = TODO_DIR / f"{method_name}.md"
        with open(output_path, "w") as f:
            f.write(content)

    print(f"  Created {len(grouped)} todo files in {TODO_DIR}")

    # Print stats
    failed_count = sum(1 for _, s in tests if s == "failed")
    crashed_count = sum(1 for _, s in tests if s == "crashed")
    print(f"\nSummary:")
    print(f"  Failed:  {failed_count}")
    print(f"  Crashed: {crashed_count}")
    print(f"  Total:   {len(tests)}")
    print(f"  Methods: {len(grouped)}")


if __name__ == "__main__":
    main()
