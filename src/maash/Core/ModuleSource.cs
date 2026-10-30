namespace Maash.Core;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Maash.State;

/// <summary>
/// Loads module definitions from the session's module search directories and
/// from arbitrary file paths (used by `maactl use`).
/// </summary>
public static class ModuleSource {
    /// <summary>
    /// Module search dirs: $MAASH_MODULE_ROOTS (colon-sep, if set), then the standard
    /// install dir and the user dir from the README.
    /// </summary>
    public static IReadOnlyList<string> SearchDirs {
        get {
            var list = new List<string>();
            var env = Environment.GetEnvironmentVariable("MAASH_MODULE_ROOTS");
            if (!string.IsNullOrEmpty(env)) {
                foreach (var p in env.Split(':', StringSplitOptions.RemoveEmptyEntries)) {
                    list.Add(p);
                }
            }
            // Development convenience: fall back to <cwd>/modules if it has a module.
            var cwdModules = System.IO.Path.Combine(Environment.CurrentDirectory, "modules");
            if (System.IO.Directory.Exists(cwdModules)) {
                list.Add(cwdModules);
            }

            list.Add("/usr/local/share/maash");
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home)) {
                list.Add(System.IO.Path.Combine(home, ".local", "share", "maash"));
            }

            return list;
        }
    }

    /// <summary>
    /// Load a module definition from an explicit path (a .json file). Returns the module + its
    /// declared name.
    /// </summary>
    public static (MaashModule Module, string DeclaredName) LoadFromFile(string path) {
        var json = System.IO.File.ReadAllText(path);
        var module = JsonSerializer.Deserialize<MaashModule>(json, MaashJsonContext.Default.MaashModule) ?? throw new MaashException("Invalid module definition file: " + path);
        var full = System.IO.Path.GetFullPath(path);
        var dirName = System.IO.Path.GetDirectoryName(full) ?? full;
        if (string.IsNullOrEmpty(module.Name)) {
            module = module with { Name = System.IO.Path.GetFileNameWithoutExtension(full) };
        }

        return (module with { SourceFile = full, SourceDir = dirName }, module.Name);
    }

    /// <summary>Recursively find a module JSON file. Matches by path-relative name (module name =
    /// extensionless path relative to a search dir) OR by declared `name` field.</summary>
    public static string? FindByName(string modName) {
        foreach (var dir in SearchDirs) {
            if (!System.IO.Directory.Exists(dir)) {
                continue;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.json", System.IO.SearchOption.AllDirectories)) {
                var rel = file.Substring(dir.Length).TrimStart(System.IO.Path.DirectorySeparatorChar)
                    .Replace(System.IO.Path.DirectorySeparatorChar, '/').Remove(".json".Length);
                if (rel == modName) {
                    return file;
                }

                try { if (LoadFromFile(file).Module.Name == modName) { return file; } } catch { /* skip bad files */ }
            }
        }
        return null;
    }

    /// <summary>Load a module by name via the search dirs (throws MaashException if not found).</summary>
    public static (MaashModule Module, string DeclaredName) LoadByName(string modName) {
        var file = FindByName(modName) ?? throw new MaashException(
            "module not found: '" + modName + "' (search: " + string.Join(":", SearchDirs) + ")");
        return LoadFromFile(file);
    }
}
