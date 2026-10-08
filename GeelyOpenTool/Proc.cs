using System.Diagnostics;
using System.Text;

namespace GeelyOpenTool;

internal static class Paths
{
    public static string Base => AppContext.BaseDirectory;
    public static string ToolsDir => Path.Combine(Base, "tools");
    public static string PlatformTools => Path.Combine(ToolsDir, "platform-tools");
    public static string MtkDir => Path.Combine(ToolsDir, "mtkclient");
    public static string Backups => Path.Combine(Base, "backups");
    public static string Packages => Path.Combine(Base, "packages");

    public static string Adb
    {
        get
        {
            var bundled = Path.Combine(PlatformTools, "adb.exe");
            return File.Exists(bundled) ? bundled : "adb";
        }
    }

    public static string MtkPython(string fallback)
    {
        var venv = Path.Combine(MtkDir, ".venv", "Scripts", "python.exe");
        return File.Exists(venv) ? venv : fallback;
    }
}

internal static class Proc
{
    public static async Task<(int Code, string Output)> RunAsync(
        string file, IEnumerable<string> args, string? workDir, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir ?? Paths.Base,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        var sb = new StringBuilder();
        var gate = new object();
        using var p = new Process { StartInfo = psi };
        DataReceivedEventHandler handler = (_, e) =>
        {
            if (e.Data is null) return;
            lock (gate) sb.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        p.OutputDataReceived += handler;
        p.ErrorDataReceived += handler;

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            throw;
        }
        lock (gate) return (p.ExitCode, sb.ToString());
    }
}
