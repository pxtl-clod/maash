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

    /// <summary>The operator token emitted to <c>maash eval</c> (always starts with <c>@@</c>).</summary>
    public string EvalArg => "@@" + OperatorString;
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
    /// Greedy longest-operator-wins match at a <c>@@</c> position.
    /// Returns null on no match.
    /// </summary>
    public static ResolvedOperator? TryResolve(ModuleRegistry modules, int position, string text)
    {
        ResolvedOperator? best = null;
        var primaryAlias = modules.PrimaryAlias;
        foreach (var loaded in modules.Modules)
        {
            foreach (var (suffix, opString) in ModuleRegistry.BuildOperatorStrings(loaded.Module, loaded.Infix, loaded.Alias == primaryAlias, loaded.AllowEmptySuffix))
            {
                if (text.Length - (position + 2) < opString.Length) continue;
                if (!text.AsSpan(position + 2, opString.Length).StartsWith(opString, StringComparison.Ordinal))
                    continue;

                if (best is null || opString.Length > best.OperatorString.Length)
                    best = new ResolvedOperator
                    {
                        Alias = loaded.Alias,
                        Definition = loaded.Module.Operators[suffix],
                        Suffix = suffix,
                        OperatorString = opString
                    };
            }
        }
        return best;
    }

    /// <summary>
    /// Runs the preprocessor over the whole text. Replaces each maash operator
    /// invocation with a bash command substitution
    /// <c>$(maash eval '@@OPT' operand)</c>.
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
                output.Append(Unescape(text, pos, text.Length - pos));
                break;
            }
            output.Append(Unescape(text, pos, idx - pos));

            var resolved = TryResolve(modules, idx, text)
                ?? throw new MaashPreprocessorException(
                    $"No registered maash operator matches the '@@' sequence at index {idx}.");

            var afterToken = idx + 2 + resolved.OperatorString.Length;

            // Optional whitespace after operator before operand.
            while (afterToken < text.Length && char.IsWhiteSpace(text[afterToken]))
                afterToken++;

            string operand;
            int end;
            int parenClose = -1;       // index of the enclosing $( ) closer, if any
            var insideParen = false;   // true when @@ is inside a user $() — no wrapper
            if (resolved.Definition.Bracketed)
            {
                var close = FindBracketClose(text, afterToken);
                if (close < 0)
                    throw new MaashPreprocessorException(
                        $"Unterminated bracketed operand for operator '@@{resolved.OperatorString}'.");
                // [ and ] are delimiters, not part of the operand.
                var os = afterToken;
                if (os < close && text[os] == '[') os++;
                operand = text[os..close];
                end = close + 3;                    // len("]@@")
                // If the operator sits inside a user $( ), the user's own closer
                // will close the bare invocation we emit below.
                var p = FindBalancedCloseParen(text, idx, text.Length);
                if (p > idx) { insideParen = true; parenClose = p; }
            }
            else
            {
                // Non-bracketed operand scoping.
                //
                // If the operator sits inside a bash command-substitution
                // `$( ... )`, the operand must be scoped to that innermost
                // unmatched `)`. Maash's sugar wraps each invocation in `$( )`,
                // so scoping to the nearest balance closes the wrapping `$( )`
                // cleanly for the outer bash. The module remains responsible for
                // reporting how much it consumed at runtime (`maash eval`);
                // here we only need a valid bash-quoted string.
                var next = FindOperator(text, afterToken);
                var stop = next < 0 ? text.Length : next;

                var inner = FindBalancedCloseParen(text, afterToken, stop);
                if (inner >= 0)
                {
                    stop = inner;         // operand ends at innermost enclosing $( ) closer
                    parenClose = inner;
                    insideParen = true;   // preprocessor will NOT emit $( ) wrapper
                }

                operand = text[afterToken..stop];
                end = stop;
            }

            // Convention: operators are invoked without leading whitespace.
            var t = operand.TrimStart();

            // When the operator sits inside a user-provided $(), emit the maash eval
            // invocation bare so it becomes the body of that $(), consuming the closer.
            // When it is NOT inside $(), wrap the invocation in $( ) so the
            // substitution result is captured inline in the bash output.
            if (insideParen)
            {
                end = parenClose + 1;    // consume the ) so bash sees $(maash eval … )
                output.Append("maash eval ");
            }
            else
            {
                output.Append("$(maash eval ");
            }
            output.Append(QuoteBash("@@" + resolved.OperatorString));
            output.Append(' ');
            output.Append(QuoteBash(t));
            output.Append(")");

            pos = end;
        }

        return new PreprocessResult(output.ToString(), warnings);
    }

    /// <summary>
    /// Finds the innermost (first) closing paren that balances the deepest enclosing
    /// `$( ... )` containing <c>start</c>, but only if that paren lies within
    /// <c>[&#95;start, stop)</c>. Scanning forward from <c>start</c>, it tracks paren
    /// depth; the first `)` at depth <= 0 is the outermost-enclosing command-
    /// substitution closer. If the operator is NOT inside a `$( ... )` the search
    /// yields nothing and -1 is returned. Returns -1 when there is no enclosing
    /// `$( )` closer before <c>stop</c>.
    /// </summary>
    public static int FindBalancedCloseParen(string text, int start, int stop)
    {
        // Locate the nearest `$( ` open before the operand start; if it is still
        // open at `start`, return its matching `)` (the innermost closer), else -1.
        var back = text.LastIndexOf("$(", start);
        if (back < 0 || back + 1 >= start) return -1;   // no enclosing $(), or its paren is AT start position
        var depth = 0;
        for (var i = back; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth <= 0) return i;               // matching closer of that $()
            }
        }
        return -1;                                        // that $() never closed
    }

    /// <summary>
    /// Emit a run of source text verbatim, but unescape any <c>\@@</c>
    /// sequences (a backslash immediately before an operator token) into a
    /// bare, literal <c>@@</c>. Implements the README backslash rule:
    /// a leading backslash on an operator means "literal, no operator".
    /// </summary>
    public static string Unescape(string text, int start, int length)
    {
        var slice = text.Substring(start, length);
        var outp = new StringBuilder(slice.Length);
        var i = 0;
        while (i < slice.Length)
        {
            if (slice[i] == '\\' && i + 2 < slice.Length && slice[i + 1] == '@' && slice[i + 2] == '@')
            {
                outp.Append("@@");
                i += 3;
            }
            else
            {
                outp.Append(slice[i]);
                i++;
            }
        }
        return outp.ToString();
    }

    /// <summary>
    /// Bash single-quote quoting, escaping embedded single quotes using the
    /// classic <c>'\''</c> splice.
    /// </summary>
    public static string QuoteBash(string s)
    {
        var builder = new StringBuilder(s.Length + 5);
        builder.Append('\'');
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\'')
            {
                builder.Append("\'\"\'");
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
