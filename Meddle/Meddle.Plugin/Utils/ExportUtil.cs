using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Models;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using SkiaSharp;

namespace Meddle.Plugin.Utils;

public static class ExportUtil
{
    private static readonly WriteSettings WriteSettings = new WriteSettings
    {
        Validation = ValidationMode.TryFix,
        JsonIndented = false,
    };
    
    public static void OpenExportFolderInExplorer(string path, Configuration config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            throw new ArgumentException("Path is null or does not exist.", nameof(path));
        }

        var notification = cancellationToken.IsCancellationRequested
                               ? new Notification
                               {
                                   Content = $"Export to {path} was cancelled.",
                                   Type = NotificationType.Warning,
                                   RespectUiHidden = false,
                                   ShowIndeterminateIfNoExpiry = true,
                                   Minimized = false
                               }
                               : new Notification
                               {
                                   Content = $"Exported files to {path}",
                                   Type = NotificationType.Success,
                                   RespectUiHidden = false,
                                   ShowIndeterminateIfNoExpiry = true,
                                   Minimized = false
                               };
        
        var activeNotification = Plugin.NotificationManager.AddNotification(notification);

        activeNotification.DrawActions += args => DrawOpenFolderButton();
        
        if (config.OpenFolderOnExport)
        {
            OpenFolder(path);
        }
        return;
        void DrawOpenFolderButton()
        {
            if (ImGui.Button("Open Folder"))
            {
                OpenFolder(path);
                activeNotification.DismissNow();
            }
        }
        
        void OpenFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                throw new ArgumentException("Folder path is null or does not exist.", nameof(folderPath));
            }
            
            var fullPath = Path.GetFullPath(folderPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer",
                Arguments = fullPath,
                UseShellExecute = true
            });
        }
    }
    
    public static void SaveAsType(ModelRoot? gltf, ExportType typeFlags, string path, string name)
    {
        if (typeFlags.HasFlag(ExportType.GLTF))
        {
            gltf?.SaveGLTF(Path.Combine(path, name + ".gltf"), WriteSettings);
        }
        
        if (typeFlags.HasFlag(ExportType.GLB))
        {
            gltf?.SaveGLB(Path.Combine(path, name + ".glb"), WriteSettings);
        }
        
        if (typeFlags.HasFlag(ExportType.OBJ))
        {
            // sanitize obj name
            var objName = name.Replace(" ", "_");
            gltf?.SaveAsWavefront(Path.Combine(path, objName + ".obj"));
        }
    }
    
    public static Vector4 ToVector4(this SKColor color) => new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
    public static SKColor ToSkColor(this Vector4 color)
    {
        var c = color.Clamp(0, 1);
        return new SKColor((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(c.W * 255));
    }
    public static Vector4 Clamp(this Vector4 v, float min, float max)
    {
        return new Vector4(
            Math.Clamp(v.X, min, max),
            Math.Clamp(v.Y, min, max),
            Math.Clamp(v.Z, min, max),
            Math.Clamp(v.W, min, max)
        );
    }
    public static float[] AsFloatArray(this Vector4 v) => new[] { v.X, v.Y, v.Z, v.W };
    public static float[] AsFloatArray(this Vector3 v) => new[] { v.X, v.Y, v.Z };
    public static float[] AsFloatArray(this Vector2 v) => new[] { v.X, v.Y };
}
