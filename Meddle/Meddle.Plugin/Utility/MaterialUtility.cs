using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Xande.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Plugin.Utility;

public class MaterialUtility
{
    /// <summary> Dependency-less material configuration, for use when no material data can be resolved. </summary>
    public static readonly MaterialBuilder Unknown = new MaterialBuilder("UNKNOWN")
                                                     .WithMetallicRoughnessShader()
                                                     .WithDoubleSide(true)
                                                     .WithBaseColor(Vector4.One);
    
    private static readonly Vector4 DefaultEyeColor = new Vector4(21, 176, 172, 255) / new Vector4(255);
    private static readonly Vector4 DefaultHairColor      = new Vector4(130, 64,  13,  255) / new Vector4(255);
    private static readonly Vector4 DefaultHighlightColor = new Vector4(77,  126, 240, 255) / new Vector4(255);
    public static MaterialBuilder ParseMaterial(Material material, string name)
    {
        name = $"{name}_{material.ShaderPackage.Name.Replace(".shpk", "")}";

        return material.ShaderPackage.Name switch
        {
            "character.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.MASK, 0.5f),
            "characterglass.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.BLEND),
            "hair.shpk" => BuildHair(material, name),
            "iris.shpk"           => BuildIris(material, name),
            "skin.shpk"           => BuildSkin(material, name),
            _ => BuildFallback(material, name),
        };
    }
    
    private static MaterialBuilder BuildCharacter(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        
        var operation = new ProcessCharacterNormalOperation(normal.Resource.ToBitmap(), material.ColorTable).Run();
        
        var baseColor = operation.BaseColor;
        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuseTexture))
        {
            var diffuse = diffuseTexture.Resource.ToBitmap();
            baseColor = MultiplyBitmaps(diffuse, operation.BaseColor);
        }
        
        var specular = operation.Specular;
        if (material.TryGetTexture(TextureUsage.SamplerSpecular, out var specularTexture))
        {
            var spec = specularTexture.Resource.ToBitmap();
            specular = MultiplyBitmaps(spec, operation.Specular);
        }
        
        if (material.TryGetTexture(TextureUsage.SamplerMask, out var mask))
        {
            var maskImage = mask.Resource.ToBitmap();
            maskImage.Resize(new SKImageInfo(baseColor.Width, baseColor.Height), SKFilterQuality.High);

            for (var x = 0; x < baseColor.Width; x++)
            for (int y = 0; y < baseColor.Height; y++)
            {
                var maskPixel = maskImage.GetPixel(x, y);
                var baseColorPixel = baseColor.GetPixel(x, y);
                // multiply base by mask red channel
                var r = maskPixel.Red / 255f;

                var result = new SKColor(
                    (byte)(baseColorPixel.Red * r),
                    (byte)(baseColorPixel.Green * r),
                    (byte)(baseColorPixel.Blue * r),
                    baseColorPixel.Alpha
                );
                baseColor.SetPixel(x, y, result);
            }
        }
        
        var specularImage = BuildImage(specular, name, "specular");
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor,         name, "basecolor"))
               .WithNormal(BuildImage(operation.Normal,     name, "normal"))
               .WithEmissive(BuildImage(operation.Emissive, name, "emissive"), Vector3.One, 1)
               .WithSpecularFactor(specularImage, 1)
               .WithSpecularColor(specularImage);
    }
    
    /// <summary> Build a material following the semantics of hair.shpk. </summary>
    private static MaterialBuilder BuildHair(Material material, string name)
    {
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var hairCol      = material.PrimaryColor ?? DefaultHairColor;
        var highlightCol = material.SecondaryColor ?? DefaultHighlightColor;

        // TODO: ShaderKeys 
        var isFace = false;
        
        var normalTexture = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.SamplerMask);
        
        var normal = normalTexture.Resource.ToBitmap();
        var mask   = maskTexture.Resource.ToBitmap();
        mask.Resize(new SKImageInfo(normal.Width, normal.Height), SKFilterQuality.High);
        
        var baseColor = new SKBitmap(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal.GetPixel(x, y);
            var maskPixel = mask.GetPixel(x, y);
            var color = Vector4.Lerp(hairCol, highlightCol, maskPixel.Alpha / 255f);
            color *= new Vector4(maskPixel.Red / 255f);
            color.W = normalPixel.Alpha / 255f;

            baseColor.SetPixel(x, y, ToSkColor(color));
            normal.SetPixel(x, y, normalPixel.WithAlpha(byte.MaxValue));
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal,       name, "normal"))
               .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
    }
    
    private static MaterialBuilder BuildIris(Material material, string name)
    {
        var normalTexture = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.SamplerMask);
        
        var normal = normalTexture.Resource.ToBitmap();
        var mask   = maskTexture.Resource.ToBitmap();
        
        mask.Resize(new SKImageInfo(normal.Width, normal.Height), SKFilterQuality.High);
        
        var baseColor = new SKBitmap(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (int y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal.GetPixel(x, y);
            var maskPixel = mask.GetPixel(x, y);

            var color = (material.PrimaryColor ?? DefaultEyeColor) * new Vector4(maskPixel.Red / 255f);
            color.W = normalPixel.Alpha / 255f;

            baseColor.SetPixel(x, y, ToSkColor(color));
            normal.SetPixel(x, y, normalPixel.WithAlpha(byte.MaxValue));
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal, name, "normal"));
    }
    
        /// <summary> Build a material following the semantics of skin.shpk. </summary>
    private static MaterialBuilder BuildSkin(Material material, string name)
    {
        // Trust me bro.
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace        = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        //var isFace = !material.Mtrl.ShaderPackage.ShaderKeys
        //    .Any(key => key.Category == categorySkinType && key.Value != valueFace);
        var isFace = true;
        
        var diffuseTexture = material.GetTexture(TextureUsage.SamplerDiffuse);
        var normalTexture  = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture    = material.GetTexture(TextureUsage.SamplerMask);
        
        var diffuse = diffuseTexture.Resource.ToBitmap();
        var normal  = normalTexture.Resource.ToBitmap();
        var mask    = maskTexture.Resource.ToBitmap();
        
        var resizedNormal = normal.Resize(new SKImageInfo(diffuse.Width, diffuse.Height), SKFilterQuality.High);

        for (var x = 0; x < diffuse.Width; x++)
        for (int y = 0; y < diffuse.Height; y++)
        {
            var diffusePixel = diffuse.GetPixel(x, y);
            var normalPixel = resizedNormal.GetPixel(x, y);
            diffuse.SetPixel(
                x, y, new SKColor(diffusePixel.Red, diffusePixel.Green, diffusePixel.Blue, normalPixel.Blue));
        }
        
        // Clear the blue channel out of the normal now that we're done with it.
        for (var x = 0; x < normal.Width; x++)
        for (int y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal.GetPixel(x, y);
            normal.SetPixel(
                x, y, new SKColor(normalPixel.Red, normalPixel.Green, byte.MaxValue, normalPixel.Alpha));
        }

        var resizedMask = mask.Resize(new SKImageInfo(diffuse.Width, diffuse.Height), SKFilterQuality.High);
        if (material.PrimaryColor.HasValue || material.SecondaryColor.HasValue)
        {
            for (var x = 0; x < diffuse.Width; x++)
            for (int y = 0; y < diffuse.Height; y++)
            {
                var maskPixel = resizedMask.GetPixel(x, y);
                var diffusePixel = diffuse.GetPixel(x, y);

                if (material.PrimaryColor.HasValue)
                {
                    var intensity = maskPixel.Red;
                    if (intensity > 128)
                    {
                        var ratio = (intensity - 128) / 127f;
                        var color = material.PrimaryColor.Value;
                        var diffuseVec = new Vector4(diffusePixel.Red, diffusePixel.Green, diffusePixel.Blue,
                                                     diffusePixel.Alpha) / 255f;
                        var lerpCol = Vector4.Lerp(diffuseVec, color, ratio);
                        diffuse.SetPixel(
                            x, y,
                            new SKColor((byte)(lerpCol.X * 255), (byte)(lerpCol.Y * 255), (byte)(lerpCol.Z * 255),
                                        diffusePixel.Alpha));
                    }
                }

                if (isFace && material.SecondaryColor.HasValue)
                {
                    var lipIntensity = maskPixel.Blue / 255f;
                    var highlight = material.SecondaryColor.Value;
                    var diffuseVec = new Vector4(diffusePixel.Red, diffusePixel.Green, diffusePixel.Blue,
                                                 diffusePixel.Alpha) / 255f;
                    var lerpCol = Vector4.Lerp(diffuseVec, highlight, lipIntensity);
                    diffuse.SetPixel(
                        x, y,
                        new SKColor((byte)(lerpCol.X * 255), (byte)(lerpCol.Y * 255), (byte)(lerpCol.Z * 255),
                                    diffusePixel.Alpha));
                }
            }
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(diffuse, name, "basecolor"))
               .WithNormal(BuildImage(normal,     name, "normal"))
               .WithOcclusion(BuildImage(mask, name, "mask"))
               .WithAlpha(isFace ? AlphaMode.MASK : AlphaMode.OPAQUE, 0.5f);
    }
    
    private static MaterialBuilder BuildFallback(Material material, string name)
    {
        var materialBuilder = BuildSharedBase(material, name)
                              .WithMetallicRoughnessShader()
                              .WithBaseColor(Vector4.One);

        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuse))
            materialBuilder.WithBaseColor(BuildImage(diffuse, name, "basecolor"));

        if (material.TryGetTexture(TextureUsage.SamplerNormal, out var normal))
            materialBuilder.WithNormal(BuildImage(normal, name, "normal"));

        return materialBuilder;
    }
    
    private static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        // TODO: Move this and potentially the other known stuff into MtrlFile?
        //const uint backfaceMask  = 0x1;
        //var        showBackfaces = (material.ShaderPackage.Flags & backfaceMask) == 0;

        return new MaterialBuilder(name);
        //.WithDoubleSide(showBackfaces);
    }
    
    private static SKColor ToSkColor(Vector4 color)
    {
        return new SKColor((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), (byte)(color.W * 255));
    }

    private static ImageBuilder BuildImage(Texture texture, string materialName, string suffix)
    {
        return BuildImage(texture.Resource.ToBitmap(), materialName, suffix);
    }
    
    private static ImageBuilder BuildImage(SKBitmap texture, string materialName, string suffix)
    {
        var name = materialName.Replace("/", "").Replace(".mtrl", "") + $"_{suffix}";
        
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var imageBuilder = ImageBuilder.From(textureBytes, name);
        imageBuilder.AlternateWriteFileName = $"{name}.*";
        return imageBuilder;
    }
    
    private class ProcessCharacterNormalOperation(SKBitmap normal, Half[] table)
    {
        public SKBitmap Normal    { get; } = normal.Copy();
        public SKBitmap BaseColor { get; } = new(normal.Width, normal.Height);
        public SKBitmap Specular  { get; } = new(normal.Width, normal.Height);
        public SKBitmap  Emissive  { get; } = new(normal.Width, normal.Height);

        private static TableRow GetTableRowIndices(float input)
        {
            // These calculations are ported from character.shpk.
            var smoothed = (MathF.Floor(input * 7.5f % 1.0f * 2)
                            * ((-input * 15) + MathF.Floor((input * 15) + 0.5f)))
                           + (input * 15);

            var stepped = MathF.Floor(smoothed + 0.5f);

            return new TableRow
            {
                Stepped  = (int)stepped,
                Previous = (int)MathF.Floor(smoothed),
                Next     = (int)MathF.Ceiling(smoothed),
                Weight   = smoothed % 1,
            };
        }
        
        private ref struct TableRow
        {
            public int   Stepped;
            public int   Previous;
            public int   Next;
            public float Weight;
        }
        
        private class ColorTable
        {
            public Vector3 Diffuse { get; set; }          // 0,1,2
            public Vector3 Specular { get; set; }         // 4,5,6
            public float   SpecularStrength { get; set; } // 3
            public Vector3 Emissive { get; set; }         // 8,9,10

            public object Serialize()
            {
                // serialize vec3 not supported, so we'll just do it manually
                
                var diff = $"{Diffuse.X},{Diffuse.Y},{Diffuse.Z}";
                var spec = $"{Specular.X},{Specular.Y},{Specular.Z}";
                var emis = $"{Emissive.X},{Emissive.Y},{Emissive.Z}";
                
                return new
                {
                    Diffuse = diff,
                    Specular = spec,
                    SpecularStrength,
                    Emissive = emis,
                };
            }
        }
        
        public ProcessCharacterNormalOperation Run()
        {
            // Convert table to ColorTable rows
            // table is 256, we want 16 rows
            var colorTable = new ColorTable[16];
            for (var i = 0; i < colorTable.Length; i++)
            {
                var set = table.AsSpan(i * 16, 16);
                // convert to floats
                // values 0 to 1
                var floats = set.ToArray().Select(x => (float)x).ToArray();
                var diff = new Vector3(floats[0], floats[1], floats[2]);
                var spec = new Vector3(floats[4], floats[5], floats[6]);
                var emis = new Vector3(floats[8], floats[9], floats[10]);
                var ss = floats[3];
                
                var colorRow = new ColorTable
                {
                    Diffuse           = diff,
                    Specular          = spec,
                    SpecularStrength  = ss,
                    Emissive          = emis,
                };

                colorTable[i] = colorRow;
            }
            
            for (var y = 0; y < normal.Height; y++)
            {
                ProcessRow(y, colorTable);
            }
            
            return this;
        }
        
        private void ProcessRow(int y, ColorTable[] colorTable)
        {
            for (var x = 0; x < normal.Width; x++)
            {
                var pixel = Normal.GetPixel(x, y);
                
                // Table row data (.a)
                var tableRow = GetTableRowIndices(pixel.Alpha / 255f);
                var prevRow  = colorTable[tableRow.Previous];
                var nextRow  = colorTable[tableRow.Next];
                
                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                BaseColor.SetPixel(x, y, new SKColor((byte)(lerpedDiffuse.X * 255), (byte)(lerpedDiffuse.Y * 255), (byte)(lerpedDiffuse.Z * 255), pixel.Blue));
                
                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                // float.Lerp is .NET8 ;-; #TODO
                var lerpedSpecularFactor = (prevRow.SpecularStrength * (1.0f - tableRow.Weight)) + (nextRow.SpecularStrength * tableRow.Weight);
                Specular.SetPixel(x, y, new SKColor((byte)(lerpedSpecularColor.X * 255), (byte)(lerpedSpecularColor.Y * 255), (byte)(lerpedSpecularColor.Z * 255), (byte)(lerpedSpecularFactor * 255)));
                
                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                Emissive.SetPixel(x, y, new SKColor((byte)(lerpedEmissive.X * 255), (byte)(lerpedEmissive.Y * 255), (byte)(lerpedEmissive.Z * 255), 255));
                
                // Normal (.rg)
                // TODO: we don't actually need alpha at all for normal, but _not_ using the existing rgba texture means I'll need a new one, with a new accessor. Think about it.
                Normal.SetPixel(x, y, new SKColor(pixel.Red, pixel.Green, byte.MaxValue, byte.MaxValue));
            }
        }
    }

    public static SKBitmap MultiplyBitmaps(SKBitmap target, SKBitmap multiplier)
    {
        var (small, large) = target.Width < multiplier.Width && target.Height < multiplier.Height
            ? (target, multiplier)
            : (multiplier, target);
        
        small = small.Resize(new SKImageInfo(large.Width, large.Height), SKFilterQuality.High);

        var result = new SKBitmap(target.Width, target.Height);
        using var canvas = new SKCanvas(result);
        canvas.Clear();
        canvas.DrawBitmap(target, 0, 0);
        canvas.DrawBitmap(multiplier, 0, 0, new SKPaint
        {
            BlendMode = SKBlendMode.Multiply
        });

        return result;
    }
}
