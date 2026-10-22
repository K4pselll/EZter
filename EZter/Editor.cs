using System.Diagnostics;
using Terminal.Gui;

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

    public static int Run()
    {
        Shims.Sync(Program.Store);
        Application.Init();
        try
        {
            ApplyTheme();
            Build();
            ApplyTheme(); // again, now that the views exist
            ResetForm(new CustomCommand());
            RefreshCommands();
            Application.MainLoop.Invoke(OfferPath);
            Application.Run();
        }
        finally
        {
            Application.Shutdown();
        }
        return 0;
    }

    // ---- theme: black background, white text, blue accent ----

    static ColorScheme Scheme(Color text, Color back, Color hot, Color focusText, Color focusBack) => new()
    {
        Normal = Application.Driver.MakeAttribute(text, back),
        HotNormal = Application.Driver.MakeAttribute(hot, back),
        Focus = Application.Driver.MakeAttribute(focusText, focusBack),
        HotFocus = Application.Driver.MakeAttribute(focusText, focusBack),
        Disabled = Application.Driver.MakeAttribute(Color.DarkGray, back),
    };

    static void ApplyTheme()
    {
        // Terminal.Gui v1 has no DarkBlue/BrightWhite. Many terminal palettes render ANSI Blue as a light
        // blue, so Black (dark navy in those palettes) is the background that gives the dark blue look.
        Colors.Base = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.Black, Color.Blue),
            HotNormal = Application.Driver.MakeAttribute(Color.Blue, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.Black, Color.Blue),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
        };
        Colors.TopLevel = Colors.Base;
        Colors.Dialog = Colors.Base;
        Colors.Menu = Scheme(Color.White, Color.Black, Color.Blue, Color.Black, Color.Blue);
        Colors.Error = Scheme(Color.White, Color.Red, Color.BrightYellow, Color.Black, Color.Gray);
        if (Application.Top != null)
            ForceScheme(Application.Top, Colors.Base);
    }

    // Forces the scheme onto a view and everything inside it (the menu bar keeps its own look)
    static void ForceScheme(View view, ColorScheme scheme)
    {
        if (view is MenuBar) return;
        view.ColorScheme = scheme;
        foreach (var child in view.Subviews)
            ForceScheme(child, scheme);
    }

    // ---- layout ----

    static void Build()
    {
        var top = Application.Top;
        Application.QuitKey = Key.Null; // quitting goes through our own check for unsaved changes

        var menu = new MenuBar(new[]
        {
            new MenuBarItem("_File", new[]
            {
                new MenuItem("_New command", "", NewCommand, null, null, Key.CtrlMask | Key.N),
                new MenuItem("_Save", "", () => Save(), null, null, Key.CtrlMask | Key.S),
                new MenuItem("_Delete command", "", DeleteCommand),
                new MenuItem("_Quit", "", Quit, null, null, Key.CtrlMask | Key.Q),
            }),
            new MenuBarItem("S_tep", new[]
            {
                new MenuItem("_Add step", "", AddStep),
                new MenuItem("_Edit step", "", EditSelectedStep),
                new MenuItem("_Remove step", "", RemoveStep),
                new MenuItem("Move _up", "", () => MoveStep(-1)),
                new MenuItem("Move _down", "", () => MoveStep(1)),
            }),
            new MenuBarItem("_Run", new[]
            {
                new MenuItem("_Run command", "", RunCurrent, null, null, Key.F5),
            }),
            new MenuBarItem("_Tools", new[]
            {
                new MenuItem("_Add EZ to PATH", "", AddToPath),
                new MenuItem("_Remove EZ from PATH", "", RemoveFromPath),
                new MenuItem("Re_fresh EZ launchers", "", () => { Shims.Sync(Program.Store); SetStatus("Launchers refreshed."); }),
                new MenuItem("_Open commands folder", "", OpenFolder),
            }),
            new MenuBarItem("_Help", new[]
            {
                new MenuItem("_About steps", "", ShowHelp),
            }),
        });

        var win = new Window("EZter") { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };

        var listFrame = new FrameView("Commands (Enter = open)") { X = 0, Y = 0, Width = 26, Height = Dim.Fill(1) };
        _commandList = new ListView(_names) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _commandList.OpenSelectedItem += args => OpenCommand(_names[args.Item]);
        listFrame.Add(_commandList);

        var form = new FrameView("Command") { X = 26, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _nameField = new TextField("") { X = 8, Y = 0, Width = 30 };
        _descField = new TextField("") { X = 8, Y = 1, Width = Dim.Fill(1) };

        var stepsFrame = new FrameView("Steps (Enter = edit)") { X = 0, Y = 3, Width = Dim.Fill(), Height = Dim.Fill(2) };
        _stepList = new ListView(_stepLabels) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _stepList.OpenSelectedItem += _ => EditSelectedStep();
        stepsFrame.Add(_stepList);

        var add = new Button("_Add") { X = 1, Y = Pos.AnchorEnd(1) };
        add.Clicked += AddStep;
        var edit = new Button("_Edit") { X = Pos.Right(add) + 1, Y = Pos.AnchorEnd(1) };
        edit.Clicked += EditSelectedStep;
        var remove = new Button("Re_move") { X = Pos.Right(edit) + 1, Y = Pos.AnchorEnd(1) };
        remove.Clicked += RemoveStep;
        var up = new Button("_Up") { X = Pos.Right(remove) + 1, Y = Pos.AnchorEnd(1) };
        up.Clicked += () => MoveStep(-1);
        var down = new Button("_Down") { X = Pos.Right(up) + 1, Y = Pos.AnchorEnd(1) };
        down.Clicked += () => MoveStep(1);
        var save = new Button("_Save") { X = Pos.Right(down) + 3, Y = Pos.AnchorEnd(1) };
        save.Clicked += () => Save();
        var run = new Button("_Run") { X = Pos.Right(save) + 1, Y = Pos.AnchorEnd(1) };
        run.Clicked += RunCurrent;

        form.Add(new Label("Name:") { X = 1, Y = 0 }, _nameField,
                 new Label("About:") { X = 1, Y = 1 }, _descField,
                 stepsFrame, add, edit, remove, up, down, save, run);

        _status = new Label("") { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };

        win.Add(listFrame, form, _status);
        top.Add(menu, win);
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
        return MessageBox.Query("Unsaved changes", "Save the changes to this command first?", "Save", "Discard", "Cancel") switch
        {
            0 => Save(),
            1 => true,
            _ => false,
        };
    }

    static void RefreshCommands()
    {
        _names = Program.Store.Names().ToList();
        _commandList.SetSource(_names);
    }

    static void RefreshSteps(int select)
    {
        _stepLabels.Clear();
        _stepLabels.AddRange(_current.Steps.Select(Describe));
        _stepList.SetSource(_stepLabels);
        if (_stepLabels.Count > 0)
            _stepList.SelectedItem = Math.Clamp(select, 0, _stepLabels.Count - 1);
    }

    static string Describe(Step step) => step.Kind switch
    {
        StepKind.Ez => $"[EZ]       {step.Text}",
        StepKind.Shell => $"[outside]  {step.Text}",
        _ => $"[{step.Language}]  {FirstLine(step.Text)}  ({step.Text.Split('\n').Length} lines)",
    };

    static NStack.ustring[] Labels(params string[] items) => items.Select(i => (NStack.ustring)i).ToArray();

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
            MessageBox.ErrorQuery("Name", "Give the command a name: 1-40 characters, letters, digits, '-' or '_'.", "OK");
            _nameField.SetFocus();
            return false;
        }
        if (Builtins.Table.ContainsKey(name))
        {
            MessageBox.ErrorQuery("Name", $"'{name}' is a built-in EZ command. Pick another name.", "OK");
            return false;
        }
        if (_current.Steps.Count == 0)
        {
            MessageBox.ErrorQuery("Steps", "Add at least one step first.", "OK");
            return false;
        }
        if (name != _savedName && Program.Store.Names().Contains(name) &&
            MessageBox.Query("Overwrite?", $"A command called '{name}' already exists. Replace it?", "Replace", "Cancel") != 0)
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
        if (MessageBox.Query("Delete", $"Delete '{_savedName}'? 'ez {_savedName}' stops working too.", "Delete", "Cancel") != 0)
            return;

        Program.Store.Delete(_savedName);
        SetStatus($"Deleted {_savedName}.");
        ResetForm(new CustomCommand());
        RefreshCommands();
    }

    static void Quit()
    {
        if (ConfirmDiscard()) Application.RequestStop();
    }

    // ---- steps ----

    static void AddStep()
    {
        var steps = StepDialog(null);
        if (steps == null) return;
        int at = _current.Steps.Count == 0 ? 0 : _stepList.SelectedItem + 1;
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
        int at = _stepList.SelectedItem;
        var steps = StepDialog(_current.Steps[at]);
        if (steps == null) return;
        _current.Steps.RemoveAt(at);
        _current.Steps.InsertRange(at, steps);
        RefreshSteps(at);
    }

    static void RemoveStep()
    {
        if (_current.Steps.Count == 0) return;
        int at = _stepList.SelectedItem;
        _current.Steps.RemoveAt(at);
        RefreshSteps(at);
    }

    static void MoveStep(int direction)
    {
        int at = _stepList.SelectedItem, to = at + direction;
        if (_current.Steps.Count == 0 || to < 0 || to >= _current.Steps.Count) return;
        (_current.Steps[at], _current.Steps[to]) = (_current.Steps[to], _current.Steps[at]);
        RefreshSteps(to);
    }

    // Asks for a step. Several lines of an EZ or outside command become several steps.
    static List<Step>? StepDialog(Step? existing)
    {
        var kind = new RadioGroup(Labels("EZ command", "Outside command", "Piece of code"))
        {
            X = 1, Y = 1,
            SelectedItem = existing == null ? 0 : (int)existing.Kind,
        };
        int languageIndex = existing == null ? 0 : Math.Max(0, Array.IndexOf(Ezc.Languages, existing.Language));
        var language = new RadioGroup(Labels(Ezc.Languages)) { X = 30, Y = 1, SelectedItem = languageIndex };

        var text = new TextView
        {
            X = 1, Y = 8, Width = Dim.Fill(1), Height = Dim.Fill(1),
            AllowsTab = false,
            Text = existing?.Text ?? "",
            ColorScheme = Scheme(Color.White, Color.Black, Color.Blue, Color.White, Color.Black),
        };

        bool accepted = false;
        var ok = new Button("_OK", true);
        ok.Clicked += () => { accepted = true; Application.RequestStop(); };
        var cancel = new Button("_Cancel");
        cancel.Clicked += () => Application.RequestStop();

        var dialog = new Dialog(existing == null ? "Add step" : "Edit step", 80, 28, ok, cancel);
        dialog.Add(
            new Label("Step type:") { X = 1, Y = 0 }, kind,
            new Label("Language (code only):") { X = 30, Y = 0 }, language,
            new Label("Content ($1, $2 ... are arguments; Tab moves to the next field):") { X = 1, Y = 7 },
            text);
        Application.Run(dialog);

        if (!accepted) return null;

        var content = text.Text.ToString()!.ReplaceLineEndings("\n");
        if (content.Trim().Length == 0) return null;

        if (kind.SelectedItem == 2)
            return new List<Step> { new(StepKind.Code, content.TrimEnd(), Ezc.Languages[language.SelectedItem]) };

        var stepKind = kind.SelectedItem == 0 ? StepKind.Ez : StepKind.Shell;
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
        var field = new TextField(initial) { X = 1, Y = 1, Width = Dim.Fill(1) };
        var ok = new Button("_OK", true);
        ok.Clicked += () => { accepted = true; Application.RequestStop(); };
        var cancel = new Button("_Cancel");
        cancel.Clicked += () => Application.RequestStop();

        var dialog = new Dialog(title, 60, 8, ok, cancel);
        dialog.Add(new Label(prompt) { X = 1, Y = 0 }, field);
        field.SetFocus();
        Application.Run(dialog);
        return accepted ? field.Text.ToString() : null;
    }

    static void ShowText(string title, string content)
    {
        var ok = new Button("_OK", true);
        ok.Clicked += () => Application.RequestStop();
        var dialog = new Dialog(title, 90, 28, ok);
        dialog.Add(new TextView
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1),
            ReadOnly = true, Text = content,
        });
        Application.Run(dialog);
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

        int answer = MessageBox.Query("Add EZ to PATH?",
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
