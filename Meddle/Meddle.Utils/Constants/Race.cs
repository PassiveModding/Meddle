namespace Meddle.Utils.Constants;

public enum BodyType : byte {
    Unknown = 0,
    Normal  = 1,
    Elder   = 3,
    Child   = 4
}

// https://github.com/xivdev/Penumbra/blob/182546ee101561f8512fad54da445462afab356f/Penumbra.GameData/Enums/Race.cs

public enum Race : byte {
    Unknown,
    Hyur,
    Elezen,
    Lalafell,
    Miqote,
    Roegadyn,
    AuRa,
    Hrothgar,
    Viera,
}

public enum Gender : byte {
    Unknown,
    Male,
    Female,
    MaleNpc,
    FemaleNpc,
}

public enum ModelRace : byte {
    Unknown,
    Midlander,
    Highlander,
    Elezen,
    Lalafell,
    Miqote,
    Roegadyn,
    AuRa,
    Hrothgar,
    Viera,
}

public enum Clan : byte {
    Unknown,
    Midlander,
    Highlander,
    Wildwood,
    Duskwight,
    Plainsfolk,
    Dunesfolk,
    SeekerOfTheSun,
    KeeperOfTheMoon,
    Seawolf,
    Hellsguard,
    Raen,
    Xaela,
    Helion,
    Lost,
    Rava,
    Veena,
}

// The combined gender-race-npc numerical code as used by the game.
public enum GenderRace : ushort {
    Unknown             = 0,
    MidlanderMale       = 0101,
    MidlanderMaleNpc    = 0104,
    MidlanderFemale     = 0201,
    MidlanderFemaleNpc  = 0204,
    HighlanderMale      = 0301,
    HighlanderMaleNpc   = 0304,
    HighlanderFemale    = 0401,
    HighlanderFemaleNpc = 0404,
    ElezenMale          = 0501,
    ElezenMaleNpc       = 0504,
    ElezenFemale        = 0601,
    ElezenFemaleNpc     = 0604,
    MiqoteMale          = 0701,
    MiqoteMaleNpc       = 0704,
    MiqoteFemale        = 0801,
    MiqoteFemaleNpc     = 0804,
    RoegadynMale        = 0901,
    RoegadynMaleNpc     = 0904,
    RoegadynFemale      = 1001,
    RoegadynFemaleNpc   = 1004,
    LalafellMale        = 1101,
    LalafellMaleNpc     = 1104,
    LalafellFemale      = 1201,
    LalafellFemaleNpc   = 1204,
    AuRaMale            = 1301,
    AuRaMaleNpc         = 1304,
    AuRaFemale          = 1401,
    AuRaFemaleNpc       = 1404,
    HrothgarMale        = 1501,
    HrothgarMaleNpc     = 1504,
    HrothgarFemale      = 1601,
    HrothgarFemaleNpc   = 1604,
    VieraMale           = 1701,
    VieraMaleNpc        = 1704,
    VieraFemale         = 1801,
    VieraFemaleNpc      = 1804,
    UnknownMaleNpc      = 9104,
    UnknownFemaleNpc    = 9204,
}
