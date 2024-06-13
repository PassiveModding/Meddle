using System.Diagnostics;
using System.Text;
using ImGuiNET;
using Meddle.UI.Windows.Views;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using Category = Meddle.Utils.Files.SqPack.Category;
using ParsedFilePath = Meddle.Utils.Files.SqPack.ParsedFilePath;
using Repository = Meddle.Utils.Files.SqPack.Repository;
using SqPack = Meddle.Utils.Files.SqPack.SqPack;

namespace Meddle.UI.Windows;

public class SqPackWindow
{
    private readonly ImageHandler imageHandler;
    private readonly SqPack sqPack;
    private readonly PathManager pathManager;
    private readonly Configuration config;
    private readonly ILogger<SqPackWindow> logger;
    private (int index, Category cat, IndexHashTableEntry hash, SqPackFile file)? selectedFile;
    private string filter = string.Empty;
    private string search = string.Empty;
    private Repository selectedRepository;
    private Category selectedCategory;
    private IView? view;
    private ExportView exportView;
    private DiscoverView discoverView;


    public SqPackWindow(SqPack sqPack, ImageHandler imageHandler, PathManager pathManager, Configuration config, ILogger<SqPackWindow> logger)
    {
        this.imageHandler = imageHandler;
        this.sqPack = sqPack;
        selectedRepository = sqPack.Repositories.First();
        selectedCategory = selectedRepository.Categories.First().Value;
        this.pathManager = pathManager;
        this.config = config;
        this.logger = logger;
        exportView = new ExportView(sqPack, config, imageHandler);
        discoverView = new DiscoverView(sqPack, config, pathManager);
    }

    public void Draw()
    {
        // menubar
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Import paths"))
                {
                    pathManager.ImportFilePickerOpen = true;
                }

                if (ImGui.MenuItem("Export paths"))
                {
                    pathManager.ExportFilePickerOpen = true;
                }
                
                if (ImGui.MenuItem("Discover texture paths"))
                {
                    pathManager.TextureDiscoveryOpen = true;
                }
                
                if (ImGui.MenuItem("Discovery"))
                {
                    pathManager.DiscoveryOpen = true;
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
        
        pathManager.DrawImport();
        pathManager.DrawExport();
        pathManager.RunTextureDiscovery();
        pathManager.RunDiscovery();
        
        InternalDraw();
    }

    private bool isFirstDraw = true;

    private void InternalDraw()
    {
        ImGui.Columns(3, "SqPackColumns", true);
        if (isFirstDraw)
        {
            ImGui.SetColumnWidth(0, 250);

            isFirstDraw = false;
        }

        if (ImGui.BeginChild("##SqPackWindow"))
        {
            DrawRepoCategory();
            BrowseCategory();
            ImGui.EndChild();
        }

        ImGui.NextColumn();
        if (ImGui.BeginChild("##PathViewer"))
        {
            DrawPathViewer();
            ImGui.EndChild();
        }

        ImGui.NextColumn();
        if (ImGui.BeginChild("##SqPackFile"))
        {
            if (view is ExportView ev)
            {
                ev.Draw();
            }
            else if (view is DiscoverView dv)
            {
                dv.Draw();
            }
            else
            {
                DrawFile();
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

        if (ImGui.BeginCombo(
                "Category", Category.TryGetCategoryName(selectedCategory.Id) ?? $"{selectedCategory.Id:X2}"))
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


    private void DrawPathViewer()
    {
        var cra = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##SqPackCategoryEntries", cra with {Y = cra.Y - 50}))
        {
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
                else
                {
                    logger.LogWarning("File not found: {Path}", search);
                }
            }

            if (ImGui.InputText("Filter", ref filter, 100))
            {
                pathManager.PathViewerCache = pathManager.ParsedPaths
                                                         .Where(x => x.Path.Contains(filter))
                                                         .OrderBy(x => x.Path)
                                                         .GroupBy(x => x.Path.Split('/')[0])
                                                         .ToList();
                pathManager.FolderCache.Clear();
            }
            else if (pathManager.PathViewerCache.Count == 0)
            {
                pathManager.PathViewerCache = pathManager.ParsedPaths
                                                         .OrderBy(x => x.Path)
                                                         .GroupBy(x => x.Path.Split('/')[0])
                                                         .ToList();
                pathManager.FolderCache.Clear();
            }

            foreach (var group in pathManager.PathViewerCache)
            {
                if (ImGui.TreeNode(group.Key))
                {
                    DrawPathSet(group.Key, group);
                    ImGui.TreePop();
                }
            }
            
            ImGui.EndChild();
        }
        
        if (ImGui.Button("Export Menu"))
        {
            view = exportView;
        }

        ImGui.SameLine();
        if (ImGui.Button("Test view"))
        {
            view = discoverView;
        }
    }

    private void DrawPathSet(string key, IEnumerable<ParsedFilePath> paths)
    {
        IGrouping<string, ParsedFilePath>[] groups;
        if (pathManager.FolderCache.TryGetValue(key, out var cached))
        {
            groups = cached;
        }
        else
        {
            groups = paths.GroupBy(x => x.Path.Substring(key.Length + 1).Split('/')[0]).ToArray();
            pathManager.FolderCache[key] = groups;
        }

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
        Sklb,
        Shpk
    }

    private int sliceStartIndex;
    private readonly int sliceSize = 200;

    private void BrowseCategory()
    {
        var cat = selectedCategory;
        ImGui.Text($"Index: {cat.Index}");
        ImGui.Text($"Index2: {cat.Index2}");
        ImGui.Text($"Entries: {cat.UnifiedIndexEntries.Count}");
        foreach (var datFilePath in cat.DatFilePaths)
        {
            var name = Path.GetFileName(datFilePath);
            ImGui.Text(name);
        }

        var cra = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##SqPackCategoryEntries", cra with {Y = cra.Y - 50}))
        {
            bool displayDatFileIdx = cat.DatFilePaths.Length > 1;
            var entries = cat.UnifiedIndexEntries.Skip(sliceStartIndex).Take(sliceSize).ToArray();
            for (var i = 0; i < entries.Length; i++)
            {
                var (hash, entry) = entries[i];
                var entryName =
                    $"{(displayDatFileIdx ? $"[{entry.DataFileId:00}]" : "")}[{entry.Offset:X8}] {hash:X16}##{hash:X16}";

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

                if (ImGui.IsItemHovered())
                {
                    // popup with path
                    if (path != null)
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text(path.Path);
                        ImGui.EndTooltip();
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


    private (Category category, IndexHashTableEntry hash, SqPackFile file)[]? discoverResults;

    private void DrawFile()
    {
        if (selectedFile == null)
        {
            return;
        }

        var hash = selectedFile.Value.hash;
        var path = DrawPathDiscovery(hash);
        hash = selectedFile.Value.hash;
        var file = selectedFile.Value.file;
        var type = TryGetType(path?.Path, file);
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

        // click to copy any of the info
        DrawField("Hash", hash.Hash.ToString("X16"));
        DrawField("DataFileIdx", hash.DataFileId.ToString());
        DrawField("Offset", hash.Offset.ToString("X8"));
        DrawField("Size", file.RawData.Length.ToString());
        ImGui.SameLine();
        DrawField("Raw File Size", file.FileHeader.RawFileSize.ToString());
        ImGui.SameLine();
        DrawField("Header Size", file.FileHeader.Size.ToString());
        DrawField("Blocks", file.FileHeader.NumberOfBlocks.ToString());
        DrawField("Type", file.FileHeader.Type.ToString());
        ImGui.SameLine();
        DrawField("Parsed Type", type.ToString());

        DrawFileView(hash, file, type, path?.Path);
    }

    private ParsedFilePath? DrawPathDiscovery(IndexHashTableEntry hash)
    {
        // This is used to discover other files with the same path.
        // Either between index and index2 or between different repositories (ie. copying the retail files to benchmark)
        if (discoverResults != null && discoverResults.All(x => x.hash.Hash != hash.Hash))
        {
            discoverResults = null;
        }

        var path = pathManager.GetPath(hash.Hash);
        if (path == null)
        {
            ImGui.Text("Path: Unknown");
            return null;
        }

        ImGui.Text($"Path: {path.Path}");
        if (ImGui.Button("Discover all"))
        {
            var files = sqPack.GetFiles(path.Path);
            discoverResults = files;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Discover any other files with the same path");
            ImGui.EndTooltip();
        }

        if (discoverResults != null)
        {
            ImGui.Text("Discovered files:");
            for (var i = 0; i < discoverResults.Length; i++)
            {
                var (category, sHash, sFile) = discoverResults[i];
                var repoName = category.Repository?.Path.Split(Path.DirectorySeparatorChar).Last();
                if (ImGui.Selectable($"[{i}] - {repoName} {sHash.Hash:X16}"))
                {
                    selectedFile = (selectedFile!.Value.index, category, sHash, sFile);
                    view = null;
                }
            }
        }

        return path;
    }
    
    private void DrawField(string label, string value)
    {
        ImGui.Text($"{label}: {value}");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(value);
            ImGui.EndTooltip();
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            ImGui.SetClipboardText(value);
        }
    }

    private void DrawFileView(IndexHashTableEntry hash, SqPackFile file, SelectedFileType type, string? path)
    {
        try
        {
            view = type switch
            {
                SelectedFileType.Texture => view ?? new TexView(hash, new TexFile(file.RawData), imageHandler, path),
                SelectedFileType.Material => view ?? new MtrlView(new MtrlFile(file.RawData), sqPack, imageHandler),
                SelectedFileType.Model => view ?? new MdlView(new MdlFile(file.RawData), path),
                SelectedFileType.Sklb => view ?? new SklbView(new SklbFile(file.RawData), config),
                SelectedFileType.Shpk => view ?? new ShpkView(new ShpkFile(file.RawData), path),
                SelectedFileType.None => view ?? new DefaultView(hash, file),
                _ => view ?? new DefaultView(hash, file)
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error drawing file view");
            view = new DefaultView(hash, file, e.ToString());
        }

        view?.Draw();
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
            ".sklb" => SelectedFileType.Sklb,
            ".shpk" => SelectedFileType.Shpk,
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
        if (magic == MtrlFile.MtrlMagic) // known .mtrl file version value
        {
            return SelectedFileType.Material;
        }

        if (magic == SklbFile.SklbMagic)
        {
            return SelectedFileType.Sklb;
        }

        if (magic == ShpkFile.ShPkMagic)
        {
            return SelectedFileType.Shpk;
        }

        return SelectedFileType.None;
    }
}
