#!/usr/bin/env python3

import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "tools" / "faktorial_quality_evidence.py"
SPEC = importlib.util.spec_from_file_location("faktorial_quality_evidence", MODULE_PATH)
assert SPEC is not None
faktorial_quality_evidence = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = faktorial_quality_evidence
SPEC.loader.exec_module(faktorial_quality_evidence)


SAMPLE_TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="exec-pass" testId="test-pass" testName="ExampleTests.Passes" outcome="Passed" />
    <UnitTestResult executionId="exec-fail" testId="test-fail" testName="ExampleTests.Fails" outcome="Failed">
      <Output>
        <ErrorInfo>
          <Message>expected true</Message>
          <StackTrace>at ExampleTests.Fails()</StackTrace>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
    <UnitTestResult executionId="exec-skip" testId="test-skip" testName="ExampleTests.Skips" outcome="NotExecuted" />
  </Results>
  <TestDefinitions>
    <UnitTest name="ExampleTests.Passes" storage="tests/ExampleTests.cs" id="test-pass">
      <Execution id="exec-pass" />
      <TestMethod className="ExampleTests" name="Passes" />
    </UnitTest>
    <UnitTest name="ExampleTests.Fails" storage="tests/ExampleTests.cs" id="test-fail">
      <Execution id="exec-fail" />
      <TestMethod className="ExampleTests" name="Fails" />
    </UnitTest>
    <UnitTest name="ExampleTests.Skips" storage="tests/ExampleTests.cs" id="test-skip">
      <Execution id="exec-skip" />
      <TestMethod className="ExampleTests" name="Skips" />
    </UnitTest>
  </TestDefinitions>
  <ResultSummary outcome="Failed">
    <Counters total="3" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" />
  </ResultSummary>
</TestRun>
"""


class FaktorialQualityEvidenceTests(unittest.TestCase):
    def test_main_verify_declares_single_internal_producer(self) -> None:
        config = json.loads((ROOT / ".faktorial" / "main-verify.json").read_text(encoding="utf-8"))

        self.assertEqual(config["schema_version"], "main-verify.v2")
        self.assertEqual(config["commands"], ["./tools/faktorial-quality-evidence"])
        self.assertEqual(
            config["evidence"],
            [
                {
                    "command": "./tools/faktorial-quality-evidence",
                    "producer_id": "internal-tests",
                    "kind": "test-results",
                    "payload_schema": "test-results.v1",
                    "required": True,
                }
            ],
        )

    def test_parse_trx_preserves_counts_and_failed_identity(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            trx = Path(directory) / faktorial_quality_evidence.TRX_NAME
            trx.write_text(SAMPLE_TRX, encoding="utf-8")

            counts, failures, cases = faktorial_quality_evidence.parse_trx(trx)

        self.assertEqual(counts, {"total": 3, "passed": 1, "failed": 1, "skipped": 1})
        self.assertEqual(len(failures), 1)
        self.assertEqual(failures[0]["suite"], "ExampleTests")
        self.assertEqual(failures[0]["file"], "tests/ExampleTests.cs")
        self.assertEqual(failures[0]["test"], "ExampleTests.Fails")
        self.assertIn("expected true", failures[0]["message"])
        self.assertEqual(len(cases), 3)

    def test_worktree_storage_path_is_normalized_for_stable_identity(self) -> None:
        path = "/Users/example/repo/.faktorial/worktrees/fk5/tests/Asynkron.JsEngine.Tests/bin/Debug/net10.0/Asynkron.JsEngine.Tests.dll"

        self.assertEqual(
            faktorial_quality_evidence.stable_file_path(path),
            "tests/Asynkron.JsEngine.Tests/bin/Debug/net10.0/Asynkron.JsEngine.Tests.dll",
        )

    def test_writes_complete_envelope_and_matching_junit_native_file(self) -> None:
        counts = {"total": 1, "passed": 0, "failed": 1, "skipped": 0}
        failures = [{"suite": "ExampleTests", "file": "tests/ExampleTests.cs", "test": "ExampleTests.Fails", "message": "expected true"}]
        cases = [
            faktorial_quality_evidence.TestCaseResult(
                suite="ExampleTests",
                file="tests/ExampleTests.cs",
                test="ExampleTests.Fails",
                outcome="Failed",
                message="expected true",
            )
        ]
        with tempfile.TemporaryDirectory() as directory:
            evidence_dir = Path(directory)
            faktorial_quality_evidence.write_manifest(evidence_dir, started=True)
            faktorial_quality_evidence.write_complete(evidence_dir, counts, failures)
            faktorial_quality_evidence.write_junit(evidence_dir / faktorial_quality_evidence.JUNIT_NAME, counts, cases)

            envelope = json.loads((evidence_dir / faktorial_quality_evidence.ARTIFACT_NAME).read_text(encoding="utf-8"))
            manifest = json.loads((evidence_dir / "producers.json").read_text(encoding="utf-8"))
            junit = (evidence_dir / faktorial_quality_evidence.JUNIT_NAME).read_text(encoding="utf-8")

        self.assertEqual(envelope["schema_version"], "quality-evidence.v1")
        self.assertEqual(envelope["producer_id"], "internal-tests")
        self.assertEqual(envelope["payload_schema"], "test-results.v1")
        self.assertEqual(envelope["status"], "complete")
        self.assertEqual(envelope["payload"]["counts"], counts)
        self.assertEqual(envelope["payload"]["omitted_failures"], 0)
        self.assertEqual(manifest, {"started": ["internal-tests"], "categories": ["all"]})
        self.assertIn('<testsuite name="internal-tests" tests="1" failures="1"', junit)
        self.assertIn("<failure", junit)

    def test_unbalanced_trx_rejects_instead_of_guessing(self) -> None:
        bad = SAMPLE_TRX.replace('failed="1"', 'failed="2"', 1)
        with tempfile.TemporaryDirectory() as directory:
            trx = Path(directory) / faktorial_quality_evidence.TRX_NAME
            trx.write_text(bad, encoding="utf-8")

            with self.assertRaises(faktorial_quality_evidence.TranslationError):
                faktorial_quality_evidence.parse_trx(trx)

    def test_not_applicable_is_explicit_when_tests_do_not_run(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            evidence_dir = Path(directory)
            faktorial_quality_evidence.write_manifest(evidence_dir, started=False)
            faktorial_quality_evidence.write_not_applicable(evidence_dir, "build failed before tests")

            envelope = json.loads((evidence_dir / faktorial_quality_evidence.ARTIFACT_NAME).read_text(encoding="utf-8"))
            manifest = json.loads((evidence_dir / "producers.json").read_text(encoding="utf-8"))

        self.assertEqual(envelope["status"], "not_applicable")
        self.assertEqual(envelope["reason"], "build failed before tests")
        self.assertEqual(manifest, {"started": [], "categories": []})


if __name__ == "__main__":
    unittest.main()
