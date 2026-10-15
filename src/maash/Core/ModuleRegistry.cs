namespace Maash.Core;

/// <summary>
/// Runtime registry of loaded maash modules. Owns the load-time collision rules
/// from the README: alias collisions are hard errors; fully-resolved operator
/// collisions are errors EXCEPT that the primary module may shadow non-primary
/// operators (with a warning).
/// </summary>
/// <summary>A module loaded with its resolved identity (alias is the lookup key).</summary>
public sealed record LoadedModule(MaashModule Module, string Alias, string Infix, bool Primary);

public sealed class ModuleRegistry
{
    /// <summary>Back-reference convenience: LoadedModule is top-level in this namespace.</summary>
    public sealed record loadedModuleAlias;


    /// <summary>A fully-resolved operator: the operator string following <c>@@</c>.</summary>
    public sealed record LoadedOperator(
        string OperatorString, string Alias, string Suffix, bool ShadowedByNonPrimary);

    /// <summary>Resolved operators, keyed by the full operator string after <c>@@</c>.</summary>
    public Dictionary<string, LoadedOperator> Operators { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<LoadedModule> Modules => _modules.Values;

    private readonly Dictionary<string, LoadedModule> _modules = new(StringComparer.Ordinal);
    private string? _inferredPrimaryAlias;

    public bool TryGetModule(string alias, out LoadedModule module)
    {
        return _modules.TryGetValue(alias, out module);
    }

    public string? PrimaryAlias
    {
        get
        {
            if (_inferredPrimaryAlias is not null && _modules.ContainsKey(_inferredPrimaryAlias))
                return _inferredPrimaryAlias;
            foreach (var (alias, m) in _modules)
                if (m.Primary) return alias;
            return null;
        }
    }

    public List<string> Load(MaashModule module, string? alias, string? infix, bool primary, bool allowClobber = false)
    {
        var warnings = new List<string>();
        var name = string.IsNullOrEmpty(alias) ? module.Name : alias;

        if (_modules.ContainsKey(name))
            throw new InvalidOperationException($"Module alias '{name}' is already loaded.");

        var effectivePrimary = primary;
        if (!_modules.Any() && !primary)
            _inferredPrimaryAlias = name;

        _modules[name] = new LoadedModule(module, name, infix ?? module.Name, effectivePrimary);

        if (primary && _inferredPrimaryAlias is not null && _inferredPrimaryAlias != name)
        {
            warnings.Add($"WARN: module '{_inferredPrimaryAlias}' was inferred primary; module '{name}' is now primary.");
            _inferredPrimaryAlias = null;
        }

        Reindex(warnings, allowClobber);
        return warnings;
    }

    public static IEnumerable<(string, string)> BuildOperatorStrings(MaashModule module, string infix)
    {
        foreach (var (suffix, _) in module.Operators)
            yield return (suffix, infix + suffix);
    }

    public List<string> Move(string alias, string? newInfix, bool? primary)
    {
        var warnings = new List<string>();
        if (!_modules.TryGetValue(alias, out var loaded))
            throw new InvalidOperationException($"Module alias '{alias}' is not loaded.");

        if (primary is false && _inferredPrimaryAlias == alias)
            _inferredPrimaryAlias = null;

        _modules[alias] = loaded with { Infix = newInfix ?? loaded.Infix, Primary = primary ?? loaded.Primary };
        Reindex(warnings, allowClobber: true);
        return warnings;
    }

    public bool Remove(string alias)
    {
        var removed = _modules.Remove(alias);
        if (_inferredPrimaryAlias == alias)
            _inferredPrimaryAlias = null;
        Reindex(allowClobber: true);
        return removed;
    }

    public void Clean()
    {
        _modules.Clear();
        _inferredPrimaryAlias = null;
        Reindex(allowClobber: true);
    }

    private void Reindex(List<string>? warnings = null, bool allowClobber = false)
    {
        Operators.Clear();

        var primaryAlias = PrimaryAlias;
        var ordered = _modules
            .Select(m => m.Value)
            .OrderBy(m => m.Alias == primaryAlias ? 0 : 1)
            .ToList();

        foreach (var loaded in ordered)
        {
            foreach (var (suffix, opString) in BuildOperatorStrings(loaded.Module, loaded.Infix))
            {
                bool collisionWarned = false;
                if (Operators.TryGetValue(opString, out var existing) && existing.Alias != loaded.Alias)
                {
                    bool meIsPrimary = primaryAlias == loaded.Alias;
                    bool existingIsPrimary = primaryAlias == existing.Alias;

                    if (!meIsPrimary && !existingIsPrimary)
                    {
                        if (!allowClobber)
                            throw new InvalidOperationException(
                                $"Operator '@@{opString}' is already registered by module '{existing.Alias}' (collision with '{loaded.Alias}').");
                        warnings?.Add($"WARN: operator '@@{opString}' clobbered by module '{loaded.Alias}'. Use an --infix to disambiguate.");
                    }
                    else
                    {
                        warnings?.Add($"WARN: operator '@@{opString}' registered by non-primary module '{loaded.Alias}' shadows the primary registration.");
                    }
                    collisionWarned = true;
                }

                var shadowed = collisionWarned;
                Operators[opString] = new LoadedOperator(opString, loaded.Alias, suffix, shadowed);
            }
        }
    }
}
