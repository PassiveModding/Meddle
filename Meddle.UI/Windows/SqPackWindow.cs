using System.Diagnostics;
using ImGuiNET;
using Meddle.UI.Windows.Views;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Category = Meddle.Utils.Files.SqPack.Category;
using ParsedFilePath = Meddle.Utils.Files.SqPack.ParsedFilePath;
using Repository = Meddle.Utils.Files.SqPack.Repository;
using SqPack = Meddle.Utils.Files.SqPack.SqPack;

namespace Meddle.UI.Windows;

public class SqPackWindow
{
    public class PathManager
    {
        public readonly List<ParsedFilePath> ParsedPaths = new();
        public List<IGrouping<string, ParsedFilePath>> PathViewerCache = new();
        public readonly Dictionary<ulong, ParsedFilePath?> ParsedPathDict = new();
        
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
    }
    
    private readonly ImageHandler imageHandler;
    private readonly SqPack sqPack;
    private readonly PathManager pathManager;
    private (int index, Category cat, IndexHashTableEntry hash, SqPackFile file)? selectedFile;
    private string filter = string.Empty;
    private string search = string.Empty;
    private Repository selectedRepository;
    private Category selectedCategory;
    private IView? view;
    

    public SqPackWindow(SqPack sqPack, ImageHandler imageHandler, PathManager pathManager)
    {
        this.imageHandler = imageHandler;
        this.sqPack = sqPack;
        selectedRepository = sqPack.Repositories.First();
        selectedCategory = selectedRepository.Categories.First().Value;
        this.pathManager = pathManager;
    }

    public void Draw()
    {
        InternalDraw();
    }

    private void InternalDraw()
    {
        var cra = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##SqPackWindow", cra with {X = 300}, ImGuiChildFlags.Border))
        {
            DrawRepoCategory();
            BrowseCategory(selectedCategory!);
            ImGui.EndChild();
        }

        ImGui.SameLine();
        if (ImGui.BeginChild("##PathViewer", cra with {X = 300}, ImGuiChildFlags.Border))
        {
            DrawPathViewer();
            ImGui.EndChild();
        }

        ImGui.SameLine();
        
        if (ImGui.BeginChild("##SqPackFile", cra with {X = cra.X - 620 }, ImGuiChildFlags.Border))
        {
            if (selectedFile.HasValue)
            {
                DrawSelectedFile();
            }

            ImGui.EndChild();
        }
    }

    private void DrawRepoCategory()
    {
        var selectedRepositoryName = selectedRepository.Path.Split(Path.DirectorySeparatorChar).Last();
        if (ImGui.BeginCombo("Repository", selectedRepositoryName))
        {
            foreach (var repo in sqPack.Repositories)
            {
                var folderName = repo.Path.Split(Path.DirectorySeparatorChar).Last();
                if (ImGui.Selectable(folderName))
                {
                    selectedRepository = repo;
                    selectedCategory = repo.Categories.First().Value;
                }
            }
            ImGui.EndCombo();
        }
        
        if (ImGui.BeginCombo("Category", Category.TryGetCategoryName(selectedCategory.Id) ?? $"0x{selectedCategory.Id:X2}"))
        {
            var categories = selectedRepository.Categories.GroupBy(x => x.Key.category);
            foreach (var cg in categories)
            {
                ImGui.Separator();
                var cName = $"[{cg.Key:X2}]";
                var catName = Category.TryGetCategoryName(cg.Key);
                if (ImGui.Selectable($"{cName} {catName}"))
                {
                    selectedCategory = cg.First().Value;
                }
            }
            ImGui.EndCombo();
        }
    }
    
    private Task? importTask;

    private void DrawImport()
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
            ImGui.OpenPopup("Import");
        }
        
        if (ImGui.BeginPopup("Import", ImGuiWindowFlags.AlwaysAutoResize))
        {
            foreach (var file in Directory.GetFiles(Program.DataDirectory, "*.txt"))
            {
                if (ImGui.Selectable(file))
                {
                    var lines = File.ReadAllLines(file);
                    importTask = Task.Run(() =>
                    {
                        var paths = PathUtils.ParsePaths(lines);
                        foreach (var path in paths)
                        {
                            pathManager.ParsedPaths.Add(path);
                        }
                    });
                }
            }
            
            ImGui.EndPopup();
        }

    }
    
    private void DrawPathViewer()
    {
        DrawImport();
        ImGui.InputText("##AddPath", ref search, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add"))
        {
            var hash = SqPack.GetFileHash(search);
            var file = sqPack.GetFile(hash);
            if (file != null)
            {
                selectedFile = (0, file.Value.category, file.Value.hash, file.Value.file);
                view = null;
                pathManager.ParsedPaths.Add(hash);
            }
        }
        
        if (ImGui.InputText("Filter", ref filter, 100))
        {
            pathManager.PathViewerCache = pathManager.ParsedPaths
                      .Where(x => x.Path.Contains(filter))
                      .OrderBy(x => x.Path)
                      .GroupBy(x => x.Path.Split('/')[0])
                      .ToList();
        }
        else if (pathManager.PathViewerCache.Count == 0)
        {
            pathManager.PathViewerCache = pathManager.ParsedPaths
                                  .OrderBy(x => x.Path)
                                  .GroupBy(x => x.Path.Split('/')[0])
                                  .ToList();
        }

        foreach (var group in pathManager.PathViewerCache)
        {
            if (ImGui.TreeNode(group.Key))
            {
                DrawPathSet(group.Key, group);
                ImGui.TreePop();
            }
        }
    }

    // Recursively draw folders
    private void DrawPathSet(string key, IEnumerable<ParsedFilePath> paths)
    {
        var groups = paths.GroupBy(x => x.Path.Substring(key.Length + 1).Split('/')[0]);
        foreach (var group in groups)
        {
            if (group.Key.Contains('.'))
            {
                if (ImGui.Selectable(group.Key))
                {
                    var hash = SqPack.GetFileHash(key + "/" + group.Key);
                    var file = sqPack.GetFile(hash);
                    if (file != null)
                    {
                        selectedFile = (0, file.Value.category, file.Value.hash, file.Value.file);
                        view = null;
                    }
                }
                
                // right click copy path
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.Selectable("Copy Path"))
                    {
                        ImGui.SetClipboardText(key + "/" + group.Key);
                    }
                    ImGui.EndPopup();
                }

                continue;
            }

            if (ImGui.TreeNode(group.Key))
            {
                DrawPathSet(key + "/" + group.Key, group.ToList());
                ImGui.TreePop();
            }
        }
    }

    private enum SelectedFileType
    {
        None,
        Texture,
        Model,
        Material,
    }

    private int sliceStartIndex;
    private readonly int sliceSize = 200;

    private void BrowseCategory(Category cat)
    {
        ImGui.Text($"Index: {cat.Index}");
        ImGui.Text($"Index2: {cat.Index2}");
        ImGui.Text($"Entries: {cat.UnifiedIndexEntries.Count}");
        var cra = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##SqPackCategoryEntries", cra with {Y = cra.Y - 50}))
        {
            bool displayDatFileIdx = cat.DatFilePaths.Length > 1;
            var entries = cat.UnifiedIndexEntries.Skip(sliceStartIndex).Take(sliceSize).ToArray();
            for (var i = 0; i < entries.Length; i++)
            {
                var (hash, entry) = entries[i];
                var entryName =
                    $"{(displayDatFileIdx ? $"[{entry.DataFileId:00}]" : "")}[0x{entry.Offset:X8}] {hash:X16}##{hash:X16}";
                
                var path = pathManager.GetPath(hash);
                if (path != null)
                {
                    entryName = path.Path;
                }

                if (ImGui.Selectable(entryName))
                {
                    if (cat.TryGetFile(hash, out var data))
                    {
                        var index = sliceStartIndex + i;
                        selectedFile = (index, cat, entry, data);
                        view = null;
                    }
                }
            }

            ImGui.EndChild();
        }

        if (ImGui.Button("Prev Page"))
        {
            sliceStartIndex -= sliceSize;
            if (sliceStartIndex < 0)
            {
                sliceStartIndex = 0;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Next Page"))
        {
            sliceStartIndex += sliceSize;
            if (sliceStartIndex >= cat.UnifiedIndexEntries.Count)
            {
                sliceStartIndex = 0;
            }
        }

        ImGui.SameLine();
        var endIdx = sliceStartIndex + sliceSize;
        if (endIdx > cat.UnifiedIndexEntries.Count)
        {
            endIdx = cat.UnifiedIndexEntries.Count;
        }

        ImGui.Text($"{sliceStartIndex} - {endIdx}");

        if (ImGui.Button("Prev File"))
        {
            if (selectedFile.HasValue)
            {
                var index = selectedFile.Value.index - 1;
                if (index < 0)
                {
                    index = cat.UnifiedIndexEntries.Count - 1;
                }

                var (hash, entry) = cat.UnifiedIndexEntries.ElementAt(index);
                if (cat.TryGetFile(hash, out var data))
                {
                    selectedFile = (index, cat, entry, data);
                    view = null;
                }
                else
                {
                    selectedFile = null;
                }
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Next File"))
        {
            if (selectedFile.HasValue)
            {
                var index = selectedFile.Value.index + 1;
                if (index >= cat.UnifiedIndexEntries.Count)
                {
                    index = 0;
                }

                var (hash, entry) = cat.UnifiedIndexEntries.ElementAt(index);
                if (cat.TryGetFile(hash, out var data))
                {
                    selectedFile = (index, cat, entry, data);
                    view = null;
                }
                else
                {
                    selectedFile = null;
                }
            }
        }

        ImGui.SameLine();
        ImGui.Text(selectedFile.HasValue ? $"Selected: {selectedFile.Value.index}" : "No file selected");
    }

    private void DrawSelectedFile()
    {
        if (selectedFile.HasValue)
        {
            var file = selectedFile.Value;
            DrawFile(file.hash, file.file);
        }
    }

    private void DrawFile(IndexHashTableEntry hash, SqPackFile file)
    {
        var path = pathManager.GetPath(hash.Hash);
        if (path != null)
        {
            ImGui.Text($"Path: {path.Path}"); 
        }
        else
        {
            ImGui.Text("Path: Unknown");
        }

        ImGui.Text($"DataFileIdx: {hash.DataFileId}");
        ImGui.SameLine();
        ImGui.Text($"Offset: 0x{hash.Offset:X8}");
        ImGui.SameLine();
        ImGui.Text($"Hash: {hash.Hash:X16}");

        ImGui.Text($"Size: {file.RawData.Length} bytes");
        ImGui.SameLine();
        ImGui.Text($"Raw File Size: {file.FileHeader.RawFileSize} bytes");
        ImGui.Text($"Header Size: {file.FileHeader.Size}");
        ImGui.SameLine();
        ImGui.Text($"Blocks: {file.FileHeader.NumberOfBlocks}");
        ImGui.SameLine();
        ImGui.Text($"Type: {file.FileHeader.Type}");
        var type = TryGetType(path?.Path, file);
        ImGui.Text($"Parsed Type: {type}");
        
        if (ImGui.Button("Save raw file"))
        {
            var outFolder = Path.Combine(Program.DataDirectory, "SqPackFiles");
            if (!Directory.Exists(outFolder))
            {
                Directory.CreateDirectory(outFolder);
            }

            var ext = GetExtensionFromType(type);
            var outPath = Path.Combine(outFolder, $"{hash.Hash:X16}{ext}");
            File.WriteAllBytes(outPath, file.RawData.ToArray());
            Process.Start("explorer.exe", outFolder);
        }

        switch (type)
        {
            case SelectedFileType.Texture:
                if (view == null)
                {
                    var parsedFile = new TexFile(file.RawData);
                    view = new TexView(hash, parsedFile, imageHandler);
                }
                view.Draw();
                break;
            case SelectedFileType.Material:
            {
                if (view == null)
                {
                    var parsedFile = new MtrlFile(file.RawData);
                    view = new MtrlView(parsedFile, sqPack, imageHandler);
                }
                
                view.Draw();
                break;
            }
            case SelectedFileType.Model:
            {
                if (view == null)
                {
                    var parsedFile = new MdlFile(file.RawData);
                    view = new MdlView(parsedFile);
                }
                
                view.Draw();
                break;
            }
            default:
                view ??= new DefaultView(hash, file);
                view.Draw();
                break;
        }
    }
    
    private string GetExtensionFromType(SelectedFileType type)
    {
        return type switch
        {
            SelectedFileType.Texture => ".tex",
            SelectedFileType.Model => ".mdl",
            SelectedFileType.Material => ".mtrl",
            _ => ""
        };
    }
    
    private SelectedFileType GetTypeFromExtension(string extension)
    {
        return extension switch
        {
            ".tex" => SelectedFileType.Texture,
            ".mdl" => SelectedFileType.Model,
            ".mtrl" => SelectedFileType.Material,
            _ => SelectedFileType.None
        };
    }

    private SelectedFileType TryGetType(string? path, SqPackFile file)
    {
        var extension = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(extension))
        {
            if (GetTypeFromExtension(extension) != SelectedFileType.None)
            {
                return GetTypeFromExtension(extension);
            }
        }
        
        switch (file.FileHeader.Type)
        {
            case FileType.Model:
                return SelectedFileType.Model;
            case FileType.Texture:
                return SelectedFileType.Texture;
            case FileType.Empty:
                return SelectedFileType.None;
        }
        
        if (file.RawData.Length < 4)
        {
            return SelectedFileType.None;
        }
        var reader = new SpanBinaryReader(file.RawData);
        var magic = reader.ReadUInt32();
        if (magic == 16973824) // known .mtrl file version value
        {
            return SelectedFileType.Material;
        }
        
        return SelectedFileType.None;
    }
}
