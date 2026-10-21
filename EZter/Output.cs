using System.Diagnostics;

namespace EZter;

// Everything EZter prints goes through here, so the editor can capture it and show it in a window
static class Output
{
    public static TextWriter Writer { get; set; } = Console.Out;
    public static bool Captured { get; set; }

    public static void Line(string text = "") => Writer.WriteLine(text);

    // Runs an outside program. In the editor its output is captured instead of drawn over the screen.
    public static bool RunProcess(ProcessStartInfo psi)
    {
        psi.UseShellExecute = false;
        if (Captured)
        {
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }

        try
        {
            using var process = Process.Start(psi)!;
            if (Captured)
            {
                process.StandardInput.Close();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Writer.Write(stdout.Result);
                Writer.Write(stderr.Result);
            }
            else process.WaitForExit();

            if (process.ExitCode != 0) Line($"(exit code {process.ExitCode})");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Line($"Could not start '{psi.FileName}': {ex.Message}");
            return false;
        }
    }
}
