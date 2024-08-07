﻿using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;

namespace Meddle.UI.Windows;

public class PathManager : IDisposable
{
    public readonly List<ParsedFilePath> ParsedPaths = new();
    public List<IGrouping<string, ParsedFilePath>> PathViewerCache = new();
    public readonly Dictionary<ulong, ParsedFilePath?> ParsedPathDict = new();
    public readonly Dictionary<string, IGrouping<string, ParsedFilePath>[]> FolderCache = new();
    
    public PathManager(SqPack sqPack, ILogger logger)
    {
        this.sqPack = sqPack;
        this.logger = logger;
    }

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

    public bool TextureDiscoveryOpen;
    private readonly List<string> discoveredTexturePaths = new();
    private Task? textureDiscoveryTask;
    private Task? importTask;
    public bool ExportFilePickerOpen;
    public bool ImportFilePickerOpen;
    private readonly SqPack sqPack;
    private readonly ILogger logger;

    public void RunTextureDiscovery()
    {
        if (textureDiscoveryTask is {IsCompleted: true})
        {
            textureDiscoveryTask = null;
        }

        if (TextureDiscoveryOpen)
        {
            ImGui.OpenPopup("Texture Discovery");
        }

        if (ImGui.BeginPopupModal("Texture Discovery", ref TextureDiscoveryOpen, ImGuiWindowFlags.Modal))
        {
            
            if (textureDiscoveryTask != null)
            {
                ImGui.Text("Discovering textures...");
            }
            
            ImGui.Text($"Discovered {discoveredTexturePaths.Count} textures.");

            if (ImGui.Button($"Discover Textures") && textureDiscoveryTask == null)
            {
                discoveredTexturePaths.Clear();
                var paths = ParsedPaths
                            .Where(x => x.Path.EndsWith(".mtrl"))
                            .Select(x => x.Path)
                            .ToList();
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
                            foreach (var (_, texPath) in mtrlFile.GetTexturePaths())
                            {
                                if (ParsedPaths.Any(x => x.Path == texPath))
                                {
                                    continue;
                                }

                                if (texPath.Contains('/'))
                                {
                                    var texHash = SqPack.GetFileHash(texPath);
                                    ParsedPaths.Add(texHash);
                                    discoveredTexturePaths.Add(texPath);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Failed to discover textures for {Path}", path);
                        }
                    }
                });
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Parse all known .mtrl files and discover any textures used in them.");
                    ImGui.EndTooltip();
                }
            }
            
            foreach (var path in discoveredTexturePaths.TakeLast(10))
            {
                ImGui.Text(path);
            }

            ImGui.EndPopup();
        }
    }

    private Task? discoveryTask;
    public bool DiscoveryOpen;
    public Dictionary<string, List<string>> DiscoveredPaths = new();
    public void RunDiscovery()
    {
        if (discoveryTask is {IsCompleted: true})
        {
            discoveryTask = null;
        }

        if (DiscoveryOpen)
        {
            ImGui.OpenPopup("Discovery");
        }

        if (ImGui.BeginPopupModal("Discovery", ref DiscoveryOpen, ImGuiWindowFlags.Modal))
        {
            if (discoveryTask != null)
            {
                ImGui.Text("Discovering...");
            }

            if (ImGui.Button($"Discover Stuff") && discoveryTask == null)
            {
                DiscoveredPaths.Clear();
                var paths = ParsedPaths
                            .Where(x => x.Path.EndsWith(".mtrl"))
                            .Select(x => x.Path)
                            .ToList();
                discoveryTask = Task.Run(() =>
                {
                    foreach (var path in paths)
                    {
                        try
                        {
                            var hash = SqPack.GetFileHash(path);
                            var file = sqPack.GetFile(hash);
                            if (file == null) continue;
                            var mtrlFile = new MtrlFile(file.Value.file.RawData);
                            var pathTypes = new List<string>();
                            foreach (var (_, texPath) in mtrlFile.GetTexturePaths())
                            {
                                pathTypes.Add(texPath.Split('_').Last());
                            }
                            
                            // order alphabetically
                            pathTypes.Sort();
                            var allPathStr = string.Join(", ", pathTypes);
                            var str = $"{mtrlFile.GetShaderPackageName()} - {allPathStr}";
                            if (DiscoveredPaths.TryGetValue(str, out var list))
                            {
                                list.Add(path);
                            }
                            else
                            {
                                DiscoveredPaths.Add(str, [path]);
                            }
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Failed to discover for {Path}", path);
                        }
                    }

                    var discoverFile = Path.Combine(Program.DataDirectory, "discovered.txt");
                    File.WriteAllLines(discoverFile, DiscoveredPaths.OrderBy(x => x.Key).Select(x => $"[{x.Value.Count}]{x.Key}"));
                    var discoverFileExtended = Path.Combine(Program.DataDirectory, "discovered_extended.txt");
                    File.WriteAllLines(discoverFileExtended, DiscoveredPaths.OrderBy(x => x.Key).Select(x => $"[{x.Value.Count}]{x.Key}\n{x.Value.Aggregate((x, y) => $"{x}\n{y}")}"));
                });
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Parse all known .mtrl files and discover things.");
                    ImGui.EndTooltip();
                }
            }
            
            foreach (var path in DiscoveredPaths.TakeLast(10))
            {
                ImGui.Text(path.Key);
            }

            ImGui.EndPopup();
        }
    }
    
    public void DrawExport()
    {
        if (ExportFilePickerOpen)
        {
            ImGui.OpenPopup("Export Paths");
        }
        
        if (ImGui.BeginPopupModal("Export Paths", ref ExportFilePickerOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // path select
            var picker = FilePicker.GetFilePicker("ExportPaths", Path.Combine(Program.DataDirectory));
            if (picker.DrawFileSaver())
            {
                var path = picker.SelectedFile;
                if (path != null)
                {
                    var lines = ParsedPaths.Select(x => x.Path).ToList();
                    File.WriteAllLines(path, lines);
                }
                
                ExportFilePickerOpen = false;
            }

            ImGui.EndPopup();
        }
    }


    public void DrawImport()
    {
        if (ImportFilePickerOpen)
        {
            ImGui.OpenPopup("Import paths");
        }

        if (ImGui.BeginPopupModal("Import paths", ref ImportFilePickerOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (importTask != null)
            {
                if (importTask.IsCompleted)
                {
                    ImGui.Text("Import complete.");
                    if (ImGui.Button("Close"))
                    {
                        ImportFilePickerOpen = false;
                        importTask = null;
                    }
                }
                else
                {
                    ImGui.Text("Importing paths...");
                }
            }
            else
            {
                ImGui.Text("Import paths from file");
                ImGui.Text("This will add any new paths to the list.");
                var picker = FilePicker.GetFilePicker("ImportPaths", Path.Combine(Program.DataDirectory));
                if (picker.Draw())
                {
                    var path = picker.SelectedFile;
                    if (path != null)
                    {
                        RunImport(path);
                    }
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

    public void Dispose()
    {
        importTask?.Dispose();
        textureDiscoveryTask?.Dispose();
    }
}
