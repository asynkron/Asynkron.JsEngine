namespace Asynkron.JsEngine.Runtime;

public sealed class RegExpStatics
{
    public string Input { get; set; } = string.Empty;
    public string LastMatch { get; set; } = string.Empty;
    public string LastParen { get; set; } = string.Empty;
    public string LeftContext { get; set; } = string.Empty;
    public string RightContext { get; set; } = string.Empty;
    public string[] Captures { get; } = new string[9];
}
