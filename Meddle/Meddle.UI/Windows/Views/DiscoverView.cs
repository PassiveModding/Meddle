using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;

namespace Meddle.UI.Windows.Views;

public class DiscoverView(SqPack pack, Configuration configuration, PathManager pathManager) : IView
{
    private CancellationTokenSource? discoverCts;
    private Task? discoverTask;
    private readonly List<string> log = new();
    
    public record DiscoveredMtrl(string SourcePath, string ResolvedPath, string MdlPath);
    public record DiscoveredTexture(string SourcePath, string MtrlPath);
    private readonly Dictionary<string, DiscoveredMtrl> discoveredMtrls = new();
    private readonly Dictionary<string, DiscoveredTexture> discoveredTextures = new();
    private readonly HashSet<string> discoveredShaders = new();

    public void HandlePath(ParsedFilePath pathInfo)
    {
        try 
        { 
            if (pathInfo.Path.EndsWith(".mdl"))
            {
                HandleMdlFile(pathInfo.Path);
            }
            else if (pathInfo.Path.EndsWith(".mtrl"))
            {
                var file = pack.GetFile(pathInfo.Path);
                if (file is null)
                {
                    log.Add($"Failed to get mtrl file {pathInfo.Path}");
                    return;
                }
                var mtrl = new MtrlFile(file.Value.file.RawData);
                DiscoverShaders(mtrl);
                DiscoverTextures(mtrl, pathInfo.Path);
            }
        }
        catch (Exception ex)
        {
            log.Add($"[{pathInfo.Path}] Exception: {ex}");
        }
    }
    
    private int progress;
    private int count;
    public async Task DiscoverAsync(CancellationToken token)
    {
        discoveredMtrls.Clear();
        discoveredTextures.Clear();
        discoveredShaders.Clear();
        log.Clear();
        var initPaths = pathManager.ParsedPaths.ToArray();
        progress = 0;
        count = initPaths.Length;

        await Parallel.ForEachAsync(initPaths, token, (path, tkn) =>
        {
            if (tkn.IsCancellationRequested)
            {
                return ValueTask.CompletedTask;
            }
            
            HandlePath(path);
            Interlocked.Increment(ref progress);
            return ValueTask.CompletedTask;
        });
                    
        await File.WriteAllLinesAsync("discovered_mtrls.txt", discoveredMtrls.Select(x => x.Value.ResolvedPath), token);
        await File.WriteAllLinesAsync("discovered_textures.txt", discoveredTextures.Select(x => x.Key), token);
        await File.WriteAllLinesAsync("discovered_shaders.txt", discoveredShaders, token);
    }
    
    private string searchHash = "";
    public void Draw()
    {
        if (ImGui.Button("RunDiscovery"))
        {
            discoverCts?.Cancel();
            discoverCts = new CancellationTokenSource();
            discoverTask = Task.Run(async () => await DiscoverAsync(discoverCts.Token));
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Cancel Discovery"))
        {
            discoverCts?.Cancel();
        }
        
        if (discoverTask is not null && discoverTask.IsCompleted)
        {
            ImGui.Text("Discover task completed");
        }
        
        ImGui.SameLine();
        if (ImGui.Button("ShpkDump"))
        {
            ShpkDump();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("ShpkDump2"))
        {
            ShpkDump2();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("MtrlDump"))
        {
            discoverCts?.Cancel();
            // iterate all .mtrl files and find the one with stocking in it
            discoverCts = new CancellationTokenSource();
            discoverTask = Task.Run(async () => await MtrlDump(discoverCts.Token));
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Discover New"))
        {
            discoverCts?.Cancel();
            discoverCts = new CancellationTokenSource();
            discoverTask = Task.Run(async () => await DiscoverNew(discoverCts.Token));
        }
        
        // search by hash
        ImGui.SameLine();
        searchHash ??= "";
        if (ImGui.InputText("Search by hash", ref searchHash, 100))
        {
            searchHash = Regex.Replace(searchHash, "[^0-9]", "");
            var hash = ulong.Parse(searchHash);
            foreach (var repo in pack.Repositories)
            {
                foreach (var category in repo.Categories)
                {
                    if (category.Value.TryGetFile(hash, out var file))
                    {
                        var tmp = new MtrlFile(file.RawData);
                        
                        var strings = tmp.GetStrings();
                        Console.WriteLine($"Found val in {category.Key}");
                    }
                }
            }
        }
        
        // progress bar
        ImGui.ProgressBar(progress / (float)count, new Vector2(0, 0));
        
        // scroll box of discovered files
        ImGui.SeparatorText("Discovered Files");
        var availableSize = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("Discovered Files", new Vector2(0, availableSize.Y - 350));
        try
        {
            // show all paths not present in parsed paths
            foreach (var (key, value) in discoveredMtrls.ToArray())
            {
                ImGui.Text($"[{value.MdlPath}] {value.SourcePath} -> {value.ResolvedPath}");
            }
            
            foreach (var (key, value) in discoveredTextures.ToArray())
            {
                ImGui.Text($"[{value.MtrlPath}] {key}");
            }
            
            foreach (var shader in discoveredShaders.ToArray())
            {
                ImGui.Text($"Shader: {shader}");
            }
        }
        finally
        {
            ImGui.EndChild();
        }
        
        // scroll box of logs
        ImGui.SeparatorText("Logs");
        ImGui.BeginChild("Logs", new Vector2(0, 300));
        try
        {
            foreach (var line in log.ToArray())
            {
                ImGui.Text(line);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private record ShaderInfo(float[] Defaults, int index);
    public void ShpkDump2()
    {
        var shpkPaths = pathManager.ParsedPaths.Where(x => x.Path.EndsWith(".shpk")).ToList();
        var distinctCrc = new Dictionary<uint, (string name, HashSet<string> shaders, HashSet<string> types)>();
        var materialParams = new Dictionary<uint, Dictionary<string, ShaderInfo>>();
        foreach (var path in shpkPaths)
        {
            var file = pack.GetFile(path.Path);
            if (file == null) continue;
            
            var shpk = new ShpkFile(file.Value.file.RawData);
            var stringReader = new SpanBinaryReader(shpk.RawData[(int)shpk.FileHeader.StringsOffset..]);
            foreach (var sampler in shpk.Samplers)
            {
                var resName = stringReader.ReadString((int)sampler.StringOffset);
                // compute crc
                var crc = Crc32.GetHash(resName);
                if (!distinctCrc.TryGetValue(crc, out var entry))
                {
                    entry = (resName, new HashSet<string>(), new HashSet<string>());
                    distinctCrc[crc] = entry;
                }
                
                entry.shaders.Add(path.Path);
                entry.types.Add("SAMPLER");
            }
                
            foreach (var constant in shpk.Constants)
            {
                var resName = stringReader.ReadString((int)constant.StringOffset);  
                var crc = Crc32.GetHash(resName);
                
                if (!distinctCrc.TryGetValue(crc, out var entry))
                {
                    entry = (resName, new HashSet<string>(), new HashSet<string>());
                    distinctCrc[crc] = entry;
                }
                
                entry.shaders.Add(path.Path);
                entry.types.Add("CONSTANT");
            }
                
            foreach (var texture in shpk.Textures)
            {
                var resName = stringReader.ReadString((int)texture.StringOffset);
                var crc = Crc32.GetHash(resName);
                
                if (!distinctCrc.TryGetValue(crc, out var entry))
                {
                    entry = (resName, new HashSet<string>(), new HashSet<string>());
                    distinctCrc[crc] = entry;
                }
                
                entry.shaders.Add(path.Path);
                entry.types.Add("TEXTURE");
            }
                
            foreach (var uav in shpk.Uavs)
            {
                var resName = stringReader.ReadString((int)uav.StringOffset);
                var crc = Crc32.GetHash(resName);
                
                if (!distinctCrc.TryGetValue(crc, out var entry))
                {
                    entry = (resName, new HashSet<string>(), new HashSet<string>());
                    distinctCrc[crc] = entry;
                }
                
                entry.shaders.Add(path.Path);
                entry.types.Add("UAV");
            }

            for (int i = 0; i < shpk.MaterialParams.Length; i++)
            {
                var materialParam = shpk.MaterialParams[i];
                var defaults = shpk.MaterialParamDefaults.Skip(materialParam.ByteOffset / 4).Take(materialParam.ByteSize / 4).ToArray();
                
                if (!materialParams.TryGetValue(materialParam.Id, out var entry))
                {
                    entry = new Dictionary<string, ShaderInfo>();
                    materialParams[materialParam.Id] = entry;
                }
                
                entry[path.Path] = new ShaderInfo(defaults, i);
            }
        }
        
        var outputMaterialParamsList = new List<string>();
        outputMaterialParamsList.Add("ID\tIndex\tDefaults\tShader");
        foreach (var (id, shaders) in materialParams)
        {
            // group by defaults
            var groupedShaders = shaders.GroupBy(x => x.Value.Defaults, new FloatArrayComparer());
            foreach (var group in groupedShaders)
            {
                foreach (var (shader, info) in group)
                {
                    outputMaterialParamsList.Add($"0x{id:X8}\t{info.index}\t{string.Join(",", group.Key)}\t{shader}");
                }
            }
        }
        
        File.WriteAllLines("material_params.txt", outputMaterialParamsList);
        
        // another but ordered by shader
        var outputMaterialParamsList2 = new List<string>();
        outputMaterialParamsList2.Add("Shader\tID\tIndex\tDefaults");
        var flattenedMaterialParams = materialParams.SelectMany(x => x.Value.Select(y => (x.Key, y.Key, y.Value.Defaults, y.Value.index)));
        foreach (var (id, shader, defaults, index) in flattenedMaterialParams.OrderBy(x => x.Item2))
        {
            outputMaterialParamsList2.Add($"{shader}\t0x{id:X8}\t{index}\t{string.Join(",", defaults)}");
        }
        
        File.WriteAllLines("material_params2.txt", outputMaterialParamsList2);
    }
    
    private class FloatArrayComparer : IEqualityComparer<float[]>
    {
        public bool Equals(float[] x, float[] y)
        {
            return x.SequenceEqual(y);
        }

        public int GetHashCode(float[] obj)
        {
            return obj.Aggregate(0, (acc, val) => acc ^ val.GetHashCode());
        }
    }

    public async Task MtrlDump(CancellationToken token)
    {
        var stats = new ConcurrentDictionary<string, HashSet<ulong>>();
        foreach (var repo in pack.Repositories)
        {
            foreach (var category in repo.Categories)
            {
                //foreach (var hash in category.Value.UnifiedIndexEntries)
                await Parallel.ForEachAsync(category.Value.UnifiedIndexEntries, token, (hash, lTok) =>
                {
                    try
                    {
                        if (category.Value.TryGetFile(hash.Key, out var packFile))
                        {
                            var shpkName = TryParseFileFromHash(category.Value, hash, packFile);
                            if (shpkName is not null)
                            {
                                Console.WriteLine($"Found {shpkName} on {category.Key}");
                                stats.GetOrAdd(shpkName, _ => []).Add(hash.Key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while processing {category.Key}: {ex}");
                    }

                    return ValueTask.CompletedTask;
                });
            }
        }
        
        var sb = new StringBuilder();
        foreach (var (key, value) in stats)
        {
            sb.AppendLine($"{key}: {value.Count}");
        }
        
        await File.WriteAllTextAsync("mtrl_dump.txt", sb.ToString(), token);
        shpkStats = stats.ToDictionary(x => x.Key, x => x.Value);
    }

    private Dictionary<string, HashSet<ulong>> shpkStats = new();
    
    public string? TryParseFileFromHash(Category category, KeyValuePair<ulong, IndexHashTableEntry> hash, SqPackFile packFile)
    {
        // if data starts with mtrlMagic
        var fileReader = new SpanBinaryReader(packFile.RawData);
        if (fileReader.ReadUInt32() == MtrlFile.MtrlMagic)
        {
            var mtrl = new MtrlFile(packFile.RawData);
            var shaderPackageName = mtrl.GetShaderPackageName();
            return shaderPackageName;
        }
        
        return null;
    }
    
    public void ShpkDump()
    {
        var shpkPaths = pathManager.ParsedPaths.Where(x => x.Path.EndsWith(".shpk")).ToList();
        var sb = new StringBuilder();
        var distinctCrc = new Dictionary<uint, string>();
        foreach (var path in shpkPaths)
        {
            var file = pack.GetFile(path.Path);
            if (file == null) continue;
            
            var shpk = new ShpkFile(file.Value.file.RawData);
            var stringReader = new SpanBinaryReader(shpk.RawData[(int)shpk.FileHeader.StringsOffset..]);
            foreach (var sampler in shpk.Samplers)
            {
                if (sampler.Slot != 2)
                    continue;
        
                var resName = stringReader.ReadString((int)sampler.StringOffset);
                // compute crc
                var crc = Crc32.GetHash(resName);
                sb.AppendLine($"{path.Path}: SAMPLER {resName} - {crc}");
                distinctCrc[crc] = resName;
            }
                
            foreach (var constant in shpk.Constants)
            {
                if (constant.Slot != 2)
                    continue;
                var resName = stringReader.ReadString((int)constant.StringOffset);  
                var crc = Crc32.GetHash(resName);
                sb.AppendLine($"{path.Path}: CONSTANT {resName} - {crc}");
                distinctCrc[crc] = resName;
            }
                
            foreach (var texture in shpk.Textures)
            {
                if (texture.Slot != 2)
                    continue;
                var resName = stringReader.ReadString((int)texture.StringOffset);
                var crc = Crc32.GetHash(resName);
                sb.AppendLine($"{path.Path}: TEXTURE {resName} - {crc}");
                distinctCrc[crc] = resName;
            }
                
            foreach (var uav in shpk.Uavs)
            {
                if (uav.Slot != 2)
                    continue;
                var resName = stringReader.ReadString((int)uav.StringOffset);
                var crc = Crc32.GetHash(resName);
                sb.AppendLine($"{path.Path}: UAV {resName} - {crc}");
                distinctCrc[crc] = resName;
            }
        }
        
        File.WriteAllText("shpk_dump.txt", sb.ToString());
        File.WriteAllLines("shpk_distinct.txt", distinctCrc.Select(x => $"{x.Value} = 0x{x.Key:X8},"));
    }

    public void HandleMtrlPath(string mdlPath, string mtrlPath)
    {
        if (!mtrlPath.StartsWith('/'))
        {
            var file = pack.GetFile(mtrlPath);
            if (file is null)
            {
                log.Add($"Failed to find {mdlPath} -> {mtrlPath}");
                return;
            }

            var mtrlFile = new MtrlFile(file.Value.file.RawData);
            DiscoverShaders(mtrlFile);
            DiscoverTextures(mtrlFile, mtrlPath);
            return;
        }
        
        var resolvedPath = PathUtil.Resolve(mdlPath, mtrlPath);
        var lookupResult = pack.GetFile(resolvedPath);
        var foundCount = 0;
        if (lookupResult is null)
        {
            var prefix = resolvedPath[..resolvedPath.LastIndexOf('/')];
            
            // in chunks of 5, try and find each mtrlPath, if none in group, break, otherwise continue
            var chunkSize = 5;
            var offset = 0;

            while (true)
            {
                var found = false;

                for (var i = offset; i < offset + chunkSize; i++)
                {
                    var newResolvedPath = $"{prefix}/v{i:D4}{mtrlPath}";
                    var newLookupResult = pack.GetFile(newResolvedPath);
                    if (newLookupResult is not null)
                    {
                        var mtrlFile = new MtrlFile(newLookupResult.Value.file.RawData);
                        discoveredMtrls[newResolvedPath] = new DiscoveredMtrl(mtrlPath, newResolvedPath, mdlPath);
                        DiscoverShaders(mtrlFile);
                        DiscoverTextures(mtrlFile, newResolvedPath);
                        Interlocked.Increment(ref foundCount);
                        found = true;
                    }
                }
                
                if (!found)
                {
                    break;
                }
                
                offset += chunkSize;
            }
        }
        else
        {
            var mtrlFile = new MtrlFile(lookupResult.Value.file.RawData);
            discoveredMtrls[resolvedPath] = new DiscoveredMtrl(mtrlPath, resolvedPath, mdlPath);
            DiscoverShaders(mtrlFile);
            DiscoverTextures(mtrlFile, mtrlPath);
            foundCount = 1;
        }

        if (foundCount == 0)
        {
            log.Add($"Failed to find {mdlPath} -> {mtrlPath}");
        }
        else if (foundCount > 1)
        {
            log.Add($"Found multiple ({foundCount}) {mdlPath} -> {mtrlPath}");
        }
    }
    
    public void HandleMdlFile(string path)
    {
        var mdlFile = pack.GetFile(path);
        if (mdlFile is null)
        {
            log.Add($"Failed to get mdl file {path}");
            return;
        }
        
        var mdl = new MdlFile(mdlFile.Value.file.RawData);
        foreach (var (offset, mtrlPath) in mdl.GetMaterialNames())
        {
            HandleMtrlPath(path, mtrlPath);
        }
    }
    public void DiscoverTextures(MtrlFile mtrlFile, string mtrlPath)
    {
        var texturePaths = mtrlFile.GetTexturePaths();
        foreach (var (_, texPath) in texturePaths)
        {
            if (texPath == "dummy.tex")
            {
                continue;
            }
            
            try
            {
                var texResult = pack.GetFile(texPath);
                if (texResult is null)
                {
                    log.Add($"Failed to find {texPath}");
                }
                else
                {
                    discoveredTextures[texPath] = new DiscoveredTexture(texPath, mtrlPath);
                }
            }
            catch (Exception ex)
            {
                log.Add($"Error while discovering {texPath}: {ex}");
            }
        }
    }

    public void DiscoverShaders(MtrlFile mtrlFile)
    {
        var shpkName = mtrlFile.GetShaderPackageName();
        var sm5Path = $"shader/sm5/shpk/{shpkName}";
        var legacyPath = $"shader/shpk/{shpkName}";
        var sm5Result = pack.GetFile(sm5Path);
        var legacyResult = pack.GetFile(legacyPath);
        if (sm5Result is not null)
        {
            discoveredShaders.Add(sm5Path);
        }
        
        if (legacyResult is not null)
        {
            discoveredShaders.Add(legacyPath);
        }

        if (sm5Result is null && legacyResult is null)
        {
            log.Add($"Failed to find {shpkName}");
        }
    }

    public Task DiscoverNew(CancellationToken token)
    {
        var textureNames = new ConcurrentDictionary<string, bool>();
        var mtrlNamesFull = new ConcurrentDictionary<string, bool>();
        var mtrlNames = new ConcurrentDictionary<string, bool>();
        var mdlnames = new ConcurrentDictionary<string, bool>();
        var shpkNames = new ConcurrentDictionary<string, bool>();
        foreach (var repository in pack.Repositories)
        {
            if (repository.ExpansionId != null) continue;
            foreach (var category in repository.Categories)
            {
                if (Category.TryGetCategoryName(category.Key.category) != "chara")
                {
                    continue;
                }
                
                if (token.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }
                
                Console.WriteLine($"Processing {category.Key}");
                
                Parallel.ForEach(category.Value.UnifiedIndexEntries, (hash, tk) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    
                    try
                    {
                        if (category.Value.TryGetFile(hash.Value.Hash, out var data))
                        {
                            if (data.FileHeader.Type == FileType.Model)
                            {
                                var mdlFile = new MdlFile(data.RawData);
                                var mtrlPaths = mdlFile.GetMaterialNames();
                                foreach (var (_, mtrlPath) in mtrlPaths)
                                {
                                    if (mtrlPath.StartsWith("/"))
                                    {
                                        if (mtrlNames.TryAdd(mtrlPath, true))
                                        {
                                            //Console.WriteLine($"[MTRL] Found {mtrlPath} on {category.Key}");
                                        }
                                    }
                                    else
                                    {
                                        if (mtrlNamesFull.TryAdd(mtrlPath, true))
                                        {
                                            //Console.WriteLine($"[MTRL] Found {mtrlPath} on {category.Key}");
                                        }
                                    }
                                }
                            }
                            else if (data.FileHeader.Type == FileType.Standard)
                            {
                                if (data.RawData.Length < 4)
                                {
                                    return;
                                }

                                var reader = new SpanBinaryReader(data.RawData);
                                var magic = reader.ReadUInt32();
                                if (magic == MtrlFile.MtrlMagic)
                                {
                                    var mtrl = new MtrlFile(data.RawData);
                                    var shaderPackageName = mtrl.GetShaderPackageName();
                                    if (shpkNames.TryAdd(shaderPackageName, true))
                                    {
                                        //Console.WriteLine($"[SHPK] Found {shaderPackageName} on {category.Key}");
                                    }

                                    var texturePaths = mtrl.GetTexturePaths();
                                    foreach (var (_, texPath) in texturePaths)
                                    {
                                        if (texPath == "dummy.tex")
                                        {
                                            continue;
                                        }

                                        if (textureNames.TryAdd(texPath, true))
                                        {
                                            //Console.WriteLine($"[TEX] Found {texPath} on {category.Key}");
                                            var mdlPath = ResolveMdlFromTexPath(texPath);
                                            if (mdlPath is not null && mdlnames.TryAdd(mdlPath, true))
                                            {
                                                //Console.WriteLine($"[MDL] Found {mdlPath} on {category.Key}");
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // skip for now
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Add($"Error while processing {category.Key}: {ex}");
                        Console.WriteLine($"Error while processing {category.Key}: {ex}");
                    }
                });
            }
        }
        
        Parallel.ForEach(mdlnames.Keys, (mdlPath, tk) =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }
            
            try
            {
                var mdlFile = pack.GetFile(mdlPath);
                if (mdlFile is null)
                {
                    log.Add($"Failed to get mdl file {mdlPath}");
                    return;
                }
                
                var mdl = new MdlFile(mdlFile.Value.file.RawData);
                var newPaths = DiscoverMtrlsFromMdl(mdl, mdlPath);
                foreach (var mtrlPath in newPaths.mtrlPaths)
                {
                    if (mtrlNames.TryAdd(mtrlPath, true))
                    {
                        Console.WriteLine($"[MTRL] Found {mtrlPath} from {mdlPath}");
                    }
                }
                
                foreach (var texPath in newPaths.texPaths)
                {
                    if (textureNames.TryAdd(texPath, true))
                    {
                        Console.WriteLine($"[TEX] Found {texPath} from {mdlPath}");
                    }
                }
                
                foreach (var shpkName in newPaths.shpkNames)
                {
                    if (shpkNames.TryAdd(shpkName, true))
                    {
                        Console.WriteLine($"[SHPK] Found {shpkName} from {mdlPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Add($"Error while processing {mdlPath}: {ex}");
            }
        });
        
        Console.WriteLine("Writing discovered files");
        File.WriteAllLines("discovered_mtrls.txt", mtrlNames.Keys.OrderBy(x => x).ToArray());
        File.WriteAllLines("discovered_mtrls_full.txt", mtrlNamesFull.Keys.OrderBy(x => x).ToArray());
        File.WriteAllLines("discovered_textures.txt", textureNames.Keys.OrderBy(x => x).ToArray());
        File.WriteAllLines("discovered_mdls.txt", mdlnames.Keys.OrderBy(x => x).ToArray());
        File.WriteAllLines("discovered_shpk.txt", shpkNames.Keys.OrderBy(x => x).ToArray());
        
        return Task.CompletedTask;
    }

    private (List<string> mtrlPaths, List<string> texPaths, List<string> shpkNames) DiscoverMtrlsFromMdl(MdlFile file, string mdlPath)
    {
        var mtrlResolvedPaths = new List<string>();
        var texPaths = new List<string>();
        var shpkNames = new List<string>();
        var mtrlNames = file.GetMaterialNames();
        foreach (var (offset, originalPath) in mtrlNames)
        {
            var mtrlPath = originalPath.StartsWith('/') ? PathUtil.Resolve(mdlPath, originalPath) : originalPath;
            var mtrlLookupResult = pack.GetFile(mtrlPath);
            if (mtrlLookupResult == null)
            {
                // versioning
                var prefix = mtrlPath[..mtrlPath.LastIndexOf('/')];
                for (var j = 1; j < 9999; j++)
                {
                    // 1 -> v0001
                    var versionedPath = $"{prefix}/v{j:D4}{originalPath}";
                    var versionedLookupResult = pack.GetFile(versionedPath);
                    if (versionedLookupResult != null)
                    {
                        mtrlLookupResult = versionedLookupResult;
                        break;
                    }
                }

                if (mtrlLookupResult == null)
                {
                    continue;
                }
            }

            var mtrlFile = new MtrlFile(mtrlLookupResult.Value.file.RawData);
            var texturePaths = mtrlFile.GetTexturePaths();
            foreach (var (_, texPath) in texturePaths)
            {
                if (texPath == "dummy.tex")
                {
                    continue;
                }

                texPaths.Add(texPath);
            }
            
            mtrlResolvedPaths.Add(mtrlPath);
            shpkNames.Add(mtrlFile.GetShaderPackageName());
        }
        
        return (mtrlResolvedPaths, texPaths, shpkNames);
    }

    private string? ResolveMdlFromTexPath(string texPath)
    {
        if (texPath.StartsWith("chara/demihuman/"))
        {
            var regex = new Regex(@"chara/demihuman/(\w+)/obj/equipment/(\w+)/texture/(\w+)\.tex");
            var match = regex.Match(texPath);
            if (match.Success)
            {
                var demihuman = match.Groups[1].Value;
                var equipment = match.Groups[2].Value;
                var texture = match.Groups[3].Value;
                var textureRegex = new Regex(@"v\d+_(\w+)_(\w+)_(\w+)");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var subPart = textureMatch.Groups[2].Value;
                    var mdlPath = $"chara/demihuman/{demihuman}/obj/equipment/{equipment}/model/{mainPart}_{subPart}.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }

            return null;
        }
        else if (texPath.StartsWith("chara/equipment/"))
        {
            var regex = new Regex(@"chara/equipment/(\w+)/texture/(\w+)\.tex");
            var match = regex.Match(texPath);
            if (match.Success)
            {
                var equipment = match.Groups[1].Value;
                var texture = match.Groups[2].Value;
                var textureRegex = new Regex(@"v\d+_(\w+)_(\w+)_(\w+)");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var subPart = textureMatch.Groups[2].Value;
                    var mdlPath = $"chara/equipment/{equipment}/model/{mainPart}_{subPart}.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }

            return null;
        }
        else if (texPath.StartsWith("chara/human/"))
        {
            var bodyTexRegex = new Regex(@"chara/human/(\w+)/obj/body/(\w+)/texture/(\w+)\.tex");
            var bodyMatch = bodyTexRegex.Match(texPath);
            if (bodyMatch.Success)
            {
                var human = bodyMatch.Groups[1].Value;
                var body = bodyMatch.Groups[2].Value;
                var texture = bodyMatch.Groups[3].Value;
                var textureRegex = new Regex(@"(\w+)_.+");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/human/{human}/obj/body/{body}/model/{mainPart}.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }
            
            var faceTexRegex = new Regex(@"chara/human/(\w+)/obj/face/(\w+)/texture/(\w+)\.tex");
            var faceMatch = faceTexRegex.Match(texPath);
            if (faceMatch.Success)
            {
                var human = faceMatch.Groups[1].Value;
                var face = faceMatch.Groups[2].Value;
                var texture = faceMatch.Groups[3].Value;
                var textureRegex = new Regex(@"v\d+_(\w+)_.+");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/human/{human}/obj/face/{face}/model/{mainPart}_fac.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }
            
            var hairTexRegex = new Regex(@"chara/human/(\w+)/obj/hair/(\w+)/texture/(\w+)\.tex");
            var hairMatch = hairTexRegex.Match(texPath);
            if (hairMatch.Success)
            {
                var human = hairMatch.Groups[1].Value;
                var hair = hairMatch.Groups[2].Value;
                var texture = hairMatch.Groups[3].Value;
                var textureRegex = new Regex(@"(\w+)_.+\.tex");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/human/{human}/obj/hair/{hair}/model/{mainPart}_hir.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }
            
            var tailTexRegex = new Regex(@"chara/human/(\w+)/obj/tail/(\w+)/texture/(\w+)\.tex");
            var tailMatch = tailTexRegex.Match(texPath);
            if (tailMatch.Success)
            {
                var human = tailMatch.Groups[1].Value;
                var tail = tailMatch.Groups[2].Value;
                var texture = tailMatch.Groups[3].Value;
                var textureRegex = new Regex(@"(\w+)_.+");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/human/{human}/obj/tail/{tail}/model/{mainPart}_til.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }
            
            var zearTexRegex = new Regex(@"chara/human/(\w+)/obj/zear/(\w+)/texture/(\w+)\.tex");
            var zearMatch = zearTexRegex.Match(texPath);
            if (zearMatch.Success)
            {
                var human = zearMatch.Groups[1].Value;
                var zear = zearMatch.Groups[2].Value;
                var texture = zearMatch.Groups[3].Value;
                var textureRegex = new Regex(@"(\w+)_.+");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/human/{human}/obj/zear/{zear}/model/{mainPart}_ear.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }

            return null;
        }
        else if (texPath.StartsWith("chara/monster/"))
        {
            var monsterTexRegex = new Regex(@"chara/monster/(\w+)/obj/body/(\w+)/texture/(\w+)\.tex");
            var monsterMatch = monsterTexRegex.Match(texPath);
            if (monsterMatch.Success)
            {
                var monster = monsterMatch.Groups[1].Value;
                var body = monsterMatch.Groups[2].Value;
                var texture = monsterMatch.Groups[3].Value;
                var textureRegex = new Regex(@"v\d+_(\w+)_(\w+)");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/monster/{monster}/obj/body/{body}/model/{mainPart}.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }
            
            return null;
        }
        else if (texPath.StartsWith("chara/weapon/"))
        {
            var weaponTexRegex = new Regex(@"chara/weapon/(\w+)/obj/body/(\w+)/texture/(\w+)\.tex");
            var weaponMatch = weaponTexRegex.Match(texPath);
            if (weaponMatch.Success)
            {
                var weapon = weaponMatch.Groups[1].Value;
                var body = weaponMatch.Groups[2].Value;
                var texture = weaponMatch.Groups[3].Value;
                var textureRegex = new Regex(@"v\d+_(\w+)_(\w+)");
                var textureMatch = textureRegex.Match(texture);
                if (textureMatch.Success)
                {
                    var mainPart = textureMatch.Groups[1].Value;
                    var mdlPath = $"chara/weapon/{weapon}/obj/body/{body}/model/{mainPart}.mdl";
                    var file = pack.GetFile(mdlPath);
                    if (file is not null)
                    {
                        return mdlPath;
                    }
                }
            }
            
            return null;
        }
        
        return null;
    }
}
