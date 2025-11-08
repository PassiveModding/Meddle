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
    Head = 0,       // 0x0
    Top = 1,        // 0x1
    Arms = 2,       // 0x2
    Legs = 3,       // 0x3
    Feet = 4,       // 0x4
    Ear = 5,        // 0x5
    Neck = 6,       // 0x6
    Wrist = 7,      // 0x7
    RFinger = 8,    // 0x8
    LFinger = 9,    // 0x9
    Hair = 10,      // 0xA all slots < 0xA (not including) *can* have Human->LegacyBodyDecal if shpk = skin.shpk
    Face = 11,      // 0xB enables Human->Decal
    TailEars = 12,  // 0xC
    Glasses = 16,   // 0x10
    Extra = 17,     // 0x11
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
