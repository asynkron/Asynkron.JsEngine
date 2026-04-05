#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import urlparse


DEFAULT_MAX_COMMENT_CHARS = 12000
DEFAULT_MAX_BODY_CHARS = 12000
DEFAULT_MAX_ITERATIONS = 20


@dataclass(frozen=True)
class IssueContext:
    url: str
    repo: str
    number: int
    title: str
    body: str
    state: str
    comments: list[dict[str, Any]]


RESULT_SCHEMA: dict[str, Any] = {
    "type": "object",
    "additionalProperties": False,
    "properties": {
        "outcome": {
            "type": "string",
            "enum": ["completed_step", "done", "blocked"],
        },
        "summary": {"type": "string"},
        "progress_percent": {"type": "integer", "minimum": 0, "maximum": 100},
        "proof_summary": {
            "type": "array",
            "items": {"type": "string"},
        },
        "issue_updated": {"type": "boolean"},
        "commit": {
            "anyOf": [
                {"type": "string"},
                {"type": "null"},
            ]
        },
        "next_hint": {"type": "string"},
    },
    "required": [
        "outcome",
        "summary",
        "progress_percent",
        "proof_summary",
        "issue_updated",
        "commit",
        "next_hint",
    ],
}


def run(
    args: list[str],
    *,
    cwd: Path,
    capture_output: bool = True,
    check: bool = True,
    input_text: str | None = None,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=str(cwd),
        text=True,
        input=input_text,
        capture_output=capture_output,
        check=check,
    )


def issue_url_to_repo_and_number(issue_url: str) -> tuple[str, int]:
    parsed = urlparse(issue_url)
    if parsed.scheme not in {"https", "http"} or parsed.netloc not in {
        "github.com",
        "www.github.com",
    }:
        raise ValueError(f"Unsupported GitHub issue URL: {issue_url}")

    parts = [part for part in parsed.path.split("/") if part]
    if len(parts) < 4 or parts[2] != "issues":
        raise ValueError(f"Expected URL like https://github.com/owner/repo/issues/123, got: {issue_url}")

    owner = parts[0]
    repo = parts[1]
    try:
        number = int(parts[3])
    except ValueError as exc:
        raise ValueError(f"Could not parse issue number from URL: {issue_url}") from exc

    return f"{owner}/{repo}", number


def ensure_cli_available(cwd: Path, cli: str, version_args: list[str]) -> None:
    try:
        run([cli, *version_args], cwd=cwd, capture_output=True, check=True)
    except (FileNotFoundError, subprocess.CalledProcessError) as exc:
        raise RuntimeError(f"Required CLI '{cli}' is not available or failed to run") from exc


def ensure_gh_auth(cwd: Path) -> None:
    try:
        run(["gh", "auth", "status"], cwd=cwd, capture_output=True, check=True)
    except subprocess.CalledProcessError as exc:
        raise RuntimeError(
            "GitHub CLI is not authenticated. Run `gh auth login` first."
        ) from exc


def fetch_issue(cwd: Path, issue_url: str) -> IssueContext:
    repo, number = issue_url_to_repo_and_number(issue_url)
    completed = run(
        [
            "gh",
            "issue",
            "view",
            issue_url,
            "--json",
            "url,number,title,body,state,comments",
        ],
        cwd=cwd,
    )
    data = json.loads(completed.stdout)
    comments = data.get("comments", [])
    return IssueContext(
        url=data["url"],
        repo=repo,
        number=int(data["number"]),
        title=data["title"],
        body=data.get("body") or "",
        state=data["state"],
        comments=comments,
    )


def summarize_comments(comments: list[dict[str, Any]], max_chars: int) -> str:
    if not comments:
        return "No comments yet."

    rendered: list[str] = []
    remaining = max_chars

    for comment in reversed(comments):
        author = (((comment.get("author") or {}).get("login")) or "unknown").strip()
        body = (comment.get("body") or "").strip()
        if not body:
            continue

        chunk = f"Author: {author}\n{body}\n"
        if len(chunk) > remaining and rendered:
            break
        if len(chunk) > remaining:
            chunk = chunk[: max(0, remaining - 1)] + "…"
        rendered.append(chunk)
        remaining -= len(chunk)
        if remaining <= 0:
            break

    rendered.reverse()
    return "\n---\n".join(rendered) if rendered else "No non-empty comments yet."


def build_prompt(
    issue: IssueContext,
    *,
    iteration: int,
    max_iterations: int,
    body_char_limit: int,
    comment_char_limit: int,
) -> str:
    issue_body = issue.body.strip()
    if len(issue_body) > body_char_limit:
        issue_body = issue_body[: body_char_limit - 1] + "…"

    comments = summarize_comments(issue.comments, comment_char_limit)

    return textwrap.dedent(
        f"""
        You are running one autonomous Ralph loop step for GitHub issue #{issue.number}: {issue.title}
        Issue URL: {issue.url}
        Repository: {issue.repo}
        Iteration: {iteration} of {max_iterations}

        Requirements for this step:
        - Read `AGENTS.md` and the linked playbooks before changing code.
        - Read the issue and recent comments below as the persistent working log.
        - Figure out the highest-leverage next natural step for this issue from the current repo state.
        - Implement exactly one coherent, proved chunk of progress. Do not try to finish everything in one jump unless it is genuinely small and safe.
        - Build, run owning tests, and run any issue-appropriate verification. Do not claim wins without proof on the current worktree.
        - If the chunk proves cleanly, commit it, push the current branch, and update the GitHub issue with a progress comment that includes:
          - what seam/task you attacked
          - what was rewritten
          - exact proof commands
          - exact proof results
          - a rough overall progress estimate as a percentage
          - the next best seam
        - If the chunk does not prove cleanly, do not leave a broken half-step behind. Either fix it or back it out.
        - No fallback-style design. Prefer deletion over keeping old and new paths side by side.
        - Return only JSON matching the provided schema.

        Stop rules:
        - Return `outcome: "done"` only if the issue goal is genuinely complete in the current worktree.
        - Return `outcome: "blocked"` only if you cannot continue without a real external blocker.
        - Otherwise return `outcome: "completed_step"` after one proved chunk so the outer loop can continue.

        Issue body:
        {issue_body if issue_body else "(empty)"}

        Recent issue comments:
        {comments}
        """
    ).strip()


def run_codex_iteration(
    cwd: Path,
    prompt: str,
    *,
    model: str | None,
    dangerous: bool,
) -> dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix="ralph-loop-") as temp_dir_name:
        temp_dir = Path(temp_dir_name)
        schema_path = temp_dir / "result-schema.json"
        output_path = temp_dir / "last-message.json"
        schema_path.write_text(json.dumps(RESULT_SCHEMA, indent=2), encoding="utf-8")

        cmd = [
            "codex",
            "exec",
            "-C",
            str(cwd),
            "--output-schema",
            str(schema_path),
            "--output-last-message",
            str(output_path),
            "--color",
            "never",
        ]

        if model:
            cmd.extend(["-m", model])

        if dangerous:
            cmd.append("--dangerously-bypass-approvals-and-sandbox")
        else:
            cmd.append("--full-auto")

        proc = subprocess.run(
            cmd,
            input=prompt,
            text=True,
            cwd=str(cwd),
            check=False,
        )
        if proc.returncode != 0:
            raise RuntimeError(f"codex exec failed with exit code {proc.returncode}")

        if not output_path.exists():
            raise RuntimeError("codex exec completed but no final message file was produced")

        raw_output = output_path.read_text(encoding="utf-8").strip()
        try:
            return json.loads(raw_output)
        except json.JSONDecodeError as exc:
            raise RuntimeError(f"Could not parse Codex final message as JSON: {raw_output}") from exc


def print_iteration_header(iteration: int, issue: IssueContext) -> None:
    print(f"\n== Ralph Loop Iteration {iteration} ==")
    print(f"Issue: #{issue.number} {issue.title}")
    print(f"URL:   {issue.url}")
    print()


def print_iteration_result(iteration: int, result: dict[str, Any]) -> None:
    print(f"Outcome:  {result['outcome']}")
    print(f"Progress: {result['progress_percent']}%")
    print(f"Commit:   {result['commit'] or '(none)'}")
    print("Summary:")
    print(textwrap.indent(result["summary"].strip(), "  "))
    if result["proof_summary"]:
        print("Proof:")
        for line in result["proof_summary"]:
            print(f"  - {line}")
    print(f"Next:     {result['next_hint']}")
    print()


def maybe_sleep(seconds: float) -> None:
    if seconds > 0:
        time.sleep(seconds)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Autonomous Codex issue loop: read a GitHub issue, run one proved Codex step, update the issue, and continue."
    )
    parser.add_argument("issue_url", help="GitHub issue URL, for example https://github.com/owner/repo/issues/364")
    parser.add_argument(
        "--cwd",
        default=os.getcwd(),
        help="Working directory for the repo. Defaults to the current directory.",
    )
    parser.add_argument(
        "--max-iterations",
        type=int,
        default=DEFAULT_MAX_ITERATIONS,
        help=f"Maximum Codex iterations to run. Defaults to {DEFAULT_MAX_ITERATIONS}.",
    )
    parser.add_argument(
        "--model",
        default=None,
        help="Optional Codex model override, passed to `codex exec -m`.",
    )
    parser.add_argument(
        "--dangerous",
        action="store_true",
        help="Run Codex with `--dangerously-bypass-approvals-and-sandbox` so it can commit, push, and update GitHub without sandbox interference.",
    )
    parser.add_argument(
        "--body-char-limit",
        type=int,
        default=DEFAULT_MAX_BODY_CHARS,
        help=f"Max issue body characters to inline into each prompt. Defaults to {DEFAULT_MAX_BODY_CHARS}.",
    )
    parser.add_argument(
        "--comment-char-limit",
        type=int,
        default=DEFAULT_MAX_COMMENT_CHARS,
        help=f"Max recent comment characters to inline into each prompt. Defaults to {DEFAULT_MAX_COMMENT_CHARS}.",
    )
    parser.add_argument(
        "--pause-seconds",
        type=float,
        default=0.0,
        help="Optional pause between iterations.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    cwd = Path(args.cwd).resolve()

    if not cwd.exists():
        print(f"Working directory does not exist: {cwd}", file=sys.stderr)
        return 2

    try:
        ensure_cli_available(cwd, "gh", ["--version"])
        ensure_cli_available(cwd, "codex", ["--help"])
        ensure_gh_auth(cwd)
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    for iteration in range(1, args.max_iterations + 1):
        try:
            issue = fetch_issue(cwd, args.issue_url)
        except Exception as exc:  # noqa: BLE001
            print(f"Failed to fetch issue context: {exc}", file=sys.stderr)
            return 2

        if issue.state.upper() == "CLOSED":
            print(f"Issue #{issue.number} is closed. Stopping.")
            return 0

        print_iteration_header(iteration, issue)
        prompt = build_prompt(
            issue,
            iteration=iteration,
            max_iterations=args.max_iterations,
            body_char_limit=args.body_char_limit,
            comment_char_limit=args.comment_char_limit,
        )

        try:
            result = run_codex_iteration(
                cwd,
                prompt,
                model=args.model,
                dangerous=args.dangerous,
            )
        except Exception as exc:  # noqa: BLE001
            print(f"Codex iteration failed: {exc}", file=sys.stderr)
            return 1

        print_iteration_result(iteration, result)

        outcome = result["outcome"]
        if outcome in {"done", "blocked"}:
            return 0

        maybe_sleep(args.pause_seconds)

    print(f"Reached the max iteration limit ({args.max_iterations}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
