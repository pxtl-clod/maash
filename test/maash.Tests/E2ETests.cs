using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Maash.Tests;

public class E2ETests
{
    static readonly string RepoRoot = Environment.GetEnvironmentVariable("MAASH_REPO") ?? FindRepo();
    static readonly string MaashBin = Path.Combine(RepoRoot, "src", "maash", "bin", "Debug", "net10.0", "maash");

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
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["MAASH_STATE_KEY"] = stateKey;
        psi.Environment["MAASH_MODULE_ROOTS"] = Path.Combine(RepoRoot, "modules");
        var proc = new Process { StartInfo = psi };
        proc.Start();
        var outp = proc.StandardOutput.ReadToEnd();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (outp, err, proc.ExitCode);
    }

    static string NewKey() => "e2e-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Eval_Echo_Module_JoinsArgs_And_Returns()
    {
        var key = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo", "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: rc={load.rc} err={load.err} out={load.outp}");
        var r = RunMaash(new[] { "eval", "@@", "hello", "world" }, key);
        Assert.True(r.rc == 0, $"eval rc={r.rc} err={r.err}");
        Assert.Equal("hello world", r.outp.Trim());
    }

    [Fact]
    public void Eval_Echo_Plus_UpperCases()
    {
        var key = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo", "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: {load.err}");
        var r = RunMaash(new[] { "eval", "@@+", "hey", "there" }, key);
        Assert.True(r.rc == 0, $"eval: {r.err}");
        Assert.Equal("HEY THERE", r.outp.Trim());
    }

    [Fact]
    public void Preprocess_Fixture_Script_EmitsWrappers()
    {
        var key = NewKey();
        var script = Path.Combine(RepoRoot, "tests", "fixtures", "prep_ok.maash");
        Assert.True(File.Exists(script), "fixture missing: " + script);
        var r = RunMaash(new[] { "preprocess", "-f", script }, key);
        Assert.True(r.rc == 0, $"preprocess: rc={r.rc} err={r.err}");
        Assert.Contains("$(maash eval '@@' 'hello one')", r.outp);
        Assert.Contains("$(maash eval '@@+' 'UPPER TWO')", r.outp);
    }

    [Fact]
    public void Ctl_Use_Then_Help_ListsModule()
    {
        var key = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "echo", "-f", Path.Combine(RepoRoot, "modules", "echo.json") }, key);
        Assert.True(load.rc == 0, $"load: rc={load.rc} err={load.err}");
        var help = RunMaash(new[] { "help", "echo" }, key);
        Assert.True(help.rc == 0, $"help: {help.err}");
        Assert.Contains("module: echo", help.outp);
    }

    [Fact]
    public void Eval_Shout_Module_From_Fixture_UpperCases()
    {
        var key = NewKey();
        var load = RunMaash(new[] { "ctl", "use", "shout", "-f", Path.Combine(RepoRoot, "tests", "fixtures", "two_modules.json"), "-x", "#shout" }, key);
        Assert.True(load.rc == 0, $"load: rc={load.rc} err={load.err}");
        var r1 = RunMaash(new[] { "eval", "@@#shout", "hi" }, key);
        Assert.True(r1.rc == 0, $"r1: {r1.err}");
        Assert.Equal("HI", r1.outp.Trim());
        var r2 = RunMaash(new[] { "eval", "@@#shout#", "twice" }, key);
        Assert.True(r2.rc == 0, $"r2: {r2.err}");
        Assert.Equal("TWICE TWICE", r2.outp.Trim());
    }
}
