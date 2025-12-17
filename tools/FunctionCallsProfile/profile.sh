#!/bin/bash
cd /Users/rogerjohansson/git/asynkron/JsEngine2/tools/FunctionCallsProfile
dotnet run -c Release &
PROFILE_PID=$!
sleep 2
sample $PROFILE_PID 6 1 -file /tmp/funcprofile.txt
wait $PROFILE_PID
echo "=== TOP HOT SPOTS ==="
cat /tmp/funcprofile.txt | grep -E "^\s+[0-9]+" | sort -rn | head -60
