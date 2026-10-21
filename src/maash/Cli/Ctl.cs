namespace Maash.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.CommandLine;
using Maash.Core;
using Maash.State;

/// <summary>
/// <c>maash ctl</c> subcommand (alias: maactl): manage loaded modules, primary
/// selection, and persistence. Same subcommands work in script headers (before
/// the ^---$ break) without the "ctl" prefix.
/// </summary>
public static class Ctl
{
    /// <summary>A directive parsed from one maactl line in a script header.</summary>
    public sealed class HeaderDirective
    {
        public string Verb = "";
        public string? Name;
        public string? Alias;
        public string? Infix;
        public bool Primary;
        public bool AllowClobber;
        public bool SuppressAlnumRule;
        public bool IsFile;
        public bool NameAliasPending;
    }

    /// <summary>Verbs recognized as maactl subcommands (for header/body detection).</summary>
    public static readonly string[] Verbs = { "use", "move", "load", "remove", "clean", "help" };

    /// <summary>
    /// True when a body line starts with a maactl verb (a script-mode maactl
    /// misuse: body maactl lines are only honored in the @@/maash eval context,
    /// never by the preprocessor).
    /// </summary>
    public static List<string> BodyMaactlWarnings(string body)
    {
        var warnings = new List<string>();
        foreach (var raw in body.Split('\n'))
        {
            var t = raw.TrimEnd('\r').TrimStart();
            foreach (var verb in Verbs)
            {
                if (t == verb || t.StartsWith(verb + " "))
                {
                    warnings.Add($"WARN: maactl subcommand '{verb}' in script body; only honored in the @@/maash eval context, not by the preprocessor.");
                    break;
                }
            }
        }
        return warnings;
    }

    public static Command BuildCommand()
    {
        var ctl = new Command("ctl", "Manage maash modules and state (alias: maactl).");

        var f = FileOpt();
        var i = InlineOpt();
        var a = AliasOpt();
        var x = InfixOpt();
        var p = PrimaryOpt();
        var ac = ClobberOpt();
        var sar = SuppressAlnumOpt();

        // use [module] -f|--file, -i|--inline, -a|--alias, -x|--infix, -p|--primary, --allowclobber, --suppress-alnum-rule
        var useCmd = new Command("use", "Load a module by name (search dirs), file, or inline JSON.");
        var moduleArg = new Argument<string?>("module") { Arity = ArgumentArity.ZeroOrOne, Description = "Module name (path relative to a module search dir); or use -f/-i. Use '-' for the primary position." };
        useCmd.Add((Argument) moduleArg);
        foreach (var o in new Option[] { f, i, a, x, p, ac, sar, QuietOpt() }) useCmd.Add(o);
        useCmd.SetAction(pr => RunUse(pr, pr.GetValue<string?>(moduleArg), f, i, a, x, p, ac, sar));
        ctl.Add(useCmd);

        // move [module-alias] (accepts use params)
        var moveCmd = new Command("move", "Move an existing module to a new alias/infix/primary.");
        var moveModuleArg = new Argument<string>("module") { Description = "Existing module alias." };
        moveCmd.Add(moveModuleArg);
        foreach (var o in new Option[] { a, x, p, sar }) moveCmd.Add(o);
        moveCmd.SetAction(pr => RunMove(pr.GetRequiredValue<string>(moveModuleArg), pr, a, x, p, sar));
        ctl.Add(moveCmd);

        // load [file-path] - alias of use -f
        var loadCmd = new Command("load", "Load a module file (alias of 'use -f').");
        var loadFileArg = new Argument<string>("file") { Description = "Path to a module JSON file. Use '-' for the primary position." };
        loadCmd.Add((Argument) loadFileArg);
        foreach (var o in new Option[] { a, x, p, ac, sar }) loadCmd.Add(o);
        loadCmd.SetAction(pr => UseRun(pr, pr.GetRequiredValue<string>(loadFileArg), null, a, x, p, ac, sar, asFile: true));
        ctl.Add(loadCmd);

        // remove [alias]
        var removeCmd = new Command("remove", "Remove a loaded module by alias.");
        var removeAliasArg = new Argument<string>("alias");
        removeCmd.Add(removeAliasArg);
        var rmQuiet = new Option<bool>("-q", "--quiet");
        removeCmd.Add(rmQuiet);
        removeCmd.SetAction(pr =>
        {
            var alias = pr.GetRequiredValue<string>(removeAliasArg);
            var reg = StateBridge.LoadRegistry();
            if (!reg.TryGetModule(alias, out _))
            {
                Console.Error.WriteLine("maash: no module with alias '" + alias + "'");
                return 1;
            }
            reg.Remove(alias);
            StateBridge.SaveRegistry(reg);
            if (pr.GetValue(rmQuiet)) return 0;
            Console.WriteLine("removed: " + alias);
            return 0;
        });
        ctl.Add(removeCmd);

        // clean
        var cleanCmd = new Command("clean", "Remove all imported modules.");
        var cleanQuiet = new Option<bool>("-q", "--quiet");
        cleanCmd.Add(cleanQuiet);
        cleanCmd.SetAction(pr =>
        {
            StateBridge.SaveRegistry(new ModuleRegistry());
            if (pr.GetValue(cleanQuiet)) return 0;
            Console.WriteLine("state cleaned");
            return 0;
        });
        ctl.Add(cleanCmd);

        // help [topic] [--file F]
        var helpCmd = new Command("help", "Show help for a module or operator (same as maash help).");
        var helpTopicArg = new Argument<string?>("topic") { Arity = ArgumentArity.ZeroOrOne };
        helpCmd.Add(helpTopicArg);
        var hf = new Option<string?>("-f", "--file") { DefaultValueFactory = _ => (string?)null };
        helpCmd.Add(hf);
        helpCmd.SetAction(pr =>
        {
            var f2 = pr.GetValue<string?>(hf);
            if (f2 != null) return Program.HelpRunFile(f2);
            return Program.HelpRun(pr.GetValue<string?>(helpTopicArg));
        });
        ctl.Add(helpCmd);

        return ctl;
    }

    // ---------- option factory (shared across subcommands; one instance added per command) ----------
    static Option<string?> FileOpt() => new("-f", "--file") { Description = "Module file path (overrides positional name).", DefaultValueFactory = _ => (string?)null };
    static Option<string?> InlineOpt() => new("-i", "--inline") { Description = "Inline module JSON (overrides positional name).", DefaultValueFactory = _ => (string?)null };
    static Option<string?> AliasOpt() => new("-a", "--alias") { Description = "Module alias (default: use-name or filename).", DefaultValueFactory = _ => (string?)null };
    static Option<string?> InfixOpt() => new("-x", "--infix") { Description = "Custom infix (default: module name).", DefaultValueFactory = _ => (string?)null };
    static Option<bool> PrimaryOpt() => new("-p", "--primary") { Description = "Mark as primary module.", DefaultValueFactory = _ => false };
    static Option<bool> ClobberOpt() => new("--allowclobber") { Description = "Allow clobbering an existing operator registration.", DefaultValueFactory = _ => false };
    static Option<bool> SuppressAlnumOpt() => new("--suppress-alnum-rule") { Description = "Do not force the '@' suffix on alphanumeric infixes.", DefaultValueFactory = _ => false };
    static Option<bool> QuietOpt() => new("-q", "--quiet") { Description = "Suppress stderr echoes.", DefaultValueFactory = _ => false };

    // ---------- handlers ----------

    internal static int RunUse(ParseResult pr, string? positionalOrFile, Option<string?> fOpt, Option<string?> iOpt,
        Option<string?> aOpt, Option<string?> xOpt, Option<bool> pOpt, Option<bool> acOpt, Option<bool> sarOpt)
        => UseRun(pr, positionalOrFile, pr.GetValue(fOpt), aOpt, xOpt, pOpt, acOpt, sarOpt, asFile: false, inline: pr.GetValue(iOpt));

    internal static int UseRun(ParseResult pr, string? nameArg, string? fileArg,
        Option<string?> aOpt, Option<string?> xOpt, Option<bool> pOpt, Option<bool> acOpt, Option<bool> sarOpt,
        bool asFile = false, string? inline = null)
    {
        try
        {
            var alias = pr.GetValue(aOpt);
            var infix = pr.GetValue(xOpt);
            var primary = pr.GetValue(pOpt);
            var allowClobber = pr.GetValue(acOpt);
            var suppressAlnum = pr.GetValue(sarOpt);

            MaashModule module;
            string declared;
            if (inline is not null)
                (module, declared) = LoadInline(inline, asFile ? nameArg : alias);
            else if (fileArg != null)
                (module, declared) = ModuleSource.LoadFromFile(fileArg);
            else
            {
                var byName = asFile ? (fileArg ?? nameArg) : (nameArg ?? fileArg);
                if (byName is null) throw new MaashException("module name, -f file, or -i inline required");
                (module, declared) = LoadByNameOrPath(byName);
            }

            var usedAlias = alias ?? declared;
            var reg = StateBridge.LoadRegistry();
            var warns = reg.Load(module, usedAlias, infix, primary, allowClobber: allowClobber, suppressAlnumRule: suppressAlnum);
            StateBridge.SaveRegistry(reg);
            foreach (var w in warns) Console.Error.WriteLine("maash: " + w);
            Console.WriteLine("loaded: " + usedAlias + "  (infix '" + (infix ?? declared) + "')");
            return 0;
        }
        catch (MaashException ex) { Console.Error.WriteLine("maash: " + ex.Message); return 1; }
        catch (InvalidOperationException ex) { Console.Error.WriteLine("maash: " + ex.Message); return 1; }
    }

    static int RunMove(string alias, ParseResult pr, Option<string?> aOpt, Option<string?> xOpt, Option<bool> pOpt, Option<bool> sarOpt)
    {
        try
        {
            var reg = StateBridge.LoadRegistry();
            if (!reg.TryGetModule(alias, out var lm) || lm is null)
            {
                Console.Error.WriteLine("maash: no module with alias '" + alias + "'");
                return 1;
            }
            var newAlias = pr.GetValue(aOpt);
            var newInfix = pr.GetValue(xOpt);
            var newPrimary = pr.GetValue(pOpt);

            if (newAlias != null && newAlias != alias)
            {
                if (reg.Modules.Any(m => m.Alias == newAlias))
                {
                    Console.Error.WriteLine("maash: alias '" + newAlias + "' is already in use");
                    return 1;
                }
                reg.Remove(alias);
                reg.Load(lm.Module, newAlias, newInfix ?? lm.Infix, newPrimary, suppressAlnumRule: lm.SuppressAlnumRule);
            }
            else
            {
                reg.Move(alias, newInfix, primary: newPrimary ? (bool?)true : null);
            }

            StateBridge.SaveRegistry(reg);
            Console.WriteLine("moved: " + (newAlias ?? alias));
            return 0;
        }
        catch (MaashException ex) { Console.Error.WriteLine("maash: " + ex.Message); return 1; }
        catch (InvalidOperationException ex) { Console.Error.WriteLine("maash: " + ex.Message); return 1; }
    }

    // ---------- module loading ----------

    static (MaashModule, string) LoadByNameOrPath(string nameOrPath)
        => LoadModulePath(nameOrPath, null);

    static (MaashModule, string) LoadModulePath(string nameOrPath, string? baseDir)
    {
        var path = ResolveModulePath(nameOrPath, baseDir);
        if (path.EndsWith(".json") || File.Exists(path))
            return ModuleSource.LoadFromFile(path);
        var found = ModuleSource.FindByName(nameOrPath);
        if (found != null) return ModuleSource.LoadFromFile(found);
        throw new MaashException("module not found: " + nameOrPath + " (search dirs: " + string.Join(":", ModuleSource.SearchDirs) + ")");
    }

    static (MaashModule, string) LoadInline(string json, string? declaredName)
    {
        var module = System.Text.Json.JsonSerializer.Deserialize<MaashModule>(json, MaashJsonContext.Default.MaashModule)
            ?? throw new MaashException("invalid inline module JSON");
        var name = !string.IsNullOrEmpty(declaredName) ? declaredName : module.Name;
        if (string.IsNullOrEmpty(name)) throw new MaashException("inline module must specify a name or an alias");
        return (module, name);
    }

    // ---------- script header support ----------

    /// <summary>
    /// Resolve a module path from a script header/CLI relative to the script's
    /// directory (so scripts are portable), falling back to the CWD-relative
    /// interpretation when no base dir is available.
    /// </summary>
    internal static string ResolveModulePath(string path, string? baseDir)
    {
        if (path.StartsWith("-")) return path;
        // Try baseDir-relative (script-portable) first, then CWD-relative.
        if (!string.IsNullOrEmpty(baseDir) && !Path.IsPathRooted(path))
        {
            var combined = Path.Combine(baseDir, path);
            if (File.Exists(combined)) return combined;
        }
        if (Path.IsPathRooted(path)) return path;
        if (File.Exists(path)) return path;
        return path;
    }

    public static (HeaderDirective Directive, List<string> Warnings) ApplyHeaderLine(ModuleRegistry reg, string line, bool authoritativeUse = false, string? baseDir = null)
    {
        var warns = new List<string>();
        var d = new HeaderDirective();
        var fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0) return (d, warns);

        d.Verb = fields[0];
        var pos = 1;
        while (pos < fields.Length)
        {
            var tok = fields[pos];
            if (!tok.StartsWith("-"))
            {
                // Positional: first one is the module name; a second is an alias
                // (useful with -f where the name is derived from the file).
                if (d.Name == null) d.Name = tok;
                else if (d.Alias == null) d.Alias = tok;
                pos++;
                continue;
            }
            string val = "";
            bool hasVal = pos + 1 < fields.Length && !fields[pos + 1].StartsWith("-");
            if (hasVal) { val = fields[pos + 1]; pos += 2; } else pos++;

            switch (tok)
            {
                case "-p": case "--primary": d.Primary = true; break;
                case "--allowclobber": d.AllowClobber = true; break;
                case "--suppress-alnum-rule": d.SuppressAlnumRule = true; break;
                case "-a": case "--alias": d.Alias = val; break;
                case "-x": case "--infix": d.Infix = val; break;
                case "-q": case "--quiet": break;
                case "-f": case "--file":
                    d.IsFile = true;
                    if (d.Name != null && d.Alias == null && d.Name != val && val != "")
                        d.Alias = d.Name; // 'use <name> -f <file>': name becomes the alias
                    if (val != "") d.Name = val;
                    break;
                case "-i": case "--inline":
                    // Inline JSON in a header is not supported (use a file or search-dir module).
                    break;
            }
        }

        switch (d.Verb)
        {
            case "use":
            case "load":
                if (d.Name == null)
                {
                    warns.Add("maactl: 'use'/'load' requires a module name or -f/--file value.");
                    break;
                }
        var (mod, _) = LoadModulePath(d.Name!, baseDir);
                var alias = d.Alias ?? d.Name;
                // In a script header a 'use' for an already-loaded alias is re-entrant:
                // the header is the authoritative declaration, so clobber the prior load.
                if (authoritativeUse && reg.TryGetModule(alias, out _))
                    reg.Remove(alias);
                if (authoritativeUse && d.IsFile && d.Name is not null && d.Name.Contains("/") &&
                    reg.TryGetModule(System.IO.Path.GetFileNameWithoutExtension(d.Name), out var legacy))
                    reg.Remove(legacy!.Alias); // alias derived from the file path in a prior load
                var loadWarns = reg.Load(mod, alias, d.Infix, d.Primary,
                    allowClobber: d.AllowClobber, suppressAlnumRule: d.SuppressAlnumRule);
                warns.AddRange(loadWarns);
                break;
            case "move":
                if (d.Alias == null)
                {
                    warns.Add("maactl: 'move' requires -a/--alias.");
                    break;
                }
                if (!reg.TryGetModule(d.Alias, out var lm) || lm is null)
                {
                    warns.Add("maactl: no module with alias '" + d.Alias + "'.");
                    break;
                }
                warns.AddRange(reg.Move(d.Alias, d.Infix, primary: d.Primary ? (bool?)true : null));
                break;
            case "remove":
                reg.Remove(d.Alias ?? "");
                break;
            case "clean":
                reg.Clean();
                break;
            case "help":
                break;
            default:
                warns.Add("maactl: unknown verb '" + d.Verb + "'.");
                break;
        }
        return (d, warns);
    }
}
