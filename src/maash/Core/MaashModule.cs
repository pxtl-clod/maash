namespace Maash.Core;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Parsed module definition (JSON schema from README §"Module JSON").
/// </summary>
public sealed record MaashModule {
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

    /// <summary>
    /// Default infix for operators when the user does not pass one (-x/--infix).
    /// An empty string is meaningful: it registers operators with no infix
    /// (e.g. `@@+` instead of `@@echo+`). Falls back to the module name when unset.
    /// </summary>
    [JsonPropertyName("defaultInfix")]
    public string? DefaultInfix { get; init; }

    [JsonPropertyName("operators")]
    public Dictionary<string, MaashOperatorDefinition> Operators { get; init; } = new();

    /// <summary>Directory the module was loaded from (for shell-script relative paths).</summary>
    [JsonPropertyName("sourceDir")]
    public string? SourceDir { get; init; }

    /// <summary>The file this module definition was loaded from (if any).</summary>
    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; init; }
}

/// <summary>How to spawn the persistent interpreter process for a module.</summary>
public sealed record MaashInterpreter {
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public string[] Args { get; init; } = System.Array.Empty<string>();

    /// <summary>true if the interpreter command and args are relative to the module's SourceDir.</summary>
    public bool? RelativePaths { get; init; }
}

/// <summary>
/// A single operator within a module. Bracketed operators are those whose
/// suffix ends with `[` — the `[` is the implicit open bracket of the operand
/// (see README), e.g. suffixes `[` and `+[`.
/// </summary>
public sealed record MaashOperatorDefinition {
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}
