#!/usr/bin/env python3
"""
Generate a pass/total summary for Test262 groups listed in 262tests.md.

Usage:
    python3 tools/generate_test262_report.py [--groups 262tests.md] [--results-dir /tmp/jsengine-results]

The script:
  - Extracts group names from the given markdown (lines with group names, with or without pass/total prefixes or a leading ✅).
  - Runs each group separately via `dotnet test --filter FullyQualifiedName=Asynkron.JsEngine.Tests.Test262.BuiltInsTests.<Group>`.
  - Writes TRX files per group into --results-dir (default: /tmp/jsengine-results).
  - Prints a summary `passed/total GroupName` for easy copy/paste back into 262tests.md.
"""

import argparse
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEST_PROJ = os.path.join(
    ROOT, "tests", "Asynkron.JsEngine.Tests.Test262", "Asynkron.JsEngine.Tests.Test262.csproj"
)


GROUP_LINE = re.compile(r"^(?:✅\s*)?(?:\d+/\d+\s+)?([A-Za-z0-9_.-]+)$")


def parse_groups(path: str) -> list[str]:
    groups = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            m = GROUP_LINE.match(line)
            if m:
                groups.append(m.group(1))
    return groups


def run_group(group: str, results_dir: str) -> tuple[str, int, int, int]:
    os.makedirs(results_dir, exist_ok=True)
    trx_name = f"{group}.trx"
    trx_path = os.path.join(results_dir, trx_name)
    cmd = [
        "dotnet",
        "test",
        TEST_PROJ,
        "--filter",
        f"FullyQualifiedName=Asynkron.JsEngine.Tests.Test262.BuiltInsTests.{group}",
        "--logger",
        f"trx;LogFileName={trx_name}",
        "--results-directory",
        results_dir,
    ]
    try:
        subprocess.run(cmd, check=True, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    except subprocess.CalledProcessError:
        # Even on failure a TRX may exist; parsing will reveal the counts.
        pass

    passed = failed = skipped = 0
    if os.path.exists(trx_path):
        try:
            root = ET.parse(trx_path).getroot()
            ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
            summary = root.find(".//t:ResultSummary/t:Counters", ns)
            if summary is not None:
                passed = int(summary.attrib.get("passed", "0"))
                failed = int(summary.attrib.get("failed", "0"))
                skipped = int(summary.attrib.get("notExecuted", "0"))
        except ET.ParseError:
            pass

    total = passed + failed + skipped
    return group, passed, failed, total


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate Test262 group pass/total report.")
    parser.add_argument("--groups", default=os.path.join(ROOT, "262tests.md"), help="Path to group list markdown.")
    parser.add_argument("--results-dir", default="/tmp/jsengine-results", help="Directory for TRX outputs.")
    args = parser.parse_args()

    groups = parse_groups(args.groups)
    if not groups:
        print("No groups found to run.", file=sys.stderr)
        return 1

    summaries = []
    total_groups = len(groups)
    for idx, group in enumerate(groups, 1):
        print(f"[{idx}/{total_groups}] Running {group}...", file=sys.stderr, flush=True)
        name, passed, failed, total = run_group(group, args.results_dir)
        if failed == 0 and passed > 0 and passed == total:
            summary = f"✅ {name}"
        else:
            summary = f"{passed}/{total} {name}"
        summaries.append(summary)
        print(summary)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
