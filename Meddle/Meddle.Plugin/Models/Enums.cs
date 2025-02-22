using System.ComponentModel;

namespace Meddle.Plugin.Models;

public enum MenuType
{
    Default = 0,
    Debug = 1,
    Testing = 2,
}

[Flags]
public enum ExportType
{
    // ReSharper disable InconsistentNaming
    GLTF = 1,
    GLB = 2,
    OBJ = 4
    // ReSharper restore InconsistentNaming
}

public enum TextureMode
{
    Bake,
    Raw
}

[Flags]
public enum CacheFileType
{
    [Description(".tex")]
    Tex = 1,
    [Description(".mtrl")]
    Mtrl = 2,
    [Description(".mdl")]
    Mdl = 4,
    [Description(".shpk")]
    Shpk = 8,
    [Description(".pbd")]
    Pbd = 16
}
