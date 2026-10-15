using System.Text.Json.Serialization;

namespace Maash.Core;

/// <summary>A maash module as declared in a module json definition.</summary>
public class MaashModule
{
    /// <summary>Module name: the extensionless path relative to a module search dir.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional interpreter command used to spawn the persistent module process.</summary>
    public MaashInterpreter? Interpreter { get; set; }

    /// <summary>Operators keyed by their registered suffix (empty suffix "" is the default operator).</summary>
    public Dictionary<string, MaashOperatorDefinition> Operators { get; } = new();

    public Dictionary<string, object?> Metadata { get; set; } = new();
}

/// <summary>How to spawn the persistent interpreter process for a module.</summary>
public class MaashInterpreter
{
    /// <summary>Command (and its fixed arguments) launched per module-session.</summary>
    public string? Command { get; set; }
}

public class MaashOperatorDefinition
{
    public string Description { get; set; } = "";

    /// <summary>
    /// Bracketed mode: the operand ends at the next unescaped <c>]@@</c>, as the module
    /// cannot itself report the parsed operand length.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bracketed { get; set; }
}
