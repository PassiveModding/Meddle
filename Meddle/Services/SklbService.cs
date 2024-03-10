using Dalamud.Plugin.Services;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utility;
using Penumbra.GameData.Files;

namespace Meddle.Plugin.Services;

public class SklbService : IService
{
    private readonly IFramework _framework;
    private readonly FileService _fileService;

    public SklbService(IFramework framework, FileService fileService)
    {
        _framework = framework;
        _fileService = fileService;
    }
    
    public async Task<XivSkeleton[]> BuildSkeletonsAsync(IEnumerable<string> sklbPaths, CancellationToken cancel)
    {
        // We're intentionally filtering failed reads here - the failure will
        // be picked up, if relevant, when the model tries to create mappings
        // for a bone in the failed sklb.
        var sklbFiles = sklbPaths
            .Select(path => _fileService.ReadFile(path))
            .Where(bytes => bytes != null)
            .Select(bytes => new SklbFile(bytes!))
            .ToArray();

        var havokTasks = new Task<string>[sklbFiles.Length];
        for (int i = 0; i < sklbFiles.Length; i++)
        {
            var sklb = sklbFiles[i];
            havokTasks[i] = CreateHavokTask((sklb, i));;
        }
        
        var havokResults = await Task.WhenAll(havokTasks);
        return havokResults.Select(SkeletonConverter.FromXml).ToArray();

        // The havok methods we're relying on for this conversion are a bit
        // finicky at the best of times, and can outright cause a CTD if they
        // get upset. Running each conversion on its own tick seems to make
        // this consistently non-crashy across my testing.
        Task<string> CreateHavokTask((SklbFile Sklb, int Index) pair)
            => _framework.RunOnTick(
                () => HavokConverter.HkxToXml(pair.Sklb.Skeleton),
                delayTicks: pair.Index, cancellationToken: cancel);
    }
}