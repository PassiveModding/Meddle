using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Lumina.Excel.GeneratedSheets;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Models.Structs;
using Meddle.Utils.Export;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Plugin.Models.Layout;

[Flags]
public enum ParsedInstanceType
{
    Unsupported = 1,
    SharedGroup = 2,
    Housing = 4,
    BgPart = 8,
    Light = 16,
    Character = 32,
    AllSupported = SharedGroup | Housing | BgPart | Character
}

public abstract class ParsedInstance
{
    public nint Id;
    public abstract ParsedInstanceType Type { get; }
    public Transform Transform;
    public string? Path;
    public List<ParsedInstance> Children = new();
    
    public ParsedInstance[] Flatten()
    {
        var list = new List<ParsedInstance> { this };
        foreach (var child in Children)
        {
            list.AddRange(child.Flatten());
        }

        return list.ToArray();
    }
}

public class ParsedUnsupportedInstance : ParsedInstance
{
    public ParsedUnsupportedInstance(ParsedInstanceType type, InstanceType instanceType)
    {
        Type = type;
        InstanceType = instanceType;
    }
    
    public override ParsedInstanceType Type { get; }
    public InstanceType InstanceType { get; }
}

public class ParsedSharedInstance : ParsedInstance
{
    public override ParsedInstanceType Type => ParsedInstanceType.SharedGroup;
}

public class ParsedHousingInstance : ParsedSharedInstance
{
    public override ParsedInstanceType Type => ParsedInstanceType.Housing;
    
    public Stain? Stain;
    public Item? Item;
    public string Name;
    public ObjectKind Kind;
}
        
public class ParsedBgPartsInstance : ParsedInstance
{
    public override ParsedInstanceType Type => ParsedInstanceType.BgPart;
}

public class ParsedLightInstance : ParsedInstance
{
    public override ParsedInstanceType Type => ParsedInstanceType.Light;
}

public class ParsedLayer
{
    public nint Id;
    public List<ParsedInstance> Instances = [];
}

public class ParsedTextureInfo(string path, string pathFromMaterial) 
{
    public string Path { get; } = path;
    public string PathFromMaterial { get; } = pathFromMaterial;
}

public class ParsedMaterialInfo(string path, string pathFromModel, string shpk, ColorTable? colorTable, IList<ParsedTextureInfo> textures) 
{
    public string Path { get; } = path;
    public string PathFromModel { get; } = pathFromModel;
    public string Shpk { get; } = shpk;
    public ColorTable? ColorTable { get; } = colorTable;
    public IList<ParsedTextureInfo> Textures { get; } = textures;
}

public class ParsedModelInfo(string path, string pathFromCharacter, DeformerCachedStruct? deformer, Model.ShapeAttributeGroup shapeAttributeGroup, IList<ParsedMaterialInfo> materials) 
{
    public string Path { get; } = path;
    public string PathFromCharacter { get; } = pathFromCharacter;
    public DeformerCachedStruct? Deformer { get; } = deformer;
    public Model.ShapeAttributeGroup ShapeAttributeGroup { get; } = shapeAttributeGroup;
    public IList<ParsedMaterialInfo> Materials { get; } = materials;
}

public class ParsedCharacterInfo
{
    public IList<ParsedModelInfo> Models;
    public ParsedSkeleton Skeleton;
    public CustomizeData CustomizeData;
    public Structs.CustomizeParameter CustomizeParameter;
    public GenderRace GenderRace;
}

public class ParsedCharacterInstance : ParsedInstance
{
    public ParsedCharacterInfo? CharacterInfo;
    public string Name;
    public ObjectKind Kind;
    public override ParsedInstanceType Type => ParsedInstanceType.Character;
}
