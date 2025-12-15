#!/bin/bash

# CPU profiling script for JsEngine
# Usage: ./cpu-profile.sh [fib|forloop]

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PROFILE_TYPE="${1:-fib}"
OUTPUT_DIR="$SCRIPT_DIR/profile-output"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${GREEN}=== JsEngine CPU Profiler ===${NC}"

# Select profile
case "$PROFILE_TYPE" in
    fib|fibonacci)
        PROJECT_DIR="$SCRIPT_DIR/FibonacciProfile"
        PROJECT_NAME="FibonacciProfile"
        ;;
    for|forloop|loop)
        PROJECT_DIR="$SCRIPT_DIR/ForLoopProfile"
        PROJECT_NAME="ForLoopProfile"
        ;;
    *)
        echo "Usage: $0 [fib|forloop]"
        exit 1
        ;;
esac

echo -e "${GREEN}Profile: $PROJECT_NAME${NC}"

# Build
echo -e "${GREEN}Building...${NC}"
cd "$REPO_ROOT"
dotnet build "$PROJECT_DIR" -c Release -v q --nologo

# Setup output
mkdir -p "$OUTPUT_DIR"
TRACE_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}.nettrace"
SPEEDSCOPE_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}.speedscope.json"
REPORT_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}_cpu_report.txt"

# Find the executable
EXE_PATH="$PROJECT_DIR/bin/Release/net10.0/$PROJECT_NAME"
if [ ! -f "$EXE_PATH" ]; then
    echo "Executable not found at $EXE_PATH"
    exit 1
fi

# Run with profiling
echo -e "${GREEN}Profiling $EXE_PATH...${NC}"

dotnet-trace collect \
    --providers Microsoft-DotNETCore-SampleProfiler \
    --output "$TRACE_FILE" \
    -- "$EXE_PATH"

echo -e "${GREEN}Converting to speedscope...${NC}"
dotnet-trace convert "$TRACE_FILE" --format Speedscope 2>/dev/null || true

# Find the actual speedscope file (handles double extension)
ACTUAL_SPEEDSCOPE=$(ls -1 "${TRACE_FILE%.nettrace}"*.json 2>/dev/null | head -1)

if [ -z "$ACTUAL_SPEEDSCOPE" ] || [ ! -f "$ACTUAL_SPEEDSCOPE" ]; then
    echo "Speedscope conversion failed"
    exit 1
fi

# Generate report using Python
echo -e "${GREEN}Generating report...${NC}"

python3 << EOF > "$REPORT_FILE"
import json
from collections import defaultdict

with open('$ACTUAL_SPEEDSCOPE', 'r') as f:
    d = json.load(f)

frames = d['shared']['frames']
profile = d['profiles'][0]  # Main thread

# Calculate time spent in each frame
frame_times = defaultdict(float)
frame_counts = defaultdict(int)
stack = []

for event in profile.get('events', []):
    event_type = event['type']
    frame_idx = event['frame']
    at = event['at']

    if event_type == 'O':
        stack.append((frame_idx, at))
        frame_counts[frame_idx] += 1
    elif event_type == 'C':
        if stack and stack[-1][0] == frame_idx:
            open_frame, open_time = stack.pop()
            frame_times[frame_idx] += at - open_time

sorted_frames = sorted(frame_times.items(), key=lambda x: -x[1])

print("=" * 110)
print("CPU PROFILE REPORT - $PROJECT_NAME")
print("=" * 110)
print()

print("=== TOP HOT FUNCTIONS (by time spent) ===")
print()
print(f"{'Time (ms)':>12} {'Calls':>10}  Function")
print("-" * 110)

for frame_idx, time_spent in sorted_frames[:30]:
    name = frames[frame_idx]['name'] if frame_idx < len(frames) else "Unknown"
    count = frame_counts[frame_idx]
    if len(name) > 80:
        name = name[:77] + "..."
    print(f"{time_spent:>12.2f} {count:>10}  {name}")

print()
print("=== JSENGINE HOT FUNCTIONS ===")
print()
print(f"{'Time (ms)':>12} {'Calls':>10}  Function")
print("-" * 110)

jsengine_frames = [(idx, t) for idx, t in sorted_frames
                   if idx < len(frames) and 'Asynkron' in frames[idx]['name']]

for frame_idx, time_spent in jsengine_frames[:40]:
    name = frames[frame_idx]['name']
    count = frame_counts[frame_idx]
    if len(name) > 80:
        name = name[:77] + "..."
    print(f"{time_spent:>12.2f} {count:>10}  {name}")

total_js = sum(t for _, t in jsengine_frames)
total_all = sum(frame_times.values())
print()
print(f"JsEngine time: {total_js:.2f} ms ({100*total_js/total_all:.1f}% of total)")
print(f"Total time: {total_all:.2f} ms")
EOF

echo ""
cat "$REPORT_FILE"

echo ""
echo -e "${GREEN}Report saved to: $REPORT_FILE${NC}"
echo -e "${YELLOW}Speedscope file: $ACTUAL_SPEEDSCOPE${NC}"
echo -e "${YELLOW}View at: https://speedscope.app${NC}"
