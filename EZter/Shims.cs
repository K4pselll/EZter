using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EZter;

// Makes EZ commands usable from a normal terminal.
// One launcher, EZ.cmd, lives in Documents\EZter\bin (added to your user PATH).
// Every command is used as "ez [command]", e.g. "ez hi Pawel".
static class Shims
{
    public static string BinDir => Path.Combine(Program.DataDir, "bin");

    public static void Sync(CommandStore store)
    {
        Directory.CreateDirectory(BinDir);
        var launcher = Launcher();

        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EZ.cmd"] = $"@echo off\r\n{launcher} %*\r\n",
        };

        foreach (var (file, content) in wanted)
        {
            var path = Path.Combine(BinDir, file);
            if (!File.Exists(path) || File.ReadAllText(path) != content)
                File.WriteAllText(path, content);
        }

        // Remove launchers of commands that no longer exist
        foreach (var path in Directory.GetFiles(BinDir, "EZ*.cmd"))
            if (!wanted.ContainsKey(Path.GetFileName(path)))
                File.Delete(path);
    }

    static string Launcher()
    {
        var exe = Environment.ProcessPath ?? "dotnet";
        // Started with "dotnet EZter.dll": the launcher has to go through dotnet too
        return Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{exe}\" \"{typeof(Shims).Assembly.Location}\""
            : $"\"{exe}\"";
    }

    // ---- PATH ----

    public static bool IsOnPath() =>
        SplitPath(ReadUserPath()).Any(p => SamePath(p, BinDir));

    public static void AddToPath()
    {
        var current = ReadUserPath();
        if (SplitPath(current).Any(p => SamePath(p, BinDir))) return;
        WriteUserPath(current.Length == 0 ? BinDir : current.TrimEnd(';') + ";" + BinDir);
    }

    public static void RemoveFromPath()
    {
        var kept = SplitPath(ReadUserPath()).Where(p => !SamePath(p, BinDir));
        WriteUserPath(string.Join(';', kept));
    }

    static string[] SplitPath(string path) =>
        path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static bool SamePath(string a, string b) =>
        string.Equals(Environment.ExpandEnvironmentVariables(a).TrimEnd('\\'), b.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    // Read and write the registry directly so %VARIABLES% already in your PATH stay as they are
    static string ReadUserPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
    }

    static void WriteUserPath(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        key.SetValue("Path", value, RegistryValueKind.ExpandString);

        // Tell Windows so newly opened terminals pick up the change
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 0x0002, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);

    // ---- "don't ask again" for the PATH prompt ----

    static string NoPromptFile => Path.Combine(Program.DataDir, ".no-path-prompt");

    public static bool PromptDisabled => File.Exists(NoPromptFile);

    public static void DisablePrompt() => File.WriteAllText(NoPromptFile, "");
}
