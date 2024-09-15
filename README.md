# Meddle

This project is a Dalamud plugin which provides a GUI for exporting player models from FFXIV.

- This is very much a work in progress, and is not yet fully functional.
- There is no guarantee that memory safety is maintained, and it is possible that this plugin could crash your game.

For single model export, use Penumbra. This plugin is intended for exporting multiple meshes at once, such as for a full character model.

## Attributions
Much of this code is from or based on the following projects and wouldn't have been possible without them:
- [Xande](https://github.com/xivdev/Xande) [[GNU AGPL v3](https://github.com/xivdev/Xande/blob/main/LICENSE)]
  - PBD file structure
  - MeshBuilder
  - RaceDeformer
  - Havok Skeletons
- [Penumbra](https://github.com/xivdev/Penumbra) 
  - shader comp logic
  - vertex type info
  - SpanBinaryReader
- [Lumina](https://github.com/NotAdam/Lumina/)
  - file structures
- [SaintCoinach](https://github.com/xivapi/SaintCoinach)
  - terrain and level file structures
- [Alpha](https://github.com/NotNite/Alpha) [[MIT](https://github.com/NotNite/Alpha/blob/main/LICENSE)]
  - initial reference for Meddle.UI project setup and draw logic 
- [PathFinder](https://github.com/chirpxiv/ffxiv-pathfinder) [[MIT](https://github.com/chirpxiv/ffxiv-pathfinder/blob/main/LICENSE)]
  - 🐇
  - reference for world overlay logic
- [Ktisis](https://github.com/ktisis-tools/Ktisis) [[GNU GPL v3](https://github.com/ktisis-tools/Ktisis/blob/main/LICENSE)]
  - lighting structs

Important contributors:
- [WorkingRobot](https://github.com/WorkingRobot) 
  - GPU texture exports 
  - Character-tree structure for exports
  - Shape and attribute data
  - Attach structs
  - Skeleton Parsing
- Members of the Penumbra discord
  - Testing and feedback

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
