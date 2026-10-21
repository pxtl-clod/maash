namespace Maash.Core;

/// <summary>
/// Runtime registry of loaded maash modules. Owns the load-time collision rules
/// from the README: alias collisions are hard errors; fully-resolved operator
/// collisions are errors EXCEPT that the primary module may shadow non-primary
/// operators (with a warning).
/// </summary>
/// <summary>A module loaded with its resolved identity (alias is the lookup key).</summary>
public sealed record LoadedModule(MaashModule Module, string Alias, string Infix, bool Primary, bool SuppressAlnumRule)
{
    /// <summary>
    /// Whether this module's empty (primary) suffix may be a bare infix.
    /// Alphanumeric infixes force the "@" suffix on the primary operator
    /// unless the rule was suppressed at load time.
    /// </summary>
    public bool AllowEmptySuffix => SuppressAlnumRule || !IsAlnumInfix(Infix);

    /// <summary>An infix is alphanumeric when non-empty and every char is a letter or digit.</summary>
    public static bool IsAlnumInfix(string? infix)
    {
        if (string.IsNullOrEmpty(infix)) return false;
        foreach (var c in infix) if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }
}

public sealed class ModuleRegistry
{
    
    /// <summary>A fully-resolved operator: the operator string following <c>@@</c>.</summary>
    public sealed record LoadedOperator(
        string OperatorString, string Alias, string Suffix, bool ShadowedByNonPrimary);

    /// <summary>Resolved operators, keyed by the full operator string after <c>@@</c>.</summary>
    public Dictionary<string, LoadedOperator> Operators { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<LoadedModule> Modules => _modules.Values;

    private readonly Dictionary<string, LoadedModule> _modules = new(StringComparer.Ordinal);
    private string? _inferredPrimaryAlias;

    public bool TryGetModule(string alias, out LoadedModule? module)
    {
        module = null;
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

    public List<string> Load(MaashModule module, string? alias, string? infix, bool primary,
        bool allowClobber = false, bool suppressAlnumRule = false)
    {
        var warnings = new List<string>();
        var name = string.IsNullOrEmpty(alias) ? module.Name : alias;

        if (_modules.ContainsKey(name))
            throw new InvalidOperationException($"Module alias '{name}' is already loaded.");

        var isFirst = !_modules.Any();
        if (isFirst && !primary)
            _inferredPrimaryAlias = name;

        // A second non-primary load makes the inferred primary ambiguous: WARN and drop the flag.
        if (isFirst == false && primary == false && _inferredPrimaryAlias is not null && _inferredPrimaryAlias != name)
        {
            var old = _modules[_inferredPrimaryAlias];
            _modules[_inferredPrimaryAlias] = old with { Primary = false };
            warnings.Add($"WARN: primary is ambiguous (module '{name}' loaded after '{_inferredPrimaryAlias}'); primary flag removed from '{_inferredPrimaryAlias}'.");
            _inferredPrimaryAlias = null;
        }

        _modules[name] = new LoadedModule(module, name, infix ?? module.Name, primary, suppressAlnumRule);
        
        // If the only module is the inferred primary, make it primary so it gets
        // both unprefixed and infix operator registrations.
        var effectivePrimary = primary || (_inferredPrimaryAlias == name && _modules.Count == 1 && !primary);
        if (effectivePrimary && !primary) {
            _modules[name] = _modules[name] with { Primary = true };
        }

        if (primary && _inferredPrimaryAlias is not null && _inferredPrimaryAlias != name)
        {
            warnings.Add($"WARN: module '{_inferredPrimaryAlias}' was inferred primary; module '{name}' is now primary.");
            _inferredPrimaryAlias = null;
        }

        Reindex(warnings, allowClobber);
        return warnings;
    }

    /// <summary>
    /// Operator strings for a module. The primary module registers its operators
    /// unprefixed (@@ + suffix) and, when it has an infix, ALSO on the infix
    /// (so a shadowed primary can still be reached through its infix). Non-primary
    /// modules register only on their infix. Alphanumeric infixes force the "@"
    /// suffix on the empty (primary) suffix unless <paramref name="allowEmptySuffix"/>.
    /// Yields (originalSuffix, operatorStringAfterAtAt).
    /// </summary>
    public static IEnumerable<(string, string)> BuildOperatorStrings(MaashModule module, string infix,
        bool isPrimary = false, bool allowEmptySuffix = true)
    {
        foreach (var (suffix, _) in module.Operators)
        {
            var effective = (suffix.Length == 0 && !allowEmptySuffix) ? "@" : suffix;
            if (isPrimary)
            {
                yield return (suffix, suffix); // unprefixed: @@<suffix>
                if (infix.Length > 0)
                    yield return (suffix, infix + effective); // also reachable on the infix
            }
            else
            {
                yield return (suffix, infix + effective);
            }
        }
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
            foreach (var (suffix, opString) in BuildOperatorStrings(loaded.Module, loaded.Infix, loaded.Alias == primaryAlias, loaded.AllowEmptySuffix))
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
