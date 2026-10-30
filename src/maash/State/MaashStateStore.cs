namespace Maash.State;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maash.Core;

/// <summary>A loaded module entry: module definition + resolved identity (alias/infix/primary).</summary>
public class LoadedModuleEntry {
    public MaashModule Module { get; set; } = new();
    public string Alias { get; set; } = "";
    public string Infix { get; set; } = "";
    /// <summary>True when this module is (or was loaded as) the primary module.</summary>
    [JsonPropertyName("primary")]
    public bool IsPrimary { get; set; }
}

/// <summary>The persisted session state: which modules are loaded + their identity.</summary>
public class MaashRegistryState {
    public string? PrimaryAlias { get; set; }
    public bool PrimaryInferred { get; set; }
    public List<LoadedModuleEntry> Modules { get; set; } = new();
}

public static class MaashStateStore {
    static string StatePath => StateDir.StateFile;

    public static MaashRegistryState Load() {
        var p = StatePath;
        if (!File.Exists(p)) {
            return new MaashRegistryState();
        }

        var json = File.ReadAllText(p);
        var s = JsonSerializer.Deserialize(json, MaashJsonContext.Default.MaashRegistryState);
        return s ?? new MaashRegistryState();
    }

    public static void Save(MaashRegistryState state) {
        var p = StatePath;
        var json = JsonSerializer.Serialize(state, MaashJsonContext.Default.MaashRegistryState);
        File.WriteAllText(p, json);
    }

    public static bool Exists() => File.Exists(StatePath);
}

/// <summary>Bridges the persisted state model to/from the runtime <see cref="ModuleRegistry"/>.</summary>
public static class StateBridge {
    /// <summary>Rebuild a runtime registry from persisted state (collisions already reconciled at load time).</summary>
    public static ModuleRegistry BuildRegistry(MaashRegistryState state) {
        var reg = new ModuleRegistry();
        foreach (var e in state.Modules) {
            if (string.IsNullOrEmpty(e.Alias)) {
                continue;
            }

            reg.Load(e.Module, e.Alias, e.Infix, e.IsPrimary, allowClobber: true, inferPrimary: false);
        }
        if (state.PrimaryInferred && state.PrimaryAlias is not null) {
            reg.RestoreInferredPrimary(state.PrimaryAlias);
        }

        return reg;
    }

    /// <summary>Serialize a runtime registry into its persisted form.</summary>
    public static MaashRegistryState FromRegistry(ModuleRegistry reg) {
        var s = new MaashRegistryState { PrimaryAlias = reg.PrimaryAlias, PrimaryInferred = reg.PrimaryInferred };
        foreach (var m in reg.Modules) {
            s.Modules.Add(new LoadedModuleEntry {
                Module = m.Module,
                Alias = m.Alias,
                Infix = m.Infix,
                IsPrimary = m.IsPrimary
            });
        }

        return s;
    }

    /// <summary>Load current state, or an empty one if none exists.</summary>
    public static MaashRegistryState LoadRegistryState() => MaashStateStore.Load();

    /// <summary>Load the current runtime registry (empty if no state persisted yet).</summary>
    public static ModuleRegistry LoadRegistry() => BuildRegistry(MaashStateStore.Load());

    /// <summary>Persist a registry to the session state file.</summary>
    public static void SaveRegistry(ModuleRegistry reg) => MaashStateStore.Save(FromRegistry(reg));
}
