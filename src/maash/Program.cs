namespace Maash;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using Maash.Cli;
using Maash.Core;
using Maash.State;

public sealed class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var config = new ParserConfiguration
            {
                ResponseFileTokenReplacer = (string _t, out IReadOnlyList<string>? _l, out string? _e) => { _l = Array.Empty<string>(); _e = ""; return false; },
            };
            var parsed = BuildRoot().Parse(args, config);
            return parsed.Invoke();
        }
        catch (MaashException ex)
        {
            Console.Error.WriteLine("maash: " + ex.Message);
            return 1;
        }
    }

    public static Command BuildRoot()
    {
        var root = new RootCommand("Invoke language interpreters from bash.");

        var fileArg = new Argument<string?>("file") { Description = "Script file to run. No argument: read one line from stdin and print the preprocessed text.", DefaultValueFactory = _ => (string?)null };
        var quietOpt = new Option<bool>("-q", "--quiet") { Description = "Suppress stderr echoes.", DefaultValueFactory = _ => false };
        root.Add(fileArg);
        root.Add(quietOpt);
        root.SetAction((ParseResult pr) =>
        {
            var file = pr.GetValue(fileArg);
            var q = pr.GetValue(quietOpt);
            return DefaultRun(file, q);
        });

        var evalCmd = new Command("eval", "Evaluate a maash operator with args joined into one operand");
        var opArg = new Argument<string>("op") { Description = "Operator (e.g. @@, @@+, @@#)." };
        var evalArgs = new Argument<string[]>("args") { DefaultValueFactory = _ => Array.Empty<string>(), Description = "Extra operand pieces (joined with spaces into one operand)." };
        evalCmd.Add(opArg);
        evalCmd.Add(evalArgs);
        evalCmd.SetAction((ParseResult pr) =>
        {
            var op = pr.GetRequiredValue(opArg);
            var a = pr.GetValue(evalArgs);
            return EvalOp(op, a ?? Array.Empty<string>());
        });
        root.Add(evalCmd);

        var preCmd = new Command("preprocess", "Rewrite @@ tokens into maash eval invocations (no execution).");
        var inFileOpt = new Option<string?>("-f", "--file") { Description = "Read text from this file.", DefaultValueFactory = _ => (string?)null };
        var inQuietOpt = new Option<bool>("-q", "--quiet");
        var textArg = new Argument<string[]?>("text") { DefaultValueFactory = _ => (string[]?)null, Description = "Inline text (when -f is not given)." };
        preCmd.Add(inFileOpt);
        preCmd.Add(inQuietOpt);
        preCmd.Add(textArg);
        preCmd.SetAction((ParseResult pr) =>
        {
            return PreprocessRun(
                pr.GetValue(inFileOpt),
                pr.GetValue(inQuietOpt),
                textArg != null ? (pr.GetValue(textArg)) : null);
        });
        root.Add(preCmd);

        var initCmd = new Command("init", "Print bash snippet for .bashrc (run via: eval \"$(maash init)\").");
        initCmd.SetAction(_ => { Console.Write(BashSnippet.Init); return 0; });
        root.Add(initCmd);

        var helpCmd = new Command("help", "Show help for a module or operator.");
        var helpTopic = new Argument<string?>("topic") { Arity = ArgumentArity.ZeroOrOne };
        var helpFileOpt = new Option<string?>("-f", "--file") { Description = "Show help for a module by module file path.", DefaultValueFactory = _ => (string?)null };
        helpCmd.Add(helpTopic);
        helpCmd.Add(helpFileOpt);
        helpCmd.SetAction((ParseResult pr) =>
        {
            var f = pr.GetValue(helpFileOpt);
            if (f != null) return HelpRunFile(f);
            return HelpRun(pr.GetValue(helpTopic));
        });
        root.Add(helpCmd);

        root.Add(Ctl.BuildCommand());
        return root;
    }

    // ---------- run methods ----------

    internal static int DefaultRun(string? file, bool quiet)
    {
        string text;
        if (file != null)
        {
            if (!File.Exists(file)) { Console.Error.WriteLine("maash: no such file: " + file); return 2; }
            text = File.ReadAllText(file);
        }
        else if (Console.IsInputRedirected)
        {
            text = ReadAllStdin();
        }
        else
        {
            // interactive (TTY) stdin: read one line, preprocess, print (low-priority MVP mode)
            text = Console.ReadLine() ?? string.Empty;
            var reg2 = StateBridge.LoadRegistry();
            var (_, b2) = SplitHeader(text);
            var pre2 = Preprocessor.Run(reg2, b2);
            foreach (var w in pre2.Warnings) if (!quiet) Console.Error.WriteLine(w);
            Console.Out.Write(pre2.Text);
            return 0;
        }

        var (hdr, body) = SplitHeader(text);
        var reg = StateBridge.LoadRegistry();
        if (hdr.Count > 0)
        {
            foreach (var line in hdr)
            {
                var (d, warns) = Ctl.ApplyHeaderLine(reg, line, authoritativeUse: true);
                foreach (var w in warns) Console.Error.WriteLine("maash: " + w);
            }
            StateBridge.SaveRegistry(reg);
        }

        if (!quiet)
            foreach (var w in Ctl.BodyMaactlWarnings(body))
                Console.Error.WriteLine("maash: " + w);

        var pre = Preprocessor.Run(reg, body);
        foreach (var w in pre.Warnings) if (!quiet) Console.Error.WriteLine(w);

        // script/pipe mode: run the preprocessed bash in a bash child process
        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(pre.Text);
        psi.ArgumentList.Add("--");
        psi.Environment["MAASH_STATE_KEY"] = StateDir.SessionKey;
        var proc = Process.Start(psi) ?? throw new MaashException("failed to start bash");
        proc.StandardInput.Close();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    static string ReadAllStdin()
    {
        using var sr = new StreamReader(Console.OpenStandardInput());
        return sr.ReadToEnd();
    }

    internal static int EvalOp(string op, string[] extra)
    {
        var opText = op.StartsWith("@@") ? op : "@@" + op;
        var line = opText + (extra.Length > 0 ? " " + string.Join(" ", extra) : "");
        // eval: fully evaluate operators, return their result text to stdout
        var result = Evaluator.Run(line);
        Console.Out.Write(result.Output);
        return result.ExitCode;
    }

    internal static int PreprocessRun(string? file, bool quiet, string[]? inline)
    {
        string text;
        if (file != null)
        {
            if (!File.Exists(file)) { Console.Error.WriteLine("maash: no such file: " + file); return 2; }
            text = File.ReadAllText(file);
        }
        else if (inline != null && inline.Length > 0)
            text = string.Join(" ", inline);
        else if (Console.IsInputRedirected)
            text = ReadAllStdin();
        else
        {
            Console.Error.WriteLine("maash: no input (use -f file, pipe stdin, or pass text)");
            return 2;
        }

        var (hdr, body) = SplitHeader(text);
        var reg = StateBridge.LoadRegistry();
        if (hdr.Count > 0)
        {
            foreach (var line in hdr)
            {
                var (_, warns) = Ctl.ApplyHeaderLine(reg, line, authoritativeUse: true);
                foreach (var w in warns) Console.Error.WriteLine("maash: " + w);
            }
            StateBridge.SaveRegistry(reg);
        }

        var result = Preprocessor.Run(reg, body);
        foreach (var w in result.Warnings) if (!quiet) Console.Error.WriteLine("maash: " + w);
        Console.Out.Write(result.Text);
        return 0;
    }

    internal static int HelpRun(string? topic)
    {
        if (topic == null || topic.Length == 0)
        {
            Console.WriteLine("maash help [module-alias|module-name|operator]  (or -f module-file.json)");
            return 0;
        }
        var reg = StateBridge.LoadRegistry();
        if (reg.TryGetModule(topic, out var lm) && lm is not null)
        {
            Console.WriteLine("module: " + lm.Alias + "  (infix '" + lm.Infix + "', primary: " + lm.Primary + ")");
            var primaryAlias = reg.PrimaryAlias;
            var seen = new HashSet<string>();
            foreach (var (suffix, op) in ModuleRegistry.BuildOperatorStrings(lm.Module, lm.Infix, lm.Alias == primaryAlias, lm.AllowEmptySuffix))
            {
                if (!seen.Add(op)) continue;
                Console.WriteLine("  @@" + op);
            }
            return 0;
        }
        if (reg.Operators.TryGetValue(topic, out var lo))
        {
            Console.WriteLine("operator: @@" + lo.OperatorString + "  (alias=" + lo.Alias + ")");
            return 0;
        }
        Console.Error.WriteLine("maash: no topic found: " + topic);
        return 1;
    }

    internal static int HelpRunFile(string path)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine("maash: no such file: " + path); return 2; }
        var (module, name) = ModuleSource.LoadFromFile(path);
        Console.WriteLine("module: " + name + "  (file: " + path + ")");
        foreach (var (suffix, def) in module.Operators)
            Console.WriteLine("  @@" + (suffix.Length == 0 ? "" : suffix) + (def.Bracketed ? " [bracketed]" : "") + ": " + def.Description);
        return 0;
    }

    internal static (List<string>, string) SplitHeader(string text)
    {
        var lines = text.Split('\n');
        var hdr = new List<string>();
        var i = 0;
        while (i < lines.Length)
        {
            var t = lines[i].TrimEnd('\r').TrimStart();
            if (t == "^---$" || t == "---") break;
            bool isCtl = t.StartsWith("use ") || t.StartsWith("move ") || t.StartsWith("load ")
                || t.StartsWith("remove ") || t == "clean" || t.StartsWith("clean ") || t.StartsWith("set-primary ");
            if (isCtl) { hdr.Add(t); i++; continue; }
            if (t.Length > 0) break; // first non-blank, non-ctl line ends header
            i++;
        }
        return (hdr, string.Join('\n', lines[i..]));
    }
}
