using System.Diagnostics;
using System.Globalization;

namespace EZter;

// The EZ commands that come with EZter
static class Builtins
{
    public static readonly Dictionary<string, Func<List<string>, bool>> Table = new()
    {
        ["help"] = Help,
        ["define"] = Define,
        ["forget"] = Forget,
        ["init"] = Init,
        ["commands"] = ListCustom,
        ["echo"] = args => { Output.Line(string.Join(' ', args)); return true; },
        ["time"] = _ => { Output.Line(DateTime.Now.ToString("HH:mm:ss")); return true; },
        ["date"] = _ => { Output.Line(DateTime.Now.ToString("yyyy-MM-dd dddd", CultureInfo.InvariantCulture)); return true; },
        ["sleep"] = Sleep,
        ["clear"] = Clear,
        ["cd"] = Cd,
        ["ls"] = Ls,
        ["open"] = Open,
        ["run"] = Run,
        ["exit"] = Exit,
    };

    static bool Help(List<string> _)
    {
        Output.Line("""
            Built-in commands:
              help                      show this list
              echo <text...>            print text
              time                      show the current time
              date                      show today's date
              sleep <seconds>           wait
              clear                     clear the screen
              cd [path]                 show or change the current folder
              ls [path]                 list files in a folder
              open <path>               open a file or folder/app with Windows
              run <program> [args...]   run an outside program and show its output
              define <name> <steps>     create your own command
              init <file> [name]        turn a script into a command (.ezc) right away
              forget <name>             delete one of your commands
              commands                 list your commands
              exit                      quit EZter

            Custom commands:
              Steps are separated by ';'. Use $1, $2, ... for arguments and $* for all of them.
              Example:  define hi echo Hello $1 ; time
                        hi Pawel
              For commands with outside programs or code, use the editor: run 'ezter' with no arguments.

            In a normal terminal every command is written "ez [command]", e.g. ez hi Pawel, ez time, ez ls.
            """);
        return true;
    }

    static bool Define(List<string> args)
    {
        if (args.Count < 2)
        {
            Output.Line("Usage: define <name> <steps>   e.g. define hi echo Hello $1");
            return false;
        }

        var name = args[0].ToLowerInvariant();
        if (Table.ContainsKey(name))
        {
            Output.Line($"'{name}' is a built-in command and can't be redefined.");
            return false;
        }
        if (!CommandStore.IsValidName(name))
        {
            Output.Line("Command names can use letters, digits, '-' and '_' (up to 40 characters).");
            return false;
        }

        var command = new CustomCommand { Name = name };
        var body = string.Join(' ', args.Skip(1).Select(Executor.Quote));
        foreach (var statement in Executor.SplitStatements(body))
            if (statement.Trim().Length > 0)
                command.Steps.Add(new Step(StepKind.Ez, statement.Trim()));

        Program.Store.Save(command);
        Output.Line($"Defined '{name}'. In a normal terminal: ez {name}");
        return true;
    }

    static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".py"] = "python",
        [".ps1"] = "powershell",
        [".cmd"] = "cmd",
        [".bat"] = "cmd",
        [".js"] = "node",
        [".cs"] = "csharp",
    };

    // init <file> [name]: copy a script into the commands folder as a .ezc command
    static bool Init(List<string> args)
    {
        if (args.Count is < 1 or > 2)
        {
            Output.Line("Usage: init <file> [name]   e.g. init backup.py");
            return false;
        }

        var file = args[0];
        if (!File.Exists(file))
        {
            Output.Line($"File not found: {file}");
            return false;
        }
        if (!LanguageByExtension.TryGetValue(Path.GetExtension(file), out var language))
        {
            Output.Line($"Don't know how to run '{Path.GetExtension(file)}' files. Supported: {string.Join(' ', LanguageByExtension.Keys)}");
            return false;
        }

        var name = (args.Count == 2 ? args[1] : Path.GetFileNameWithoutExtension(file)).ToLowerInvariant();
        if (Table.ContainsKey(name))
        {
            Output.Line($"'{name}' is a built-in command. Pick another name: init {file} <name>");
            return false;
        }
        if (!CommandStore.IsValidName(name))
        {
            Output.Line("Command names can use letters, digits, '-' and '_' (up to 40 characters). Pass a name: init <file> <name>");
            return false;
        }

        var command = new CustomCommand { Name = name, Description = $"From {Path.GetFileName(file)}" };
        command.Steps.Add(new Step(StepKind.Code, File.ReadAllText(file), language));
        Program.Store.Save(command);
        Output.Line($"Created '{name}' from {Path.GetFileName(file)}. In a normal terminal: ez {name}");
        return true;
    }

    static bool Forget(List<string> args)
    {
        if (args.Count != 1)
        {
            Output.Line("Usage: forget <name>");
            return false;
        }

        if (Program.Store.Delete(args[0].ToLowerInvariant()))
        {
            Output.Line($"Forgot '{args[0]}'.");
            return true;
        }
        Output.Line($"No custom command called '{args[0]}'.");
        return false;
    }

    static bool ListCustom(List<string> _)
    {
        var names = Program.Store.Names();
        if (names.Count == 0)
        {
            Output.Line("You have no custom commands yet. Try: define hi echo Hello $1");
            return true;
        }
        foreach (var name in names)
        {
            var command = Program.Store.Load(name);
            if (command == null) continue;
            var about = command.Description.Length > 0 ? command.Description : $"{command.Steps.Count} step(s)";
            Output.Line($"  {name,-16} {about}");
        }
        return true;
    }

    static bool Sleep(List<string> args)
    {
        if (args.Count != 1 || !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            Output.Line("Usage: sleep <seconds>");
            return false;
        }
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        return true;
    }

    static bool Clear(List<string> _)
    {
        if (!Output.Captured) Console.Clear();
        return true;
    }

    static bool Exit(List<string> _)
    {
        if (Output.Captured)
        {
            Output.Line("(exit is ignored in the editor)");
            return true;
        }
        Environment.Exit(0);
        return true;
    }

    static bool Cd(List<string> args)
    {
        if (args.Count == 0)
        {
            Output.Line(Directory.GetCurrentDirectory());
            return true;
        }

        var path = string.Join(' ', args);
        if (!Directory.Exists(path))
        {
            Output.Line($"Folder not found: {path}");
            return false;
        }
        Directory.SetCurrentDirectory(path);
        return true;
    }

    static bool Ls(List<string> args)
    {
        var path = args.Count == 0 ? "." : string.Join(' ', args);
        if (!Directory.Exists(path))
        {
            Output.Line($"Folder not found: {path}");
            return false;
        }

        foreach (var dir in Directory.GetDirectories(path).OrderBy(Path.GetFileName))
            Output.Line($"[DIR]  {Path.GetFileName(dir)}");
        foreach (var file in Directory.GetFiles(path).OrderBy(Path.GetFileName))
            Output.Line($"       {Path.GetFileName(file)}");
        return true;
    }

    static bool Open(List<string> args)
    {
        if (args.Count == 0)
        {
            Output.Line("Usage: open <path>");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(string.Join(' ', args)) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Output.Line($"Could not open: {ex.Message}");
            return false;
        }
    }

    static bool Run(List<string> args)
    {
        if (args.Count == 0)
        {
            Output.Line("Usage: run <program> [args...]");
            return false;
        }
        return Executor.RunShell(string.Join(' ', args.Select(Executor.Quote)));
    }
}
