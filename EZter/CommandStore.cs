using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EZter;

// Your custom commands: one .ezc file per command in Documents\EZter\commands
sealed partial class CommandStore
{
    readonly string _dir;

    public CommandStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        ImportLegacyJson(Path.Combine(Path.GetDirectoryName(dir)!, "commands.json"));
    }

    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    [GeneratedRegex("^[a-z0-9_-]{1,40}$")]
    private static partial Regex NamePattern();

    public IReadOnlyList<string> Names() =>
        Directory.GetFiles(_dir, "*.ezc")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public CustomCommand? Load(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? Ezc.Parse(name, File.ReadLines(path)) : null;
    }

    public void Save(CustomCommand command)
    {
        File.WriteAllText(PathFor(command.Name), Ezc.Write(command), new UTF8Encoding(false));
        Shims.Sync(this);
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        Shims.Sync(this);
        return true;
    }

    string PathFor(string name) => Path.Combine(_dir, name + ".ezc");

    // Version 1 of EZter kept commands in commands.json - turn those into .ezc files once
    void ImportLegacyJson(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return;
        try
        {
            var old = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(jsonPath));
            foreach (var (name, body) in old ?? new())
            {
                var key = name.ToLowerInvariant();
                if (!IsValidName(key) || File.Exists(PathFor(key))) continue;
                var command = new CustomCommand { Name = key };
                foreach (var statement in Executor.SplitStatements(body))
                    if (statement.Trim().Length > 0)
                        command.Steps.Add(new Step(StepKind.Ez, statement.Trim()));
                File.WriteAllText(PathFor(key), Ezc.Write(command), new UTF8Encoding(false));
            }
            File.Move(jsonPath, jsonPath + ".imported", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Leave the old file alone if it can't be read
        }
    }
}
