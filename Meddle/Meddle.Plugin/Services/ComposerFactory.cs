using System.Numerics;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.UI.Layout;
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
    
    public InstanceComposer CreateComposer(string outDir,
                                           Configuration.ExportConfiguration exportConfig,
                                           CancellationToken cancellationToken = default)
    {
        return new InstanceComposer(configuration, pack, exportConfig,
                                  outDir, cancellationToken);

    }
    
    public CharacterComposer CreateCharacterComposer(string outDir,
                                                     Configuration.ExportConfiguration exportConfig,
                                                     CancellationToken cancellationToken = default)
    {
        return new CharacterComposer(configuration, pack, exportConfig, outDir, cancellationToken);
    }
}
