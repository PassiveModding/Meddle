# Meddle

This project is a Dalamud plugin which provides a GUI for exporting player models from FFXIV.

- Much of this code is from [Penumbra](https://github.com/xivdev/Penumbra) and [Xande](https://github.com/xivdev/Xande) and would not have been possible without the contributions of the authors of those projects.
- This is very much a work in progress, and is not yet fully functional.
- There is no guarantee that memory safety is maintained, and it is possible that this plugin could crash your game.

For single model export, use Penumbra. This plugin is intended for exporting multiple meshes at once, such as for a full character model.


## Installation
Meddle is written as a Dalamud plugin and as such, requires that you use FFXIVQuickLauncher to start your game.
This will enable you to install community-created plugins.

1. Type the `/xlsettings` command into your chatbox. This will open your Dalamud Settings.
2. In the window that opens, navigate to the "Experimental" tab. Scroll down to "Custom Plugin Repositories". 
3. Copy and paste the repo URL (seen below) into the input box, making sure to press the "+" button to add it. 
4. Press the "Save and Close" button. This will add Meddle to Dalamud's list of available plugins. 
5. Open the plugin installer by typing the `/xlplugins` command, search for Meddle, and click install.

Repo URL

```
https://raw.githubusercontent.com/PassiveModding/Meddle/main/repo.json
```
