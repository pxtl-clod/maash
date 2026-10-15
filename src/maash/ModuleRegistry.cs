using System.Collections.Concurrent;
using Maash.Core;
using System.Text.Json;

namespace Maash;

public interface IModuleRegistry
{
    void LoadModuleFromFile(string filePath, string? alias = null);
    void LoadModuleFromInline(string json, string? alias = null);
    bool RemoveModule(string alias);
    void ClearModules();
    List<MaashModule> GetAllModules();
    MaashModule GetModuleByAlias(string alias);
    bool IsModuleLoaded(string alias);
    bool IsPrimaryModuleLoaded(string alias);
    bool IsOperatorAvailable(string infix, string suffix);
    MaashModule GetModuleByOperator(string infix, string suffix);
    string GetOperatorResult(string infix, string suffix, string operand);
    IEnumerable<(string Alias, MaashModule Module)> GetAllLoadedModules();
}

public class ModuleRegistry : IModuleRegistry
{
    private readonly ConcurrentDictionary<string, MaashModule> _modules = new();
    private MaashModule? _primaryModule;
    
    public void LoadModuleFromFile(string filePath, string? alias = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Module file not found: {filePath}");
            
        var json = File.ReadAllText(filePath);
        var module = JsonSerializer.Deserialize<MaashModule>(json);
        if (module == null)
            throw new InvalidDataException($"Failed to parse module from file: {filePath}");
            
        var moduleAlias = alias ?? module.Alias ?? module.Name;
        if (_modules.ContainsKey(moduleAlias))
            throw new InvalidOperationException($"Module with alias '{moduleAlias}' is already loaded");
            
        if (module.Primary)
        {
            if (_primaryModule != null)
                throw new InvalidOperationException("A primary module is already loaded");
            _primaryModule = module;
        }
        
        _modules[moduleAlias] = module;
    }
    
    public void LoadModuleFromInline(string json, string? alias = null)
    {
        var module = JsonSerializer.Deserialize<MaashModule>(json);
        if (module == null)
            throw new InvalidDataException("Failed to parse module from inline JSON");
            
        var moduleAlias = alias ?? module.Alias ?? module.Name;
        if (_modules.ContainsKey(moduleAlias))
            throw new InvalidOperationException($"Module with alias '{moduleAlias}' is already loaded");
            
        if (module.Primary)
        {
            if (_primaryModule != null)
                throw new InvalidOperationException("A primary module is already loaded");
            _primaryModule = module;
        }
        
        _modules[moduleAlias] = module;
    }
    
    public bool RemoveModule(string alias)
    {
        if (!_modules.TryRemove(alias, out var module))
            return false;
            
        if (module.Primary)
            _primaryModule = null;
            
        return true;
    }
    
    public void ClearModules()
    {
        _modules.Clear();
        _primaryModule = null;
    }
    
    public List<MaashModule> GetAllModules()
    {
        return _modules.Values.ToList();
    }
    
    public MaashModule GetModuleByAlias(string alias)
    {
        if (!_modules.TryGetValue(alias, out var module))
            throw new KeyNotFoundException($"Module with alias '{alias}' not found");
        return module;
    }
    
    public bool IsModuleLoaded(string alias)
    {
        return _modules.ContainsKey(alias);
    }
    
    public bool IsPrimaryModuleLoaded(string alias)
    {
        return _primaryModule?.Alias == alias;
    }
    
    public bool IsOperatorAvailable(string infix, string suffix)
    {
        foreach (var module in _modules.Values)
        {
            if (module.Infix == infix && module.Operators.ContainsKey(suffix))
                return true;
        }
        return false;
    }
    
    public MaashModule GetModuleByOperator(string infix, string suffix)
    {
        foreach (var module in _modules.Values)
        {
            if (module.Infix == infix && module.Operators.ContainsKey(suffix))
                return module;
        }
        throw new KeyNotFoundException($"Operator '{infix}{suffix}' not found in loaded modules");
    }
    
    public string GetOperatorResult(string infix, string suffix, string operand)
    {
        var module = GetModuleByOperator(infix, suffix);
        var operatorDef = module.Operators[suffix];
        
        // TODO: Actually invoke the operator using the interpreter
        // For now, just return a placeholder
        return $"Operator result for {infix}{suffix} with operand '{operand}'";
    }
    
    public IEnumerable<(string Alias, MaashModule Module)> GetAllLoadedModules()
    {
        foreach (var kvp in _modules)
        {
            yield return (kvp.Key, kvp.Value);
        }
    }
}