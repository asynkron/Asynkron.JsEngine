#!/bin/bash

# Allocation tracing script for JsEngine benchmarks
# Usage: ./trace-allocations.sh [profile]

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PROFILE_KEY="${1:-fib}"
OUTPUT_DIR="$SCRIPT_DIR/trace-output"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== JsEngine Allocation Tracer ===${NC}"

# Check for required tools
check_tools() {
    if ! command -v dotnet-trace &> /dev/null; then
        echo -e "${YELLOW}Installing dotnet-trace...${NC}"
        dotnet tool install --global dotnet-trace || true
    fi
}

# Select profile project
select_profile() {
    PROJECT_DIR="$SCRIPT_DIR/ProfileRunner"
    PROJECT_NAME="ProfileRunner"
    echo -e "${GREEN}Selected profile: $PROFILE_KEY${NC}"
}

# Build the project
build_project() {
    echo -e "${GREEN}Building $PROJECT_NAME...${NC}"
    cd "$REPO_ROOT"
    dotnet build "$PROJECT_DIR" -c Release -v q --nologo
}

# Create output directory
setup_output() {
    mkdir -p "$OUTPUT_DIR"
    TRACE_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}.nettrace"
    SPEEDSCOPE_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}.speedscope.json"
    REPORT_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}_allocations.txt"
}

# Run and collect trace
run_and_collect() {
    echo -e "${GREEN}Starting $PROJECT_NAME with allocation tracing...${NC}"

    cd "$PROJECT_DIR"

    # Start the process in background
    dotnet run -c Release --no-build -- "$PROFILE_KEY" &
    APP_PID=$!

    # Wait for process to start
    sleep 1

    # Find the actual .NET process
    DOTNET_PID=$(pgrep -P $APP_PID dotnet 2>/dev/null || echo $APP_PID)

    if [ -z "$DOTNET_PID" ]; then
        DOTNET_PID=$(pgrep -f "$PROJECT_NAME" | head -1)
    fi

    echo -e "${GREEN}Process PID: $DOTNET_PID${NC}"
    echo -e "${GREEN}Collecting allocation trace...${NC}"

    # Collect trace with GC allocation tick events (high volume sampling)
    # Provider: Microsoft-DotNETCore-SampleProfiler for CPU + allocations
    # gc-verbose gives allocation ticks
    dotnet-trace collect \
        --process-id $DOTNET_PID \
        --providers gc-verbose \
        --output "$TRACE_FILE" \
        --duration 00:00:30 &
    TRACE_PID=$!

    # Wait for benchmark to finish or trace to complete
    wait $APP_PID 2>/dev/null || true
    wait $TRACE_PID 2>/dev/null || true

    echo -e "${GREEN}Trace saved to: $TRACE_FILE${NC}"
}

# Convert and analyze
analyze_trace() {
    if [ ! -f "$TRACE_FILE" ]; then
        echo -e "${RED}No trace file found at $TRACE_FILE${NC}"
        return 1
    fi

    echo -e "${GREEN}Converting trace to speedscope format...${NC}"
    dotnet-trace convert "$TRACE_FILE" --format speedscope -o "$SPEEDSCOPE_FILE" 2>/dev/null || true

    echo -e "${GREEN}Generating allocation report...${NC}"

    # Generate report header
    {
        echo "=== Allocation Trace Report ==="
        echo "Profile: $PROJECT_NAME"
        echo "Date: $(date)"
        echo "Trace: $TRACE_FILE"
        echo ""
    } > "$REPORT_FILE"

    # If speedscope file exists, extract allocation info
    if [ -f "$SPEEDSCOPE_FILE" ]; then
        echo "Speedscope file: $SPEEDSCOPE_FILE" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"

        # Extract frame names that look like allocations (contain "Alloc" or common types)
        echo "=== Allocation-related frames ===" >> "$REPORT_FILE"
        grep -o '"name":"[^"]*"' "$SPEEDSCOPE_FILE" 2>/dev/null | \
            sed 's/"name":"//g' | sed 's/"//g' | \
            grep -iE 'alloc|new |\.ctor|Create|JsObject|JsArray|PropertyDescriptor|JsEnvironment|Dictionary|List<' | \
            sort | uniq -c | sort -rn | head -50 >> "$REPORT_FILE" || true

        echo "" >> "$REPORT_FILE"
        echo "=== All unique frames (top 100) ===" >> "$REPORT_FILE"
        grep -o '"name":"[^"]*"' "$SPEEDSCOPE_FILE" 2>/dev/null | \
            sed 's/"name":"//g' | sed 's/"//g' | \
            sort | uniq -c | sort -rn | head -100 >> "$REPORT_FILE" || true
    fi

    echo -e "${GREEN}Report saved to: $REPORT_FILE${NC}"
    echo ""
    echo -e "${YELLOW}=== Allocation Report ===${NC}"
    cat "$REPORT_FILE"
}

# Alternative: Use EventPipe with allocation keywords
run_with_alloc_events() {
    echo -e "${GREEN}Starting $PROJECT_NAME with GC allocation events...${NC}"

    cd "$PROJECT_DIR"

    # Set environment to enable allocation tracking
    export DOTNET_EnableEventPipe=1
    export DOTNET_EventPipeOutputPath="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}_events.nettrace"
    export DOTNET_EventPipeConfig="Microsoft-Windows-DotNETRuntime:0x80000:5"

    dotnet run -c Release --no-build -- "$PROFILE_KEY"

    unset DOTNET_EnableEventPipe
    unset DOTNET_EventPipeOutputPath
    unset DOTNET_EventPipeConfig

    if [ -f "$DOTNET_EventPipeOutputPath" ]; then
        echo -e "${GREEN}Event trace saved to: $DOTNET_EventPipeOutputPath${NC}"
        dotnet-trace convert "$DOTNET_EventPipeOutputPath" --format speedscope -o "${OUTPUT_DIR}/${PROJECT_NAME}_${TIMESTAMP}_events.speedscope.json" 2>/dev/null || true
    fi
}

# Main
main() {
    check_tools
    select_profile
    setup_output
    build_project
    run_and_collect
    analyze_trace

    echo ""
    echo -e "${GREEN}Done!${NC}"
    echo -e "${YELLOW}For detailed analysis, open the speedscope file in https://speedscope.app${NC}"
}

main
