namespace Maash.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

/// <summary>
/// The M1 process protocol between maash and a module's interpreter process.
/// 
/// Request (maash → module stdin):
///   `M1 <opString> <operandByteLen>\n<operand bytes>`
/// Response (module stdout):
///   `M1 <parsedCharLen> <outputByteLen>\n<output bytes>`
/// Exit code: 0 on success, non-zero on error.
/// </summary>
public sealed record InvokeResult(string Output, int ParsedLength, int ExitCode);

public static class Invoker
{
    const int HeaderMaxBytes = 1 << 20; // 1 MiB

    /// <summary>Invoke a module interpreter for a single operator call. Blocks until the process exits.</summary>
    public static InvokeResult Invoke(MaashModule module, MaashOperatorDefinition def, string opString, string operand)
    {
        var interp = module.Interpreter ?? throw new MaashException($"Module '{module.Name}' has no interpreter.");
        var cmd = interp.Command ?? throw new MaashException($"Module '{module.Name}' interpreter has no command.");
        var opBytes = Encoding.UTF8.GetBytes(opString);
        var operandBytes = Encoding.UTF8.GetBytes(operand);

        var psi = new ProcessStartInfo(cmd) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in interp.Args) psi.ArgumentList.Add(a);
        var proc = Process.Start(psi) ?? throw new MaashException($"Failed to start '{cmd}'");

        try
        {
            var header = new MemoryStream();
            var request = Encoding.UTF8.GetBytes("M1 " + opString + " " + operandBytes.Length + "\n");
            var stdin = proc.StandardInput.BaseStream;
            stdin.Write(request, 0, request.Length);
            if (operandBytes.Length > 0) stdin.Write(operandBytes, 0, operandBytes.Length);
            proc.StandardInput.Flush();
            proc.StandardInput.Close();

            var outBytes = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            var code = proc.ExitCode;
            return ParseResponse(outBytes, code);
        }
        catch (Exception)
        {
            try { proc.Kill(); } catch { }
            throw;
        }
    }

    internal static InvokeResult ParseResponse(string outText, int exitCode)
    {
        if (string.IsNullOrEmpty(outText))
            return new InvokeResult("", -1, exitCode);

        var nl = outText.IndexOf('\n');
        var header = nl < 0 ? outText : outText[0..nl];
        var rest = nl < 0 ? "" : outText[(nl + 1)..];

        if (!header.StartsWith("M1 "))
            return new InvokeResult(outText, -1, exitCode); // module did not conform; surface raw output

        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return new InvokeResult(outText, -1, exitCode);
        if (!int.TryParse(parts[1], out var pl) || !int.TryParse(parts[2], out var ol))
            return new InvokeResult(outText, -1, exitCode);

        var output = ol >= 0 && rest.Length >= ol ? Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(rest)[0..Math.Min(ol, rest.Length)]) : rest;
        return new InvokeResult(output, pl, exitCode);
    }
}
