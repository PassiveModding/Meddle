using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class ComposerFactory : IService
{
    private readonly ILoggerFactory loggerProvider;
    private readonly SqPack pack;
    private readonly Configuration configuration;

    public ComposerFactory(ILoggerFactory loggerFactory, SqPack pack, Configuration configuration)
    {
        this.loggerProvider = loggerFactory;
        this.pack = pack;
        this.configuration = configuration;
    }
    
    private DataProvider CreateDataProvider(string cacheDir, CancellationToken cancellationToken)
    {
        return new DataProvider(cacheDir, pack, loggerProvider.CreateLogger<DataProvider>(), cancellationToken);
    }
    
    public InstanceComposer CreateComposer(ParsedInstance[] instances, string? cacheDir = null,
                                           Action<ProgressEvent>? progressEvent = null, 
                                           CancellationToken cancellationToken = default)
    {
        cacheDir ??= Path.Combine(Path.GetTempPath(), "Meddle", "Cache");
        Directory.CreateDirectory(cacheDir);
        
        var dataProvider = CreateDataProvider(cacheDir, cancellationToken);
        return new InstanceComposer(configuration,
                                    instances,
                                    progressEvent,
                                    cancellationToken,
                                    CreateCharacterComposer(dataProvider),
                                    dataProvider);
    }
    
    public CharacterComposer CreateCharacterComposer(string? cacheDir = null, Action<ProgressEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        cacheDir ??= Path.Combine(Path.GetTempPath(), "Meddle", "Cache");
        Directory.CreateDirectory(cacheDir);
        
        var dataProvider = CreateDataProvider(cacheDir, cancellationToken);
        return new CharacterComposer(dataProvider, progress);
    }
    
    public CharacterComposer CreateCharacterComposer(DataProvider dataProvider)
    {
        return new CharacterComposer(dataProvider);
    }
}
