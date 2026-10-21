namespace Maash;

using System;
using System.Collections.Generic;
using System.IO;
using Maash.Core;
using Maash.State;

/// <summary>Bash integration snippet printed by <c>maash init</c>.</summary>
public static class BashSnippet
{
    public static readonly string Init =
        "# maash bash integration (add to .bashrc via: eval \"$(maash init)\")\n" +
        "alias maactl='maash ctl'\n" +
        "@@() { local op=\"$1\"; shift; maash eval \"$op\" \"$@\" }\n" +
        "# Ctrl+Alt+M hotkey — bind to run `maash preprocess` on the current input line.\n" +
        "# Add to .inputrc (readline) under the emacs keymap:\n" +
        "#   set keymap emacs\n" +
        "#   \"\\\\e[24m\": maash-preprocess\n" +
        "# Or, for emacs-mode bash use a shell alias and the readline bind:\n" +
        "#   alias mp='maash preprocess'\n" +
        "#   bind '\\\\e[24m': \"\\\\e[136b\"  # (emacs: Ctrl+Alt+m → \"a m\" → alias)\n";
}
