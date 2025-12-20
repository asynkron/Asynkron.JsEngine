#!/bin/bash

# Memory profiling script for JsEngine benchmarks
# Usage: ./profile-memory.sh [profile]

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PROFILE_KEY="${1:-fib}"
OUTPUT_DIR="$SCRIPT_DIR/profile-output"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== JsEngine Memory Profiler ===${NC}"

# Check for required tools
check_tools() {
    local missing=0

    if ! command -v dotnet-gcdump &> /dev/null; then
        echo -e "${YELLOW}Installing dotnet-gcdump...${NC}"
        dotnet tool install --global dotnet-gcdump || true
    fi

    if ! command -v dotnet-dump &> /dev/null; then
        echo -e "${YELLOW}Installing dotnet-dump...${NC}"
        dotnet tool install --global dotnet-dump || true
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
    DUMP_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}.dmp"
    REPORT_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${TIMESTAMP}_report.txt"
}

# Run benchmark and collect GC dump
run_and_collect() {
    echo -e "${GREEN}Starting $PROJECT_NAME...${NC}"

    # Start the process in background, auto-accepting prompts
    cd "$PROJECT_DIR"
    (echo ""; sleep 60; echo "") | dotnet run -c Release --no-build -- "$PROFILE_KEY" &
    APP_PID=$!

    # Wait for process to start
    sleep 2

    # Find the actual .NET process (child of our shell)
    DOTNET_PID=$(pgrep -P $APP_PID dotnet 2>/dev/null || echo $APP_PID)

    if [ -z "$DOTNET_PID" ]; then
        # Try to find by name
        DOTNET_PID=$(pgrep -f "$PROJECT_NAME" | head -1)
    fi

    echo -e "${GREEN}Process PID: $DOTNET_PID${NC}"
    echo -e "${GREEN}Waiting for benchmark to complete...${NC}"

    # Wait for the benchmark to finish (watch for the dots to stop)
    sleep 5

    # Collect dump while process is still running
    echo -e "${GREEN}Collecting dump...${NC}"
    dotnet-dump collect -p $DOTNET_PID -o "$DUMP_FILE" 2>/dev/null || true

    # Wait for process to finish
    wait $APP_PID 2>/dev/null || true

    echo -e "${GREEN}Dump saved to: $DUMP_FILE${NC}"
}

# Analyze the dump
analyze_dump() {
    if [ ! -f "$DUMP_FILE" ]; then
        echo -e "${RED}No dump file found at $DUMP_FILE${NC}"
        return 1
    fi

    echo -e "${GREEN}Analyzing dump...${NC}"

    # Generate report header
    {
        echo "=== Memory Profile Report ==="
        echo "Profile: $PROJECT_NAME"
        echo "Date: $(date)"
        echo "Dump: $DUMP_FILE"
        echo ""
        echo "=== Heap Statistics (Top types by size) ==="
        echo ""
    } > "$REPORT_FILE"

    # Use dotnet-dump analyze
    echo -e "${GREEN}Generating heap statistics...${NC}"
    dotnet-dump analyze "$DUMP_FILE" -c "dumpheap -stat" -c "exit" 2>/dev/null | tail -60 >> "$REPORT_FILE"

    {
        echo ""
        echo "=== PropertyDescriptor Instances ==="
    } >> "$REPORT_FILE"
    dotnet-dump analyze "$DUMP_FILE" -c "dumpheap -stat -type PropertyDescriptor" -c "exit" 2>/dev/null | grep -v "^>" >> "$REPORT_FILE" || true

    {
        echo ""
        echo "=== JsArgumentsObject Instances ==="
    } >> "$REPORT_FILE"
    dotnet-dump analyze "$DUMP_FILE" -c "dumpheap -stat -type JsArgumentsObject" -c "exit" 2>/dev/null | grep -v "^>" >> "$REPORT_FILE" || true

    {
        echo ""
        echo "=== JsObject Instances ==="
    } >> "$REPORT_FILE"
    dotnet-dump analyze "$DUMP_FILE" -c "dumpheap -stat -type JsObject" -c "exit" 2>/dev/null | grep -v "^>" >> "$REPORT_FILE" || true

    {
        echo ""
        echo "=== Dictionary Instances ==="
    } >> "$REPORT_FILE"
    dotnet-dump analyze "$DUMP_FILE" -c "dumpheap -stat -type Dictionary" -c "exit" 2>/dev/null | grep -v "^>" >> "$REPORT_FILE" || true

    echo -e "${GREEN}Report saved to: $REPORT_FILE${NC}"
    echo ""
    echo -e "${YELLOW}=== Summary ===${NC}"
    cat "$REPORT_FILE"
}

# Interactive analysis
interactive_analysis() {
    echo ""
    echo -e "${YELLOW}Starting interactive analysis...${NC}"
    echo "Useful commands:"
    echo "  dumpheap -stat                    - Show all types by size"
    echo "  dumpheap -stat -type <TypeName>   - Show specific type stats"
    echo "  dumpheap -type <TypeName>         - Show all instances"
    echo "  gcroot <address>                  - Find what's keeping object alive"
    echo "  exit                              - Exit analyzer"
    echo ""
    dotnet-dump analyze "$DUMP_FILE"
}

# Main
main() {
    check_tools
    select_profile
    setup_output
    build_project
    run_and_collect
    analyze_dump

    echo ""
    read -p "Start interactive analysis? [y/N] " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        interactive_analysis
    fi

    echo -e "${GREEN}Done!${NC}"
}

main
