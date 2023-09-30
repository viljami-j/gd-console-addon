#if TOOLS
using Godot;
using System;

[Tool]
public partial class GodotDeveloperConsolePlugin : EditorPlugin
{
	public override void _EnterTree()
	{
		// Initialization of the plugin goes here.
		AddAutoloadSingleton("DeveloperConsole", "res://addons/godot_developer_console/Console.cs");
	}

	public override void _ExitTree()
	{
		// Clean-up of the plugin goes here.
		RemoveAutoloadSingleton("DeveloperConsole");
	}
}
#endif
