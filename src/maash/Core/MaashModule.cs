using System.Text.Json.Serialization;

namespace Maash.Core;

public class MaashModule
{
    public string Name { get; set; } = "";
    public string? Alias { get; set; }
    public string? Infix { get; set; }
    public bool Primary { get; set; }
    public Dictionary<string, MaashOperatorDefinition> Operators { get; } = new();
}

public class MaashOperatorDefinition
{
    public string Suffix { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Bracketed { get; set; }
}
