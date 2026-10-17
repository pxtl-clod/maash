namespace Maash.Core;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Parsed module definition (JSON schema from README §"Module JSON").
/// </summary>
public sealed record MaashModule
{
    private const string Version = "v1";

    [JsonPropertyName("version")]
    public string VersionStr { get; init; } = Version;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("interpreter")]
    public MaashInterpreter? Interpreter { get; init; }

    [JsonPropertyName("shell")]
    public string? Shell { get; init; } = null;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("operators")]
    public Dictionary<string, MaashOperatorDefinition> Operators { get; init; } = new();

    /// <summary>Directory the module was loaded from (for shell-script relative paths). Not serialized.</summary>
    [JsonIgnore]
    public string? SourceDir { get; init; }

    /// <summary>The file this module definition was loaded from (if any). Not serialized.</summary>
    [JsonIgnore]
    public string? SourceFile { get; init; }
}

/// <summary>How to spawn the persistent interpreter process for a module.</summary>
public sealed record MaashInterpreter
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public string[] Args { get; init; } = System.Array.Empty<string>();
}

/// <summary>A single operator within a module.</summary>
public sealed record MaashOperatorDefinition
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("bracketed")]
    public bool Bracketed { get; init; }
}
