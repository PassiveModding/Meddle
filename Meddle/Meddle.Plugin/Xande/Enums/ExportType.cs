namespace Meddle.Plugin.Xande.Enums;

[Flags]
public enum ExportType {
    Gltf = 1,
    Glb = 2,
    Wavefront = 4,
    All = Gltf | Glb | Wavefront
}
