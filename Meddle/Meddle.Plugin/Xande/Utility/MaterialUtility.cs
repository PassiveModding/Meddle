/*using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Xande.Models;
using SharpGLTF.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Meddle.Plugin.Xande.Utility;

public class MaterialUtility
{
    /// <summary> Dependency-less material configuration, for use when no material data can be resolved. </summary>
    public static readonly MaterialBuilder Unknown = new MaterialBuilder("UNKNOWN")
                                                     .WithMetallicRoughnessShader()
                                                     .WithDoubleSide(true)
                                                     .WithBaseColor(Vector4.One);
    
    public static MaterialBuilder Export(Material material, string name)
    {
        name = $"{name}_{material.ShaderPackage.Name.Replace(".shpk", "")}";
        return material.ShaderPackage.Name switch
        {
            // NOTE: this isn't particularly precise to game behavior (it has some fade around high opacity), but good enough for now.
            "character.shpk"      => BuildCharacter(material, name).WithAlpha(AlphaMode.MASK, 0.5f),
            "characterglass.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.BLEND),
            "hair.shpk"           => BuildHair(material, name),
            "iris.shpk"           => BuildIris(material, name),
            "skin.shpk"           => BuildSkin(material, name),
            _                     => BuildFallback(material, name),
        };
    }
    
    private static MaterialBuilder BuildCharacter(Material material, string name)
    {
        // Build the textures from the color table.
        var normal = material.GetTexture(TextureUsage.SamplerNormal);

        var operation = new ProcessCharacterNormalOperation(normal, material.ColorTableRows);
        ParallelRowIterator.IterateRows(SixLabors.ImageSharp.Configuration.Default, normal.Bounds, in operation);

        // Check if full textures are provided, and merge in if available.
        var baseColor = operation.BaseColor;
        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuse))
        {
            MultiplyOperation.Execute(diffuse, operation.BaseColor);
            baseColor = diffuse;
        }

        Image specular = operation.Specular;
        if (material.TryGetTexture(TextureUsage.SamplerSpecular, out var specularTexture))
        {
            MultiplyOperation.Execute(specularTexture, operation.Specular);
            specular = specularTexture;
        }

        // Pull further information from the mask.
        if (material.TryGetTexture(TextureUsage.SamplerMask, out var maskTexture))
        {
            // Extract the red channel for "ambient occlusion".
            maskTexture.Mutate(context => context.Resize(baseColor.Width, baseColor.Height));
            maskTexture.ProcessPixelRows(baseColor, (maskAccessor, baseColorAccessor) =>
            {
                for (var y = 0; y < maskAccessor.Height; y++)
                {
                    var maskSpan      = maskAccessor.GetRowSpan(y);
                    var baseColorSpan = baseColorAccessor.GetRowSpan(y);

                    for (var x = 0; x < maskSpan.Length; x++)
                        baseColorSpan[x].FromVector4(baseColorSpan[x].ToVector4() * new Vector4(maskSpan[x].R / 255f));
                }
            });
            // TODO: handle other textures stored in the mask?
        }

        // Specular extension puts colour on RGB and factor on A. We're already packing like that, so we can reuse the texture.
        var specularImage = BuildImage(specular, name, "specular");

        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(baseColor,         name, "basecolor"))
            .WithNormal(BuildImage(operation.Normal,     name, "normal"))
            .WithEmissive(BuildImage(operation.Emissive, name, "emissive"), Vector3.One, 1)
            .WithSpecularFactor(specularImage, 1)
            .WithSpecularColor(specularImage);
    }
    
        private readonly struct ProcessCharacterNormalOperation(Image<Rgba32> normal, Material.ColorTableRow[] table) : IRowOperation
    {
        public Image<Rgba32> Normal    { get; } = normal.Clone();
        public Image<Rgba32> BaseColor { get; } = new(normal.Width, normal.Height);
        public Image<Rgba32> Specular  { get; } = new(normal.Width, normal.Height);
        public Image<Rgb24>  Emissive  { get; } = new(normal.Width, normal.Height);

        private Buffer2D<Rgba32> NormalBuffer
            => Normal.Frames.RootFrame.PixelBuffer;

        private Buffer2D<Rgba32> BaseColorBuffer
            => BaseColor.Frames.RootFrame.PixelBuffer;

        private Buffer2D<Rgba32> SpecularBuffer
            => Specular.Frames.RootFrame.PixelBuffer;

        private Buffer2D<Rgb24> EmissiveBuffer
            => Emissive.Frames.RootFrame.PixelBuffer;

        public void Invoke(int y)
        {
            var normalSpan    = NormalBuffer.DangerousGetRowSpan(y);
            var baseColorSpan = BaseColorBuffer.DangerousGetRowSpan(y);
            var specularSpan  = SpecularBuffer.DangerousGetRowSpan(y);
            var emissiveSpan  = EmissiveBuffer.DangerousGetRowSpan(y);

            for (var x = 0; x < normalSpan.Length; x++)
            {
                ref var normalPixel = ref normalSpan[x];

                // Table row data (.a)
                var tableRow = GetTableRowIndices(normalPixel.A / 255f);
                var prevRow  = table[tableRow.Previous];
                var nextRow  = table[tableRow.Next];

                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                baseColorSpan[x].FromVector4(new Vector4(lerpedDiffuse, 1));
                baseColorSpan[x].A = normalPixel.B;

                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                // float.Lerp is .NET8 ;-; #TODO
                var lerpedSpecularFactor = prevRow.SpecularStrength * (1.0f - tableRow.Weight) + nextRow.SpecularStrength * tableRow.Weight;
                specularSpan[x].FromVector4(new Vector4(lerpedSpecularColor, lerpedSpecularFactor));

                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                emissiveSpan[x].FromVector4(new Vector4(lerpedEmissive, 1));

                // Normal (.rg)
                // TODO: we don't actually need alpha at all for normal, but _not_ using the existing rgba texture means I'll need a new one, with a new accessor. Think about it.
                normalPixel.B = byte.MaxValue;
                normalPixel.A = byte.MaxValue;
            }
        }
    }

    private static TableRow GetTableRowIndices(float input)
    {
        // These calculations are ported from character.shpk.
        var smoothed = MathF.Floor(input * 7.5f % 1.0f * 2)
          * (-input * 15 + MathF.Floor(input * 15 + 0.5f))
          + input * 15;

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

    private readonly struct MultiplyOperation
    {
        public static void Execute<TPixel1, TPixel2>(Image<TPixel1> target, Image<TPixel2> multiplier)
            where TPixel1 : unmanaged, IPixel<TPixel1>
            where TPixel2 : unmanaged, IPixel<TPixel2>
        {
            // Ensure the images are the same size
            var (small, large) = target.Width < multiplier.Width && target.Height < multiplier.Height
                ? ((Image)target, (Image)multiplier)
                : (multiplier, target);
            small.Mutate(context => context.Resize(large.Width, large.Height));

            var operation = new MultiplyOperation<TPixel1, TPixel2>(target, multiplier);
            ParallelRowIterator.IterateRows(SixLabors.ImageSharp.Configuration.Default, target.Bounds, in operation);
        }
    }

    private readonly struct MultiplyOperation<TPixel1, TPixel2>(Image<TPixel1> target, Image<TPixel2> multiplier) : IRowOperation
        where TPixel1 : unmanaged, IPixel<TPixel1>
        where TPixel2 : unmanaged, IPixel<TPixel2>
    {
        public void Invoke(int y)
        {
            var targetSpan     = target.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            var multiplierSpan = multiplier.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);

            for (var x = 0; x < targetSpan.Length; x++)
                targetSpan[x].FromVector4(targetSpan[x].ToVector4() * multiplierSpan[x].ToVector4());
        }
    }

    // TODO: These are hardcoded colours - I'm not keen on supporting highly customizable exports, but there's possibly some more sensible values to use here.
    private static readonly Vector4 DefaultHairColor      = new Vector4(130, 64,  13,  255) / new Vector4(255);
    private static readonly Vector4 DefaultHighlightColor = new Vector4(77,  126, 240, 255) / new Vector4(255);

    /// <summary> Build a material following the semantics of hair.shpk. </summary>
    private static MaterialBuilder BuildHair(Material material, string name)
    {
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var hairCol      = material.PrimaryColor ?? DefaultHairColor;
        var highlightCol = material.SecondaryColor ?? DefaultHighlightColor;

        //var isFace = material.Mtrl.ShaderPackage.ShaderKeys
        //    .Any(key => key is { Category: categoryHairType, Value: valueFace });
        // TODO: ShaderKeys & lala cheeks
        var isFace = false;

        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        var mask   = material.GetTexture(TextureUsage.SamplerMask);

        mask.Mutate(context => context.Resize(normal.Width, normal.Height));

        var baseColor = new Image<Rgba32>(normal.Width, normal.Height);
        normal.ProcessPixelRows(mask, baseColor, (normalAccessor, maskAccessor, baseColorAccessor) =>
        {
            for (var y = 0; y < normalAccessor.Height; y++)
            {
                var normalSpan    = normalAccessor.GetRowSpan(y);
                var maskSpan      = maskAccessor.GetRowSpan(y);
                var baseColorSpan = baseColorAccessor.GetRowSpan(y);

                for (var x = 0; x < normalSpan.Length; x++)
                {
                    var color = Vector4.Lerp(hairCol, highlightCol, maskSpan[x].A / 255f);
                    baseColorSpan[x].FromVector4(color * new Vector4(maskSpan[x].R / 255f));
                    baseColorSpan[x].A = normalSpan[x].A;

                    normalSpan[x].A = byte.MaxValue;
                }
            }
        });

        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
            .WithNormal(BuildImage(normal,       name, "normal"))
            .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
    }

    private static readonly Vector4 DefaultEyeColor = new Vector4(21, 176, 172, 255) / new Vector4(255);

    /// <summary> Build a material following the semantics of iris.shpk. </summary>
    // NOTE: This is largely the same as the hair material, but is also missing a few features that would cause it to diverge. Keeping separate for now.
    private static MaterialBuilder BuildIris(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        var mask   = material.GetTexture(TextureUsage.SamplerMask);

        mask.Mutate(context => context.Resize(normal.Width, normal.Height));

        var baseColor = new Image<Rgba32>(normal.Width, normal.Height);
        normal.ProcessPixelRows(mask, baseColor, (normalAccessor, maskAccessor, baseColorAccessor) =>
        {
            for (var y = 0; y < normalAccessor.Height; y++)
            {
                var normalSpan    = normalAccessor.GetRowSpan(y);
                var maskSpan      = maskAccessor.GetRowSpan(y);
                var baseColorSpan = baseColorAccessor.GetRowSpan(y);

                for (var x = 0; x < normalSpan.Length; x++)
                {
                    baseColorSpan[x].FromVector4((material.PrimaryColor ?? DefaultEyeColor) * new Vector4(maskSpan[x].R / 255f));
                    baseColorSpan[x].A = normalSpan[x].A;

                    normalSpan[x].A = byte.MaxValue;
                }
            }
        });
        
        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
            .WithNormal(BuildImage(normal,       name, "normal"));
    }

    /// <summary> Build a material following the semantics of skin.shpk. </summary>
    private static MaterialBuilder BuildSkin(Material material, string name)
    {
        // Trust me bro.
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace        = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        //var isFace = !material.ShaderPackage.ShaderKeys
        //    .Any(key => key.Category == categorySkinType && key.Value != valueFace);
        // TODO: ShaderKeys
        var isFace = true;

        var diffuse = material.GetTexture(TextureUsage.SamplerDiffuse);
        var normal  = material.GetTexture(TextureUsage.SamplerNormal);
        var mask    = material.GetTexture(TextureUsage.SamplerMask);
      
        // Create a copy of the normal that's the same size as the diffuse for purposes of copying the opacity across.
        var resizedNormal = normal.Clone(context => context.Resize(diffuse.Width, diffuse.Height));
        diffuse.ProcessPixelRows(resizedNormal, (diffuseAccessor, normalAccessor) =>
        {
            for (var y = 0; y < diffuseAccessor.Height; y++)
            {
                var diffuseSpan = diffuseAccessor.GetRowSpan(y);
                var normalSpan  = normalAccessor.GetRowSpan(y);

                for (var x = 0; x < diffuseSpan.Length; x++)
                    diffuseSpan[x].A = normalSpan[x].B;
            }
        });
        
        
        // Clear the blue channel out of the normal now that we're done with it.
        normal.ProcessPixelRows(normalAccessor =>
        {
            for (var y = 0; y < normalAccessor.Height; y++)
            {
                var normalSpan = normalAccessor.GetRowSpan(y);

                for (var x = 0; x < normalSpan.Length; x++)
                    normalSpan[x].B = byte.MaxValue;
            }
        });
        
        // Masking
        var resizedMask = mask.Clone(context => context.Resize(diffuse.Width, diffuse.Height));
        if (material.PrimaryColor.HasValue || material.SecondaryColor.HasValue)
        {
            resizedMask.ProcessPixelRows(diffuse, (maskAccessor, diffuseAccessor) =>
            {
                for (var y = 0; y < maskAccessor.Height; y++)
                {
                    var maskSpan    = maskAccessor.GetRowSpan(y);
                    var diffuseSpan = diffuseAccessor.GetRowSpan(y);

                    for (var x = 0; x < maskSpan.Length; x++)
                    {
                        if (material.PrimaryColor.HasValue)
                        {
                            var intensity = maskSpan[x].R;
                            // TODO: Confirm the cutoff. For AuRa face tex, the scales sit ~128 and skin sits at 255. We don't want to apply this to the scaled areas.
                            if (intensity > 128)
                            {
                                // ratio intensity 128-255
                                var intensityRatio = (intensity - 128) / 127f;
                                var baseColor = material.PrimaryColor.Value;
                                var color = diffuseSpan[x].ToVector4();
                                var lerpCol = Vector4.Lerp(color, baseColor, intensityRatio);
                                diffuseSpan[x].FromVector4(lerpCol);
                            }
                        }

                        // TODO
                        if (isFace && material.SecondaryColor.HasValue)
                        {
                            var lipIntensity = maskSpan[x].B / 255f;
                            var highlight = material.SecondaryColor.Value;
                            var color = diffuseSpan[x].ToVector4();
                            // highlight may have alpha also, but since we are modifying the diffuse
                            var lerpCol = Vector4.Lerp(color, highlight, lipIntensity * highlight.W);
                            lerpCol.W = color.W;
                            diffuseSpan[x].FromVector4(lerpCol);
                        }
                    }
                }
            });
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
        
        var diffuse = material.Textures.FirstOrDefault(x => x.Usage == TextureUsage.SamplerDiffuse);
        if (diffuse != null)
        {
            var png = TextureHelper.ConvertImage(diffuse.Resource);
            materialBuilder.WithBaseColor(BuildImage(png, name, "diffuse"));
        }
        
        var normal = material.Textures.FirstOrDefault(x => x.Usage == TextureUsage.SamplerNormal);
        if (normal != null)
        {
            var png = TextureHelper.ConvertImage(normal.Resource);
            materialBuilder.WithNormal(BuildImage(png, name, "normal"));
        }

        return materialBuilder;
    }
    
    /// <summary> Build a material pre-configured with settings common to all XIV materials/shaders. </summary>
    private static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        // TODO: Move this and potentially the other known stuff into MtrlFile?
        const uint backfaceMask  = 0x1;
        //var        showBackfaces = (material.Mtrl.ShaderPackage.Flags & backfaceMask) == 0;

        return new MaterialBuilder(name);
        //.WithDoubleSide(showBackfaces);
    }
    
    /// <summary> Convert an ImageSharp Image into an ImageBuilder for use with SharpGLTF. </summary>
    private static ImageBuilder BuildImage(Image image, string materialName, string suffix)
    {
        var name = materialName.Replace("/", "").Replace(".mtrl", "") + $"_{suffix}";

        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            image.Save(memoryStream, PngFormat.Instance);
            textureBytes = memoryStream.ToArray();
        }

        var imageBuilder = ImageBuilder.From(textureBytes, name);
        imageBuilder.AlternateWriteFileName = $"{name}.*";
        return imageBuilder;
    }
}
*/
