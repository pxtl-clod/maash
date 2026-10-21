namespace Maash.Core;

using System;
using System.Text;
using Maash.State;
using static Maash.State.StateBridge;

public sealed record EvalResult(string Output, bool Parsed, int ExitCode);

public static class Evaluator
{
    public static ModuleRegistry LoadModules()
    {
        var reg = new ModuleRegistry();
        foreach (var entry in StateBridge.LoadRegistry().Modules)
            try { reg.Load(entry.Module, entry.Alias, entry.Infix, entry.Primary, allowClobber: true); }
            catch { /* persisted state is assumed consistent */ }
        return reg;
    }

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

            // Skip optional whitespace after the operator token (same as preprocessor).
            while (afterToken < line.Length && char.IsWhiteSpace(line[afterToken]))
                afterToken++;

            string operand;
            int end;
            // At runtime the operand is already delimited (by the shell in the $(maash eval ...)
            // wrapper or by the caller). Use the same non-bracketed scoping for all operators:
            // operand runs to the next @@ token (or EOF). The bracketed flag is only a
            // preprocessor concern (scoping [ ... ]@@ in raw script text).
            var next2 = Preprocessor.FindOperator(line, afterToken);
            var stop = next2 < 0 ? line.Length : next2;
            operand = line[afterToken..stop];
            end = stop;

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

    public static (string Alias, MaashOperatorDefinition Def, string OpString)? ResolveOp(ModuleRegistry reg, string opString)
    {
        if (!reg.Operators.TryGetValue(opString, out var lo)) return null;
        if (!reg.TryGetModule(lo.Alias, out var m) || m is null) return null;
        if (!m.Module.Operators.TryGetValue(lo.Suffix, out var d)) return null;
        return (lo.Alias, d, lo.OperatorString);
    }
}
