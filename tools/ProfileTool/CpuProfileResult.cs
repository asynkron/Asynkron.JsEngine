using System.Collections.Generic;

namespace Asynkron.JsEngine.Tools.ProfileTool;

internal sealed record CpuProfileResult(
    IReadOnlyList<FunctionSample> AllFunctions,
    IReadOnlyList<FunctionSample> JsEngineFunctions,
    double TotalTime,
    double JsEngineTime,
    CallTreeNode CallTreeRoot,
    double CallTreeTotal);
