using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Meddle.Formats.Files;
using Meddle.Formats.Helpers;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Helpers;
using SharpGLTF.Transforms;
using SkiaSharp;

namespace Meddle.Plugin.UI.Debug;

public class FileExportDebugTab : ITab
{
    private readonly Configuration config;
    private readonly INotificationManager notificationManager;
    private readonly SqPack.SqPack sqPack;
    private readonly ComposerFactory composerFactory;
    private readonly ITextureProvider textureProvider;
    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };
    private CancellationTokenSource? cancellationTokenSource;
    private Task? exportTask;
    private string exportPathInput = "";

    public FileExportDebugTab(Configuration config, INotificationManager notificationManager, SqPack.SqPack sqPack,
                              ComposerFactory composerFactory, ITextureProvider textureProvider)
    {
        this.config = config;
        this.notificationManager = notificationManager;
        this.sqPack = sqPack;
        this.composerFactory = composerFactory;
        this.textureProvider = textureProvider;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "File Export";
    public int Order => 20;
    public MenuType MenuType => MenuType.Debug;

    public void Draw()
    {
        fileDialog.Draw();

        using var indent = ImRaii.PushIndent();
        ImGui.Text("Export Path");
        ImGui.SameLine();
        ImGui.InputText("##ExportPath", ref exportPathInput, 100);
        if (ImGui.Button("Export"))
        {
            cancellationTokenSource = new CancellationTokenSource();
            var pathFileName = Path.GetFileNameWithoutExtension(exportPathInput);
            var defaultName = $"Export-{pathFileName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
            fileDialog.SaveFolderDialog("Save File", defaultName,
                            (result, exportPath) =>
                            {
                                if (!result) return;
                                var data = sqPack.GetFileOrReadFromDisk(exportPathInput);
                                if (data == null)
                                {
                                    notificationManager.AddNotification(new Notification
                                    {
                                        Content = $"File not found: {exportPathInput}",
                                        Type = NotificationType.Error
                                    });
                                    return;
                                }

                                var outPath = Path.Combine(exportPath, Path.GetFileName(exportPathInput));
                                Directory.CreateDirectory(exportPath);
                                File.WriteAllBytes(outPath, data);
                                ExportUtil.OpenExportFolderInExplorer(exportPath, config, cancellationTokenSource.Token);
                            }, config.ExportDirectory);
        }

        using (var disabled = ImRaii.Disabled(exportTask is {IsCompleted: false} || !exportPathInput.EndsWith(".mdl")))
        {
            if (ImGui.Button("Export Model"))
            {
                cancellationTokenSource = new CancellationTokenSource();
                var configClone = config.ExportConfig.Clone();
                var pathFileName = Path.GetFileNameWithoutExtension(exportPathInput);
                var defaultName = $"Export-{pathFileName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                var stubInstance = new ParsedBgPartsInstance(0, true, new Transform(AffineTransform.Identity), exportPathInput, null);
                fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                            (result, exportPath) =>
                                            {
                                                if (!result) return;
                                                exportTask = Task.Run(() =>
                                                {
                                                    var composer = composerFactory.CreateComposer(exportPath,
                                                                                                  configClone,
                                                                                                  cancellationTokenSource.Token);
                                                    composer.Compose([stubInstance], new ProgressWrapper());
                                                    ExportUtil.OpenExportFolderInExplorer(exportPath, config, cancellationTokenSource.Token);
                                                }, cancellationTokenSource.Token);
                                            }, config.ExportDirectory);
            }
        }

        using (var disabled = ImRaii.Disabled(exportTask is {IsCompleted: false} || !exportPathInput.EndsWith(".tex")))
        {
            if (ImGui.Button("Export Texture"))
            {
                cancellationTokenSource = new CancellationTokenSource();
                var configClone = config.ExportConfig.Clone();
                var pathFileName = Path.GetFileNameWithoutExtension(exportPathInput);
                var defaultName = $"Export-{pathFileName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                fileDialog.SaveFolderDialog("Save Texture", defaultName,
                                            (result, exportPath) =>
                                            {
                                                if (!result) return;
                                                exportTask = Task.Run(() =>
                                                {
                                                    var file = sqPack.GetFileOrReadFromDisk(exportPathInput);
                                                    if (file == null)
                                                    {
                                                        notificationManager.AddNotification(new Notification
                                                        {
                                                            Content = $"File not found: {exportPathInput}",
                                                            Type = NotificationType.Error
                                                        });
                                                        return;
                                                    }

                                                    var outPath = Path.Combine(exportPath, Path.GetFileName(exportPathInput));

                                                    Directory.CreateDirectory(exportPath);
                                                    File.WriteAllBytes(outPath, file);

                                                    // Convert to png
                                                    var tex = new TexFile(file);
                                                    var texture = tex.ToResource().ToTexture();
                                                    using var memoryStream = new MemoryStream();
                                                    using (var bitmap = texture.Bitmap)
                                                    {
                                                        bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
                                                    }
                                                    var textureBytes = memoryStream.ToArray();
                                                    File.WriteAllBytes(Path.ChangeExtension(outPath, ".png"), textureBytes);
                                                    ExportUtil.OpenExportFolderInExplorer(exportPath, config, cancellationTokenSource.Token);
                                                }, cancellationTokenSource.Token);
                                            }, config.ExportDirectory);
            }
        }

        if (exportPathInput.EndsWith(".tex"))
        {
            // draw the texture
            var availableWidth = ImGui.GetContentRegionAvail().X;

            var tex = textureProvider.GetFromGame(exportPathInput);
            var wrap = tex.GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, new Vector2(availableWidth, availableWidth * wrap.Height / wrap.Width));
        }
    }
}
