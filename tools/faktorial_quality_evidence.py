#!/usr/bin/env python3
"""Repository-owned Faktorial quality evidence adapter."""

from __future__ import annotations

from dataclasses import dataclass
import json
import os
import posixpath
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
TEST_PROJECT = ROOT / "tests" / "Asynkron.JsEngine.Tests" / "Asynkron.JsEngine.Tests.csproj"

PRODUCER_ID = "internal-tests"
PRODUCER_CATEGORY = "internal"
ARTIFACT_NAME = f"{PRODUCER_ID}.quality-evidence.json"
JUNIT_NAME = "internal.junit.xml"
TRX_NAME = "internal-tests.trx"


class TranslationError(Exception):
    """Raised when native test output cannot be translated exactly."""


@dataclass(frozen=True)
class TestCaseResult:
    suite: str
    file: str
    test: str
    outcome: str
    message: str = ""


def main() -> int:
    evidence_dir_value = os.environ.get("FAKTORIAL_QUALITY_EVIDENCE_DIR", "").strip()
    if not evidence_dir_value:
        print("quality-evidence: FAKTORIAL_QUALITY_EVIDENCE_DIR is required", file=sys.stderr)
        return 2

    evidence_dir = Path(evidence_dir_value)
    evidence_dir.mkdir(parents=True, exist_ok=True)

    diff_code = run_command([env_or_default("GIT", "git"), "diff", "--check"])
    if diff_code != 0:
        write_manifest(evidence_dir, started=False)
        write_not_applicable(evidence_dir, "internal tests did not run because git diff --check failed")
        return diff_code

    build_code = run_command(build_command())
    if build_code != 0:
        write_manifest(evidence_dir, started=False)
        write_not_applicable(evidence_dir, "internal tests did not run because the internal build failed")
        return build_code

    write_manifest(evidence_dir, started=True)
    test_code = run_command(test_command(evidence_dir))
    trx_path = evidence_dir / TRX_NAME
    try:
        counts, failures, cases = parse_trx(trx_path)
        write_complete(evidence_dir, counts, failures)
        write_junit(evidence_dir / JUNIT_NAME, counts, cases)
    except TranslationError as error:
        write_translation_failed(evidence_dir, str(error))
        if test_code == 0:
            return 2

    return test_code


def build_command() -> list[str]:
    return [
        resolve_dotnet(),
        "build",
        str(TEST_PROJECT),
        "-c",
        env_or_default("CONFIGURATION", "Debug"),
        *split_args("DOTNET_BUILD_ARGS", "/p:RunAnalyzers=false"),
        *split_args("DOTNET_BUILD_STABILITY_ARGS", "/m:1 /nr:false"),
    ]


def test_command(evidence_dir: Path) -> list[str]:
    return [
        resolve_dotnet(),
        "test",
        str(TEST_PROJECT),
        "-c",
        env_or_default("CONFIGURATION", "Debug"),
        "--no-build",
        *split_args("DOTNET_TEST_ARGS", '--logger "console;verbosity=minimal"'),
        "--logger",
        f"trx;LogFileName={TRX_NAME}",
        "--results-directory",
        str(evidence_dir),
        "--",
        *split_args("XUNIT_ARGS", "xUnit.MaxParallelThreads=1 -timeout 20000"),
    ]


def env_or_default(name: str, default: str) -> str:
    value = os.environ.get(name)
    if value is None or value.strip() == "":
        return default
    return value


def split_args(name: str, default: str) -> list[str]:
    value = env_or_default(name, default)
    try:
        return shlex.split(value)
    except ValueError as error:
        raise SystemExit(f"quality-evidence: could not parse {name}: {error}") from error


def resolve_dotnet() -> str:
    configured = os.environ.get("DOTNET", "").strip()
    if configured:
        return configured
    discovered = shutil.which("dotnet")
    if discovered:
        return discovered
    for candidate in (
        "/usr/local/share/dotnet/dotnet",
        "/usr/local/share/dotnet/x64/dotnet",
        "/opt/homebrew/bin/dotnet",
    ):
        if Path(candidate).exists():
            return candidate
    return "dotnet"


def run_command(command: list[str]) -> int:
    print("+ " + shlex.join(command), flush=True)
    try:
        completed = subprocess.run(command, cwd=ROOT, check=False)
    except FileNotFoundError as error:
        print(f"quality-evidence: could not execute {command[0]}: {error}", file=sys.stderr)
        return 127
    return completed.returncode


def parse_trx(path: Path) -> tuple[dict[str, int], list[dict[str, str]], list[TestCaseResult]]:
    if not path.exists():
        raise TranslationError(f"dotnet test did not produce {TRX_NAME}")
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as error:
        raise TranslationError(f"{TRX_NAME} is not valid XML: {error}") from error

    counters = first_descendant(root, "Counters")
    if counters is None:
        raise TranslationError(f"{TRX_NAME} has no ResultSummary/Counters element")

    units = unit_tests_by_id(root)
    failures: list[dict[str, str]] = []
    cases: list[TestCaseResult] = []
    for result in descendants(root, "UnitTestResult"):
        case = case_from_result(result, units)
        cases.append(case)
        if is_failed(case.outcome):
            failure = {"suite": case.suite, "file": case.file, "test": case.test}
            if case.message:
                failure["message"] = case.message
            failures.append(failure)

    passed = sum(1 for case in cases if is_passed(case.outcome))
    failed = len(failures)
    skipped = sum(1 for case in cases if is_skipped(case.outcome))
    unknown = [case.outcome for case in cases if not is_passed(case.outcome) and not is_failed(case.outcome) and not is_skipped(case.outcome)]
    if unknown:
        raise TranslationError(f"{TRX_NAME} contains unsupported test outcomes: {', '.join(sorted(set(unknown)))}")

    total = len(cases)
    counter_total = read_int(counters, "total")
    counter_passed = read_int(counters, "passed")
    counter_failed = sum(read_int(counters, name) for name in ("failed", "error", "timeout", "aborted", "notRunnable"))
    if (counter_total, counter_passed, counter_failed) != (total, passed, failed):
        raise TranslationError(
            f"{TRX_NAME} counters do not match result records: "
            f"counters total={counter_total}, passed={counter_passed}, failed={counter_failed}; "
            f"records total={total}, passed={passed}, failed={failed}"
        )

    if failed != len(failures):
        raise TranslationError(
            f"{TRX_NAME} failed count {failed} does not match {len(failures)} stable failure identities"
        )

    counts = {"total": total, "passed": passed, "failed": failed, "skipped": skipped}
    return counts, failures, cases


def unit_tests_by_id(root: ET.Element) -> dict[str, dict[str, str]]:
    units: dict[str, dict[str, str]] = {}
    for unit in descendants(root, "UnitTest"):
        method = first_child(unit, "TestMethod")
        execution = first_child(unit, "Execution")
        metadata = {
            "suite": attr(method, "className") if method is not None else "",
            "test": attr(method, "name") if method is not None else attr(unit, "name"),
            "file": stable_file_path(attr(unit, "storage")),
        }
        unit_id = attr(unit, "id")
        execution_id = attr(execution, "id") if execution is not None else ""
        if unit_id:
            units[unit_id] = metadata
        if execution_id:
            units[execution_id] = metadata
    return units


def case_from_result(result: ET.Element, units: dict[str, dict[str, str]]) -> TestCaseResult:
    metadata = units.get(attr(result, "executionId")) or units.get(attr(result, "testId")) or {}
    test = attr(result, "testName") or metadata.get("test", "")
    suite = metadata.get("suite", "")
    if not suite and "." in test:
        suite = test.rsplit(".", 1)[0]
    if not suite:
        suite = PRODUCER_ID
    return TestCaseResult(
        suite=suite,
        file=metadata.get("file", ""),
        test=test or metadata.get("test", "") or "unknown test",
        outcome=attr(result, "outcome"),
        message=bounded_text(error_message(result)),
    )


def write_complete(evidence_dir: Path, counts: dict[str, int], failures: list[dict[str, str]]) -> None:
    write_envelope(
        evidence_dir,
        {
            "schema_version": "quality-evidence.v1",
            "producer_id": PRODUCER_ID,
            "kind": "test-results",
            "payload_schema": "test-results.v1",
            "status": "complete",
            "payload": {
                "counts": counts,
                "failures": failures,
                "omitted_failures": 0,
            },
        },
    )


def write_not_applicable(evidence_dir: Path, reason: str) -> None:
    write_envelope(
        evidence_dir,
        {
            "schema_version": "quality-evidence.v1",
            "producer_id": PRODUCER_ID,
            "kind": "test-results",
            "payload_schema": "test-results.v1",
            "status": "not_applicable",
            "reason": reason,
        },
    )


def write_translation_failed(evidence_dir: Path, reason: str) -> None:
    write_envelope(
        evidence_dir,
        {
            "schema_version": "quality-evidence.v1",
            "producer_id": PRODUCER_ID,
            "kind": "test-results",
            "payload_schema": "test-results.v1",
            "status": "translation_failed",
            "reason": reason,
        },
    )


def write_envelope(evidence_dir: Path, envelope: dict[str, object]) -> None:
    atomic_json(evidence_dir / ARTIFACT_NAME, envelope)


def write_manifest(evidence_dir: Path, *, started: bool) -> None:
    manifest = {
        "started": [PRODUCER_ID] if started else [],
        "categories": ["all"] if started else [],
    }
    atomic_json(evidence_dir / "producers.json", manifest)


def write_junit(path: Path, counts: dict[str, int], cases: list[TestCaseResult]) -> None:
    root = ET.Element(
        "testsuites",
        {
            "tests": str(counts["total"]),
            "failures": str(counts["failed"]),
            "errors": "0",
            "skipped": str(counts["skipped"]),
        },
    )
    suite = ET.SubElement(
        root,
        "testsuite",
        {
            "name": PRODUCER_ID,
            "tests": str(counts["total"]),
            "failures": str(counts["failed"]),
            "errors": "0",
            "skipped": str(counts["skipped"]),
        },
    )
    for case in cases:
        testcase = ET.SubElement(suite, "testcase", {"classname": case.suite, "name": case.test})
        if case.file:
            testcase.set("file", case.file)
        if is_failed(case.outcome):
            failure = ET.SubElement(testcase, "failure", {"message": case.message or "test failed"})
            failure.text = case.message
        elif is_skipped(case.outcome):
            ET.SubElement(testcase, "skipped")

    atomic_xml(path, root)


def atomic_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    data = json.dumps(value, indent=2, sort_keys=True) + "\n"
    atomic_write(path, data.encode("utf-8"))


def atomic_xml(path: Path, root: ET.Element) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("wb", delete=False, dir=path.parent, prefix=f".{path.name}.", suffix=".tmp") as tmp:
        tmp_path = Path(tmp.name)
        tmp.write(b'<?xml version="1.0" encoding="utf-8"?>\n')
        ET.ElementTree(root).write(tmp, encoding="utf-8", xml_declaration=False)
        tmp.write(b"\n")
    os.chmod(tmp_path, 0o600)
    os.replace(tmp_path, path)


def atomic_write(path: Path, data: bytes) -> None:
    with tempfile.NamedTemporaryFile("wb", delete=False, dir=path.parent, prefix=f".{path.name}.", suffix=".tmp") as tmp:
        tmp_path = Path(tmp.name)
        tmp.write(data)
    os.chmod(tmp_path, 0o600)
    os.replace(tmp_path, path)


def descendants(root: ET.Element, name: str) -> list[ET.Element]:
    return [element for element in root.iter() if local_name(element.tag) == name]


def first_descendant(root: ET.Element, name: str) -> ET.Element | None:
    for element in root.iter():
        if local_name(element.tag) == name:
            return element
    return None


def first_child(root: ET.Element, name: str) -> ET.Element | None:
    for child in root:
        if local_name(child.tag) == name:
            return child
    return None


def local_name(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[1]
    return tag


def attr(element: ET.Element | None, name: str) -> str:
    if element is None:
        return ""
    return element.attrib.get(name, "").strip()


def read_int(element: ET.Element, name: str) -> int:
    value = element.attrib.get(name, "0").strip()
    try:
        return int(value)
    except ValueError as error:
        raise TranslationError(f"{TRX_NAME} counter {name} is not an integer: {value!r}") from error


def error_message(result: ET.Element) -> str:
    parts: list[str] = []
    for element in result.iter():
        if local_name(element.tag) in {"Message", "StackTrace"} and element.text:
            parts.append(element.text.strip())
    return "\n".join(part for part in parts if part)


def bounded_text(value: str, limit: int = 4000) -> str:
    normalized = " ".join(value.replace("\r", "\n").split())
    if len(normalized) <= limit:
        return normalized
    return normalized[: limit - 3] + "..."


def stable_file_path(value: str) -> str:
    if not value:
        return ""
    normalized = value.replace("\\", "/").strip()
    lower = normalized.lower()

    worktree_marker = "/.faktorial/worktrees/"
    worktree_index = lower.find(worktree_marker)
    if worktree_index >= 0:
        after_marker = normalized[worktree_index + len(worktree_marker) :]
        slash_index = after_marker.find("/")
        if slash_index >= 0:
            return clean_relative_path(after_marker[slash_index + 1 :])

    root = ROOT.as_posix()
    if lower.startswith(root.lower() + "/"):
        return clean_relative_path(normalized[len(root) + 1 :])

    for anchor in ("/tests/", "/src/"):
        anchor_index = lower.rfind(anchor)
        if anchor_index >= 0:
            return clean_relative_path(normalized[anchor_index + 1 :])

    if normalized.startswith("/"):
        return posixpath.basename(normalized.rstrip("/"))

    return clean_relative_path(normalized)


def clean_relative_path(value: str) -> str:
    normalized = posixpath.normpath(value.replace("\\", "/").strip())
    if normalized == ".":
        return ""
    while normalized.startswith("../"):
        normalized = normalized[3:]
    return normalized.lstrip("/")


def is_failed(outcome: str) -> bool:
    return outcome.strip().lower() in {"failed", "error", "timeout", "aborted", "notrunnable"}


def is_passed(outcome: str) -> bool:
    return outcome.strip().lower() == "passed"


def is_skipped(outcome: str) -> bool:
    return outcome.strip().lower() in {"notexecuted", "skipped"}


if __name__ == "__main__":
    raise SystemExit(main())
