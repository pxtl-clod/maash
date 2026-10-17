namespace Maash;

using System;
using System.Collections.Generic;
using System.IO;
using Maash.Core;
using Maash.State;

/// <summary>Bash integration snippet printed by `maash init`.</summary>
public static class BashSnippet
{
    public static readonly string Init =
"# maash bash integration (add to .bashrc via: eval \"$(maash init)\")\n"
+ "alias maactl='maash ctl'\n"
+ "@@() { local op=\"$1\"; shift; maash eval \"$op\" \"$@\" }\n"
+ "# Ctrl+Alt+M hotkey: press to invoke maash on the current line (configure in .inputrc or your shell of choice)\n";
}
