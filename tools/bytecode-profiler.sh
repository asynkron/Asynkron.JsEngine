#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

export PERF_PROFILES="${PERF_PROFILES:-bytecode:UnifiedBytecodeVirtualMachine.Execute}"
exec "$script_dir/performance-profiler.sh"
