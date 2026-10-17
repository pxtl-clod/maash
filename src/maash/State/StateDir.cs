namespace Maash.State;

using System;
using System.IO;

/// <summary>Session state dir for maash ($TMPDIR/maash/[key]/).</summary>
public static class StateDir
{
    /// <summary>Session key from MAASH_STATE_KEY / MAASH_STATE_DIR env, else "interactive".</summary>
    public static string SessionKey
        => Environment.GetEnvironmentVariable("MAASH_STATE_KEY")
            ?? Environment.GetEnvironmentVariable("MAASH_STATE_DIR")
            ?? "interactive";

    /// <summary>The session root dir.</summary>
    public static string Root
    {
        get
        {
            var t = Environment.GetEnvironmentVariable("TMPDIR") ?? "/tmp";
            return Path.Combine(t, "maash", SessionKey);
        }
    }

    /// <summary>A sub-directory under the session root (created).</summary>
    public static string Dir(string sub)
    {
        var p = string.IsNullOrEmpty(sub) ? Root : Path.Combine(Root, sub);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>The modules dir.</summary>
    public static string ModulesDir => Dir("modules");

    /// <summary>Path to state.json in the session dir.</summary>
    public static string StateFile
    {
        get
        {
            var d = Dir("");
            return Path.Combine(d, "state.json");
        }
    }

    /// <summary>Path to a saved module definition file for an alias.</summary>
    public static string ModuleFile(string alias)
    {
        var d = ModulesDir;
        var safe = System.Text.RegularExpressions.Regex.Replace(alias, "[^A-Za-z0-9._-]", "_");
        return Path.Combine(d, safe + ".json");
    }
}
