using System.Numerics;
using Meddle.Plugin.Models;

namespace Meddle.Plugin.Utility.Materials;


public class ProcessCharacterNormalOperation(SKTexture normal, ColorTable table)
    {
        public SKTexture Normal    { get; } = normal.Copy();
        public SKTexture BaseColor { get; } = new(normal.Width, normal.Height);
        public SKTexture Specular  { get; } = new(normal.Width, normal.Height);
        public SKTexture Emissive  { get; } = new(normal.Width, normal.Height);
        
        public ProcessCharacterNormalOperation Run()
        {
            for (var y = 0; y < normal.Height; y++)
            for (var x = 0; x < normal.Width; x++)
            {
                var normalPixel = Normal[x, y].ToVector4();
            
                // Table row data (.a)
                var (prevRow, nextRow, row) = table.Lookup(normalPixel.W);
            
                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, row.Weight);
                BaseColor[x, y] = new Vec4Ext(lerpedDiffuse, normalPixel.Z);
            
                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.FresnelValue0, nextRow.FresnelValue0, row.Weight);
                var lerpedSpecularFactor = float.Lerp(prevRow.SpecularMask, nextRow.SpecularMask, row.Weight);
                Specular[x, y] = new Vec4Ext(lerpedSpecularColor, lerpedSpecularFactor);
            
                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, row.Weight);
                Emissive[x, y] = lerpedEmissive.ToSkColor();
            
                // Normal (.rg)
                Normal[x, y] = new Vec4Ext(normalPixel.X, normalPixel.Y, 1, normalPixel.Z);
            }
            
            return this;
        }
    }
