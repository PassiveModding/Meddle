using Meddle.Plugin.Models;
using SharpGLTF.Schema2;

namespace Meddle.Plugin.Utils;

public static class ExportUtil
{
    public static void SaveAsType(this ModelRoot? gltf, ExportType typeFlags, string path, string name)
    {
        if (typeFlags.HasFlag(ExportType.GLTF))
        {
            gltf?.SaveGLTF(Path.Combine(path, name + ".gltf"));
        }
        
        if (typeFlags.HasFlag(ExportType.GLB))
        {
            gltf?.SaveGLB(Path.Combine(path, name + ".glb"));
        }
        
        if (typeFlags.HasFlag(ExportType.OBJ))
        {
            gltf?.SaveAsWavefront(Path.Combine(path, name + ".obj"));
        }
    }
}
