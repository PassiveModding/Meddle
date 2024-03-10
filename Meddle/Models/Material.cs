using FFXIVClientStructs.FFXIV.Common.Math;
using Lumina.Data.Parsing;
using Penumbra.GameData.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Meddle.Plugin.Models;

public class Material
{
    public Material(MtrlFile mtrl, Dictionary<TextureUsage, Image<Rgba32>> textures)
    {
        Mtrl = mtrl;
        Textures = textures;
    }
    
    public readonly MtrlFile Mtrl;

    public readonly Dictionary<TextureUsage, Image<Rgba32>> Textures;
}

public class CharacterMaterial : Material
{
    private const string CharacterShaderPackage = "character.shpk";
    public CharacterMaterial(Material material) : base(material.Mtrl, material.Textures)
    {
        if (material.Mtrl.ShaderPackage.Name != CharacterShaderPackage)
        {
            throw new ArgumentException($"Shader package must be {CharacterShaderPackage} but was {material.Mtrl.ShaderPackage.Name}.");
        }
    }
}

public class CharacterGlassMaterial : Material
{
    private const string GlassShaderPackage = "characterglass.shpk";
    public CharacterGlassMaterial(Material material) : base(material.Mtrl, material.Textures)
    {
        if (material.Mtrl.ShaderPackage.Name != GlassShaderPackage)
        {
            throw new ArgumentException($"Shader package must be {GlassShaderPackage} but was {material.Mtrl.ShaderPackage.Name}.");
        }
    }
}

public class IrisMaterial : Material
{
    private const string IrisShaderPackage = "iris.shpk";
    public IrisMaterial(Material material, Vector4 primaryColor, Vector4? secondaryColor = null) : base(material.Mtrl, material.Textures)
    {
        if (material.Mtrl.ShaderPackage.Name != IrisShaderPackage)
        {
            throw new ArgumentException($"Shader package must be {IrisShaderPackage} but was {material.Mtrl.ShaderPackage.Name}.");
        }
        
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor ?? primaryColor;
    }

    public readonly Vector4 SecondaryColor;

    public readonly Vector4 PrimaryColor;
}

public class HairMaterial : Material
{
    private const string HairShaderPackage = "hair.shpk";
    public HairMaterial(Material material, Vector4 primaryColor, Vector4 secondaryColor) : base(material.Mtrl, material.Textures)
    {
        if (material.Mtrl.ShaderPackage.Name != HairShaderPackage)
        {
            throw new ArgumentException($"Shader package must be {HairShaderPackage} but was {material.Mtrl.ShaderPackage.Name}.");
        }
        
        Color = primaryColor;
        HighlightColor = secondaryColor;
    }
    
    public readonly Vector4 Color;
    public readonly Vector4 HighlightColor;
}

public class SkinMaterial : Material
{
    private const string SkinShaderPackage = "skin.shpk";
    public SkinMaterial(Material material, Vector4? primaryColor, Vector4? secondaryColor) : base(material.Mtrl, material.Textures)
    {
        if (material.Mtrl.ShaderPackage.Name != SkinShaderPackage)
        {
            throw new ArgumentException($"Shader package must be {SkinShaderPackage} but was {material.Mtrl.ShaderPackage.Name}.");
        }
        
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor;
    }
    
    public readonly Vector4? PrimaryColor;
    public readonly Vector4? SecondaryColor;
}