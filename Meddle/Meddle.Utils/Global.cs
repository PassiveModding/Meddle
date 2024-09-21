using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meddle.Utils;

public static class Global
{
    public static ILogger Logger { get; set; } = NullLogger.Instance;
}
