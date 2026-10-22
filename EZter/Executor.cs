using System.Diagnostics;
using System.Text;

namespace EZter;

// Turns a line of text into commands and runs them.
// Commands are separated by ';'. Quotes group words: "hello world" is one argument.
static class Executor
{
    const int MaxDepth = 20; // stops custom commands that call themselves forever

    static readonly Dictionary<string, (string Extension, string Exe, string[] Args)> Runners =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["python"] = (".py", "python", Array.Empty<string>()),
            ["powershell"] = (".ps1", "powershell", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File" }),
            ["cmd"] = (".cmd", "cmd.exe", new[] { "/c" }),
            ["node"] = (".js", "node", Array.Empty<string>()),
            ["csharp"] = (".cs", "dotnet", new[] { "run" }),
        };

    public static bool Execute(string line, int depth = 0)
    {
        // 'define' keeps its whole body, including ';', so the body can hold several steps
        var trimmed = line.Trim();
        var statements = trimmed.StartsWith("define ", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { trimmed }
            : SplitStatements(trimmed);

        foreach (var statement in statements)
        {
            var tokens = Tokenize(statement);
            if (tokens.Count == 0) continue;
            if (!RunCommand(tokens[0].ToLowerInvariant(), tokens.GetRange(1, tokens.Count - 1), depth))
                return false;
        }
        return true;
    }

    // Runs one built-in or custom command by name
    public static bool RunCommand(string name, List<string> args, int depth = 0)
    {
        if (Builtins.Table.TryGetValue(name, out var builtin))
            return builtin(args);

        var command = Program.Store.Load(name);
        if (command != null)
            return RunCustom(command, args, depth);

        Output.Line($"Unknown command '{name}'. Type 'help' to see what you can use.");
        return false;
    }

    public static bool RunCustom(CustomCommand command, List<string> args, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            Output.Line($"Too many nested commands (limit is {MaxDepth}).");
            return false;
        }

        foreach (var step in command.Steps)
        {
            if (step.Kind != StepKind.Code && step.Text.TrimStart().StartsWith('#')) continue;
            var ok = step.Kind switch
            {
                StepKind.Ez => Execute(Substitute(step.Text, args), depth + 1),
                StepKind.Shell => RunShell(Substitute(step.Text, args)),
                StepKind.Code => RunCode(step.Language, step.Text, args),
                _ => false,
            };
            if (!ok) return false;
        }
        return true;
    }

    public static bool RunShell(string commandLine) =>
        Output.RunProcess(new ProcessStartInfo("cmd.exe") { Arguments = "/c " + commandLine });

    // Writes the code to a temporary file and runs it with the language's interpreter.
    // Arguments reach the program the normal way (sys.argv, $args, %1, ...).
    public static bool RunCode(string language, string source, List<string> args)
    {
        if (!Runners.TryGetValue(language, out var runner))
        {
            Output.Line($"Unknown language '{language}'. Use: {string.Join(", ", Ezc.Languages)}.");
            return false;
        }

        var dir = Path.Combine(Path.GetTempPath(), "ezter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "code" + runner.Extension);
            File.WriteAllText(file, source, new UTF8Encoding(false));

            var psi = new ProcessStartInfo(runner.Exe) { WorkingDirectory = Directory.GetCurrentDirectory() };
            foreach (var a in runner.Args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(file);
            if (args.Count > 0 && language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
                psi.ArgumentList.Add("--");
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Output.RunProcess(psi);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // Replaces $1..$9 with arguments and $* with all of them
    static string Substitute(string body, List<string> args)
    {
        var result = body.Replace("$*", string.Join(' ', args.Select(Quote)));
        for (int i = 9; i >= 1; i--)
            result = result.Replace("$" + i, i <= args.Count ? Quote(args[i - 1]) : "");
        return result;
    }

    // Wraps text in quotes if it has spaces, so it stays one word when read back
    public static string Quote(string text) =>
        text.Contains(' ') || text.Length == 0 ? $"\"{text}\"" : text;

    public static List<string> SplitStatements(string text)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            if (c == ';' && !inQuotes)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false, hasToken = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                    hasToken = false;
                }
                continue;
            }
            sb.Append(c);
            hasToken = true;
        }
        if (hasToken) tokens.Add(sb.ToString());
        return tokens;
    }
}
