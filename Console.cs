using Godot;
using Godot.Collections;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
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
/// PRIORITY TODO:
/// - Add helpers for parsing arguments. Currently a lot of similar code gets written.
/// Helpers could include:
///     * int 0-1 to bool mapper + arg validator
///     * int validator
///     * ...etc
/// 
/// TODO:
/// - Make adding commands dependency-free using hasnode and .Call("target", "functionName", param1, param2, ...)
/// - Warn about command overwrites
/// - Add runtime key binding ála SourceEngine
/// - Spectate: add smoothing between active camera change
///   This could be perhaps accomplished by creating a Camera just for spectating
/// - Convert GetNodes to use groups instead,
/// NOTE: When using group to tag single nodes, add suffix to avoid problems with conflicting groups, e.g. _{some_randomly_generated_value}
/// - Add autocompletion suggestions
/// - 2D freecam spectate
/// - 3D freecam spectate
/// - Purge all BBCode and reimplement color styling in a sane way
/// </summary>
/// 

public partial class Console : CanvasLayer
{
	private class BBEmphasis
	{
		// Helpers for styling Console messages
		public static string Warning(string message)
		{
			return $"[color=yellow]{message}[/color]";
		}

		public static string Error(string message)
		{
			return $"[color=red]{message}[/color]";
		}
	}

	private static Font consoleFont;

	const string TOGGLE_ACTION          = "toggle_console";
	const string SEND_LINE_ACTION       = "send_line";
	const string SPECTATE_LEFT_ACTION   = "spectate_left";
	const string SPECTATE_RIGHT_ACTION  = "spectate_right";

	public const string CONSOLE_GROUP   = "gd-console";
	public const string SPECTATE_GROUP  = "CanSpectate";

	private static bool        _isSpectating = false;
	private static Array<Node> _spectateList;
	private static int         _activeSpectateIdx;

	public static Console Instance;

	private static readonly Dictionary _commands                = new();
	private static readonly Dictionary _cmdHelpDescriptions     = new();
	private static          Dictionary _sceneIndex              = new(); // populates scenes with IDs for easy loads (not having to write out full path to load)
	private static readonly Dictionary _aliases                 = new(); // key: aliasToCommand (string), val: Callable
	private static readonly Dictionary _commandToAliasRelations = new(); // key: commandName (string), val: aliases (string[])

	public override void _Ready()
	{
		Instance = this;

		AddToGroup(CONSOLE_GROUP);

		GetFilesInDirectory("res://", Callable.From((string filepath) =>
		{
			consoleFont = ResourceLoader.Load<FontFile>(filepath);
		}), "lucon.ttf", true); // Load font

		BuildUI();

		_sceneIndex = IndexScenes();

		/* General game agnostic commands */
		AddCommand("fix_font", Callable.From((string[] args) =>
		{
			GetFilesInDirectory("res://", Callable.From((string filepath) =>
			{
				RunCommand("echo " + filepath);
			}), "", true); // Load font
		}));
		AddCommand("help",          Callable.From((string[] args) =>
		{
			if (args.Length == 0)
			{
				foreach (var cmd in _commands) RunCommand($"echo [color=cyan]{cmd.Key.AsString()}[/color]");
				RunCommand("echo Use 'help command' to look up information on a specific command");
			}
			else if (_cmdHelpDescriptions.TryGetValue(args[0].ToLower(), out Variant description))
				RunCommand("echo " + description.AsString());
			else
				RunCommand("echo [info]: no help description available");
		}));
		AddCommand("quit",          Callable.From((string[] args) => { GetTree().Quit(); }), "", "exit");
		AddCommand("echo",          Callable.From((string[] args) => {
			// prints given arguments as-is
			string output = "";
			foreach (string str in args) output += str + " ";
			RichTextLabel richTextLabel = new()
			{
				BbcodeEnabled = true,
				CustomMinimumSize = new Vector2(Instance.GetViewport().GetVisibleRect().Size.X, 35),
				Text = output
			};
			Instance.GetNode<VBoxContainer>("Panel/Scroll/VBox").AddChild(richTextLabel);
		}));
		AddCommand("aliases",       Callable.From((string[] args) =>
		{
			// First argument: commandName
			if (_commandToAliasRelations.TryGetValue(args[0], out Variant result))
				foreach (string alias in result.AsStringArray()) RunCommand("echo " + alias);
			else
				RunCommand("echo No aliases exist for command '" + args[0] + "'");

		}), "Lists all aliases of a given command (aliases are alternate names to commands)");
		AddCommand("reload",        Callable.From((string[] args) => { GetTree().ReloadCurrentScene(); }), "Reloads current scene");
		AddCommand("window_mode",    Callable.From((string[] args) =>
		{
			switch (args[0])
			{
				case "borderless":
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
					break;
				case "exclusive":
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
					break;
				case "windowed":
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
					break;
				default:
					RunCommand($"echo [INFO]: Window mode '{args[0]}' does not exist - available window modes: " +
						$"borderless | exclusive | windowed");
					break;
			}
		}), "[INFO]: Sets windowmode, takes 1 of the following arguments: borderless | exclusive | windowed", "wm");
		AddCommand("spectate",      Callable.From((string[] args) =>
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
		}), "Spectate any Node in group defined in the defined SPECTATE_GROUP");
		AddCommand("max_fps",       Callable.From((string[] args) =>
		{
			if (args.IsEmpty())
			{
				RunCommand("echo [color=yellow]Missing arguments:[/color] fps");
				return;
			}

			GD.Print(args[0].ToString());

			if (int.TryParse(args[0], out var result)) Engine.MaxFps = result;
			else RunCommand("echo [color=red]Error[/color] Invalid argument - expected a number");
		}));
		AddCommand("clear",         Callable.From((string[] args) =>
		{
			foreach (var textLine in Instance.GetNode<VBoxContainer>("Panel/Scroll/VBox").GetChildren())
				textLine.QueueFree();
		}));
		AddCommand("scenes",        Callable.From((string[] args) =>
		{
			foreach (int key in _sceneIndex.Keys)
			{
				if (!_sceneIndex.TryGetValue(key, out Variant filepath)) return;

				RunCommand($"echo [color=blue]Index: [/color]{key} | Filepath: {filepath.AsString()}");
			}
		}), "Lists all scenes in the game's filesystem", "ls");
		AddCommand("load_scene",    Callable.From((string[] args) =>
		{
			if (args.IsEmpty())
			{
				RunCommand("echo " + "[color=yellow]Missing arguments:[/color] filepath_to_tscn OR sceneIndex");
				return;
			}

			if (!int.TryParse(args[0], out int sceneIndex)) // If not Int, expect filepath to .tscn
			{
				if (!args[0].StartsWith("res:///"))
				{
					RunCommand("echo " + "[color=yellow]Warning:[/color] Make sure the filepath starts with res:/// (e.g. res:// is invalid due to an unknown Godot bug)");
					return;
				}

				if (Godot.FileAccess.FileExists(args[0]))
				{
					if (args[0].EndsWith(".tscn"))
						GetTree().ChangeSceneToFile(args[0]);
					else
						RunCommand("echo [color=red]Error:[/color] invalid scene");
				}
				else
					RunCommand("echo [color=red]Error:[/color] Scene doesn't exist!");
				return;
			}
			else
			{
				Variant sceneFilepath;
				if (!_sceneIndex.TryGetValue(sceneIndex, out sceneFilepath))
				{
					RunCommand("echo " + BBEmphasis.Warning("Invalid sceneIndex - no match"));
					return;
				}

				if (Godot.FileAccess.FileExists(((string)sceneFilepath)))
					GetTree().ChangeSceneToFile((string)sceneFilepath);
				else
					RunCommand("echo " + BBEmphasis.Error("Error:") + "Scene doesn't exist!");
			}

			
		}), "Loads the scene, either by path or sceneIndex", "loads", "ld");
		AddCommand("bind",          Callable.From((string[] args) =>
		{
			// Bind key to command(s).
			// bind [KEY] [COMMAND] [ARGS]
			// bind [KEY] "[COMMAND] [ARGS]" "[COMMAND] [ARGS]" ...

		}));
		AddCommand("bind_action",   Callable.From((string[] args) =>
		{
			// Bind action to command(s).
			// bind [KEY] [COMMAND]
			// bind [KEY] "[COMMAND1];[COMMAND2];[COMMAND3];"

		}));
		AddCommand("freezoom",      Callable.From((string[] args) =>
		{

		}));
		AddCommand("stepzoom",      Callable.From((string[] args) =>
		{

		}));
		//AddCommand("predict_fall",  Callable.From((string[] args) =>
		//{
		//    if (!int.TryParse(args[0], out int truthValue))
		//        return;

		//    truthValue = Math.Clamp(truthValue, 0, 1);

		//    if (truthValue == 1)
		//    {
		//        // calculate & visualise trajectories based on speed and direction
		//    }
		//    else
		//    {
		//        // disable
		//    }
		//}));
		AddCommand("enable_sound",  Callable.From((string[] args) =>
		{
			if (!int.TryParse(args[0], out int value))
			{
				Instance.RunCommand("echo Expected 1 argument (int), 0 or 1");
				return;
			}

			int clamped = Math.Clamp(value, 0, 1);

			bool truthValue = clamped >= 1 ? true : false;

			var masterBus = AudioServer.GetBusIndex("Master");
			AudioServer.SetBusMute(masterBus, !truthValue);
		}), "", "snd");
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

		if (@event.IsActionPressed(SEND_LINE_ACTION) && Instance.Visible)
		{
			TextEdit textEdit = Instance.GetNode<TextEdit>("Panel/TextEdit");

			if (textEdit.Text.Length > 0) RunCommand(textEdit.Text);

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


	private static void BuildUI()
	{
		var windowSize = Instance.GetViewport().GetVisibleRect().Size;

		// root: Console
		//-------------------------
		//  Panel                  |
		//		 \__Scroll         |
		//		  \	      \__VBox  |
		//		   \               |
		//		    \_TextEdit     |
		//-------------------------

		Instance.FollowViewportEnabled = false;
		Instance.Layer = int.MaxValue;
		Instance.Visible = false;

		Panel p = new()
		{
			Name = "Panel",
			CustomMinimumSize = new Vector2(Instance.GetViewport().GetVisibleRect().Size.X, Instance.GetViewport().GetVisibleRect().Size.Y / 2)
		};

		ScrollContainer sc = new()
		{
			Name = "Scroll"
		};
		sc.Position += new Vector2(20, 20);
		sc.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		sc.CustomMinimumSize = new Vector2(Instance.GetViewport().GetVisibleRect().Size.X, Instance.GetViewport().GetVisibleRect().Size.Y / 2 - 30);

		VBoxContainer vbox = new()
		{
			Name = "VBox",
			Alignment = BoxContainer.AlignmentMode.End,
			CustomMinimumSize = new Vector2(Instance.GetViewport().GetVisibleRect().Size.X, Instance.GetViewport().GetVisibleRect().Size.Y / 2 - 30)
		};
		vbox.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);

		TextEdit te = new()
		{
			Name = "TextEdit",
			CaretBlink = true,
			ScrollFitContentHeight = true,
			PlaceholderText = "Type 'help' for a list of available commands",
		};

		te.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		te.CustomMinimumSize = new(Instance.GetViewport().GetVisibleRect().Size.X, 30);
		te.Connect(TextEdit.SignalName.TextChanged, Callable.From(() =>
		{
			if (Input.IsActionPressed(SEND_LINE_ACTION))
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
		Instance.AddChild(p);

		// Apply theming
		if (consoleFont != null)
		{
			Theme theme = new Theme();
			theme.DefaultFont = consoleFont;
			theme.DefaultFontSize = 16;
			p.Theme = theme;
		}
	}

	private static Node GetActiveCamera()
	{
		// returns null if there is no active camera
		var activeCam2D = Instance.GetViewport().GetCamera2D();
		var activeCam3D = Instance.GetViewport().GetCamera3D();

		if (activeCam2D != null) return activeCam2D; // Active camera is a 2D camera
		else if (activeCam3D != null) return activeCam3D; // Active camera is a 3D camera

		return null;
	}

	private Dictionary IndexScenes()
	{
		Dictionary sceneIndex = new();

		GetFilesInDirectory("res://", Callable.From((string filepath) =>
		{
			sceneIndex.Add(sceneIndex.Count, filepath);
		}), ".tscn", true);

		return sceneIndex;
	}

	private void ReinstateToActiveCamera()
	{
		var activeCam = GetActiveCamera();
		// TODO: Verify that console persists correctly between camera changes
		if (activeCam != null && activeCam is Camera2D) CustomViewport = activeCam;
		if (activeCam != null && activeCam is Camera3D) CustomViewport = activeCam;
	}

	// WARNING: string[] args must ALWAYS be present in Callable's parameters
	// Example: Callable.From((string[] args) => SomeFunction());
	public static void AddCommand(string name, Callable callable, string helpDescription="", params string[] aliases)
	{
		string nameLower = name.ToLower();
		if (_commands.TryGetValue(nameLower, out _))
			_commands.Remove(nameLower);
		_commands.Add(nameLower, callable);


		if (_cmdHelpDescriptions.TryGetValue(nameLower, out _))
			_cmdHelpDescriptions.Remove(nameLower);

		if (helpDescription != "")
			_cmdHelpDescriptions.Add(nameLower, helpDescription);

		foreach (string alias in aliases)
		{
			if (_aliases.TryGetValue(alias, out _))
				_aliases.Remove(alias);
			_aliases.Add(alias, callable);
		}

		foreach (string alias in aliases)
		{
			if (_commandToAliasRelations.TryGetValue(nameLower, out Variant result))
			{
				string[] cmdAliases = result.AsStringArray();
				var list = cmdAliases.ToList();
				list.Add(alias);
				_commandToAliasRelations.Remove(nameLower);
				_commandToAliasRelations.Add(nameLower, list.ToArray<string>());
			}
			else
				_commandToAliasRelations.Add(nameLower, new string[] { alias });
		}
	}

	// WORKAROUND
	// Couldn't find a way to call a static C# function from GDScript.
	// This was apparently functional in Godot 3.x, but doesn't seem
	// to be working on v4.0.1.stable.mono.official [cacf49999]
	public void GDAddCommand(string name, Callable callable, string helpDescription)
	{
		AddCommand(name, callable, helpDescription);
	}

	public void RunCommand(string command)
	{
		var splitCommand = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		var key = splitCommand[0].ToLower();

		if (!_commands.TryGetValue(key, out var result))
		{
			if (!_aliases.TryGetValue(key, out var aresult))
			{
				RunCommand($"echo Invalid command '{key}'");
				return;
			}
			else
			{
				// OPEN A GODOT ISSUE?
				// We do not know how many arguments this call takes.
				// Argument count might be zero, in which case error gets printed.
				//
				// Reimplement in the future if Godot adds possibility to check how many
				// parameters a Callable takes, or adds callv to C# (calling function with array of args).
				string[] args = splitCommand.Skip(1).ToArray();
				aresult.AsCallable().Call(args);

				ReinstateToActiveCamera();
			}
		}
		else
		{
			// OPEN A GODOT ISSUE?
			// We do not know how many arguments this call takes.
			// Argument count might be zero, in which case error gets printed.
			//
			// Reimplement in the future if Godot adds possibility to check how many
			// parameters a Callable takes, or adds callv to C# (calling function with array of args).
			string[] args = splitCommand.Skip(1).ToArray();
			result.AsCallable().Call(args);

			ReinstateToActiveCamera();
		}
	}

	// Callback signature: (string filepath) => {]
	private void GetFilesInDirectory(string path, Callable callback, string endsWith="", bool recursive=false)
	{
		using var directory = DirAccess.Open(path);

		directory.ListDirBegin();

		while (true)
		{
			string file = directory.GetNext();
			if (file == "") break;

			string filepath = path + "/" + file;

			if (directory.CurrentIsDir() && recursive) GetFilesInDirectory(filepath, callback, endsWith, recursive);
			else if (filepath.EndsWith(endsWith))
			{
				callback.Call(filepath);
			}
		}

		directory.ListDirEnd();
	}
}
