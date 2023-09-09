using Godot;
using Godot.Collections;
using System;
using System.IO;
using System.Linq;

/// <summary>
/// 
/// Compatibility:
/// - Godot v4.1.1.stable.mono.official [bd6af8e0e]
/// - ...
/// 
/// NOTES:
/// - Spectate system: As spectateables might vanish, choosing target via index is likely always dangerous.
///	  Change the system so, that it instead specifies a Node target, which can also be null - this kind of situation is easily handled and does not cause a crash.
/// - ...
/// 
/// TODO:
/// - Add runtime key binding that isn't saved ála SourceEngine
/// - Take camera Zoom into account
/// - Spectate: add possibility for smoothing between active camera changes?
///   This could be perhaps accomplished by creating a Camera just for spectating
/// - Add suffix to groups to avoid problems with conflicting groups, e.g. _{some_randomly_generated_value}
/// - Convert GetNodes to use groups instead
/// - Add autocompletion suggestions
/// - 2D freecam spectate
/// - 3D freecam spectate
/// - ...
/// </summary>
/// 
public partial class Console : Node
{
    const string TOGGLE_ACTION          = "ToggleConsole";
    const string SPECTATE_LEFT_ACTION   = "SpectateLeft";
    const string SPECTATE_RIGHT_ACTION  = "SpectateRight";

    public const string CONSOLE_HOST_GROUP  = "ConsoleHost";
    public const string SPECTATE_GROUP      = "CanSpectate";

    private static bool        _isSpectating = false;
    private static Array<Node> _spectateList;
    private static int         _activeSpectateIdx;

    private static CanvasLayer _instance;
    public  static CanvasLayer Instance
    {
        get 
        {
            if (!IsInstanceValid(_instance)) _reinstantiateConsoleLayer.Call();
            return _instance; 
        }
        set { _instance = value; }
    }

    public static Vector2 WindowSize = new();

    private static SceneTree _tree = null;

    private static Callable _reinstantiateConsoleLayer = Callable.From(() =>
    {
        Instance = BuildConsole(WindowSize);

        // ISSUE #001 Problem with using name
        //_tree.Root.GetNode(CONSOLE_HOST_GROUP).AddChild(Instance);
        // WORKAROUND
        _tree.GetNodesInGroup(CONSOLE_HOST_GROUP)[0].AddChild(Instance);
        //

    });

    private static readonly Dictionary _commands = new();


    public override void _Ready()
    {
        // ISSUE #001 Problem with using name
        //Name = CONSOLE_HOST_GROUP";
        // WORKAROUND
        AddToGroup(CONSOLE_HOST_GROUP);
        //

        _tree       = GetTree();
        WindowSize  = GetViewport().GetVisibleRect().Size;

        /* General game agnostic commands */
        // Assumes every spectatable object is in group defined in SPECTATE_GROUP
        // and has a single camera2D.
        AddCommand("spectate",     Callable.From((string[] args) =>
        {
            _spectateList = GetTree().GetNodesInGroup("CanSpectate");

            if (_spectateList.Count != 0)
            {
                Instance.Hide();
                _activeSpectateIdx = 0;
                _isSpectating = true;

                // Prepare spectateables
                foreach (var spectateable in _spectateList)
                {
                    bool hasCamera = false;
                    foreach (var child in spectateable.GetChildren())
                    {
                        if (child is Camera2D)
                            hasCamera = true;
                    }

                    if (!hasCamera)
                    {
                        // If subject marked as spectateable does not have a camera
                        // we will create it
                        Camera2D newCamera = new();
                        spectateable.AddChild(newCamera);
                    }
                }
            }
        }));
        AddCommand("max_fps",      Callable.From((string[] args) =>
        {
            if (args.IsEmpty())
            {
                PrintS("[color=yellow]Missing arguments:[/color] fps");
                return;
            }

            GD.Print(args[0].ToString());

            if (int.TryParse(args[0], out var result)) Engine.MaxFps = result;
            else PrintS("[color=red]Error[/color] Invalid argument - expected a number");
        }));
        AddCommand("clear",        Callable.From((string[] args) =>
        {
            foreach (var textLine in Instance.GetNode<VBoxContainer>("Panel/Scroll/VBox").GetChildren())
                textLine.QueueFree();
        }));
        AddCommand("scenes",       Callable.From((string[] args) =>
        {
            ListFilesInDirectory("res://", ".tscn", true);
        }));
        AddCommand("change_scene", Callable.From((string[] args) =>
        {
            if (args.IsEmpty())
            {
                PrintS("[color=yellow]Missing arguments:[/color] filepath_to_tscn");
                return;
            }

            if (!args[0].StartsWith("res:///"))
            {
                PrintS("[color=yellow]Warning:[/color] Make sure the filepath starts with res:/// (e.g. res:// is invalid due to an unknown Godot bug)");
                return;
            }

            if (Godot.FileAccess.FileExists(args[0]))
            {
                if (args[0].EndsWith(".tscn"))
                    GetTree().ChangeSceneToFile(args[0]);
                else
                    PrintS("[color=red]Error:[/color] invalid scene");
            }
            else
                PrintS("[color=red]Error:[/color] Scene doesn't exist!");
        }));
        AddCommand("quit",         Callable.From((string[] args) => { GetTree().Quit(); }));
    }

    public override void _Input(InputEvent @event)
    {
        // Visibility-independent functionality
        if (_isSpectating)
        {
            if (@event.IsActionPressed(SPECTATE_LEFT_ACTION))
            {
                _activeSpectateIdx--;

                // Avoid crossing "left" boundary
                if (_activeSpectateIdx < 0)
                    _activeSpectateIdx = _spectateList.Count - 1;

                foreach (var c in _spectateList[_activeSpectateIdx].GetChildren())
                    if (c is Camera2D camera && camera.Enabled) camera.MakeCurrent();
            }

            if (@event.IsActionPressed(SPECTATE_RIGHT_ACTION))
            {
                _activeSpectateIdx++;

                // Avoid crossing "right" boundary
                if (_activeSpectateIdx > _spectateList.Count - 1)
                    _activeSpectateIdx = 0;

                foreach (var child in _spectateList[_activeSpectateIdx].GetChildren())
                    if (child is Camera2D camera && camera.Enabled) camera.MakeCurrent();
            }

            return; // When spectating, consume input
        }

        // Visibility-dependent functionality
        if (@event.IsActionPressed(TOGGLE_ACTION))
        {
            if (!Instance.Visible)
            {
                var textEdit = Instance.GetNode<TextEdit>("Panel/TextEdit");
                textEdit.GrabFocus();
                Callable.From(() =>
                {
                    textEdit.Text = "";
                    textEdit.Clear();
                    textEdit.Backspace();
                    Instance.GetNode<ScrollContainer>("Panel/Scroll").ScrollVertical = int.MaxValue;
                }).CallDeferred();
            }

            Instance.Visible = !Instance.Visible;
        }

        if (@event.IsActionPressed("UI_SendMessage") && Instance.Visible)
        {
            TextEdit textEdit = Instance.GetNode<TextEdit>("Panel/TextEdit");
            var command = textEdit.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (command.Length > 0) RunCommand(command);

            Callable.From(() =>
            {
                textEdit.Text = "";
                textEdit.Clear();
                textEdit.Backspace();
                Instance.GetNode<ScrollContainer>("Panel/Scroll").ScrollVertical = 9999;
            }).CallDeferred();

            GetTree().CreateTimer(0.01).Connect("timeout", Callable.From(() =>
            {
                Instance.GetNode<ScrollContainer>("Panel/Scroll").ScrollVertical = 9999; // Counteract a timing problem that prevents scrollbar from moving to bottom
            }));
        }
    }

    // You could define your own 'BuildConsole'
    // as long as it returns a CanvasLayer where your console resides
    // and follows the same node structure.
    public static CanvasLayer BuildConsole(Vector2 windowSize)
    {
        // ConsoleLayer
        //-------------------------
        //  Panel                  |
        //		 \__Scroll         |
        //		  \	      \__VBox  |
        //		   \               |
        //		    \_TextEdit     |
        //-------------------------

        CanvasLayer consoleLayer = new()
        {
            Name = "ConsoleLayer",
            FollowViewportEnabled = false,
            Layer = int.MaxValue,
            Visible = false,
        };

        Panel p = new()
        {
            Name = "Panel",
            CustomMinimumSize = new Vector2(WindowSize.X, WindowSize.Y / 2)
        };

        ScrollContainer sc = new()
        {
            Name = "Scroll"
        };
        sc.Position += new Vector2(20, 20);
        sc.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        sc.CustomMinimumSize = new Vector2(WindowSize.X, WindowSize.Y / 2 - 30);

        VBoxContainer vbox = new()
        {
            Name = "VBox",
            Alignment = BoxContainer.AlignmentMode.End,
            CustomMinimumSize = new Vector2(WindowSize.X, WindowSize.Y / 2 - 30)
        };
        vbox.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);

        TextEdit te = new()
        {
            Name = "TextEdit",
            CaretBlink = true,
            ScrollFitContentHeight = true,
            PlaceholderText = "Type 'help' for a list of available commands"
        };
        te.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        te.CustomMinimumSize = new(WindowSize.X, 30);
        te.Connect(TextEdit.SignalName.TextChanged, Callable.From(() =>
        {
            if (Input.IsActionPressed("UI_SendMessage"))
            {
                te.Text = "";
                te.Clear();
                te.Backspace();
                sc.ScrollVertical = 9999;
            }
        }));

        sc.AddChild(vbox);
        p.AddChild(sc);
        p.AddChild(te);
        consoleLayer.AddChild(p);

        return consoleLayer;
    }

    public static Node GetActiveCamera(int getParentCount = 0)
    {
        // TODO: add handling for Camera3Ds
        var activeCam = Instance.GetViewport().GetCamera2D();

        if (getParentCount == 0) return activeCam;

        Node parent = activeCam;
        for (int i = 0; i < getParentCount; i++) parent = parent.GetParent();
        return parent;
    }

    public static void PrintS(params string[] strings)
    {
        string output = "";
        foreach (string str in strings) output += str + " ";
        RichTextLabel richTextLabel = new()
        {
            BbcodeEnabled = true,
            CustomMinimumSize = new Vector2(WindowSize.X, 35),
            Text = output
        };
        Instance.GetNode<VBoxContainer>("Panel/Scroll/VBox").AddChild(richTextLabel);
    }

    public void ReinstateToActiveCamera() // TODO: Support Camera3D
    {
        var activeCam = GetViewport().GetCamera2D();
        if (activeCam != null)
        {
            //var consoleLayer = GetTree().Root.FindChild("ConsoleLayer") as CanvasLayer;
            //consoleLayer.CustomViewport = activeCam;
            // TODO: Verify that console persists correctly between camera changes
        }
    }

    // WARNING: string[] args must ALWAYS be present in Callable's parameters
    // Example: Callable.From((string[] args) => SomeFunction());
    public static void AddCommand(string name, Callable callable)
    {
        string nameLower = name.ToLower();
        if (_commands.TryGetValue(nameLower, out _))
            _commands.Remove(nameLower);
        _commands.Add(nameLower, callable);
    }

    // WORKAROUND
    // Couldn't find a way to call a static C# function from GDScript.
    // This was apparently functional in Godot 3.x, but doesn't seem
    // to be working on v4.0.1.stable.mono.official [cacf49999]
    public void GDAddCommand(string name, Callable callable)
    {
        AddCommand(name, callable);
    }

    public static void AddKeybind(string key, Callable callable, bool overridesExisting=false)
    {

    }

    public void RunCommand(string[] command)
    {
        var key = command[0].ToLower();

        if (key == "help")
        {
            foreach (var cmd in _commands)
                PrintS("[color=cyan]" + cmd.Key.ToString() + "[/color]");
            return;
        }

        if (!_commands.TryGetValue(key, out var result))
        {
            PrintS("Invalid command");
            return;
        }

        // OPEN A GODOT ISSUE?
        // We do not know how many arguments this call takes.
        // Argument count might be zero, in which case error gets printed.
        //
        // Reimplement in the future if Godot adds possibility to check how many
        // parameters a Callable takes, or adds callv (calling function with array of args).
        string[] args = command.Skip(1).ToArray();
        result.AsCallable().Call(args);

        ReinstateToActiveCamera();
    }

    private void ListFilesInDirectory(string path, string endsWith="", bool recursive=false)
    {
        using var directory = DirAccess.Open(path);

        directory.ListDirBegin();

        while (true)
        {
            string file = directory.GetNext();
            if (file == "") break;

            string filePath = path + "/" + file;

            if (directory.CurrentIsDir() && recursive)
            {
                ListFilesInDirectory(filePath, endsWith, recursive);
            }
            else if (filePath.EndsWith(".tscn"))
            {
                PrintS(filePath);
            }
        }

        directory.ListDirEnd();
    }
}