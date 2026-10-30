namespace Maash.Core;

using System;
using System.Collections.Generic;
using System.ComponentModel;
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

public static class Invoker {
    const int HeaderMaxBytes = 1 << 20; // 1 MiB

    /// <summary>Invoke a module interpreter for a single operator call. Blocks until the process exits.</summary>
    public static InvokeResult Invoke(MaashModule module, MaashOperatorDefinition def, string opString, string operand) {
        var interp = module.Interpreter ?? throw new MaashException($"Module '{module.Name}' has no interpreter.");
        var cmd = interp.Command ?? throw new MaashException($"Module '{module.Name}' interpreter has no command.");
        var opBytes = Encoding.UTF8.GetBytes(opString);
        var operandBytes = Encoding.UTF8.GetBytes(operand);

        var psi = new ProcessStartInfo(cmd) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in interp.Args) {
            psi.ArgumentList.Add(a);
        }
        // If the module has a SourceDir, resolve relative interpreter paths against it.
        // This lets module JSON declare args relative to the module directory.
        var workDir = module.SourceDir;
        if (!string.IsNullOrEmpty(workDir) && interp.RelativePaths != false) {
            if (!System.IO.Directory.Exists(workDir)) {
                workDir = null; // stale SourceDir → fall back to inherit-CWD
            }

            if (workDir is not null) {
                psi.WorkingDirectory = workDir;
            }
        }
        // Absolute interpreter arg paths are resolved against SourceDir at load time;
        // if a relative arg was not resolved and it exists under the SourceDir, use it.
        for (var i = 0; i < psi.ArgumentList.Count; i++) {
            var a = psi.ArgumentList[i];
            if (!Path.IsPathRooted(a) && !File.Exists(a) && workDir is not null && File.Exists(Path.Combine(workDir, a))) {
                psi.ArgumentList[i] = Path.GetFullPath(Path.Combine(workDir, a));
            }
        }

        Process proc;
        try {

            proc = Process.Start(psi) ?? throw new MaashException($"Failed to start '{cmd}'");
        } catch (Exception ex) when (ex is Win32Exception or FileNotFoundException) {
            throw new MaashException($"Failed to start module interpreter '{cmd}' (args: {string.Join(' ', interp.Args)}): {ex.Message}");
        }

        try {
            var request = Encoding.UTF8.GetBytes("M1 " + opString + " " + operandBytes.Length + "\n");
            System.Diagnostics.Trace.WriteLine($"[DBG] M1 op=<{opString}> operand=<{operand}> len={operandBytes.Length}");
            using var stdin = proc.StandardInput.BaseStream;
            stdin.Write(request, 0, request.Length);
            if (operandBytes.Length > 0) {
                stdin.Write(operandBytes, 0, operandBytes.Length);
            }

            stdin.Flush();
            stdin.Close();

            // Read the M1 header line byte-by-byte (it's short), then read exactly
            // the declared number of output bytes. Reading byte-by-byte guarantees we
            // never consume body bytes during the header read.
            var headerText = ReadHeaderLine(proc);
            System.Diagnostics.Trace.WriteLine($"[DBG] module response header=<{headerText}>");
            var result = ParseResponseHeader(headerText, proc);
            var body = new byte[Math.Max(0, result.OutputLength)];
            var read = ReadExact(proc, body);
            if (read < body.Length) {
                Array.Resize(ref body, read);
            }
            proc.WaitForExit();
            return new InvokeResult(Encoding.UTF8.GetString(body), result.ParsedLength, proc.ExitCode);
        } catch (Exception) {
            try { proc.Kill(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Read one line from the module's stdout (up to and including the newline).
    /// Returns the header text without the trailing newline.
    ///
    /// Uses a 1-byte read loop until the newline arrives, so we never consume
    /// body bytes prematurely.  The M1 header is short (tens of bytes) so this is fine.
    /// </summary>
    internal static string ReadHeaderLine(Process proc) {
        var stream = proc.StandardOutput.BaseStream;
        var sb = new System.Text.StringBuilder(64);
        while (true) {
            int b = stream.ReadByte();
            if (b < 0) {
                break; // EOF
            }

            if (b == '\n') {
                break;
            }

            if (b != '\r') {
                sb.Append((char)b);
            }
        }
        return sb.ToString();
    }

    /// <summary>Read exactly the requested length from the module's stdout. Returns bytes actually read.</summary>
    internal static int ReadExact(Process proc, byte[] buffer) {
        var stream = proc.StandardOutput.BaseStream;
        var off = 0;
        while (off < buffer.Length) {
            var n = stream.Read(buffer, off, buffer.Length - off);
            if (n <= 0) {
                break;
            }

            off += n;
        }
        return off;
    }

    internal static InvokeResult ParseResponse(string outText, int exitCode) {
        if (string.IsNullOrEmpty(outText)) {
            return new InvokeResult("", -1, exitCode);
        }

        var nl = outText.IndexOf('\n');
        var header = nl < 0 ? outText : outText[0..nl];
        var rest = nl < 0 ? "" : outText[(nl + 1)..];

        if (!header.StartsWith("M1 ")) {
            return new InvokeResult(outText, -1, exitCode); // module did not conform; surface raw output
        }

        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) {
            return new InvokeResult(outText, -1, exitCode);
        }

        if (!int.TryParse(parts[1], out var pl) || !int.TryParse(parts[2], out var ol)) {
            return new InvokeResult(outText, -1, exitCode);
        }

        var output = ol >= 0 && rest.Length >= ol ? Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(rest)[0..Math.Min(ol, rest.Length)]) : rest;
        return new InvokeResult(output, pl, exitCode);
    }

    internal record ParsedHeader(int ParsedLength, int OutputLength);

    /// <summary>
    /// Parse a module response header line (`M1 <parsedCharLen> <outputByteLen>`).
    /// Throws MaashException on malformed headers (the process is killed by the caller).
    /// </summary>
    internal static ParsedHeader ParseResponseHeader(string headerText, Process proc) {
        if (headerText.Length == 0) {
            proc.WaitForExit();
            throw new MaashException($"Module produced an empty response (exit code: {proc.ExitCode}). No M1 header.");
        }
        if (!headerText.StartsWith("M1 ")) {
            proc.WaitForExit();
            throw new MaashException($"Module did not emit an M1 response header; got: {headerText}");
        }
        var parts = headerText[3..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            (!long.TryParse(parts[0], out var pl) || pl < 0 || pl > int.MaxValue) ||
            (!long.TryParse(parts[1], out var ol) || ol < 0 || ol > HeaderMaxBytes)) {
            proc.WaitForExit();
            throw new MaashException($"Malformed M1 response header: '{headerText}'");
        }
        return new ParsedHeader((int)pl, (int)ol);
    }
}
