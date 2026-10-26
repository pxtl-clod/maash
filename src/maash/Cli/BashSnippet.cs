namespace Maash;

using System;

/// <summary>Bash integration snippet printed by <c>maash init</c>.</summary>
public static class BashSnippet
{
    public static readonly string Init =
"""
# maash bash integration (add to .bashrc via: eval "$(maash init)")
alias maactl='maash ctl'
@@() { maash eval "$1" "${@:2}"; }
# Ctrl+Alt+M hotkey: render all @@ ops on the current line in place.
# Requires bash 4.4+ (bind -x with READLINE_LINE/READLINE_POINT).
__maash_hotkey_render() {
  local __maash_r
  if __maash_r=$(maash render <<<"$READLINE_LINE" 2>/dev/null); then
    READLINE_LINE=$__maash_r
    READLINE_POINT=${#__maash_r}
  fi
}
bind -x '"\eCM": __maash_hotkey_render'
""";
}
