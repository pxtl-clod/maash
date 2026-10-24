namespace Maash;

using System;

/// <summary>Bash integration snippet printed by <c>maash init</c>.</summary>
public static class BashSnippet
{
    public static readonly string Init =
        "# maash bash integration (add to .bashrc via: eval \"$(maash init)\")\n" +
        "alias maactl='maash ctl'\n" +
        "@@() { maash eval \"$1\" \"${@:2}\"; }\n" +
        "# Ctrl+Alt+M hotkey: bind in .inputrc (readline) to run maash on current line.\n" +
        "# See .inputrc documentation for your specific shell.\n";
}
