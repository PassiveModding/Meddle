using System.Numerics;

namespace Meddle.Plugin.Models;

public class ColorTable
{
    public ColorTableRow[] Rows { get; }
    
    public ColorTable(Half[] table)
    {
        // Convert table to ColorTable rows
        // table is 256, we want 16 rows
        var colorTable = new ColorTableRow[16];
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
            colorTable[i] = new ColorTableRow(diff, spec, ss, emis);
        }
        
        Rows = colorTable;
    }
    
    public class ColorTableRow
    {
        public Vector3 Diffuse { get;}           // 0,1,2
        public Vector3 Specular { get; }         // 4,5,6
        public float   SpecularStrength { get; } // 3
        public Vector3 Emissive { get; }         // 8,9,10
    
        public ColorTableRow(Vector3 diff, Vector3 spec, float ss, Vector3 emis)
        {
            Diffuse = diff;
            Specular = spec;
            SpecularStrength = ss;
            Emissive = emis;
        }
    
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
}
