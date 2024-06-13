using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Havok;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;

namespace Meddle.UI.Windows.Views;

public class DiscoverView(SqPack pack, Configuration configuration, PathManager pathManager) : IView
{
    private CancellationTokenSource? cts;
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
    
    public void Draw()
    {
        if (ImGui.Button("RunDiscovery"))
        {
            cts = new CancellationTokenSource();
            discoverTask = Task.Run(async () => await DiscoverAsync(cts.Token));
        }
        
        
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            cts?.Cancel();
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
            foreach (var line in log)
            {
                ImGui.Text(line);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
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
}
