using System.Numerics;
using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;
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
    Terrain = 64,
    Camera = 128,
    EnvLighting = 256,
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

public interface IStainableInstance
{
    public ParsedStain? Stain { get; }
}

public interface ISearchableInstance
{
    public bool Search(string query);
}

[JsonDerivedType(typeof(ParsedUnsupportedInstance))]
[JsonDerivedType(typeof(ParsedSharedInstance))]
[JsonDerivedType(typeof(ParsedHousingInstance))]
[JsonDerivedType(typeof(ParsedBgPartsInstance))]
[JsonDerivedType(typeof(ParsedLightInstance))]
[JsonDerivedType(typeof(ParsedCharacterInstance))]
[JsonDerivedType(typeof(ParsedTerrainInstance))]
[JsonDerivedType(typeof(ParsedCameraInstance))]
[JsonDerivedType(typeof(ParsedEnvLightInstance))]
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

public class ParsedUnsupportedInstance : ParsedInstance, ISearchableInstance
{
    public string? Path { get; }

    public ParsedUnsupportedInstance(nint id, InstanceType instanceType, Transform transform, string? path) : base(id, ParsedInstanceType.Unsupported, transform)
    {
        Path = path;
        InstanceType = instanceType;
    }
    
    public InstanceType InstanceType { get; }
    
    public bool Search(string query)
    {
        return Path?.Contains(query, StringComparison.OrdinalIgnoreCase) == true || 
               "unsupported".Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public class ParsedSharedInstance : ParsedInstance, IPathInstance, ISearchableInstance
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
        return FlattenInternal([]).ToArray();
    }

    private List<ParsedInstance> FlattenInternal(HashSet<ParsedInstance> visited)
    {
        if (!visited.Add(this))
        {
            return [];
        }

        var list = new List<ParsedInstance> { this };
        foreach (var child in Children)
        {
            if (child is ParsedSharedInstance shared)
            {
                list.AddRange(shared.FlattenInternal(visited));
            }
            else if (!visited.Contains(child))
            {
                list.Add(child);
                visited.Add(child);
            }
        }

        return list;
    }
    
    public bool Search(string query)
    {
        return SearchInternal(query, []);
    }

    private bool SearchInternal(string query, HashSet<ParsedInstance> visited)
    {
        if (!visited.Add(this))
        {
            return false;
        }

        if (Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) || 
            Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            "shared".Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var child in Children)
        {
            if (child is ParsedSharedInstance shared)
            {
                if (shared.SearchInternal(query, visited))
                {
                    return true;
                }
            }
            else if (visited.Add(child))
            {
                if (child is ISearchableInstance searchable && searchable.Search(query))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public record ParsedStain
{
    public ParsedStain(Stain stain)
    {
        SeColor = stain.Color;
        Color = ImGui.ColorConvertU32ToFloat4(UiUtil.SeColorToRgba(SeColor));
        RowId = stain.RowId;
        Name = stain.Name.ExtractText();
        Name2 = stain.Name2.ExtractText();
        Shade = stain.Shade;
        SubOrder = stain.SubOrder;
        Unknown1 = stain.Unknown1;
        Unknown2 = stain.Unknown2;
    }
    
    public uint SeColor { get; }
    public Vector4 Color { get; }
    public uint RowId { get; }
    public string Name { get; }
    public string Name2 { get; }
    public uint Shade { get; }
    public uint SubOrder { get; }
    public bool Unknown1 { get; }
    public bool Unknown2 { get; }
    
    public static implicit operator ParsedStain(Stain stain) => new(stain);
    public static implicit operator ParsedStain?(Stain? stain) => stain == null ? null : new ParsedStain(stain.Value);
}

public class ParsedHousingInstance : ParsedSharedInstance, ISearchableInstance
{
    public ParsedHousingInstance(nint id, Transform transform, string path, string name, 
                                 ObjectKind kind, Stain? stain, Stain defaultStain, 
                                 IReadOnlyList<ParsedInstance> children) : base(id, ParsedInstanceType.Housing, transform, path, children)
    {
        Name = name;
        Kind = kind;
        Stain = stain;
        DefaultStain = defaultStain;
    }

    public ParsedStain? Stain { get; }
    public ParsedStain DefaultStain { get; }
    
    public string Name { get; }
    public ObjectKind Kind { get; }
    
    public new bool Search(string query)
    {
        return Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            "housing".Contains(query, StringComparison.OrdinalIgnoreCase) ||
            base.Search(query);
    }
}

public class ParsedBgPartsInstance : ParsedInstance, IPathInstance, IStainableInstance, ISearchableInstance
{
    public bool IsVisible { get; }
    public HandleString Path { get; }

    public ParsedBgPartsInstance(nint id, bool isVisible, Transform transform, string path) : base(id, ParsedInstanceType.BgPart, transform)
    {
        IsVisible = isVisible;
        Path = path;
    }

    public ParsedStain? Stain { get; set; }

    public bool Search(string query)
    {
        return Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) || 
               Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public class ParsedEnvLightInstance : ParsedInstance, ISearchableInstance
{
    public EnvLighting Lighting { get; }
    
    public ParsedEnvLightInstance(nint id, Transform transform, EnvLighting lighting) : base(id, ParsedInstanceType.EnvLighting, transform)
    {
        Lighting = lighting;
    }

    public bool Search(string query)
    {
        return "envlight".Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public class ParsedLightInstance : ParsedInstance, ISearchableInstance
{
    public ParsedRenderLight Light { get; }
    
    public unsafe ParsedLightInstance(nint id, Transform transform, RenderLight* light) : base(id, ParsedInstanceType.Light, transform)
    {
        Light = new(light);
    }
    
    public ParsedLightInstance(nint id, Transform transform, ParsedRenderLight light) : base(id, ParsedInstanceType.Light, transform)
    {
        Light = light;
    }
    
    public bool Search(string query)
    {
        return "light".Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public class ParsedCameraInstance : ParsedInstance, ISearchableInstance
{
    public float FoV { get; }
    public float AspectRatio { get; }
    public Quaternion Rotation { get; }
    
    public ParsedCameraInstance(nint id, Transform transform, float fov, float aspectRatio, Vector3 position, Vector3 lookAtVector) : base(id, ParsedInstanceType.Camera, transform)
    {
        FoV = fov;
        AspectRatio = aspectRatio;
        var rotation = CreateLookAt(position, lookAtVector);
        // flip the rotation for cameras
        rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        
        Rotation = rotation;
    }

    public static Quaternion CreateLookAt(Vector3 from, Vector3 to, Vector3? up = null)
    {
        Vector3 upVector = up ?? Vector3.UnitY;
        
        // Calculate the direction vector
        Vector3 forward = Vector3.Normalize(to - from);
        
        // Handle edge case where forward is parallel to up
        if (Math.Abs(Vector3.Dot(forward, upVector)) > 0.99f)
        {
            upVector = Math.Abs(forward.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
        }
        
        // Calculate right and up vectors
        Vector3 right = Vector3.Normalize(Vector3.Cross(upVector, forward));
        Vector3 newUp = Vector3.Cross(forward, right);
        
        // Create rotation matrix
        Matrix4x4 rotationMatrix = new Matrix4x4(
            right.X,    right.Y,    right.Z,    0,
            newUp.X,    newUp.Y,    newUp.Z,    0,
            forward.X,  forward.Y,  forward.Z,  0,
            0,          0,          0,          1
        );
        
        // Convert matrix to quaternion
        return Quaternion.CreateFromRotationMatrix(rotationMatrix);
    }
    
    public bool Search(string query)
    {
        return "camera".Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public class ParsedTerrainInstance : ParsedInstance, IPathInstance, IResolvableInstance, ISearchableInstance
{
    public HandleString Path { get; }
    public Vector3 SearchOrigin { get; }
    public ParsedTerrainInstanceData? Data { get; set; }

    public ParsedTerrainInstance(nint id, Transform transform, string path, Vector3 searchOrigin) : base(id, ParsedInstanceType.Terrain, transform)
    {
        Path = path;
        SearchOrigin = searchOrigin;
    }

    public bool IsResolved { get; private set; }
    public void Resolve(ResolverService resolver)
    {
        if (IsResolved) return;
        try
        {
            resolver.ResolveInstances(this);
        } 
        finally
        {
            IsResolved = true;
        }
    }
    
    public bool Search(string query)
    {
        if (Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            "terrain".Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        if (Data != null)
        {
            foreach (var plate in Data.ResolvedPlates.Values)
            {
                if (plate == null) continue;
                if (plate.Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    plate.Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        
        return false;
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

public class ParsedMaterialInfo(string path, string pathFromModel, string shpk, IColorTableSet? colorTable, ParsedTextureInfo[] textures, Stain? stain0, Stain? stain1)
{
    public HandleString Path { get; } = new() { FullPath = path, GamePath = pathFromModel };
    public ParsedStain? Stain0 { get; } = stain0;
    public ParsedStain? Stain1 { get; } = stain1;
    public string Shpk { get; } = shpk;
    
    [JsonIgnore]
    public IColorTableSet? ColorTable { get; } = colorTable;

    public object? ColorTableBlob => ColorTable switch
    {
        ColorTableSet colorTableSet => colorTableSet.ToObject(),
        LegacyColorTableSet legacyColorTableSet => legacyColorTableSet.ToObject(),
        _ => null
    };
    
    public ParsedTextureInfo[] Textures { get; } = textures;
}

public class ParsedModelInfo(string path, string pathFromCharacter, DeformerCachedStruct? deformer, Model.ShapeAttributeGroup? shapeAttributeGroup, ParsedMaterialInfo?[] materials, Stain? stain0, Stain? stain1) 
{
    public HandleString Path { get; } = new() { FullPath = path, GamePath = pathFromCharacter };
    public ParsedStain? Stain0 { get; } = stain0;
    public ParsedStain? Stain1 { get; } = stain1;
    public DeformerCachedStruct? Deformer { get; } = deformer;
    public Model.ShapeAttributeGroup? ShapeAttributeGroup { get; } = shapeAttributeGroup;
    public ParsedMaterialInfo?[] Materials { get; } = materials;
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

public class ParsedCharacterInstance : ParsedInstance, IResolvableInstance, ICharacterInstance, ISearchableInstance
{
    public enum ParsedCharacterInstanceIdType
    {
        GameObject,
        CharacterBase
    }
    
    public ParsedCharacterInfo? CharacterInfo;
    public string Name;
    public ObjectKind Kind;
    public bool Visible;
    public readonly ParsedCharacterInstanceIdType IdType;
    public ParsedCharacterInstance? Parent;
    
    public ParsedCharacterInstance(nint id, string name, ObjectKind kind, Transform transform, bool visible, ParsedCharacterInstanceIdType isIdDrawObject = ParsedCharacterInstanceIdType.GameObject) : base(id, ParsedInstanceType.Character, transform)
    {
        Name = name;
        Kind = kind;
        Visible = visible;
        IdType = isIdDrawObject;
    }

    public bool IsResolved { get; private set; }

    public void Resolve(ResolverService resolver)
    {
        if (IsResolved) return;
        try
        {
            resolver.ResolveInstances(this);
        } 
        finally
        {
            IsResolved = true;
        }
    }

    public CustomizeData CustomizeData => CharacterInfo?.CustomizeData ?? new CustomizeData();
    public CustomizeParameter CustomizeParameter => CharacterInfo?.CustomizeParameter ?? new CustomizeParameter();
    
    public bool Search(string query)
    {
        if (Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            "character".Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        if (CharacterInfo != null)
        {
            foreach (var model in CharacterInfo.Models)
            {
                if (model.Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    model.Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                foreach (var material in model.Materials)
                {
                    if (material == null) continue;
                    if (material.Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        material.Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    
                    foreach (var texture in material.Textures)
                    {
                        if (texture.Path.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            texture.Path.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        
        return false;
    }
}
