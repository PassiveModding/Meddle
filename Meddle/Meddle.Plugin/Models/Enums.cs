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

public enum HumanModelSlotIndex
{
    Head = 0,
    Top = 1,
    Arms = 2,
    Legs = 3,
    Feet = 4,
    Ear = 5,
    Neck = 6,
    Wrist = 7,
    RFinger = 8,
    LFinger = 9,
    Hair = 10,
    Face = 11,
    TailEars = 12,
    Glasses = 16,
    Extra = 17,
}

public enum HumanEquipmentSlotIndex
{
    Head = 0,
    Body = 1,
    Hands = 2,
    Legs = 3,
    Feet = 4,
    Ears = 5,
    Neck = 6,
    Wrists = 7,
    RFinger = 8,
    LFinger = 9,
    Glasses = 10,
    Extra = 11,
}

public enum HumanSkinSlotIndex
{
    Head = 0,
    Body = 1,
    Hands = 2,
    Legs = 3,
    Feet = 4,
}
