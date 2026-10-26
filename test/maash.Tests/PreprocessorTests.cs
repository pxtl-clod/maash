using Maash.Core;
using Xunit;

namespace Maash.Tests;

public class PreprocessorTests
{
    // Module with infix "@@", suffixes: primary "" (echo), "+" (uppercase)
    static MaashModule EchoModule() => new()
    {
        Name        = "echo",
        Description = "echo via python",
        Operators   = new Dictionary<string, MaashOperatorDefinition>
        {
            [""]  = new MaashOperatorDefinition { Description = "primary", Bracketed = false },
            ["+"] = new MaashOperatorDefinition { Description = "add",   Bracketed = false }
        },
        Interpreter = new MaashInterpreter { Command = "python3", Args = Array.Empty<string>() }
    };

    [Fact]
    public void Primary_Module_Rewrites_Bare_Infix_Operators()
    {
        var reg = new ModuleRegistry();
        reg.Load(EchoModule(), "echo", "@@", primary: true, allowClobber: true);
        var r = Preprocessor.Run(reg, "echo @@'hello world'@@");
        Assert.False(r.Text.Contains("@@'hello world'@@"), "bare @@ operator should have been rewritten");
        Assert.Contains("hello world", r.Text);
    }

    [Fact]
    public void Primary_Module_Suffixed_Operator_Fires()
    {
        var reg = new ModuleRegistry();
        reg.Load(EchoModule(), "echo", "@@", primary: true, allowClobber: true);
        var r = Preprocessor.Run(reg, "echo @@+'abc'");
        Assert.Contains("abc", r.Text);
        Assert.Contains("@@+", r.Text); // operator echoed in the maash eval call
    }

    [Fact]
    public void FindOperator_Finds_First_Operator()
    {
        var text = "no operator yet @@ then";
        var idx = Preprocessor.FindOperator(text, 0);
        Assert.Equal(text.IndexOf("@@", StringComparison.Ordinal), idx);
    }

    [Fact]
    public void FindOperator_Skipes_Escaped_AtSigns()
    {
        // A single @ or escaped @@ before a real operator should not match first.
        var text = "@ @@ then";
        var idx = Preprocessor.FindOperator(text, 0);
        Assert.Equal(2, idx);
    }

    [Fact]
    public void TryResolve_Matches_Primary_Module_With_Empty_Suffix()
    {
        var reg = new ModuleRegistry();
        reg.Load(EchoModule(), "echo", "@@", primary: true, allowClobber: true);
        var text = "echo @@op";
        var idx  = Preprocessor.FindOperator(text, text.IndexOf("@@", StringComparison.Ordinal));
        var resolved = Preprocessor.TryResolve(reg, idx, text);
        Assert.NotNull(resolved);
        Assert.Equal("echo", resolved!.Alias);
    }

    [Fact]
    public void QuoteBash_Wraps_Simple_Text_In_Single_Quotes()
    {
        Assert.Equal("'hello world'", Preprocessor.QuoteBash("hello world"));
    }

    [Fact]
    public void QuoteBash_Splices_Embedded_Single_Quotes()
    {
        // Classic bash splice for an embedded single quote is the 4-char
        // sequence: quote, backslash-quote, quote (close, escaped quote,
        // reopen). So 'it's' must come out as: ' i t  [splice]  s  '.
        var bs = ((char)92).ToString(); // backslash, indirect to keep this file escape-light
        var splice = "'" + bs + "'" + "'";
        var e1 = "'" + "it" + splice + "s" + "'";
        var e2 = "'" + "a" + splice + "b" + splice + "'";
        Assert.Equal(e1, Preprocessor.QuoteBash("it's"));
        Assert.Equal(e2, Preprocessor.QuoteBash("a'b'"));
    }

    [Fact]
    public void TryResolve_Returns_Null_When_No_Operator_String_Matches()
    {
        // Registry has module with infix "@@xyz" and suffix "abc" → only operator string "xyzabc".
        // Text "@@ op" has nothing after "@@" that starts with "xyzabc", so null.
        var reg = new ModuleRegistry();
        var mod = new MaashModule
        {
            Name = "unknown",
            Operators = new() { ["abc"] = new MaashOperatorDefinition { Description = "x" } }
        };
        reg.Load(mod, "unknown", "@@xyz", primary: false, allowClobber: true);
        var text = "@@ op";
        var resolved = Preprocessor.TryResolve(reg, Preprocessor.FindOperator(text, 0), text);
        Assert.Null(resolved);
    }
}

public class PreprocessorRenderTests
{
    // Fake live evaluator: '+' suffix upper-cases, bracketed '#' wraps in [],
    // otherwise echoes. Mirrors the echo fixture module's dispatch.
    static Func<ResolvedOperator, string, string> Eval() => (resolved, operand) =>
    {
        if (resolved.Definition.Bracketed) return "[" + operand + "]";
        if (resolved.OperatorString.EndsWith('+')) return operand.ToUpperInvariant();
        return operand;
    };

    static ModuleRegistry RenderReg()
    {
        var reg = new ModuleRegistry();
        var mod = new MaashModule
        {
            Name = "echo",
            Operators = new()
            {
                [""]  = new MaashOperatorDefinition { Description = "primary" },
                ["+"] = new MaashOperatorDefinition { Description = "upper" },
                ["#"] = new MaashOperatorDefinition { Description = "bracketed", Bracketed = true }
            }
        };
        reg.Load(mod, "echo", "@@", primary: true, allowClobber: true);
        return reg;
    }

    [Fact]
    public void Render_Bare_Operator_Is_Spliced_As_Quoted_Literal()
    {
        var reg = RenderReg();
        var r = Preprocessor.Run(reg, "echo @@+ x", Eval());
        Assert.Equal("echo 'X'", r.Text);
        Assert.DoesNotContain("maash eval", r.Text);
    }

    [Fact]
    public void Render_Paren_Operator_With_Command_Prefix_Keeps_Substitution()
    {
        var reg = RenderReg();
        var r = Preprocessor.Run(reg, "echo $(f @@+ x) more", Eval());
        Assert.Equal("echo $(f 'X') more", r.Text);
    }

    [Fact]
    public void Render_Paren_Operator_Alone_Collapses_To_Literal()
    {
        var reg = RenderReg();
        var r = Preprocessor.Run(reg, "echo $(@@+ x) tail", Eval());
        Assert.Equal("echo 'X' tail", r.Text);
    }

    [Fact]
    public void Render_Paren_Bracketed_Operator_Alone_Collapses_To_Literal()
    {
        var reg = RenderReg();
        var r = Preprocessor.Run(reg, "echo $( @@#[a b c]@@) end", Eval());
        Assert.Equal("echo '[a b c]' end", r.Text);
    }

    [Fact]
    public void Render_Multiple_Operators_Each_Evaluated()
    {
        var reg = RenderReg();
        var r = Preprocessor.Run(reg, "a=$(f @@+ one) b=$(@@+ two)", Eval());
        Assert.Equal("a=$(f 'ONE') b='TWO'", r.Text);
    }

    [Fact]
    public void Render_Escaped_AtAt_Stays_Literal()
    {
        var reg = RenderReg();
        var bs = "\\"; // single backslash
        var r = Preprocessor.Run(reg, "echo " + bs + "@@ notop", Eval());
        Assert.Equal("echo @@ notop", r.Text);
    }

    [Fact]
    public void Render_Result_With_Single_Quote_Is_BashQuoted()
    {
        var reg = RenderReg();
        var bs = "\\"; // single backslash
        var splice = "'" + bs + "''"; // '\''
        // Evaluator echoes; operand contains a single quote.
        var r = Preprocessor.Run(reg, "x=$(@@ it's)", (resolved, op) => op);
        Assert.Equal("x='it" + splice + "s'", r.Text);
    }
}

public class ModuleRegistryTests
{
    static MaashModule SingleOp() => new()
    {
        Name = "echo",
        Operators = new() { [""] = new MaashOperatorDefinition { Description = "x" } }
    };

    [Fact]
    public void Load_then_TryGetModule_Works()
    {
        var reg = new ModuleRegistry();
        reg.Load(SingleOp(), "echo", "@@", primary: false, allowClobber: true);
        Assert.True(reg.TryGetModule("echo", out var loaded));
        Assert.NotNull(loaded);
    }

    [Fact]
    public void Remove_Returns_True_And_Drops_Module()
    {
        var reg = new ModuleRegistry();
        reg.Load(SingleOp(), "echo", "@@", primary: false, allowClobber: true);
        Assert.True(reg.Remove("echo"));
        Assert.False(reg.TryGetModule("echo", out _));
    }

    [Fact]
    public void PrimaryAlias_Returns_Primary_Module_Alias()
    {
        var reg = new ModuleRegistry();
        reg.Load(SingleOp(), "echo", "@@", primary: true, allowClobber: true);
        Assert.Equal("echo", reg.PrimaryAlias);
    }

    [Fact]
    public void First_Load_Without_Flag_Is_Inferred_Primary()
    {
        var reg = new ModuleRegistry();
        reg.Load(SingleOp(), "echo", "@@", primary: false, allowClobber: true);
        Assert.Equal("echo", reg.PrimaryAlias);
    }

    [Fact]
    public void Move_Can_Reassign_Primary_Flag()
    {
        var reg = new ModuleRegistry();
        reg.Load(SingleOp(), "echo", "@@", primary: false, allowClobber: true);
        reg.Move("echo", "@@", primary: true);
        Assert.True(reg.TryGetModule("echo", out var loaded));
        Assert.True(loaded!.Primary);
    }
}
