namespace Maash.Core;

using System;
using System.Text;
using Maash.State;
using static Maash.State.StateBridge;

/// <summary>
/// Result of evaluating a raw line: the transformed text, whether any operator was
/// found, and an exit code.
/// </summary>
public sealed record EvalResult(string Output, bool Parsed, int ExitCode);

/// <summary>
/// The runtime evaluator. Given a raw bash line, locates maash operators, spawns the
/// module interpreter for each, and substitutes the module's output for the operand
/// range. This is the exec path (vs the preprocessor, which only emits `maash eval`
/// invocations).
/// </summary>
public static class Evaluator
{
    /// <summary>Build a ModuleRegistry from the persisted session state.</summary>
    public static ModuleRegistry LoadModules()
    {
        var reg = new ModuleRegistry();
        foreach (var entry in StateBridge.LoadRegistry().Modules)
            try { reg.Load(entry.Module, entry.Alias, entry.Infix, entry.Primary, allowClobber: true); }
            catch { /* persisted state is assumed consistent */ }
        return reg;
    }

    /// <summary>Evaluate every operator in a raw line; return the transformed text.</summary>
    public static EvalResult Run(string line, bool quiet = false)
    {
        var modules = LoadModules();
        var outp = new StringBuilder(line.Length + 64);
        var pos = 0;

        while (pos < line.Length)
        {
            var idx = Preprocessor.FindOperator(line, pos);
            if (idx < 0) { outp.Append(Preprocessor.Unescape(line, pos, line.Length - pos)); break; }
            outp.Append(Preprocessor.Unescape(line, pos, idx - pos));

            var resolved = Preprocessor.TryResolve(modules, idx, line)
                ?? throw new MaashException($"No registered maash operator matches '@@' at index {idx}.");

            var afterToken = idx + 2 + resolved.OperatorString.Length;
            string operand;
            int end;
            if (resolved.Definition.Bracketed)
            {
                var close = Preprocessor.FindBracketClose(line, afterToken);
                if (close < 0) throw new MaashException($"Unterminated bracketed operand for '@@{resolved.OperatorString}'.");
                operand = line[afterToken..close];
                end = close + 3;
            }
            else
            {
                var next = Preprocessor.FindOperator(line, afterToken);
                var stop = next < 0 ? line.Length : next;
                operand = line[afterToken..stop];
                end = stop;
            }

            if (resolved is null) throw new MaashException($"No operator '@@' at index {idx}.");
            var loaded = modules.TryGetModule(resolved.Alias, out var lm) ? lm : null;
            if (loaded?.Module?.Interpreter == null)
                throw new MaashException($"Module '{resolved.Alias}' has no interpreter configured.");

            var ir = Invoker.Invoke(loaded.Module, resolved.Definition, "@@" + resolved.OperatorString, operand);
            if (ir.ExitCode != 0 && !quiet)
                Console.Error.WriteLine("maash: module '" + resolved.Alias + "' exited " + ir.ExitCode);

            int subEnd = end;
            if (!resolved.Definition.Bracketed && ir.ParsedLength > 0 && ir.ParsedLength < operand.Length)
                subEnd = afterToken + ir.ParsedLength;

            outp.Append(ir.Output);
            pos = subEnd;
        }

        var hasOp = Preprocessor.FindOperator(line, 0) >= 0;
        return new EvalResult(outp.ToString(), hasOp, 0);
    }

    /// <summary>Resolve an operator string (the text following @@) to its module + definition.</summary>
    public static (string Alias, MaashOperatorDefinition Def, string OpString)? ResolveOp(ModuleRegistry reg, string opString)
    {
        if (!reg.Operators.TryGetValue(opString, out var lo)) return null;
        if (!reg.TryGetModule(lo.Alias, out var m) || m is null) return null;
        if (!m.Module.Operators.TryGetValue(lo.Suffix, out var d)) return null;
        return (lo.Alias, d, lo.OperatorString);
    }
}
