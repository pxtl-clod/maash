using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Maash.Tests;

public class E2ETests
{
    static readonly string RepoRoot  = Environment.GetEnvironmentVariable("MAASH_REPO")  ?? FindRepo();
    static readonly string MaashDir  = Path.Combine(RepoRoot, "src", "maash", "bin", "Debug", "net10.0");
    static readonly string MaashBin  = Path.Combine(MaashDir, "maash");

    static string FindRepo()
    {
        var cur = Path.GetFullPath(Environment.CurrentDirectory);
        while (cur.Length > 1)
        {
            if (Directory.Exists(Path.Combine(cur, "src", "maash"))) return cur;
            cur = Path.GetDirectoryName(cur) ?? "";
        }
        return cur;
    }

    static (string outp, string err, int rc) RunMaash(string[] args, string stateKey)
    {
        var psi = new ProcessStartInfo(MaashBin)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["MAASH_STATE_KEY"]    = stateKey;
        psi.Environment["MAASH_MODULE_ROOTS"] = Path.Combine(RepoRoot, "modules");
        var proc = new Process { StartInfo = psi };
        proc.Start();
        var outp = proc.StandardOutput.ReadToEnd();
        var err  = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (outp, err, proc.ExitCode);
    }

    static string NewKey() => "e2e-" + Guid.NewGuid().ToString("N");

    static (string outp, string err, int rc) RunBash(string script)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        var proc = new Process { StartInfo = psi };
        proc.Start();
        var outp = proc.StandardOutput.ReadToEnd();
        var err  = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (outp, err, proc.ExitCode);
    }

    // ── eval subcommand ──────────────────────────────────────────────────────

    [Fact]
    public void Eval_Echo_Module_JoinsArgs_And_Returns()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: rc={load.rc} err={load.err} out={load.outp}");
        var r = RunMaash(new[] { "eval", "@@", "hello", "world" }, key);
        Assert.True(r.rc == 0, $"eval rc={r.rc} err={r.err}");
        Assert.Equal("hello world", r.outp.Trim());
    }

    [Fact]
    public void Eval_Echo_Plus_UpperCases()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: {load.err}");
        var r = RunMaash(new[] { "eval", "@@+", "hey", "there" }, key);
        Assert.True(r.rc == 0, $"eval: {r.err}");
        Assert.Contains("HEY", r.outp);
    }

    [Fact]
    public void Eval_Bracketed_Module_Echoes()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: {load.err}");
        // @@#  is bracketed; when called via eval the operator is @@#
        var r = RunMaash(new[] { "eval", "@@#", "a b c" }, key);
        Assert.True(r.rc == 0, $"eval @@#: rc={r.rc} err={r.err}");
        Assert.Contains("a b c", r.outp);
    }

    // ── ctl / help ───────────────────────────────────────────────────────────

    [Fact]
    public void Ctl_Use_Then_Help_ListsModule()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: {load.err}");
        var help = RunMaash(new[] { "help", "echo" }, key);
        Assert.True(help.rc == 0, $"help: {help.err}");
        Assert.Contains("module", help.outp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Eval_Shout_Module_From_Fixture_UpperCases()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "shout",
            "-f", Path.Combine(RepoRoot, "tests", "fixtures", "two_modules.json"),
            "-x", "#shout" }, key);
        Assert.True(load.rc == 0, $"load: rc={load.rc} err={load.err}");
        var r1 = RunMaash(new[] { "eval", "@@#shout",  "hi"    }, key);
        Assert.True(r1.rc == 0, $"r1: {r1.err}");
        Assert.Equal("HI", r1.outp.Trim());
        var r2 = RunMaash(new[] { "eval", "@@#shout#", "twice" }, key);
        Assert.True(r2.rc == 0, $"r2: {r2.err}");
        Assert.Equal("TWICE TWICE", r2.outp.Trim());
    }

    // ── preprocess subcommand ────────────────────────────────────────────────

    [Fact]
    public void Preprocess_Fixture_Script_EmitsWrappers()
    {
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "prep_ok.maash");
        Assert.True(File.Exists(script), "fixture missing: " + script);
        var r = RunMaash(new[] { "preprocess", "-f", script }, key);
        Assert.True(r.rc == 0, $"preprocess: rc={r.rc} err={r.err}");
        Assert.Contains("maash eval", r.outp);
        Assert.Contains("'@@'",  r.outp);
        Assert.Contains("'@@+'", r.outp);
    }

    [Fact]
    public void Preprocess_Escaped_AtAt_Is_LeftUntouched()
    {
        var key = NewKey();
        var r   = RunMaash(new[] { "preprocess", "text with \\@@ escape" }, key);
        Assert.True(r.rc == 0, $"preprocess: rc={r.rc} err={r.err}");
    }

    // ── render subcommand (hotkey-eval form) ─────────────────────────────────

    [Fact]
    public void Render_Fixture_FullyEvaluates_Operators()
    {
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "render_test.maash");
        Assert.True(File.Exists(script), "fixture missing: " + script);
        var r = RunMaash(new[] { "render", "-f", script }, key);
        Assert.True(r.rc == 0, $"render: rc={r.rc} err={r.err}");
        // Fully evaluated: no @@ operators and no maash eval wrappers remain.
        Assert.DoesNotContain("@@", r.outp);
        Assert.DoesNotContain("maash eval", r.outp);
        // A $( ) wrapping only the op collapses to a quoted literal...
        Assert.Contains("one='HELLO ONE'", r.outp);
        // ...but one with a command prefix keeps the substitution.
        Assert.Contains("two=$(echo f 'two words')", r.outp);
        // The rendered output is pure bash that runs as-is.
        var (outp, berr, brc) = RunBash(r.outp);
        Assert.True(brc == 0, $"bash: rc={brc} err={berr}");
        Assert.Contains("one=HELLO ONE", outp);
        Assert.Contains("two=f two words", outp);
    }

    [Fact]
    public void Render_Inline_With_State_Evaluates_Operators()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: {load.err}");
        // Note: a trailing space before ')' is part of the operand (same as
        // the eval-wrapper path, where the quoted arg keeps it), so the
        // natural user form has no space before the closer.
        var r = RunMaash(new[] { "render", "echo $( @@+ hi there) tail" }, key);
        Assert.True(r.rc == 0, $"render: rc={r.rc} err={r.err}");
        Assert.Equal("echo 'HI THERE' tail", r.outp);
    }

    [Fact]
    public void Render_Stdin_HotkeyForm_Works()
    {
        // Mirrors the maash init hotkey function: maash render <<<"$line".
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: {load.err}");
        var (outp, err, rc) = RunBash(
            "MAASH_STATE_KEY='" + key + "' " +
            "MAASH_MODULE_ROOTS='" + Path.Combine(RepoRoot, "modules") + "' " +
            MaashBin + " render <<< 'echo $( @@+ hi)'");
        Assert.True(rc == 0, $"render: rc={rc} err={err}");
        Assert.Equal("echo 'HI'", outp.Trim());
    }

    [Fact]
    public void Init_Snippet_Is_Valid_Bash()
    {
        // The maash init output must be syntactically valid bash (it gets
        // eval'd into the user's .bashrc). We can't bind keys in a non-TTY
        // child, so syntax-check only: 'bash -n' parses without executing.
        var (outp, err, rc) = RunBash(MaashBin + " init | bash -n");
        Assert.True(rc == 0, $"bash -n: rc={rc} err={err} out={outp}");
    }

    // ── script-exec mode (maash <file>) ──────────────────────────────────────

    [Fact]
    public void ScriptExec_EchoFixture_PrintsResults()
    {
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "prep_ok.maash");
        Assert.True(File.Exists(script), "fixture missing: " + script);
        var r = RunMaash(new[] { script }, key);
        // prep_ok.maash has: echo first=$first second=$second  →  first=hello one second=UPPER TWO
        Assert.Contains("first=hello one", r.outp);
        Assert.Contains("UPPER TWO", r.outp);   // second=$second → UPPER TWO
    }

    [Fact]
    public void ScriptExec_Bracketed_StillWorks()
    {
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "prep_ok.maash");
        var r = RunMaash(new[] { script }, key);
        Assert.Contains("bracketed=a b c", r.outp);
    }

    [Fact]
    public void ScriptExec_Escaped_AtAt_ShouldNotBeTreatedAsOperator()
    {
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "prep_ok.maash");
        var r = RunMaash(new[] { script }, key);
        Assert.Contains("esc @@ notop", r.outp);
    }

    [Fact]
    public void ScriptExec_FileHeader_RelativeToScriptDir()
    {
        // Header `use echo -f ../../modules/echo.json` resolves relative to the
        // script's dir, so this works from any CWD.
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "echo_test.maash");
        var r = RunMaash(new[] { script }, key);
        Assert.True(r.rc == 0, $"rc={r.rc} err={r.err}");
        Assert.Contains("a=hello world b=SHOUT NOW", r.outp);
        Assert.Contains("bracketed=[hi there] end", r.outp);
        Assert.Contains("escaped @@ not an op", r.outp);
    }

    [Fact]
    public void ScriptExec_RawEvalInBody_LiteralOperatorEscaped()
    {
        // A user-written raw `maash eval` call must escape its literal '@@'
        // arg as \@@ (README escape rule); the preprocessor leaves it alone.
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "eval_test.maash");
        var r = RunMaash(new[] { script }, key);
        Assert.True(r.rc == 0, $"rc={r.rc} err={r.err}");
        Assert.Contains("eval form: x y z", r.outp);
    }

    [Fact]
    public void ScriptExec_Operand_With_Single_Quote_RoundTrips()
    {
        // Regression: QuoteBash must splice embedded single quotes as '\\''
        // or the generated bash is syntactically broken.
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "quote_test.maash");
        var r = RunMaash(new[] { script }, key);
        Assert.True(r.rc == 0, $"rc={r.rc} err={r.err}");
        Assert.Contains("q=it's here end", r.outp);
    }

    [Fact]
    public void Ctl_Move_Renames_Module_And_OpsFollow()
    {
        var key  = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo",
            "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: rc={load.rc} err={load.err} out={load.outp}");
        var mv = RunMaash(new[] { "ctl", "move", "echo", "-a", "shout", "-x", "S" }, key);
        Assert.True(mv.rc == 0, $"move: rc={mv.rc} err={mv.err}");
        var r = RunMaash(new[] { "eval", "@@S+", "shout", "now" }, key);
        Assert.True(r.rc == 0, $"eval rc={r.rc} err={r.err}");
        Assert.Equal("SHOUT NOW", r.outp.Trim());
    }

    [Fact]
    public void ScriptExec_MoveHeader_Renames_Module_And_Keeps_Ops()
    {
        // Header 'move echo -a shout -x S' renames the module; the bare
        // primary op and the new infix must both reach the same module.
        var key    = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "move_test.maash");
        var r = RunMaash(new[] { script }, key);
        Assert.True(r.rc == 0, $"rc={r.rc} err={r.err}");
        Assert.Contains("a=hello world", r.outp);
        Assert.Contains("b=SHOUT NOW", r.outp);
    }
}
