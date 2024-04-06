using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Plugin.Utility.Materials;

public static class MaterialParamUtility
{
    public static MaterialBuilder BuildCharacter(Material material)
    {
        
        var normal = material.GetTexture(TextureUsage.SamplerNormal).Resource.ToTexture();
        material.TryGetSkTexture(TextureUsage.SamplerDiffuse, out var diffuse);
        material.TryGetSkTexture(TextureUsage.SamplerSpecular, out var specular);
        material.TryGetSkTexture(TextureUsage.SamplerMask, out var mask);

        var textureModeKey =
            material.ShaderKeys.First(x => x.CategoryEnum == Material.ShaderKey.ShaderKeyCategory.TextureMode);

        var textureMode = textureModeKey.TextureModeEnum;
        var alphaThreshold = material.MaterialParameters.AlphaThreshold;

        var diffuseColorMap = new SKTexture(normal.Width, normal.Height);
        var fresnelValue0Map = new SKTexture(normal.Width, normal.Height); // fresnel, specular mask
        var glossMaskMap = new SKTexture(normal.Width, normal.Height);
        var emissiveColorMap = new SKTexture(normal.Width, normal.Height); // emmissive, shininess
        
        diffuseColorMap.Bitmap.Erase(SKColors.Transparent);
        fresnelValue0Map.Bitmap.Erase(SKColors.Transparent);
        glossMaskMap.Bitmap.Erase(SKColors.Transparent);
        emissiveColorMap.Bitmap.Erase(SKColors.Transparent);
        
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var pos = new Vector2(x / (float)normal.Width, y / (float)normal.Height);
            var normalPixel = normal[x, y].ToVector4();
            var alpha = normalPixel.Z; // g_SamplerNormal.Sample(ps.texCoord2.xy).z;
            if (alpha < alphaThreshold) continue;

            if (textureMode == Material.ShaderKey.TextureMode.Simple)
            {
                diffuseColorMap[x, y] = new Vec4Ext(0.7f, 0, 0, alpha);
                fresnelValue0Map[x, y] = new Vec4Ext(1f, 1f, 1f, 1f);
                glossMaskMap[x, y] = new Vec4Ext(0f);
                emissiveColorMap[x, y] = new Vec4Ext(0.3f, 0, 0, 100f / 255f);
            }
            else
            {
                var index = normalPixel.W; // g_SamplerIndex.Sample(ps.texCoord2.xy).w;
                var (prev, next, row) = material.ColorTable!.Value.Lookup(index);
                
                var diffuseColor = Vector3.Lerp(prev.Diffuse, next.Diffuse, row.Weight);
                var specularMask = float.Lerp(prev.SpecularMask, next.SpecularMask, row.Weight);
                var fresnelValue0 = Vector3.Lerp(prev.FresnelValue0, next.FresnelValue0, row.Weight);
                var shininess = float.Lerp(prev.Shininess, next.Shininess, row.Weight);
                var emissiveCol = Vector3.Lerp(prev.Emissive, next.Emissive, row.Weight);
                if (textureMode == Material.ShaderKey.TextureMode.Compatibility)
                {
                    var diffuseS = diffuse!.Sample(pos).XYZ;
                    diffuseColor *= diffuseS;
                    
                    var specularS = specular!.Sample(pos).XYZ;

                    if (true) // COMPAT_MASK TODO
                    {
                        glossMaskMap[x, y] = new Vec4Ext(specularS.X);
                        fresnelValue0 *= specularS.Y;
                        specularMask *= specularS.Z;
                    }
                    else
                    {
                        glossMaskMap[x, y] = new Vec4Ext(0f);
                        fresnelValue0 *= specularS;
                    }
                }
                else
                {
                    var maskS = mask!.Sample(pos).XYZ;
                    diffuseColor *= maskS.X;
                    fresnelValue0 *= maskS.Y;
                    specularMask *= maskS.Z;
                    glossMaskMap[x, y] = new Vec4Ext(0f);
                }
                
                emissiveCol *= material.MaterialParameters.EmissiveColor;
                
                diffuseColorMap[x, y] = new Vec4Ext(diffuseColor, alpha);
                fresnelValue0Map[x, y] = new Vec4Ext(fresnelValue0, specularMask);
                emissiveColorMap[x, y] = new Vec4Ext(emissiveCol, shininess / 255f);
            }
        }
        
        var name = $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_" +
                   $"{Path.GetFileNameWithoutExtension(material.ShaderPackage.Name)}_{textureMode}";
        
        var fresnelImage = fresnelValue0Map.Build(name, "fresnel");
        
        var builder = BuildSharedBase(material)
            .WithEmissive(emissiveColorMap.Build(name, "emissive"))
            .WithSpecularColor(fresnelImage)
            .WithSpecularFactor(fresnelImage, 1f)
            .WithClearCoat(glossMaskMap.Build(name, "gloss"), 1f)
            .WithNormal(normal.Build(name, "normal"))
            .WithBaseColor(diffuseColorMap.Build(name, "base"));
        
        return builder;
    }

    public static MaterialBuilder BuildSharedBase(Material material)
    {
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.ShaderFlags & backfaceMask) == 0;
        
        
        var name = $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_" +
                   $"{Path.GetFileNameWithoutExtension(material.ShaderPackage.Name)}";
        
        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }
    
    public static ImageBuilder Build(this SKTexture texture, string name, string type)
    {
        name = $"{name}_{type}";
        
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var imageBuilder = ImageBuilder.From(textureBytes, name);
        imageBuilder.AlternateWriteFileName = $"{name}.*";
        return imageBuilder;
    }
}
