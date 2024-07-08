using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meddle.Utils.Files;

public struct LgbFile
{
    public enum LgbMagic : uint
    {
        LGB1 = 0x3142474C,
        LGP1 = 0x3150474C
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct HeaderData {
        public uint Magic1; // LGB1
        public uint FileSize;
        public uint Unknown1;
        public uint Magic2; // LGP1
        public uint Unknown2;
        public uint Unknown3;
        public uint Unknown4;
        public uint Unknown5;
        public int GroupCount;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct BgInstanceObject
    {
        public uint ModelAssetPathOffset;
        public uint CollisionAssetPathOffset;
        public uint ModelCollisionType;
        public uint AttributeMask;            
        public uint Attribute;
        public int CollisionConfig;
        public byte IsVisible;
        public byte RenderShadowEnabled;
        public byte RenderLightShadowEnabled;
        public byte _pad;
        public float RenderModelClipRange;
    }

    public struct Group
    {
        public int Offset;
        public LayerHeader Header;
        public LayerSetReferencedList ReferencedList;
        public uint[] InstanceObjectOffsets;
        public InstanceObject[] InstanceObjects;
        public LayerSetReference[] LayerSetReferences;
        public OBSetReference[] OBSetReferences;
        public OBSetReferenceList[] OBSetEnableReferences;

        public Group(SpanBinaryReader reader)
        {
            var start = reader.Position;
            Offset = start;
            Header = reader.Read<LayerHeader>();

            reader.Seek(start + (int)Header.LayerSetReferencedListOffset, SeekOrigin.Begin);
            ReferencedList = reader.Read<LayerSetReferencedList>();
            
            reader.Seek(start + ReferencedList.LayerSetsOffset, SeekOrigin.Begin);
            LayerSetReferences = reader.Read<LayerSetReference>(ReferencedList.LayerSetCount).ToArray();
            
            reader.Seek(start + (int)Header.OBSetReferencesOffset, SeekOrigin.Begin);
            OBSetReferences = reader.Read<OBSetReference>((int)Header.ObSetReferencedListCount).ToArray();
            
            reader.Seek(start + (int)Header.ObSetEnableReferencedListOffset, SeekOrigin.Begin);
            OBSetEnableReferences = reader.Read<OBSetReferenceList>((int)Header.ObSetEnableReferencedListCount).ToArray();
                        
            reader.Seek(start + (int)Header.InstanceObjectsOffset, SeekOrigin.Begin);
            InstanceObjectOffsets = reader.Read<uint>((int)Header.InstanceObjectCount).ToArray();
            
            InstanceObjects = new InstanceObject[Header.InstanceObjectCount];
            for (int i = 0; i < Header.InstanceObjectCount; i++)
            {
                var instanceObjectPos = start + (int)Header.InstanceObjectsOffset + (int)InstanceObjectOffsets[i];
                reader.Seek(instanceObjectPos, SeekOrigin.Begin);
                InstanceObjects[i] = reader.Read<InstanceObject>();
            }
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct LayerHeader
        {
            public uint LayerId;
            public uint NameOffset;
            public uint InstanceObjectsOffset;
            public uint InstanceObjectCount;
            public byte ToolModeVisible;
            public byte ToolModeReadOnly;
            public byte IsBushLayer;
            public byte PS3Visible;
            public uint LayerSetReferencedListOffset;
            public ushort FestivalID;
            public ushort FestivalPhaseID;
            public byte IsTemporary;
            public byte IsHousing;
            public ushort VersionMask;
            public uint _padding;
            public uint OBSetReferencesOffset;
            public uint ObSetReferencedListCount;
            public uint ObSetEnableReferencedListOffset;
            public uint ObSetEnableReferencedListCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LayerSetReferencedList
        {
            public LayerSetReferencedType Type;
            public int LayerSetsOffset;
            public int LayerSetCount;
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct InstanceObject
        {
            public LayerEntryType Type;
            public uint InstanceId;
            public uint StringOffset;
            public Vector3 Translation;
            public Vector3 Rotation;
            public Vector3 Scale;
        }        
        
        [StructLayout(LayoutKind.Sequential)]
        public struct LayerSetReference
        {
            public uint LayerSetId;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct OBSetReference
        {
            public LayerEntryType AssetType;
            public uint InstanceId;
            public uint StringOffset;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct OBSetReferenceList
        {
            public LayerEntryType AssetType;
            public uint InstanceId;
            public byte OBSetEnable;
            public byte OBSetEmissiveEnable;
            private unsafe fixed byte _padding[2];
        }
    }

    public HeaderData Header;
    public int[] GroupOffsets;
    public Group[] Groups;
    
    private byte[] data;
    public ReadOnlySpan<byte> RawData => data;
    
    public LgbFile(byte[] data) : this(new ReadOnlySpan<byte>(data))
    {
    }
    
    public LgbFile(ReadOnlySpan<byte> data)
    {
        this.data = data.ToArray();
        var reader = new SpanBinaryReader( data );
        Header = reader.Read<HeaderData>();
        
        if (Header.Magic1 != (uint)LgbMagic.LGB1 || Header.Magic2 != (uint)LgbMagic.LGP1)
        {
            throw new Exception("Invalid LGB file.");
        }
        
        var pos = reader.Position;
        GroupOffsets = reader.Read<int>(Header.GroupCount).ToArray();
        Groups = new Group[Header.GroupCount];
        for (var i = 0; i < Header.GroupCount; i++)
        {
            reader.Seek(pos + GroupOffsets[i], SeekOrigin.Begin);
            Groups[i] = new Group(reader);
        }
    }
    
    public enum LayerSetReferencedType : uint
    {
        All = 0x0,
        Include = 0x1,
        Exclude = 0x2,
        Undetermined = 0x3,
    }
    
    public enum LayerEntryType : uint
    {
        AssetNone = 0x0,
        BG = 0x1, //  //
        Attribute = 0x2,
        LayLight = 0x3, //  //
        VFX = 0x4, //  //
        PositionMarker = 0x5, //  //
        SharedGroup = 0x6, //  //
        Sound = 0x7, //  //
        EventNPC = 0x8, //  //
        BattleNPC = 0x9, //  //
        RoutePath = 0xA,
        Character = 0xB,
        Aetheryte = 0xC, //  //
        EnvSet = 0xD, //  //
        Gathering = 0xE, //  //
        HelperObject = 0xF, //
        Treasure = 0x10, //  //
        Clip = 0x11,
        ClipCtrlPoint = 0x12,
        ClipCamera = 0x13,
        ClipLight = 0x14,
        ClipReserve00 = 0x15,
        ClipReserve01 = 0x16,
        ClipReserve02 = 0x17,
        ClipReserve03 = 0x18,
        ClipReserve04 = 0x19,
        ClipReserve05 = 0x1A,
        ClipReserve06 = 0x1B,
        ClipReserve07 = 0x1C,
        ClipReserve08 = 0x1D,
        ClipReserve09 = 0x1E,
        ClipReserve10 = 0x1F,
        ClipReserve11 = 0x20,
        ClipReserve12 = 0x21,
        ClipReserve13 = 0x22,
        ClipReserve14 = 0x23,
        CutAssetOnlySelectable = 0x24,
        Player = 0x25,
        Monster = 0x26,
        Weapon = 0x27, //
        PopRange = 0x28, //  //
        ExitRange = 0x29, //  //
        LVB = 0x2A,
        MapRange = 0x2B, //  //
        NaviMeshRange = 0x2C, //  //
        EventObject = 0x2D, //  //
        DemiHuman = 0x2E,
        EnvLocation = 0x2F, //  //
        ControlPoint = 0x30,
        EventRange = 0x31, //?     //
        RestBonusRange = 0x32,
        QuestMarker = 0x33, //      //
        Timeline = 0x34,
        ObjectBehaviorSet = 0x35,
        Movie = 0x36,
        ScenarioExd = 0x37,
        ScenarioText = 0x38,
        CollisionBox = 0x39, //  //
        DoorRange = 0x3A, //
        LineVFX = 0x3B, //  //
        SoundEnvSet = 0x3C,
        CutActionTimeline = 0x3D,
        CharaScene = 0x3E,
        CutAction = 0x3F,
        EquipPreset = 0x40,
        ClientPath = 0x41, //      //
        ServerPath = 0x42, //      //
        GimmickRange = 0x43, //      //
        TargetMarker = 0x44, //      //
        ChairMarker = 0x45, //      //
        ClickableRange = 0x46, //
        PrefetchRange = 0x47, //      //
        FateRange = 0x48, //      //
        PartyMember = 0x49,
        KeepRange = 0x4A, //
        SphereCastRange = 0x4B,
        IndoorObject = 0x4C,
        OutdoorObject = 0x4D,
        EditGroup = 0x4E,
        StableChocobo = 0x4F,
        MaxAssetType = 0x50,
    }
}

public static class LgbFileExtensions
{
    public static (string ModelPath, string CollisionPath, LgbFile.BgInstanceObject bgObject) GetBgInstanceObject(LgbFile file, int layerIdx, int objectIdx)
    {
        var layer = file.Groups[layerIdx];
        var instanceObject = layer.InstanceObjects[objectIdx];
        if (instanceObject.Type != LgbFile.LayerEntryType.BG) throw new Exception("Not a BG object");
        var instanceObjectOffset = layer.InstanceObjectOffsets[objectIdx];
        var reader = new SpanBinaryReader(file.RawData);
        var instanceRoot = layer.Offset + (int)layer.Header.InstanceObjectsOffset + (int)instanceObjectOffset;
        reader.Seek(instanceRoot+ Unsafe.SizeOf<LgbFile.Group.InstanceObject>(), SeekOrigin.Begin);
        var bgInstanceObject = reader.Read<LgbFile.BgInstanceObject>();
        var modelPath = reader.ReadString(instanceRoot + (int)bgInstanceObject.ModelAssetPathOffset);
        var collisionPath = reader.ReadString(instanceRoot + (int)bgInstanceObject.CollisionAssetPathOffset);
        
        return (modelPath, collisionPath, bgInstanceObject);
    }
}
