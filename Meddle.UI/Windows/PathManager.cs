using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;

namespace Meddle.UI.Windows;

public class PathManager
{
    public readonly List<ParsedFilePath> ParsedPaths = new();
    public List<IGrouping<string, ParsedFilePath>> PathViewerCache = new();
    public readonly Dictionary<ulong, ParsedFilePath?> ParsedPathDict = new();
    public readonly Dictionary<string, IGrouping<string, ParsedFilePath>[]> FolderCache = new();

    public ParsedFilePath? GetPath(ulong hash)
    {
        if (ParsedPathDict.TryGetValue(hash, out var path))
        {
            return path;
        }

        var match = ParsedPaths.FirstOrDefault(x => x.IndexHash == hash || x.Index2Hash == hash);
        ParsedPathDict[hash] = match;
        return match;
    }

    private Task? importTask;
    private int discoveredTextureCount;
    private Task? textureDiscoveryTask;
    private bool exportFilePickerOpen;
    private bool importFilePickerOpen;

    public void RunTextureDiscovery(SqPack sqPack, ILogger logger)
    {
        if (textureDiscoveryTask != null)
        {
            ImGui.Text($"Discovered {discoveredTextureCount} textures...");
            if (textureDiscoveryTask.IsCompleted)
            {
                textureDiscoveryTask = null;
            }

            return;
        }

        if (ImGui.Button("Discover textures"))
        {
            discoveredTextureCount = 0;
            var paths = ParsedPaths
                        .Where(x => x.Path.EndsWith(".mtrl"))
                        .Select(x => x.Path)
                        .ToList();
            if (paths.Count == 0) return;
            textureDiscoveryTask = Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    try
                    {
                        var hash = SqPack.GetFileHash(path);
                        var file = sqPack.GetFile(hash);
                        if (file == null) continue;
                        var mtrlFile = new MtrlFile(file.Value.file.RawData);
                        var material = new Material(mtrlFile);
                        foreach (var (_, texPath) in material.TexturePaths)
                        {
                            if (ParsedPaths.Any(x => x.Path == texPath))
                            {
                                continue;
                            }

                            if (texPath.Contains('/'))
                            {
                                var texHash = SqPack.GetFileHash(texPath);
                                ParsedPaths.Add(texHash);
                                discoveredTextureCount++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to discover textures for {Path}", path);
                    }
                }
            });
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Parse all known .mtrl files and discover any textures used in them.");
            ImGui.EndTooltip();
        }
    }

    public void DrawExport()
    {
        if (ImGui.Button("Export"))
        {
            ImGui.OpenPopup("Export Paths");
            exportFilePickerOpen = true;
        }

        if (ImGui.BeginPopupModal("Export Paths", ref exportFilePickerOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // path select
            var picker = FilePicker.GetFilePicker(this, Path.Combine(Program.DataDirectory));
            if (picker.DrawFileSaver())
            {
                var path = picker.SelectedFile;
                if (path != null)
                {
                    var lines = ParsedPaths.Select(x => x.Path).ToList();
                    File.WriteAllLines(path, lines);
                }
            }

            ImGui.EndPopup();
        }
    }


    public void DrawImport()
    {
        if (importTask != null)
        {
            ImGui.Text("Importing...");
            if (importTask.IsCompleted)
            {
                importTask = null;
            }

            return;
        }

        if (ImGui.Button("Import"))
        {
            ImGui.OpenPopup("Import paths");
            importFilePickerOpen = true;
        }

        if (ImGui.BeginPopupModal("Import paths", ref importFilePickerOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var picker = FilePicker.GetFilePicker(importFilePickerOpen, Path.Combine(Program.DataDirectory));
            if (picker.Draw())
            {
                var path = picker.SelectedFile;
                if (path != null)
                {
                    RunImport(path);
                }
            }

            ImGui.EndPopup();
        }
    }

    public void RunImport(string path)
    {
        var lines = File.ReadAllLines(path);
        importTask = Task.Run(() =>
        {
            var distinct = new HashSet<string>(lines);
            foreach (var existingPath in ParsedPaths)
            {
                distinct.Remove(existingPath.Path);
            }
            
            var paths = PathUtils.ParsePaths(distinct);
            foreach (var p in paths)
            {
                ParsedPaths.Add(p);
            }
        });
    }
}
