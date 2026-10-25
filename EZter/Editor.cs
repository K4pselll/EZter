using System.Collections.ObjectModel;
using System.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace EZter;

// The interactive editor: build commands out of EZ commands, outside commands and pieces of code
static class Editor
{
    static CustomCommand _current = new();
    static string? _savedName;   // the name the command has on disk (null while it is new)
    static string _snapshot = "";

    static List<string> _names = new();
    static readonly List<string> _stepLabels = new();

    static ListView _commandList = null!;
    static ListView _stepList = null!;
    static TextField _nameField = null!;
    static TextField _descField = null!;
    static Label _status = null!;
    static IApplication _app = null!;

    public static int Run()
    {
        Shims.Sync(Program.Store);
        _app = Application.Create();
        try
        {
            _app.Init();
            _app.Keyboard.KeyBindings.Remove(Key.Q.WithCtrl); // quitting goes through our own check for unsaved changes
            var window = Build();
            ResetForm(new CustomCommand());
            RefreshCommands();
            _app.Invoke(OfferPath);
            _app.Run(window);
            window.Dispose();
        }
        finally
        {
            _app.Dispose();
        }
        return 0;
    }

    static readonly Color Background = new(31, 30, 47);
    static readonly Color Border = new(178, 177, 195);
    static readonly Color Accent = new(139, 180, 242);
    static readonly Color Text = new(137, 137, 154);

    static readonly Scheme Theme = new()
    {
        Normal = new Attribute(Text, Background),
        HotNormal = new Attribute(Accent, Background),
        Focus = new Attribute(Background, Accent),
        HotFocus = new Attribute(Background, Accent),
        Active = new Attribute(Background, Accent),
        HotActive = new Attribute(Background, Accent),
        Highlight = new Attribute(Background, Accent),
        Editable = new Attribute(Accent, Background),
        ReadOnly = new Attribute(Text, Background),
        Disabled = new Attribute(new Color(89, 89, 104), Background),
    };

    static readonly Scheme BorderTheme = new()
    {
        Normal = new Attribute(Border, Background),
        Focus = new Attribute(Border, Background),
        Active = new Attribute(Border, Background),
        HotNormal = new Attribute(Border, Background),
        HotFocus = new Attribute(Border, Background),
        HotActive = new Attribute(Border, Background),
        Highlight = new Attribute(Border, Background),
        Editable = new Attribute(Border, Background),
        ReadOnly = new Attribute(Border, Background),
        Disabled = new Attribute(Border, Background),
    };

    static T Themed<T>(T view) where T : View
    {
        view.SetScheme(Theme);
        view.Border?.View?.SetScheme(BorderTheme);
        return view;
    }

    // ---- layout ----

    static Window Build()
    {
        var menu = new MenuBar(new[]
        {
            new MenuBarItem("_File", new[]
            {
                new MenuItem("_New command", "", NewCommand, Key.N.WithCtrl),
                new MenuItem("_Save", "", () => Save(), Key.S.WithCtrl),
                new MenuItem("_Delete command", "", DeleteCommand, Key.Empty),
                new MenuItem("_Quit", "", Quit, Key.Q.WithCtrl),
            }),
            new MenuBarItem("S_tep", new[]
            {
                new MenuItem("_Add step", "", AddStep, Key.Empty),
                new MenuItem("_Edit step", "", EditSelectedStep, Key.Empty),
                new MenuItem("_Remove step", "", RemoveStep, Key.Empty),
                new MenuItem("Move _up", "", () => MoveStep(-1), Key.Empty),
                new MenuItem("Move _down", "", () => MoveStep(1), Key.Empty),
            }),
            new MenuBarItem("_Run", new[]
            {
                new MenuItem("_Run command", "", RunCurrent, Key.F5),
            }),
            new MenuBarItem("_Tools", new[]
            {
                new MenuItem("_Add EZ to PATH", "", AddToPath, Key.Empty),
                new MenuItem("_Remove EZ from PATH", "", RemoveFromPath, Key.Empty),
                new MenuItem("Re_fresh EZ launchers", "", () => { Shims.Sync(Program.Store); SetStatus("Launchers refreshed."); }, Key.Empty),
                new MenuItem("_Open commands folder", "", OpenFolder, Key.Empty),
            }),
            new MenuBarItem("_Help", new[]
            {
                new MenuItem("_About steps", "", ShowHelp, Key.Empty),
            }),
        });

        var win = Themed(new Window { Title = "EZter", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() });

        var listFrame = Themed(new FrameView { Title = "Commands (Enter = open)", X = 0, Y = 1, Width = 26, Height = Dim.Fill(1) });
        _commandList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _commandList.Accepting += (_, e) =>
        {
            if (_commandList.SelectedItem is int i && i < _names.Count) OpenCommand(_names[i]);
            e.Handled = true;
        };
        listFrame.Add(_commandList);

        var form = Themed(new FrameView { Title = "Command", X = 26, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(1) });
        _nameField = new TextField { X = 8, Y = 0, Width = 30 };
        _descField = new TextField { X = 8, Y = 1, Width = Dim.Fill(1) };

        var stepsFrame = Themed(new FrameView { Title = "Steps (Enter = edit)", X = 0, Y = 3, Width = Dim.Fill(), Height = Dim.Fill(2) });
        _stepList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _stepList.Accepting += (_, e) => { EditSelectedStep(); e.Handled = true; };
        stepsFrame.Add(_stepList);

        var add = new Button { Text = "_Add", X = 1, Y = Pos.AnchorEnd(1) };
        add.Accepted += (_, _) => AddStep();
        var edit = new Button { Text = "_Edit", X = Pos.Right(add) + 1, Y = Pos.AnchorEnd(1) };
        edit.Accepted += (_, _) => EditSelectedStep();
        var remove = new Button { Text = "Re_move", X = Pos.Right(edit) + 1, Y = Pos.AnchorEnd(1) };
        remove.Accepted += (_, _) => RemoveStep();
        var up = new Button { Text = "_Up", X = Pos.Right(remove) + 1, Y = Pos.AnchorEnd(1) };
        up.Accepted += (_, _) => MoveStep(-1);
        var down = new Button { Text = "_Down", X = Pos.Right(up) + 1, Y = Pos.AnchorEnd(1) };
        down.Accepted += (_, _) => MoveStep(1);
        var save = new Button { Text = "_Save", X = Pos.Right(down) + 3, Y = Pos.AnchorEnd(1) };
        save.Accepted += (_, _) => Save();
        var run = new Button { Text = "_Run", X = Pos.Right(save) + 1, Y = Pos.AnchorEnd(1) };
        run.Accepted += (_, _) => RunCurrent();

        form.Add(new Label { Text = "Name:", X = 1, Y = 0 }, _nameField,
                 new Label { Text = "About:", X = 1, Y = 1 }, _descField,
                 stepsFrame, add, edit, remove, up, down, save, run);

        _status = new Label { Text = "", X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };

        win.Add(menu, listFrame, form, _status);
        return win;
    }

    static void SetStatus(string text) => _status.Text = text;

    // ---- the form ----

    static void ResetForm(CustomCommand command)
    {
        _current = command;
        _savedName = null;
        _nameField.Text = command.Name;
        _descField.Text = command.Description;
        RefreshSteps(0);
        _snapshot = Snapshot();
    }

    // Copies what is typed in the fields into the command being edited
    static void Collect()
    {
        _current.Name = _nameField.Text.ToString()!.Trim().ToLowerInvariant();
        _current.Description = _descField.Text.ToString()!.Trim();
    }

    static string Snapshot()
    {
        Collect();
        return _current.Name + "\n" + Ezc.Write(_current);
    }

    static bool IsDirty() => Snapshot() != _snapshot;

    // True when it is fine to throw away what is on screen
    static bool ConfirmDiscard()
    {
        if (!IsDirty()) return true;
        return Query("Unsaved changes", "Save the changes to this command first?", "Save", "Discard", "Cancel") switch
        {
            0 => Save(),
            1 => true,
            _ => false,
        };
    }

    static void RefreshCommands()
    {
        _names = Program.Store.Names().ToList();
        _commandList.SetSource(new ObservableCollection<string>(_names));
    }

    static void RefreshSteps(int select)
    {
        _stepLabels.Clear();
        _stepLabels.AddRange(_current.Steps.Select(Describe));
        _stepList.SetSource(new ObservableCollection<string>(_stepLabels));
        if (_stepLabels.Count > 0)
            _stepList.SelectedItem = Math.Clamp(select, 0, _stepLabels.Count - 1);
    }

    static string Describe(Step step) => step.Kind switch
    {
        StepKind.Ez => $"[EZ]       {step.Text}",
        StepKind.Shell => $"[outside]  {step.Text}",
        _ => $"[{step.Language}]  {FirstLine(step.Text)}  ({step.Text.Split('\n').Length} lines)",
    };

    static int Query(string title, string message, params string[] buttons) => MessageBox.Query(_app, title, message, buttons) ?? -1;

    static int Fail(string title, string message, string ok = "OK") => MessageBox.ErrorQuery(_app, title, message, ok) ?? -1;

    static string FirstLine(string text) => text.Split('\n')[0].Trim();

    // ---- commands: new / open / save / delete ----

    static void NewCommand()
    {
        if (!ConfirmDiscard()) return;
        ResetForm(new CustomCommand());
        _nameField.SetFocus();
    }

    static void OpenCommand(string name)
    {
        if (!ConfirmDiscard()) return;
        var command = Program.Store.Load(name);
        if (command == null)
        {
            SetStatus($"Could not read {name}.ezc");
            return;
        }
        ResetForm(command);
        _savedName = name;
        SetStatus($"Opened {name}.ezc");
    }

    static bool Save()
    {
        Collect();
        var name = _current.Name;

        if (!CommandStore.IsValidName(name))
        {
            Fail("Name", "Give the command a name: 1-40 characters, letters, digits, '-' or '_'.", "OK");
            _nameField.SetFocus();
            return false;
        }
        if (Builtins.Table.ContainsKey(name))
        {
            Fail("Name", $"'{name}' is a built-in EZ command. Pick another name.", "OK");
            return false;
        }
        if (_current.Steps.Count == 0)
        {
            Fail("Steps", "Add at least one step first.", "OK");
            return false;
        }
        if (name != _savedName && Program.Store.Names().Contains(name) &&
            Query("Overwrite?", $"A command called '{name}' already exists. Replace it?", "Replace", "Cancel") != 0)
            return false;

        Program.Store.Save(_current);
        if (_savedName != null && _savedName != name)
            Program.Store.Delete(_savedName);

        _savedName = name;
        _snapshot = Snapshot();
        RefreshCommands();
        SetStatus($"Saved {name}.ezc - use it in a normal terminal as: ez {name}" +
                  (Shims.IsOnPath() ? "" : "  (add EZ to PATH from the Tools menu first)"));
        return true;
    }

    static void DeleteCommand()
    {
        if (_savedName == null)
        {
            ResetForm(new CustomCommand());
            return;
        }
        if (Query("Delete", $"Delete '{_savedName}'? 'ez {_savedName}' stops working too.", "Delete", "Cancel") != 0)
            return;

        Program.Store.Delete(_savedName);
        SetStatus($"Deleted {_savedName}.");
        ResetForm(new CustomCommand());
        RefreshCommands();
    }

    static void Quit()
    {
        if (ConfirmDiscard()) _app.RequestStop();
    }

    // ---- steps ----

    static void AddStep()
    {
        var steps = StepDialog(null);
        if (steps == null) return;
        int at = _current.Steps.Count == 0 ? 0 : (_stepList.SelectedItem ?? -1) + 1;
        _current.Steps.InsertRange(at, steps);
        RefreshSteps(at + steps.Count - 1);
    }

    static void EditSelectedStep()
    {
        if (_current.Steps.Count == 0)
        {
            AddStep();
            return;
        }
        int at = _stepList.SelectedItem ?? 0;
        var steps = StepDialog(_current.Steps[at]);
        if (steps == null) return;
        _current.Steps.RemoveAt(at);
        _current.Steps.InsertRange(at, steps);
        RefreshSteps(at);
    }

    static void RemoveStep()
    {
        if (_current.Steps.Count == 0) return;
        int at = _stepList.SelectedItem ?? 0;
        _current.Steps.RemoveAt(at);
        RefreshSteps(at);
    }

    static void MoveStep(int direction)
    {
        int at = _stepList.SelectedItem ?? 0, to = at + direction;
        if (_current.Steps.Count == 0 || to < 0 || to >= _current.Steps.Count) return;
        (_current.Steps[at], _current.Steps[to]) = (_current.Steps[to], _current.Steps[at]);
        RefreshSteps(to);
    }

    // Asks for a step. Several lines of an EZ or outside command become several steps.
    static List<Step>? StepDialog(Step? existing)
    {
        var kind = new OptionSelector
        {
            X = 1, Y = 1,
            Labels = new[] { "EZ command", "Outside command", "Piece of code" },
            Value = existing == null ? 0 : (int)existing.Kind,
        };
        int languageIndex = existing == null ? 0 : Math.Max(0, Array.IndexOf(Ezc.Languages, existing.Language));
        var language = new OptionSelector { X = 30, Y = 1, Labels = Ezc.Languages, Value = languageIndex };

        var text = Themed(new TextView
        {
            X = 1, Y = 8, Width = Dim.Fill(1), Height = Dim.Fill(1),
            TabKeyAddsTab = false,
            Text = existing?.Text ?? "",
        });

        bool accepted = false;
        var ok = new Button { Text = "_OK", IsDefault = true };
        ok.Accepted += (_, _) => { accepted = true; _app.RequestStop(); };
        var cancel = new Button { Text = "_Cancel" };
        cancel.Accepted += (_, _) => _app.RequestStop();

        var dialog = Themed(new Dialog
        {
            Title = existing == null ? "Add step" : "Edit step", Width = 80, Height = 28,
            Buttons = new[] { ok, cancel },
        });
        dialog.Add(
            new Label { Text = "Step type:", X = 1, Y = 0 }, kind,
            new Label { Text = "Language (code only):", X = 30, Y = 0 }, language,
            new Label { Text = "Content ($1, $2 ... are arguments; Tab moves to the next field):", X = 1, Y = 7 },
            text);
        _app.Run(dialog);

        if (!accepted) return null;

        var content = text.Text.ToString()!.ReplaceLineEndings("\n");
        if (content.Trim().Length == 0) return null;

        if (kind.Value == 2)
            return new List<Step> { new(StepKind.Code, content.TrimEnd(), Ezc.Languages[language.Value ?? 0]) };

        var stepKind = kind.Value == 0 ? StepKind.Ez : StepKind.Shell;
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new Step(stepKind, line))
            .ToList();
    }

    // ---- running ----

    static void RunCurrent()
    {
        Collect();
        if (_current.Steps.Count == 0)
        {
            SetStatus("Nothing to run yet - add a step first.");
            return;
        }

        var args = Ask("Run", "Arguments (optional):", "");
        if (args == null) return;

        var original = Output.Writer;
        var captured = new StringWriter();
        var folder = Directory.GetCurrentDirectory();
        bool ok;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        Output.Writer = captured;
        Output.Captured = true;
        try
        {
            ok = Executor.RunCustom(_current, Executor.Tokenize(args));
        }
        catch (Exception ex)
        {
            captured.WriteLine($"Error: {ex.Message}");
            ok = false;
        }
        finally
        {
            Output.Writer = original;
            Output.Captured = false;
            Directory.SetCurrentDirectory(folder);
        }

        captured.WriteLine();
        timer.Stop();
        var took = timer.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        captured.WriteLine(ok ? $"--- done {took}s ---" : $"--- stopped: a step failed ({took}s) ---");
        ShowText(ok ? "Output" : "Output (failed)", captured.ToString());
    }

    // ---- small dialogs ----

    static string? Ask(string title, string prompt, string initial)
    {
        bool accepted = false;
        var field = new TextField { Text = initial, X = 1, Y = 1, Width = Dim.Fill(1) };
        var ok = new Button { Text = "_OK", IsDefault = true };
        ok.Accepted += (_, _) => { accepted = true; _app.RequestStop(); };
        var cancel = new Button { Text = "_Cancel" };
        cancel.Accepted += (_, _) => _app.RequestStop();

        var dialog = Themed(new Dialog { Title = title, Width = 60, Height = 8, Buttons = new[] { ok, cancel } });
        dialog.Add(new Label { Text = prompt, X = 1, Y = 0 }, field);
        field.SetFocus();
        _app.Run(dialog);
        return accepted ? field.Text.ToString() : null;
    }

    static void ShowText(string title, string content)
    {
        var ok = new Button { Text = "_OK", IsDefault = true };
        ok.Accepted += (_, _) => _app.RequestStop();
        var dialog = Themed(new Dialog { Title = title, Width = 90, Height = 28, Buttons = new[] { ok } });
        dialog.Add(Themed(new TextView
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1),
            ReadOnly = true, Text = content,
        }));
        _app.Run(dialog);
    }

    static void ShowHelp() => ShowText("About steps", """
        A command is a list of steps that run one after another. If a step fails, the rest are skipped.

        EZ command       any EZ command, e.g.   echo Hello $1     or another command of yours
        Outside command  anything you would type in a normal terminal, e.g.   git status
        Piece of code    Python, PowerShell, cmd, Node or C# - saved inside the command

        $1 ... $9 are the arguments you give the command, $* is all of them.
        Code gets the arguments the normal way (sys.argv, $args, %1, process.argv).

        Commands are saved as .ezc files in Documents\EZter\commands.
        In a normal terminal, run commands as "ez [command]": a command 'hi' is run with: ez hi
        """);

    // ---- PATH and folders ----

    static void OfferPath()
    {
        if (Shims.IsOnPath() || Shims.PromptDisabled) return;

        int answer = Query("Add EZ to PATH?",
            "Add EZter to your PATH so your commands work in any normal terminal?\n\n" +
            "Every command is run as \"ez [command]\" (ez hi, ez time, ez ls ...).\n" +
            "This only changes your own user account.",
            "Yes", "Not now", "Never ask");

        if (answer == 0) AddToPath();
        else if (answer == 2) Shims.DisablePrompt();
    }

    static void AddToPath()
    {
        Shims.Sync(Program.Store);
        Shims.AddToPath();
        SetStatus("EZ is on your PATH. Open a NEW terminal and try: ez help");
    }

    static void RemoveFromPath()
    {
        Shims.RemoveFromPath();
        SetStatus("EZ removed from your PATH.");
    }

    static void OpenFolder()
    {
        var folder = Path.Combine(Program.DataDir, "commands");
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
