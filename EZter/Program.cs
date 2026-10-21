namespace EZter;

static class Program
{
    // Custom commands, launchers and settings live in Documents\EZter
    // (set EZTER_HOME to use another folder, e.g. for testing)
    public static readonly string DataDir = Environment.GetEnvironmentVariable("EZTER_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EZter");

    public static CommandStore Store = null!;

    static int Main(string[] args)
    {
        Directory.CreateDirectory(DataDir);
        Store = new CommandStore(Path.Combine(DataDir, "commands"));

        if (args.Length == 0 || args[0].Equals("edit", StringComparison.OrdinalIgnoreCase))
            return Editor.Run();

        switch (args[0].ToLowerInvariant())
        {
            case "repl":
                return Repl();

            case "run": // used by the EZ launchers: ezter run <command> [args...]
                if (args.Length < 2)
                {
                    Output.Line("Usage: ezter run <command> [args...]");
                    return 1;
                }
                return Executor.RunCommand(args[1].ToLowerInvariant(), args.Skip(2).ToList()) ? 0 : 1;

            case "install":
                Shims.Sync(Store);
                Shims.AddToPath();
                Output.Line($"EZ commands are ready in {Shims.BinDir} and on your PATH.");
                Output.Line("Open a new terminal and try: ez help");
                return 0;

            case "uninstall":
                Shims.RemoveFromPath();
                Output.Line("Removed EZ from your PATH.");
                return 0;

            default: // ezter echo hello
                return Executor.Execute(string.Join(' ', args.Select(Executor.Quote))) ? 0 : 1;
        }
    }

    static int Repl()
    {
        Shims.Sync(Store);
        Output.Line("EZter ready. Type 'help' for commands, 'exit' to quit.");
        while (true)
        {
            Console.Write("ezter> ");
            var line = Console.ReadLine();
            if (line == null) break;
            if (line.Trim().Length == 0) continue;
            Executor.Execute(line);
        }
        return 0;
    }
}
