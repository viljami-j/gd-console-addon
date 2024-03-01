# godot-developer-console
An easy-to-use, simple in-game console inspired by the console used in Valve Source 1.

***Documentation is a work-in-progress. Feel free to open an issue about it, I will then prioritize and deliver it ASAP! (= 1-2 days, or less)***

## Features
- Cross-language scripting: Add & call console commands from GDScript or C#
- Helpful game-agnostic commands


### Upcoming
- Source-style autocompletion

### Notes on Cross-language scripting
- When adding console commands from GDScript, lambdas aren't currently supported - this might change in future versions of Godot.

## Installation:

### Via cloning (recommended)
1. Clone the repo to 'res://addons/'
2. Open Project -> Project Settings -> Plugins
3. Enable the plugin

### Via releases
1. Download a release that supports your version of Godot
2. Extract the .zip file
3. Move the resulting folder within 'res://addons/'
4. Open Project -> Project Settings -> Plugins
5. Enable the plugin

## Examples
<details>

<summary>GDScript</summary>

### Adding a command

todo

todo

```GDScript
   todo
```

</details>
<details>

<summary>C#</summary>

### Adding a command

When adding commands, you should always use the following template.

If your Callable doesn't contain ```string[] args``` in it's parameters, result is undefined behavior.

```cs
    // You can call this from anywhere
    Console.AddCommand("my_command", Callable.From((string[] args) => {
        // Command code goes here
        }), 
        "This is an optional help description. It is shown upon running 'help my_command' in the console");
```

### Running a command

todo: add code examples & pictures of running via the console window

</details>

#### Disclaimer
This addon is being developed as per my own needs.
