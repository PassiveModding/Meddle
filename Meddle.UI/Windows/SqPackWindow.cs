using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using OtterTex;

namespace Meddle.UI.Windows;

public class SqPackWindow 
{
    private readonly string name;
    private readonly Vector2? initialSize;
    public bool Open;
    public readonly SqPack SqPack;
    public Task? InitTask;

    public SqPackWindow()
    {
        this.name = "SqPack";
        this.initialSize = new Vector2(400, 400);
        SqPack = new SqPack(Services.Configuration.GameDirectory!);
        InitTask = InitRl2();
    }
    
    private readonly ConcurrentBag<ParsedFilePath> parsedPaths = new();
    private async Task InitRl2()
    {
        
        var parsedPathsFile = Path.Combine(Program.DataDirectory, "parsed_paths.txt");
        if (File.Exists(parsedPathsFile))
        {
            var lines = File.ReadAllLines(parsedPathsFile);
            Parallel.ForEach(lines, new ParallelOptions
            {
                MaxDegreeOfParallelism = 50
            }, line =>
            {
                var hash = SqPack.GetFileHash(line);
                parsedPaths.Add(hash);
            });
        }
        else
        {
            var url = "https://rl2.perchbird.dev/download/export/CurrentPathList.gz";
            var path = Path.Combine(Program.DataDirectory, "paths_rl2_m.txt");
            if (!File.Exists(path))
            {
                using var client = new HttpClient();
                var req = await client.GetStreamAsync(url);
                await using var gz = new GZipStream(req, CompressionMode.Decompress);
                using var reader = new StreamReader(gz);

                var rl = new List<string>();
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line is null) continue;
                    rl.Add(line);
                }

                File.WriteAllLines(path, rl);
            }

            var lines = File.ReadAllLines(path);
            Parallel.ForEach(lines, new ParallelOptions
            {
                MaxDegreeOfParallelism = 50
            }, line =>
            {
                if (SqPack.FileExists(line, out var hash))
                {
                    parsedPaths.Add(hash);
                }
            });

            File.WriteAllLines(parsedPathsFile, parsedPaths.Select(x => x.Path));
        }
    }

    public void Draw()
    {
        if (initialSize != null) {
            ImGui.SetNextWindowSize(initialSize.Value, ImGuiCond.FirstUseEver);
        }
        
        if (ImGui.Begin(name, ref this.Open, ImGuiWindowFlags.None)) InternalDraw();
        ImGui.End();
        
        if (InitTask?.IsCompleted == true)
        {
            InitTask = null;
            parsedPathDict.Clear();
        }
    }

    private string search = string.Empty;
    private void InternalDraw()
    {
        ImGui.SetNextItemWidth(200);
        var cra = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##SqPackWindow", cra with {X = 200}, ImGuiChildFlags.Border);
        foreach (var repo in SqPack.Repositories)
        {
            var folderName = repo.Path.Split(Path.DirectorySeparatorChar).Last();
            if (ImGui.CollapsingHeader(folderName))
            {
                DrawRepository(repo);
            }
        }
        
        ImGui.EndChild();
        
        if (selectedCategory == null)
        {
            selectedCategory = SqPack.Repositories[0].Categories.First().Value;
        }

        ImGui.SameLine();
        ImGui.BeginChild("##SqPackCategory", cra with {X = 300 }, ImGuiChildFlags.Border);
        BrowseCategory(selectedCategory!);
        ImGui.EndChild();
        
        ImGui.SameLine();
        ImGui.BeginChild("##PathViewer", cra with {X = 300 }, ImGuiChildFlags.Border);
        DrawPathViewer();
        ImGui.EndChild();
        
        if (selectedFile.HasValue)
        {
            ImGui.SameLine();
            ImGui.BeginChild("##SqPackFile", cra with {X = cra.X - 830}, ImGuiChildFlags.Border);
            DrawFile(selectedFile.Value.hash, selectedFile.Value.file);
            ImGui.EndChild();
        }
    }
    
    private List<IGrouping<string, ParsedFilePath>> pvCache = new();
    private void DrawPathViewer()
    {
        if (ImGui.InputText("Search", ref search, 100))
        {
            var hash = SqPack.GetFileHash(search);
            var file = SqPack.GetFile(hash);
            if (file != null)
            {
                selectedFile = (0, file.Value.hash, file.Value.file);
                parsedFile = null;
            }
        }
        
        if (pvCache.Count == 0)
        {
            pvCache = parsedPaths
                      .OrderBy(x => x.Path)
                      .GroupBy(x => x.Path.Split('/')[0])
                      .ToList();
        }
        
        foreach (var group in pvCache)
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
                    var file = SqPack.GetFile(hash);
                    if (file != null)
                    {
                        selectedFile = (0, file.Value.hash, file.Value.file);
                        parsedFile = null;
                    }
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
    
    
    private void DrawRepository(Repository repo)
    {
        foreach (var cg in repo.Categories.GroupBy(x => x.Key.category))
        {
            ImGui.Separator();
            var cName = $"[{cg.Key:X2}]";
            var catName = Category.TryGetCategoryName(cg.Key);
            ImGui.Text($"{cName} {catName}");
                
            foreach (var (key, cat) in cg)
            {
                DrawCategory(cat);
            }
        }
    }
    
    private Category? selectedCategory;
    private void DrawCategory(Category cat)
    {
        if (ImGui.Button($"Select##{cat.GetHashCode()}"))
        {
            selectedCategory = cat;
            sliceStartIndex = 0;
        }
        ImGui.Text($"Entries: {cat.UnifiedIndexEntries.Count}");
        ImGui.Text($"Index: {cat.Index}");
        ImGui.Text($"Index2: {cat.Index2}");
        foreach (var path in cat.DatFilePaths)
        {
            var fileName = Path.GetFileName(path);
            ImGui.Text($"{fileName}");
        }
    }
    
    private (int index, IndexHashTableEntry hash, SqPackFile file)? selectedFile;
    private int sliceStartIndex = 0;
    private int slizeSize = 200;
    private Dictionary<ulong, ParsedFilePath?> parsedPathDict = new();
    private void BrowseCategory(Category cat)
    {
        var catName = Category.TryGetCategoryName(cat.Id);
        ImGui.Text($"Category: {catName}");
        ImGui.Text($"Entries: {cat.UnifiedIndexEntries.Count}");
        var cra = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##SqPackCategoryEntries", cra with {Y = cra.Y - 50}))
        {
            bool displayDatFileIdx = cat.DatFilePaths.Length > 1;
            var entries = cat.UnifiedIndexEntries.Skip(sliceStartIndex).Take(slizeSize).ToArray();
            for (var i = 0; i < entries.Length; i++)
            {
                var (hash, entry) = entries[i];
                var name = $"{(displayDatFileIdx ? $"[{entry.DataFileId:00}]" : "")}[0x{entry.Offset:X8}] {hash:X16}##{hash:X16}";

                if (!parsedPathDict.TryGetValue(hash, out var path))
                {
                    path = parsedPaths.FirstOrDefault(x => x.IndexHash == hash || x.Index2Hash == hash);
                    parsedPathDict[hash] = path;
                }
                
                if (path != null)
                {
                    name = path.Path;
                }
                
                if (ImGui.Selectable(name))
                {
                    if (cat.TryGetFile(hash, out var data))
                    {
                        var index = sliceStartIndex + i;
                        selectedFile = (index, entry, data);
                        parsedFile = null;
                    }
                }
            }

            ImGui.EndChild();
        }

        if (ImGui.Button("Prev Page"))
        {
            sliceStartIndex -= slizeSize;
            if (sliceStartIndex < 0)
            {
                sliceStartIndex = 0;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Next Page"))
        {
            sliceStartIndex += slizeSize;
            if (sliceStartIndex >= cat.UnifiedIndexEntries.Count)
            {
                sliceStartIndex = 0;
            }
        }
        
        ImGui.SameLine();
        var endIdx = sliceStartIndex + slizeSize;
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
                    selectedFile = (index, entry, data);
                    parsedFile = null;
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
                    selectedFile = (index, entry, data);
                    parsedFile = null;
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

    private object? parsedFile;
    private Dictionary<TexFile, nint> textureCache = new();
    private void DrawFile(IndexHashTableEntry hash, SqPackFile file)
    {
        ImGui.Text($"Hash: {hash.Hash:X16}");
        ImGui.SameLine();
        ImGui.Text($"Size: {file.RawData.Length} bytes");
        ImGui.SameLine();
        ImGui.Text($"Raw File Size: {file.FileHeader.RawFileSize} bytes");
        ImGui.Text($"Header Size: {file.FileHeader.Size}");
        ImGui.SameLine();
        ImGui.Text($"Blocks: {file.FileHeader.NumberOfBlocks}");
        ImGui.SameLine();
        ImGui.Text($"Type: {file.FileHeader.Type}");
        if (ImGui.Button("Save"))
        {
            var outFolder = Path.Combine(Program.DataDirectory, "SqPackFiles");
            if (!Directory.Exists(outFolder))
            {
                Directory.CreateDirectory(outFolder);
            }

            var ext = file.FileHeader.Type switch
            {
                FileType.Texture => ".tex",
                FileType.Model => ".mdl",
                _ => ".dat",
            };
            
            var outPath = Path.Combine(outFolder, $"{hash.Hash:X16}{ext}");
            File.WriteAllBytes(outPath, file.RawData.ToArray());
            Process.Start("explorer.exe", outFolder);
        }
        
        if (file.FileHeader.Type == FileType.Texture)
        {
            parsedFile ??= new TexFile(file.RawData);
            
            var texFile = (TexFile)parsedFile;
            
            ImGui.SameLine();
            ImGui.Text($"Size: {texFile.Header.Width}x{texFile.Header.Height}");
            ImGui.Text($"Format: {texFile.Header.Format}");
            ImGui.Text($"Type: {texFile.Header.Type}");
            ImGui.Text($"Depth: {texFile.Header.Depth}");
            ImGui.SameLine();
            ImGui.Text($"Array Size: {texFile.Header.ArraySize}");
            ImGui.SameLine();
            ImGui.Text($"Mips: {texFile.Header.MipLevels}");

            var meta = ImageUtils.GetTexMeta(texFile);
            if (!textureCache.TryGetValue(texFile, out var imagePtr) || DrawTexOptions(meta))
            {
                var texData = ImageUtils.GetTexData(texFile, arrayLevel, mipLevel, slice);
                imagePtr = Services.ImageHandler.DrawTexData(texData, out bool cleanup);
                if (cleanup)
                {
                    textureCache.Clear();
                }
                textureCache[texFile] = imagePtr;
            }
            
            var availableSize = ImGui.GetContentRegionAvail();
            // keep aspect ratio and fit in the available space
            Vector2 size = new(texFile.Header.Width, texFile.Header.Height);
            if (texFile.Header.Width > availableSize.X)
            {
                var ratio = availableSize.X / texFile.Header.Width;
                size = new Vector2(texFile.Header.Width * ratio, texFile.Header.Height * ratio);
            }
            
            if (texFile.Header.Height > availableSize.Y)
            {
                var ratio = availableSize.Y / texFile.Header.Height;
                size = new Vector2(texFile.Header.Width * ratio, texFile.Header.Height * ratio);
            }

            if (ImGui.Button("Save as png"))
            {
                var image = ImageUtils.GetTexData(texFile, arrayLevel, mipLevel, slice);
                var outFolder = Path.Combine(Program.DataDirectory, "SqPackFiles");
                if (!Directory.Exists(outFolder))
                {
                    Directory.CreateDirectory(outFolder);
                }
                
                var outPath = Path.Combine(outFolder, $"{hash.Hash:X16}.png");
                File.WriteAllBytes(outPath, ImageUtils.ImageAsPng(image).ToArray());
            }
            
            ImGui.Image(imagePtr, size);
        }
    }

    private int arrayLevel;
    private int mipLevel;
    private int slice;
    private bool SetSlice(int level)
    {
        if (level != slice)
        {
            slice = level;
            return true;
        }
        
        return false;
    }
    private bool SetMipLevel(int level)
    {
        if (level != mipLevel)
        {
            mipLevel = level;
            return true;
        }
        
        return false;
    }
    private bool SetArrayLevel(int level)
    {
        if (level != arrayLevel)
        {
            arrayLevel = level;
            return true;
        }
        
        return false;
    }
    public bool DrawTexOptions(TexMeta meta)
    {
        var changed = false;
        if (meta.ArraySize > 1)
        {
            var arrLvl = arrayLevel;
            if (ImGui.InputInt("ArrayLevel", ref arrLvl))
            {
                changed = SetArrayLevel(arrLvl);
            }
            if (this.arrayLevel >= meta.ArraySize)
            {
                changed = SetArrayLevel(0);
            }
            else if (this.arrayLevel < 0)
            {
                changed = SetArrayLevel(Math.Max(0, meta.ArraySize - 1));
            }
        }
        else if (arrayLevel != 0)
        {
            changed = SetArrayLevel(0);
        }

        if (meta.MipLevels > 1)
        {
            var mipLvl = mipLevel;
            if (ImGui.InputInt("MipLevel", ref mipLvl))
            {
                changed = SetMipLevel(mipLvl);
            }
            if (this.mipLevel >= meta.MipLevels)
            {
                changed = SetMipLevel(0);
            }
            else if (this.mipLevel < 0)
            { 
                changed = SetMipLevel(Math.Max(0, meta.MipLevels - 1));
            }
        }
        else if (mipLevel != 0)
        {
            changed = SetMipLevel(0);
        }

        if (meta.Depth > 1)
        {
            var slc = slice;
            if (ImGui.InputInt("Slice", ref slc))
            {
                changed = SetSlice(slc);
            }
            if (this.slice >= meta.Depth)
            {
                changed = SetSlice(0);
            }
            else if (this.slice < 0)
            { 
                changed = SetSlice(Math.Max(0, meta.Depth - 1));
            }
        }
        else if (slice != 0)
        {
            changed = SetSlice(0);
        }
        
        return changed;
    }
}
