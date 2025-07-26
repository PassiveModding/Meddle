using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.UI.Layout;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin;

public partial class Configuration
{
    [Obsolete("Use LayoutConfig.WorldCutoffDistance instead")]
    public float WorldCutoffDistance { get; set; } = 100;
    
    [Obsolete("Use LayoutConfig.WorldDotColor instead")]
    public Vector4 WorldDotColor { get; set; } = new(1f, 1f, 1f, 0.5f);
    
    /// <summary>
    /// If enabled, pose will be included at 0 on the timeline under the 'pose' track.
    /// </summary>
    [Obsolete("Use ExportConfig.ExportPose", true)]
    public bool IncludePose { get; set; } = true;

    [Obsolete("Use ExportConfig.TextureMode", true)]
    public TextureMode TextureMode { get; set; } = TextureMode.Bake;
    
    /// <summary>
    /// GLTF = GLTF JSON
    /// GLB = GLTF Binary
    /// OBJ = Wavefront OBJ
    /// </summary>
    [Obsolete("Use ExportConfig.ExportType", true)]
    public ExportType ExportType { get; set; } = DefaultExportType;
    
    public void Migrate()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (Version == 1)
        {
            Plugin.Logger?.LogInformation("Migrating configuration from version 1 to 2");
            if (DisableAutomaticUiHide == false)
            {
                DisableAutomaticUiHide = true;
            }
            
            if (DisableCutsceneUiHide == false)
            {
                DisableCutsceneUiHide = true;
            }
            
            Version = 2;
            Save();
        }
        
        if (Version == 2)
        {
            Plugin.Logger?.LogInformation("Migrating configuration from version 2 to 3");
            
            LayoutConfig.DrawTypes = LayoutWindow.DefaultDrawTypes;
            
            Version = 3;
            Save();
        }

        if (Version == 3)
        {
            Plugin.Logger?.LogInformation("Migrating configuration from version 3 to 4");
            LayoutConfig.WorldDotColor = WorldDotColor;
            LayoutConfig.WorldCutoffDistance = WorldCutoffDistance;
            LayoutConfig.DrawTypes |= ParsedInstanceType.Decal | ParsedInstanceType.EnvLighting;
            
            Version = 4;
            Save();
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
