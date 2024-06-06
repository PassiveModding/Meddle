using System.Text.Json.Serialization;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Models;

namespace Meddle.Utils.Export;

public unsafe class Material
{
    public string HandlePath { get; }
    public uint ShaderFlags { get; }
    public IReadOnlyList<ShaderKey> ShaderKeys { get; }
    public string ShaderPackageName { get; }
    //public ShaderPackage ShaderPackage { get; }
    public IReadOnlyList<Texture> Textures { get; }
    //public MaterialParameters MaterialParameters { get; }
    
    public bool TryGetTexture(TextureUsage usage, out Texture texture)
    {
        var match = Textures.FirstOrDefault(x => x.Usage == usage);
        if (match == null)
        {
            texture = null!;
            return false;
        }

        texture = match;
        return true;
    }
    
    public Texture GetTexture(TextureUsage usage)
    {
        if (!TryGetTexture(usage, out var texture))
            throw new ArgumentException($"No texture for {usage}");
        return texture!;
    }
    
    [JsonIgnore]
    public ColorTable? ColorTable { get; }

    public Material(MtrlFile file, string handlePath, IReadOnlyDictionary<string, TexFile> texFiles)
    {
        HandlePath = handlePath;
        ShaderFlags = 0; // TODO
        ShaderPackageName = file.GetShaderPackageName();

        var shaderKeys = new ShaderKey[file.ShaderValues.Length];
        for (var i = 0; i < file.ShaderKeys.Length; ++i)
        {
            shaderKeys[i] = new ShaderKey
            {
                Category = file.ShaderKeys[i].Category,
                Value = file.ShaderKeys[i].Value
            };
        }
        
        ShaderKeys = shaderKeys;
        
        var textures = new List<Texture>();
        var texturePaths = file.GetTexturePaths();
        for (int i = 0; i < file.TextureOffsets.Length; i++)
        {
            var texture = file.TextureOffsets[i];
            var path = texturePaths[texture.Offset];
            var texFile = texFiles[path];
            var texObj = new Texture(texFile, path, (uint)texture.Flags, (uint)i);
            
            textures.Add(texObj);
        }
        
        Textures = textures;
        ColorTable = file.ColorTable;
    }
    
    // https://github.com/Shaderlayan/Ouroboros
    public struct ShaderKey
    {
        public enum ShaderKeyCategory : uint
        {
            // Note:
            // CharacterGlass always Color
            // Hair always Color
            // Iris always Multi
            // Skin always Multi
            VertexColorModeMulti = 4113354501,
            SkinType = 940355280,
            HairType = 612525193,
            TextureMode = 3054951514,
            DecalMode = 3531043187,
            SpecularMapMode = 3367837167
        }

        public enum TextureMode : uint
        {
            Multi = 1556481461,
            
            // Diffuse Color: #D50000 Specular Color: #FFFFFF Specular Strength: 1.00 Gloss Strength: 100 Emissive Color: #8C0000
            // Ignores vertex colors and normal map (except for opacity)
            // Accepts no color table and no textures
            Simple = 581216959, 
            Compatibility = 1611594207 // Diffuse / Specular
        }
        
        public enum DecalMode : uint
        {
            None = 1111668802,
            Alpha = 1480746461, // Face paint
            Color = 4083110193 // FC crest
        }

        // This is a setting of the Compatibility (Diffuse / Specular) Texture Mode, and has no effect outside of it.
        public enum SpecularMapMode : uint
        {
            Color = 428675533,
            Multi = 2687453224 
        }

        public enum VertexColorModeMultiValue : uint
        {
            Color = 3756477356,
            Multi = 2815623008
        }

        public enum SkinTypeValue : uint
        {
            Face = 4117181732,
            Body = 735790577,
            BodyWithHair = 1476344676 // used notably on hrothgar
        }

        public enum HairTypeValue : uint
        {
            Hair = 4156069230,
            Face = 1851494160
        }
        
        public uint Category;
        public uint Value;
        
        public ShaderKeyCategory CategoryEnum => (ShaderKeyCategory)Category;
        public TextureMode TextureModeEnum => (TextureMode)Value;
        public DecalMode DecalModeEnum => (DecalMode)Value;
        public SpecularMapMode SpecularMapModeEnum => (SpecularMapMode)Value;
        public VertexColorModeMultiValue VertexColorModeMultiValueEnum => (VertexColorModeMultiValue)Value;
        public SkinTypeValue SkinTypeValueEnum => (SkinTypeValue)Value;
        public HairTypeValue HairTypeValueEnum => (HairTypeValue)Value;
    }
}
