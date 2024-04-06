using System.Numerics;
using Meddle.Plugin.Models;

namespace Meddle.Plugin.Utility.Materials;

public class ProcessCharacterNormalOperation(SKTexture normal, ColorTable table)
    {
        public SKTexture Normal    { get; } = normal.Copy();
        public SKTexture BaseColor { get; } = new(normal.Width, normal.Height);
        public SKTexture Specular  { get; } = new(normal.Width, normal.Height);
        public SKTexture Emissive  { get; } = new(normal.Width, normal.Height);

        private static TableRow GetTableRowIndices(float input)
        {
            // These calculations are ported from character.shpk.
            var smoothed = (MathF.Floor(input * 7.5f % 1.0f * 2) 
                            * (-input * 15 + MathF.Floor(input * 15 + 0.5f)))
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
        
        public ProcessCharacterNormalOperation Run()
        {
            for (var y = 0; y < normal.Height; y++)
            for (var x = 0; x < normal.Width; x++)
            {
                var normalPixel = Normal[x, y].ToVector4();
            
                // Table row data (.a)
                var tableRow = GetTableRowIndices(normalPixel.W);
                var prevRow  = table[tableRow.Previous];
                var nextRow  = table[tableRow.Next];
            
                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                BaseColor[x, y] = new Vec4Ext(lerpedDiffuse, normalPixel.Z);
            
                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                var lerpedSpecularFactor = float.Lerp(prevRow.SpecularStrength, nextRow.SpecularStrength, tableRow.Weight);
                Specular[x, y] = new Vec4Ext(lerpedSpecularColor, lerpedSpecularFactor);
            
                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                Emissive[x, y] = lerpedEmissive.ToSkColor();
            
                // Normal (.rg)
                Normal[x, y] = new Vec4Ext(normalPixel.X, normalPixel.Y, 1, normalPixel.Z);
            }
            
            return this;
        }
    }
