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
using WebSocketSharp;

namespace Meddle.UI.Windows.Views;

public class DiscoverView(SqPack pack, Configuration configuration, PathManager pathManager) : IView
{
    private CancellationTokenSource? discoverCts;
    private Task? discoverTask;
    private readonly List<string> log = new();
    
    public void Draw()
    {
        if (discoverTask is not null && discoverTask.IsCompleted)
        {
            if (discoverTask.IsFaulted)
            {
                ImGui.Text("Discover task failed");
                ImGui.Text(discoverTask.Exception?.ToString());
            }
            else if (discoverTask.IsCanceled)
            {
                ImGui.Text("Discover task canceled");
            }
            else
            {
                ImGui.Text("Discover task completed");
            }
        }
        
        ImGui.BeginDisabled(discoverTask is not null && !discoverTask.IsCompleted);
        if (ImGui.Button("Discover Paths"))
        {
            discoverCts?.Cancel();
            discoverCts = new CancellationTokenSource();
            discoverTask = Task.Run(async () => await IterateAllHashes(discoverCts.Token));
        }
        if (ImGui.Button("Bruteforce Paths from Patterns"))
        {
            discoverCts?.Cancel();
            discoverCts = new CancellationTokenSource();
            discoverTask = Task.Run(() => BruteForcePaths(discoverCts.Token));
        }
        ImGui.EndDisabled();
        
        if (ImGui.Button("Cancel"))
        {
            discoverCts?.Cancel();
        }
        
        if (total > 0)
        {
            ImGui.Text($"Processed {processed} / {total}");
        }
    }

    public record ShpkResult(Dictionary<string, uint> StringCrcs, HashSet<uint> MatParamCrcs);
    public ShpkResult HandleShpk(ShpkFile shpk)
    {
        var stringCrcs = new Dictionary<string, uint>();
        var matParamCrcs = new HashSet<uint>();
        var stringReader = new SpanBinaryReader(shpk.RawData[(int)shpk.FileHeader.StringsOffset..]);
        
        foreach (var constant in shpk.Constants)
        {
            var name = ReadString(constant, stringReader);
            stringCrcs[name] = constant.Id;
        }

        foreach (var sampler in shpk.Samplers)
        {
            var name = ReadString(sampler, stringReader);
            stringCrcs[name] = sampler.Id;
        }

        foreach (var texture in shpk.Textures)
        {
            var name = ReadString(texture, stringReader);
            stringCrcs[name] = texture.Id;
        }

        foreach (var uav in shpk.Uavs)
        {
            var name = ReadString(uav, stringReader);
            stringCrcs[name] = uav.Id;
        }

        foreach (var materialParam in shpk.MaterialParams)
        {
            matParamCrcs.Add(materialParam.Id);
        }

        return new ShpkResult(stringCrcs, matParamCrcs);

        string ReadString(ShpkFile.Resource resource, SpanBinaryReader reader)
        {
            return reader.ReadString((int)resource.StringOffset);
        }
    }
    
    public record MtrlResult(HashSet<uint> Crcs, HashSet<string> Textures, string Shader);

    public MtrlResult HandleMtrlFile(MtrlFile mtrl)
    {
        var crcs = new HashSet<uint>();
        var textures = new HashSet<string>();
        foreach (var constant in mtrl.Constants)
        {
            crcs.Add(constant.ConstantId);
        }

        var texPaths = mtrl.GetTexturePaths().Select(x => x.Value); 
        foreach (var texPath in texPaths)
        {
            textures.Add(texPath);
        }

        var shader = mtrl.GetShaderPackageName();

        return new MtrlResult(crcs, textures, shader);
    }
    
    public record MdlResult(HashSet<string> RelativePaths, HashSet<string> Paths);
    
    public MdlResult HandleMdlFile(MdlFile mdl)
    {
        var relativePaths = new HashSet<string>();
        var paths = new HashSet<string>();
        foreach (var (_, path) in mdl.GetMaterialNames())
        {
            // if path starts with / then need to handle as relative
            if (path.StartsWith("/"))
            {
                relativePaths.Add(path);
            }
            else
            {
                paths.Add(path);
            }
        }
        
        return new MdlResult(relativePaths, paths);
    }
    
    public record ConsolidatedResult(MtrlResult? MtrlResult, ShpkResult? ShpkResult, MdlResult? MdlResult);
    
    public ConsolidatedResult? HandleHash(ulong hash, Category category, CancellationToken token)
    {
        if (category.TryGetFile(hash, out var packFile))
        {
            if (packFile.RawData.Length < 4)
            {
                return null;
            }
            
            if (packFile.FileHeader.Type == FileType.Standard)
            {

                // mtrlmagic
                var fileReader = new SpanBinaryReader(packFile.RawData);
                var magic = fileReader.ReadUInt32();
                if (magic == MtrlFile.MtrlMagic)
                {
                    var mtrl = new MtrlFile(packFile.RawData);
                    var mtrlResult = HandleMtrlFile(mtrl);
                    return new ConsolidatedResult(mtrlResult, null, null);
                }
                else if (magic == ShpkFile.ShPkMagic)
                {
                    var shpk = new ShpkFile(packFile.RawData);
                    var shpkResult = HandleShpk(shpk);
                    return new ConsolidatedResult(null, shpkResult, null);
                }
                else if (magic == SklbFile.SklbMagic)
                {
                    // var sklb = new SklbFile(packFile.RawData);
                }
            }
            else if (packFile.FileHeader.Type == FileType.Model)
            {
                var mdl = new MdlFile(packFile.RawData);
                var mdlResult = HandleMdlFile(mdl);
                return new ConsolidatedResult(null, null, mdlResult);
            }
        }
        
        return null;
    }

    private int total;
    private int processed;

    public void BruteForcePaths(CancellationToken token)
    {
        var patterns = File.ReadAllLines("data/path_patterns.txt");
        //var mtrlRelativePaths = File.ReadAllLines("data/mtrl_relative_paths.txt");
        var parsedPatterns = new List<(string SimpleCmp, Token[] Tokens, string Pattern)>();
        int maxReplacements = 0;
        var now = DateTime.Now;
        foreach (var pattern in patterns)
        {
            var templatePattern = pattern;
            int idx = 0;
            var tokens = new List<Token>();
            var simpleCmp = "";
            while (Regex.IsMatch(templatePattern, "\\w%(\\d+)d"))
            {
                var matches = Regex.Matches(templatePattern, "(\\w)%(\\d+)d");

                // update pattern string to
                // in:  chara/equipment/e%04d/e%04d.imc
                // out: chara/equipment/e{0:4}/e{1:4}.imc
                // replace first occurence only
                var match = matches[0];
                var prefixChar = match.Groups[1].Value;
                var digits = int.Parse(match.Groups[2].Value);
                var replacement = $"{{{prefixChar}:{digits}}}";
                simpleCmp += replacement;
                templatePattern = templatePattern.Replace(match.Value, replacement);
                idx++;
                
                var max = prefixChar switch {
                    "e" => 9999,
                    "c" => -1,
                    "h" => 500,
                    "z" => 10,
                    "w" => 9999,
                    "b" => 999,
                    "d" => 9999,
                    "f" => 20,
                    "v" => 9999,
                    _ => 9999
                };
                
                tokens.Add(new Token(replacement, prefixChar, digits, max));
            }

            if (!templatePattern.Contains("{")) continue;
            parsedPatterns.Add((simpleCmp, tokens.ToArray(), templatePattern));
            maxReplacements = Math.Max(maxReplacements, idx);
            
            if (token.IsCancellationRequested)
            {
                break;
            }
        }
        
        // ctokens can be any value from GenderRace enum
        var genderRaceValues = Enum.GetValues(typeof(GenderRace))
                                   .Cast<GenderRace>()
                                   .Where(x => x != GenderRace.Unknown)
                                   .Select(x => $"c{(int)x:D4}".ToLower()).ToArray();
        // chara/equipment/e{0:4}/e{1:4}.imc
        // -> chara/equipment/e0000/e0000.imc
        // -> chara/equipment/e0000/e0001.imc
        // ...
        // -> chara/equipment/e9999/e9999.imc
        
        var found = new ConcurrentBag<string>();
        var autoExportTaskToken = new CancellationTokenSource();
        var autoExportTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !autoExportTaskToken.Token.IsCancellationRequested)
            {
                await Task.Delay(5000, token);
                var orderedVals = found.OrderBy(x => x).ToArray();
                await File.WriteAllLinesAsync($"found_patterns-{now:yyyyMMdd-HHmmss}.txt", orderedVals, token);
            }
        }, token);
        
        parsedPatterns = ExpandPatterns(genderRaceValues, parsedPatterns)
            // skip versioned paths for now
            //.Where(x => !x.SimpleCmp.Contains("{v:4}"))
            .ToList();
        
        try
        {
            foreach (var pattern in parsedPatterns.OrderBy(x => x.Tokens.Length))
            {
                // skip wildcard patterns for now
                if (pattern.Pattern.Contains("%")) continue;
                RecurseTokens(pattern.Tokens, 0, pattern.Pattern, found, token);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while processing: {ex}");
        }
        
        autoExportTaskToken.Cancel();
        autoExportTask.Wait(token);

        File.WriteAllLines($"found_patterns-{now:yyyyMMdd-HHmmss}.txt", found.OrderBy(x => x));
    }
    
    private IEnumerable<(string SimpleCmp, Token[] Tokens, string Pattern)> ExpandPatterns(string[] cTokens, IEnumerable<(string SimpleCmp, Token[] Tokens, string Pattern)> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.SimpleCmp.Contains("{c:4}"))
            {
                foreach (var cToken in cTokens)
                {
                    var newPattern = pattern.Pattern.Replace("{c:4}", cToken);
                    var newTokens = pattern.Tokens.Where(x => x.Data != "{c:4}").ToArray();
                    var newSimpleCmp = pattern.SimpleCmp.Replace("{c:4}", cToken);
                    yield return (newSimpleCmp, newTokens, newPattern);
                }
            }
            else
            {
                yield return pattern;
            }
        }
    }
    
    private void RecurseTokens(Token[] tokens, int idx, string pattern, ConcurrentBag<string> found, CancellationToken token)
    {
        if (idx >= tokens.Length)
        {
            TestPattern(pattern, found);
            return;
        }

        if (idx == 0 || idx == 1)
        {
            // parallel
            Parallel.For(0, tokens[idx].Max, HandleIndex);
        }
        else
        {
            for (int i = 0; i < tokens[idx].Max; i++)
            {
                HandleIndex(i);
            }
        }
        
        return;
        
        void HandleIndex(int i)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }
            
            var replacement = i.ToString($"D{tokens[idx].Digits}");
            var newPattern = pattern.Replace(tokens[idx].Data, $"{tokens[idx].PrefixChar}{replacement}");
            RecurseTokens(tokens, idx + 1, newPattern, found, token);
        }
    }

    private void TestPattern(string patternText, ConcurrentBag<string> found)
    {
        if (pack.FileExists(patternText, out var file))
        {
            try
            {
                if (patternText.EndsWith(".mdl"))
                {
                    var data = pack.GetFile(patternText, FileType.Model);
                    if (data != null)
                    {
                        Console.WriteLine($"Found {patternText}");
                        found.Add(patternText);
                    }
                }
                else if (patternText.EndsWith(".mtrl"))
                {
                    var data = pack.GetFile(patternText, FileType.Standard);
                    if (data is {file.RawData.Length: > 4})
                    {
                        var reader = new SpanBinaryReader(data.Value.file.RawData);
                        var magic = reader.ReadUInt32();
                        if (magic == MtrlFile.MtrlMagic)
                        {
                            Console.WriteLine($"Found {patternText}");
                            found.Add(patternText);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Untested path found {patternText}");
                    found.Add(patternText);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while processing {patternText}: {ex}");
            }
        }
    }
    
    private static ReadOnlySpan<int> Calculate(int size, int length, long index)
    {
        var combination = new int[length];
        var remainder = index;

        for (int i = 0; i < length; i++)
        {
            combination[i] = (int)(remainder % size);
            remainder /= size;
        }

        return combination;
    }
    
    private record Token(string Data, string PrefixChar, int Digits, int Max);
    
    public void IterateHashes(CancellationToken token)
    {
        var results = new ConcurrentBag<ConsolidatedResult>();
        total = pack.Repositories.Sum(x => x.Categories.Sum(y => y.Value.UnifiedIndexEntries.Count));
        processed = 0;
        foreach (var repo in pack.Repositories)
        {
            Console.WriteLine($"Processing {repo.Version} / {repo.Path}");
            foreach (var category in repo.Categories)
            {
                Console.WriteLine($"Processing {category.Key}");
                var localCategory = category;
                Parallel.ForEach(category.Value.UnifiedIndexEntries, (hash, state, idx) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        state.Stop();
                        return;
                    }

                    try
                    {
                        var result = HandleHash(hash.Key, localCategory.Value, token);
                        if (result is not null)
                        {
                            results.Add(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while processing {localCategory.Key}: {ex}");
                    }
                    
                    Interlocked.Increment(ref processed);
                });
            }
        }
        
        
        var mtrlResults = results.Where(x => x.MtrlResult is not null).Select(x => x.MtrlResult!).ToArray();
        var shpkResults = results.Where(x => x.ShpkResult is not null).Select(x => x.ShpkResult!).ToArray();
        var mdlResults = results.Where(x => x.MdlResult is not null).Select(x => x.MdlResult!).ToArray();
        
        var mtrlCrcs = mtrlResults.SelectMany(x => x.Crcs).Distinct();
        var mtrlTextures = mtrlResults.SelectMany(x => x.Textures).Distinct();
        var mtrlShaders = mtrlResults.Select(x => x.Shader).Distinct();
        
        var shpkStrings = shpkResults.SelectMany(x => x.StringCrcs.Keys).Distinct();
        var shpkMatParams = shpkResults.SelectMany(x => x.MatParamCrcs).Distinct();
        
        var mdlRelativePaths = mdlResults.SelectMany(x => x.RelativePaths).Distinct();
        var mdlPaths = mdlResults.SelectMany(x => x.Paths).Distinct();
        
        File.WriteAllLines("mtrl_crcs.txt", mtrlCrcs.Select(x => x.ToString()));
        File.WriteAllLines("mtrl_textures.txt", mtrlTextures);
        File.WriteAllLines("mtrl_shaders.txt", mtrlShaders);
        
        File.WriteAllLines("shpk_strings.txt", shpkStrings);
        File.WriteAllLines("shpk_matparams.txt", shpkMatParams.Select(x => x.ToString()));
        
        File.WriteAllLines("mdl_relative_paths.txt", mdlRelativePaths);
        File.WriteAllLines("mdl_paths.txt", mdlPaths);
        
        // write serialized results file
        var serializedResults = JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText("results.json", serializedResults);
        
        Console.WriteLine("Done");
    }
    
    public Task IterateAllHashes(CancellationToken token)
    {
        // iterate mtrl files and get crc from constant ids
        return Task.Run(() => IterateHashes(token), token);
    }
}
