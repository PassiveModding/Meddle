using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Lumina.Excel.GeneratedSheets;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

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
    Terrain = 64
}

public interface IResolvableInstance
{
    public bool IsResolved { get; }
    public void Resolve(ResolverService resolver);
}

public interface IPathInstance
{
    public HandleString Path { get; }
}

public abstract class ParsedInstance
{
    public ParsedInstance(nint id, ParsedInstanceType type, Transform transform)
    {
        Id = id;
        Type = type;
        Transform = transform;
    }
    
    public nint Id { get; }
    public ParsedInstanceType Type { get; }
    public Transform Transform { get; }
}

public class ParsedUnsupportedInstance : ParsedInstance
{
    public string? Path { get; }

    public ParsedUnsupportedInstance(nint id, InstanceType instanceType, Transform transform, string? path) : base(id, ParsedInstanceType.Unsupported, transform)
    {
        Path = path;
        InstanceType = instanceType;
    }
    
    public InstanceType InstanceType { get; }
}

public class ParsedSharedInstance : ParsedInstance, IPathInstance
{
    public HandleString Path { get; }
    public IReadOnlyList<ParsedInstance> Children { get; }

    public ParsedSharedInstance(nint id, Transform transform, string path, IReadOnlyList<ParsedInstance> children) : base(id, ParsedInstanceType.SharedGroup, transform)
    {
        Path = path;
        Children = children;
    }
    public ParsedSharedInstance(nint id, ParsedInstanceType type, Transform transform, string path, IReadOnlyList<ParsedInstance> children) : base(id, type, transform)
    {
        Path = path;
        Children = children;
    }
    
    public ParsedInstance[] Flatten()
    {
        var list = new List<ParsedInstance> { this };
        foreach (var child in Children)
        {
            if (child is ParsedSharedInstance shared)
            {
                list.AddRange(shared.Flatten());
            }
            else
            {
                list.Add(child);
            }
        }

        return list.ToArray();
    }
}

public class ParsedHousingInstance : ParsedSharedInstance
{
    public ParsedHousingInstance(nint id, Transform transform, string path, string name, 
                                 ObjectKind kind, Stain? stain, Item? item, IReadOnlyList<ParsedInstance> children) : base(id, ParsedInstanceType.Housing, transform, path, children)
    {
        Name = name;
        Kind = kind;
        Stain = stain;
        Item = item;
    }

    public Stain? Stain { get; }
    public Item? Item { get; }
    public string Name { get; }
    public ObjectKind Kind { get; }
}

public class ParsedBgPartsInstance : ParsedInstance, IPathInstance, IStainableInstance
{
    public HandleString Path { get; }

    public ParsedBgPartsInstance(nint id, Transform transform, string path) : base(id, ParsedInstanceType.BgPart, transform)
    {
        Path = path;
    }

    public Vector4? StainColor { get; set; }
}

public interface IStainableInstance
{
    public Vector4? StainColor { get; set; }
}

public class ParsedLightInstance : ParsedInstance
{
    public RenderLight Light { get; }
    
    public unsafe ParsedLightInstance(nint id, Transform transform, RenderLight* light) : base(id, ParsedInstanceType.Light, transform)
    {
        Light = *light;
    }
}

public class ParsedTerrainInstance : ParsedInstance, IPathInstance, IResolvableInstance
{
    public HandleString Path { get; }
    public ParsedTerrainInstanceData? Data { get; set; }

    public ParsedTerrainInstance(nint id, Transform transform, string path) : base(id, ParsedInstanceType.Terrain, transform)
    {
        Path = path;
    }

    public bool IsResolved { get; private set; }
    public void Resolve(ResolverService resolver)
    {
        if (IsResolved) return;
        if (IsResolved) return;
        try
        {
            resolver.ResolveParsedTerrainInstance(this);
        } 
        finally
        {
            IsResolved = true;
        }
    }
}

public class ParsedTerrainInstanceData(TeraFile teraFile)
{
    public readonly TeraFile TeraFile = teraFile;
    public readonly Dictionary<int, ParsedModelInfo?> ResolvedPlates = new();
}

public class ParsedInstanceSet
{
    public List<ParsedInstance> Instances = [];
}

public class ParsedTextureInfo(string path, string pathFromMaterial, TextureResource resource) 
{
    public HandleString Path { get; } = new() { FullPath = path, GamePath = pathFromMaterial };
    
    public TextureResource Resource { get; } = resource;
}

public class ParsedMaterialInfo(string path, string pathFromModel, string shpk, IColorTableSet? colorTable, ParsedTextureInfo[] textures) 
{
    public HandleString Path { get; } = new() { FullPath = path, GamePath = pathFromModel };
    public string Shpk { get; } = shpk;
    public IColorTableSet? ColorTable { get; } = colorTable;
    public ParsedTextureInfo[] Textures { get; } = textures;
}

public class ParsedModelInfo(string path, string pathFromCharacter, DeformerCachedStruct? deformer, Model.ShapeAttributeGroup? shapeAttributeGroup, ParsedMaterialInfo[] materials) 
{
    public HandleString Path { get; } = new() { FullPath = path, GamePath = pathFromCharacter };
    public DeformerCachedStruct? Deformer { get; } = deformer;
    public Model.ShapeAttributeGroup? ShapeAttributeGroup { get; } = shapeAttributeGroup;
    public ParsedMaterialInfo[] Materials { get; } = materials;
}

public interface ICharacterInstance
{
    public CustomizeData CustomizeData { get; }
    public CustomizeParameter CustomizeParameter { get; }
}

public struct HandleString
{
    public string FullPath;
    public string GamePath;

    public static implicit operator HandleString(string path) => new() { FullPath = path, GamePath = path };
}

public class ParsedCharacterInfo
{
    public readonly ParsedModelInfo[] Models;
    public readonly ParsedSkeleton Skeleton;
    public CustomizeData CustomizeData;
    public CustomizeParameter CustomizeParameter;
    public readonly GenderRace GenderRace;
    public readonly ParsedAttach Attach;
    public ParsedCharacterInfo[] Attaches = [];

    public ParsedCharacterInfo(ParsedModelInfo[] models, ParsedSkeleton skeleton, ParsedAttach attach, CustomizeData customizeData, CustomizeParameter customizeParameter, GenderRace genderRace)
    {
        Models = models;
        Skeleton = skeleton;
        CustomizeData = customizeData;
        CustomizeParameter = customizeParameter;
        GenderRace = genderRace;
        Attach = attach;
    }
}

public class ParsedCharacterInstance : ParsedInstance, IResolvableInstance, ICharacterInstance
{
    public ParsedCharacterInfo? CharacterInfo;
    public string Name;
    public ObjectKind Kind;
    public bool Visible;
    
    public ParsedCharacterInstance(nint id, string name, ObjectKind kind, Transform transform, bool visible) : base(id, ParsedInstanceType.Character, transform)
    {
        Name = name;
        Kind = kind;
        Visible = visible;
    }

    public bool IsResolved { get; private set; }

    public void Resolve(ResolverService resolver)
    {
        if (IsResolved) return;
        try
        {
            resolver.ResolveParsedCharacterInstance(this);
        } 
        finally
        {
            IsResolved = true;
        }
    }

    public CustomizeData CustomizeData => CharacterInfo?.CustomizeData ?? new CustomizeData();
    public CustomizeParameter CustomizeParameter => CharacterInfo?.CustomizeParameter ?? new CustomizeParameter();
}
