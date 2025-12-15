#!/usr/bin/env python3
"""
JsEngine Profiler - CPU and Memory profiling for JsEngine benchmarks

Usage:
    python3 profile.py [fib|forloop|all] [--cpu] [--memory] [--compare]

Examples:
    python3 profile.py fib              # Profile fibonacci (CPU + memory)
    python3 profile.py forloop --cpu    # CPU profile only for forloop
    python3 profile.py all              # Profile all benchmarks
    python3 profile.py --compare        # Run benchmarks and compare to Jint
"""

import subprocess
import json
import sys
import os
import tempfile
import argparse
from pathlib import Path
from collections import defaultdict
from datetime import datetime

# Colors for terminal output
class Colors:
    GREEN = '\033[92m'
    YELLOW = '\033[93m'
    RED = '\033[91m'
    CYAN = '\033[96m'
    BOLD = '\033[1m'
    END = '\033[0m'

def print_header(text):
    print(f"\n{Colors.BOLD}{Colors.GREEN}{'=' * 80}{Colors.END}")
    print(f"{Colors.BOLD}{Colors.GREEN}{text}{Colors.END}")
    print(f"{Colors.BOLD}{Colors.GREEN}{'=' * 80}{Colors.END}\n")

def print_section(text):
    print(f"\n{Colors.CYAN}{Colors.BOLD}{text}{Colors.END}")
    print("-" * 80)

def run_command(cmd, cwd=None, capture=True, timeout=300):
    """Run a shell command and return output"""
    try:
        result = subprocess.run(
            cmd,
            shell=True,
            cwd=cwd,
            capture_output=capture,
            text=True,
            timeout=timeout
        )
        return result.returncode == 0, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return False, "", "Command timed out"
    except Exception as e:
        return False, "", str(e)

class Profiler:
    def __init__(self, repo_root):
        self.repo_root = Path(repo_root)
        self.tools_dir = self.repo_root / "tools"
        self.output_dir = self.tools_dir / "profile-output"
        self.output_dir.mkdir(exist_ok=True)

        self.profiles = {
            "fib": {
                "name": "FibonacciProfile",
                "dir": self.tools_dir / "FibonacciProfile",
                "description": "Recursive fibonacci(20) - tests function calls"
            },
            "forloop": {
                "name": "ForLoopProfile",
                "dir": self.tools_dir / "ForLoopProfile",
                "description": "For loop with 1M iterations - tests loop performance"
            }
        }

    def build(self, profile_key):
        """Build a profile project"""
        profile = self.profiles[profile_key]
        print(f"Building {profile['name']}...")

        success, stdout, stderr = run_command(
            f"dotnet build -c Release -v q --nologo",
            cwd=profile['dir']
        )

        if not success:
            print(f"{Colors.RED}Build failed: {stderr}{Colors.END}")
            return False

        print(f"{Colors.GREEN}Build successful{Colors.END}")
        return True

    def get_executable(self, profile_key):
        """Get path to compiled executable"""
        profile = self.profiles[profile_key]
        exe_path = profile['dir'] / "bin" / "Release" / "net10.0" / profile['name']
        return exe_path if exe_path.exists() else None

    def cpu_profile(self, profile_key):
        """Run CPU profiling with dotnet-trace"""
        profile = self.profiles[profile_key]
        exe_path = self.get_executable(profile_key)

        if not exe_path:
            print(f"{Colors.RED}Executable not found. Run build first.{Colors.END}")
            return None

        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        trace_file = self.output_dir / f"{profile['name']}_{timestamp}.nettrace"

        print(f"Running CPU profile on {exe_path}...")

        success, stdout, stderr = run_command(
            f"dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler "
            f"--output {trace_file} -- {exe_path}",
            timeout=120
        )

        if not success or not trace_file.exists():
            print(f"{Colors.RED}Trace collection failed{Colors.END}")
            return None

        # Convert to speedscope
        print("Converting trace to speedscope format...")
        run_command(f"dotnet-trace convert {trace_file} --format Speedscope")

        # Find the speedscope file
        speedscope_files = list(self.output_dir.glob(f"{profile['name']}_{timestamp}*.json"))
        if not speedscope_files:
            print(f"{Colors.RED}Speedscope conversion failed{Colors.END}")
            return None

        return self.analyze_speedscope(speedscope_files[0])

    def analyze_speedscope(self, speedscope_path):
        """Parse speedscope JSON and extract hot functions"""
        with open(speedscope_path, 'r') as f:
            data = json.load(f)

        frames = data['shared']['frames']
        profile = data['profiles'][0]  # Main thread

        # Calculate time spent in each frame
        frame_times = defaultdict(float)
        frame_counts = defaultdict(int)
        caller_callee = defaultdict(lambda: defaultdict(int))  # caller -> callee -> count
        stack = []

        for event in profile.get('events', []):
            event_type = event['type']
            frame_idx = event['frame']
            at = event['at']

            if event_type == 'O':  # Open
                # Track caller -> callee relationship
                if stack:
                    caller_idx = stack[-1][0]
                    caller_callee[caller_idx][frame_idx] += 1
                stack.append((frame_idx, at))
                frame_counts[frame_idx] += 1
            elif event_type == 'C':  # Close
                if stack and stack[-1][0] == frame_idx:
                    open_frame, open_time = stack.pop()
                    frame_times[frame_idx] += at - open_time

        # Sort by time
        sorted_frames = sorted(frame_times.items(), key=lambda x: -x[1])

        results = {
            'all_functions': [],
            'jsengine_functions': [],
            'total_time': sum(frame_times.values()),
            'jsengine_time': 0,
            'frames': frames,
            'caller_callee': caller_callee,
            'frame_counts': frame_counts
        }

        for frame_idx, time_spent in sorted_frames:
            name = frames[frame_idx]['name'] if frame_idx < len(frames) else "Unknown"
            count = frame_counts[frame_idx]

            entry = {
                'name': name,
                'time_ms': time_spent,
                'calls': count,
                'frame_idx': frame_idx
            }

            results['all_functions'].append(entry)

            if 'Asynkron' in name:
                results['jsengine_functions'].append(entry)
                results['jsengine_time'] += time_spent

        return results

    def memory_profile(self, profile_key, iterations=50):
        """Run memory profiling using GC allocation tracking"""
        profile = self.profiles[profile_key]

        print(f"Running memory profile on {profile['name']}...")

        # Use the AllocationProfile tool
        alloc_profile_dir = self.tools_dir / "AllocationProfile"

        if not (alloc_profile_dir / "AllocationProfile.csproj").exists():
            print(f"{Colors.YELLOW}AllocationProfile project not found, using basic GC stats{Colors.END}")
            return self._basic_memory_profile(profile_key, iterations)

        # Build AllocationProfile
        success, _, _ = run_command("dotnet build -c Release -v q --nologo", cwd=alloc_profile_dir)
        if not success:
            return self._basic_memory_profile(profile_key, iterations)

        # Run it
        success, stdout, stderr = run_command(
            f"dotnet run -c Release --no-build -- {profile_key}",
            cwd=alloc_profile_dir,
            timeout=180
        )

        if not success:
            print(f"{Colors.RED}Memory profile failed: {stderr}{Colors.END}")
            return None

        # Parse output
        return self._parse_allocation_output(stdout)

    def _basic_memory_profile(self, profile_key, iterations):
        """Basic memory profiling by running the app multiple times"""
        exe_path = self.get_executable(profile_key)
        if not exe_path:
            return None

        print(f"Running {iterations} iterations...")

        success, stdout, stderr = run_command(str(exe_path), timeout=120)

        return {
            'iterations': iterations,
            'output': stdout
        }

    def _parse_allocation_output(self, output):
        """Parse allocation profiler output"""
        results = {}
        lines = output.split('\n')

        for line in lines:
            if 'Total allocated:' in line:
                results['total_allocated'] = line.split(':')[-1].strip()
            elif 'Per iteration:' in line and 'allocated' not in results.get('per_iteration', ''):
                results['per_iteration'] = line.split(':')[-1].strip()
            elif 'GC Gen0 collections:' in line:
                results['gen0_collections'] = line.split(':')[-1].strip()
            elif 'GC Gen1 collections:' in line:
                results['gen1_collections'] = line.split(':')[-1].strip()
            elif 'GC Gen2 collections:' in line:
                results['gen2_collections'] = line.split(':')[-1].strip()
            elif 'Total time:' in line:
                results['total_time'] = line.split(':')[-1].strip()

        results['raw_output'] = output
        return results

    def print_cpu_results(self, results, profile_key, show_call_graph=True):
        """Print CPU profiling results"""
        if not results:
            print(f"{Colors.RED}No results to display{Colors.END}")
            return

        profile = self.profiles[profile_key]

        print_section(f"CPU PROFILE: {profile['name']}")
        print(f"Description: {profile['description']}")
        print()

        # Top functions overall
        print(f"{'Time (ms)':>12} {'Calls':>10}  {'Function'}")
        print("-" * 100)

        for entry in results['all_functions'][:20]:
            name = entry['name'][:75] + "..." if len(entry['name']) > 75 else entry['name']
            print(f"{entry['time_ms']:>12.2f} {entry['calls']:>10}  {name}")

        # JsEngine functions
        print_section("JSENGINE HOT FUNCTIONS")
        print(f"{'Time (ms)':>12} {'Calls':>10}  {'Function'}")
        print("-" * 100)

        for entry in results['jsengine_functions'][:25]:
            name = entry['name'][:75] + "..." if len(entry['name']) > 75 else entry['name']
            print(f"{entry['time_ms']:>12.2f} {entry['calls']:>10}  {name}")

        # Summary
        print()
        js_pct = 100 * results['jsengine_time'] / results['total_time'] if results['total_time'] > 0 else 0
        print(f"{Colors.BOLD}JsEngine time: {results['jsengine_time']:.2f} ms ({js_pct:.1f}% of total){Colors.END}")
        print(f"{Colors.BOLD}Total time: {results['total_time']:.2f} ms{Colors.END}")

        # Call graph for hot JsEngine functions
        if show_call_graph and 'caller_callee' in results:
            self.print_call_graph(results)

    def print_call_graph(self, results, top_n=10):
        """Print call graph showing callers and callees for hot functions"""
        # Call the allocation-focused call graph
        self.print_allocation_callgraph(results, top_n=15)

    def print_allocation_callgraph(self, results, top_n=15):
        """Print call graph as a hierarchical tree starting from hottest functions"""
        frames = results.get('frames', [])
        caller_callee = results.get('caller_callee', {})
        frame_counts = results.get('frame_counts', {})

        if not caller_callee:
            print("No call graph data available")
            return

        jsengine_keywords = ['Asynkron', 'JsEngine']

        def shorten(name, max_len=55):
            short = name.split('!')[-1] if '!' in name else name
            # Extract just the method name for cleaner output
            if '(' in short:
                short = short.split('(')[0]
            if len(short) > max_len:
                short = short[:max_len-3] + "..."
            return short

        def is_jsengine(name):
            return any(kw in name for kw in jsengine_keywords)

        # Get the hottest JsEngine functions
        hot_functions = []
        for idx, frame in enumerate(frames):
            name = frame['name']
            if not is_jsengine(name):
                continue
            count = frame_counts.get(idx, 0)
            if count >= 100:
                hot_functions.append((idx, name, count))

        hot_functions.sort(key=lambda x: -x[2])

        print_section("CALL HIERARCHY (top functions → what they call)")
        print()

        # Show top hot functions with what they call (3 levels)
        shown = set()
        for root_idx, root_name, root_count in hot_functions[:8]:
            if root_idx in shown:
                continue
            shown.add(root_idx)

            print(f"{Colors.BOLD}{shorten(root_name, 65)}{Colors.END} ({root_count:,} calls)")

            # Level 1: what this function calls
            children = caller_callee.get(root_idx, {})
            sorted_children = sorted(
                [(idx, cnt) for idx, cnt in children.items()
                 if idx < len(frames) and is_jsengine(frames[idx]['name'])],
                key=lambda x: -x[1]
            )[:4]

            for i, (child_idx, child_count) in enumerate(sorted_children):
                child_name = frames[child_idx]['name']
                is_last_child = (i == len(sorted_children) - 1)
                prefix1 = "└── " if is_last_child else "├── "

                print(f"  {prefix1}{shorten(child_name)} ({child_count:,})")

                # Level 2: what the child calls
                grandchildren = caller_callee.get(child_idx, {})
                sorted_gc = sorted(
                    [(idx, cnt) for idx, cnt in grandchildren.items()
                     if idx < len(frames) and is_jsengine(frames[idx]['name'])],
                    key=lambda x: -x[1]
                )[:3]

                for j, (gc_idx, gc_count) in enumerate(sorted_gc):
                    gc_name = frames[gc_idx]['name']
                    is_last_gc = (j == len(sorted_gc) - 1)
                    connector = "    " if is_last_child else "│   "
                    prefix2 = connector + ("└── " if is_last_gc else "├── ")

                    print(f"  {prefix2}{shorten(gc_name)} ({gc_count:,})")

                    # Level 3: what grandchild calls
                    ggc = caller_callee.get(gc_idx, {})
                    sorted_ggc = sorted(
                        [(idx, cnt) for idx, cnt in ggc.items()
                         if idx < len(frames) and is_jsengine(frames[idx]['name'])],
                        key=lambda x: -x[1]
                    )[:2]

                    for k, (ggc_idx, ggc_count) in enumerate(sorted_ggc):
                        ggc_name = frames[ggc_idx]['name']
                        is_last_ggc = (k == len(sorted_ggc) - 1)
                        c2 = "    " if is_last_child else "│   "
                        c3 = "    " if is_last_gc else "│   "
                        prefix3 = c2 + c3 + ("└── " if is_last_ggc else "├── ")
                        print(f"  {prefix3}{shorten(ggc_name)} ({ggc_count:,})")

            print()

    def find_allocation_paths(self, results, target_function):
        """Find all call paths leading to a specific function (e.g., an allocator)"""
        frames = results.get('frames', [])
        caller_callee = results.get('caller_callee', {})

        # Find frame index for target
        target_idx = None
        for idx, frame in enumerate(frames):
            if target_function.lower() in frame['name'].lower():
                target_idx = idx
                break

        if target_idx is None:
            print(f"Function '{target_function}' not found")
            return []

        # Build reverse map
        callee_callers = defaultdict(set)
        for caller_idx, callees in caller_callee.items():
            for callee_idx in callees:
                callee_callers[callee_idx].add(caller_idx)

        # BFS to find paths
        paths = []
        queue = [(target_idx, [target_idx])]
        visited = set()

        while queue and len(paths) < 20:
            current, path = queue.pop(0)
            if current in visited:
                continue
            visited.add(current)

            callers = callee_callers.get(current, set())
            if not callers:
                # Root - this is a complete path
                paths.append(list(reversed(path)))
            else:
                for caller in callers:
                    if caller not in visited:
                        queue.append((caller, path + [caller]))

        return paths

    def print_memory_results(self, results, profile_key):
        """Print memory profiling results"""
        if not results:
            print(f"{Colors.RED}No results to display{Colors.END}")
            return

        profile = self.profiles[profile_key]

        print_section(f"MEMORY PROFILE: {profile['name']}")
        print(f"Description: {profile['description']}")
        print()

        if 'raw_output' in results:
            # Print parsed results
            for key, value in results.items():
                if key != 'raw_output':
                    print(f"{key.replace('_', ' ').title():20}: {value}")
        else:
            print(results.get('output', 'No output'))

    def run_benchmarks(self):
        """Run BenchmarkDotNet comparison with Jint"""
        print_header("RUNNING JINT COMPARISON BENCHMARKS")

        benchmark_dir = self.repo_root / "benchmarks" / "Asynkron.JsEngine.Benchmarks"

        print("This may take several minutes...")
        success, stdout, stderr = run_command(
            "dotnet run -c Release -- jint --job short",
            cwd=benchmark_dir,
            timeout=600,
            capture=True
        )

        if not success:
            print(f"{Colors.RED}Benchmarks failed{Colors.END}")
            return

        # Find and display the results file
        results_dir = benchmark_dir / "BenchmarkDotNet.Artifacts" / "results"
        md_files = list(results_dir.glob("*JintComparison*-github.md"))

        if md_files:
            print_section("BENCHMARK RESULTS")
            with open(md_files[0], 'r') as f:
                print(f.read())

def main():
    parser = argparse.ArgumentParser(description='JsEngine Profiler')
    parser.add_argument('profile', nargs='?', default='fib',
                        choices=['fib', 'forloop', 'all'],
                        help='Profile to run (default: fib)')
    parser.add_argument('--cpu', action='store_true', help='Run CPU profiling only')
    parser.add_argument('--memory', action='store_true', help='Run memory profiling only')
    parser.add_argument('--compare', action='store_true', help='Run Jint comparison benchmarks')

    args = parser.parse_args()

    # Find repo root
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent

    profiler = Profiler(repo_root)

    # If --compare, just run benchmarks
    if args.compare:
        profiler.run_benchmarks()
        return

    # Determine what to profile
    profiles_to_run = ['fib', 'forloop'] if args.profile == 'all' else [args.profile]
    run_cpu = args.cpu or (not args.cpu and not args.memory)
    run_memory = args.memory or (not args.cpu and not args.memory)

    print_header("JSENGINE PROFILER")
    print(f"Profiles: {', '.join(profiles_to_run)}")
    print(f"CPU profiling: {'Yes' if run_cpu else 'No'}")
    print(f"Memory profiling: {'Yes' if run_memory else 'No'}")

    for profile_key in profiles_to_run:
        print_header(f"PROFILING: {profiler.profiles[profile_key]['name']}")

        # Build
        if not profiler.build(profile_key):
            continue

        # CPU profile
        if run_cpu:
            results = profiler.cpu_profile(profile_key)
            profiler.print_cpu_results(results, profile_key)

        # Memory profile
        if run_memory:
            results = profiler.memory_profile(profile_key)
            profiler.print_memory_results(results, profile_key)

    print()
    print(f"{Colors.GREEN}Profiling complete!{Colors.END}")

if __name__ == '__main__':
    main()
