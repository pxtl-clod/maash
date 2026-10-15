using System.Text;
using Maash.Core;

namespace Maash.Core;

/// <summary>Result of a preprocess pass.</summary>
public sealed record PreprocessResult(string Text, IReadOnlyList<string> Warnings);

/// <summary>
/// A resolved maash operator: everything after <c>@@</c> in the text, mapped
/// to its module + operator definition.
/// </summary>
public sealed class ResolvedOperator
{
    public required string Alias { get; init; }
    public required MaashOperatorDefinition Definition { get; init; }
    public required string Suffix { get; init; }

    /// <summary>Full operator string following <c>@@</c> (i.e. after the two @ signs).</summary>
    public required string OperatorString { get; init; }
}

public class MaashPreprocessorException : Exception
{
    public MaashPreprocessorException(string message) : base(message) { }
}

/// <summary>
/// The maash preprocessor. Converts maash operators (@@...) in bash text
/// into escaped invocations of <c>maash eval</c>. This is syntactic sugar only:
/// it never evaluates operators itself.
/// </summary>
public static class Preprocessor
{
    /// <summary>
    /// Builds resolved operator-string candidates for a loaded module.
    /// Yields (resolvedOperator, operatorString) pairs.
    /// </summary>
    public static IEnumerable<(ResolvedOperator Resolved, string OperatorString)> BuildOperatorStrings(
        ModuleRegistry modules, LoadedModule module)
    {
        foreach (var pair in ModuleRegistry.BuildOperatorStrings(module.Module, module.Infix))
        {
            var (suffix, opString) = pair;
            var def = module.Module.Operators[suffix];
            yield return (new ResolvedOperator
            {
                Alias = module.Alias,
                Definition = def,
                Suffix = suffix,
                OperatorString = opString
            }, opString);
        }
    }

    /// <summary>
    /// Greedy longest-operator-wins match at a <c>@@</c> position.
    /// Returns null on no match.
    /// </summary>
    public static ResolvedOperator? TryResolve(ModuleRegistry modules, int position, string text)
    {
        ResolvedOperator? best = null;
        foreach (var loaded in modules.Modules)
        {
            foreach (var (resolved, opString) in BuildOperatorStrings(modules, loaded))
            {
                if (text.Length - (position + 2) < opString.Length) continue;
                if (!text.AsSpan(position + 2, opString.Length).StartsWith(opString, StringComparison.Ordinal))
                    continue;

                if (best is null || opString.Length > best.OperatorString.Length)
                    best = resolved;
            }
        }
        return best;
    }

    /// <summary>Finds the next unescaped <c>@@</c> at or after pos.</summary>
    public static int FindOperator(string text, int pos)
    {
        for (var i = pos; i + 1 < text.Length; i++)
        {
            if (text[i] == '@' && text[i + 1] == '@' &&
                (i == 0 || text[i - 1] != '\\'))
                return i;
        }
        return -1;
    }

    /// <summary>Finds the next unescaped <c>]@@</c> terminator at or after pos.</summary>
    public static int FindBracketClose(string text, int pos)
    {
        for (var i = pos; i + 2 < text.Length; i++)
        {
            if (text[i] == ']' && text[i + 1] == '@' && text[i + 2] == '@' &&
                (i == 0 || text[i - 1] != '\\'))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Runs the preprocessor over the whole text. Replaces each maash operator
    /// invocation with a bash command substitution
    /// <c>$(maash eval @@<opterand>)</c>.
    /// </summary>
    public static PreprocessResult Run(ModuleRegistry modules, string text)
    {
        var warnings = new List<string>();
        var output = new StringBuilder(text.Length + 64);
        var pos = 0;

        while (pos < text.Length)
        {
            var idx = FindOperator(text, pos);
            if (idx < 0)
            {
                output.Append(text, pos, text.Length - pos);
                break;
            }
            output.Append(text, pos, idx - pos);

            var resolved = TryResolve(modules, idx, text)
                ?? throw new MaashPreprocessorException(
                    $"No registered maash operator matches the '@@' sequence at index {idx}.");

            var afterToken = idx + 2 + resolved.OperatorString.Length;

            // Optional whitespace after operator before operand.
            while (afterToken < text.Length && char.IsWhiteSpace(text[afterToken]))
                afterToken++;

            string operand;
            int end;
            if (resolved.Definition.Bracketed)
            {
                var close = FindBracketClose(text, afterToken);
                if (close < 0)
                    throw new MaashPreprocessorException(
                        $"Unterminated bracketed operand for operator '@@{resolved.OperatorString}'.");
                operand = text[afterToken..close]; // strip trailing ]
                end = close + 3;                    // len("]@@")
            }
            else
            {
                var next = FindOperator(text, afterToken);
                var stop = next < 0 ? text.Length : next;
                operand = text[afterToken..stop];
                end = stop;
            }

            // Convention: operators are invoked without leading whitespace.
            var t = operand.TrimStart();

            output.Append("$(");
            output.Append(QuoteBash("@@" + resolved.OperatorString));
            output.Append(' ');
            output.Append(QuoteBash(t));
            output.Append(")");

            pos = end;
        }

        return new PreprocessResult(output.ToString(), warnings);
    }

    /// <summary>
    /// Bash single-quote quoting, escaped as <c>'\"'</c>.
    /// </summary>
    public static string QuoteBash(string s)
    {
        var builder = new StringBuilder(s.Length + 5);
        builder.Append('\'');
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\'')
            {
                // Close single-quote, insert literal ", reopen single-quote.
                // The literal double-quote is fine inside quotes too, but the
                // close/reopen handles it uniformly.
                builder.Append("'");
                builder.Append('\"');
                builder.Append("'");
            }
            else
            {
                builder.Append(s[i]);
            }
        }
        builder.Append('\'');
        return builder.ToString();
    }
}
