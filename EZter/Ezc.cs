using System.Text;

namespace EZter;

enum StepKind { Ez, Shell, Code }

// One step of a command: an EZ command line, an outside command, or a piece of code
sealed record Step(StepKind Kind, string Text, string Language = "");

sealed class CustomCommand
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Step> Steps { get; } = new();
}

// The .ezc file format - plain lines, so it reads and writes fast:
//
//   #ezc 1
//   @desc Says hello
//   @ez echo Hello $1
//   @sh git status
//   @code python
//   print("hi")
//   @end
//
// Inside @code, a line that starts with '@' is written with an extra '@' in front.
static class Ezc
{
    public static readonly string[] Languages = { "python", "powershell", "cmd", "node", "csharp" };

    public static string Write(CustomCommand command)
    {
        var sb = new StringBuilder();
        sb.Append("#ezc 1\n");
        if (command.Description.Length > 0)
            sb.Append("@desc ").Append(OneLine(command.Description)).Append('\n');

        foreach (var step in command.Steps)
        {
            switch (step.Kind)
            {
                case StepKind.Ez:
                    sb.Append("@ez ").Append(OneLine(step.Text)).Append('\n');
                    break;
                case StepKind.Shell:
                    sb.Append("@sh ").Append(OneLine(step.Text)).Append('\n');
                    break;
                case StepKind.Code:
                    sb.Append("@code ").Append(step.Language).Append('\n');
                    foreach (var line in step.Text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
                    {
                        if (line.StartsWith('@')) sb.Append('@');
                        sb.Append(line).Append('\n');
                    }
                    sb.Append("@end\n");
                    break;
            }
        }
        return sb.ToString();
    }

    public static CustomCommand Parse(string name, IEnumerable<string> lines)
    {
        var command = new CustomCommand { Name = name };
        var code = new List<string>();
        string? codeLanguage = null;

        foreach (var line in lines)
        {
            if (codeLanguage != null)
            {
                if (line == "@end")
                {
                    command.Steps.Add(new Step(StepKind.Code, string.Join("\n", code), codeLanguage));
                    codeLanguage = null;
                    code.Clear();
                }
                else code.Add(line.StartsWith("@@") ? line[1..] : line);
            }
            else if (line.StartsWith("@desc ")) command.Description = line[6..];
            else if (line.StartsWith("@ez ")) command.Steps.Add(new Step(StepKind.Ez, line[4..]));
            else if (line.StartsWith("@sh ")) command.Steps.Add(new Step(StepKind.Shell, line[4..]));
            else if (line.StartsWith("@code ")) codeLanguage = line[6..].Trim();
        }

        // A file cut off before @end still keeps its code
        if (codeLanguage != null)
            command.Steps.Add(new Step(StepKind.Code, string.Join("\n", code), codeLanguage));
        return command;
    }

    static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();
}
