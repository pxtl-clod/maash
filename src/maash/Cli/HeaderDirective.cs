namespace Maash.Cli;

/// <summary>A directive parsed from one maactl line in a script header.</summary>
public sealed class HeaderDirective {
    public string Verb = "";
    public string? Name;
    public string? Alias;
    public string? Infix;
    public bool IsPrimary;
    public bool AllowClobber;
    public bool SuppressAlnumRule;
    public bool IsFile;
}
