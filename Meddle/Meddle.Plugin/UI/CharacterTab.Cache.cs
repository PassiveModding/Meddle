using System.Diagnostics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.ExportRequest;
using Meddle.Plugin.Utility;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;

namespace Meddle.Plugin.UI;

public partial class CharacterTab
{
    private CharacterTreeSet? CharacterTreeCache { get; set; }

    private unsafe CharacterTreeSet InitTree(Character character, bool refresh)
    {
        if (CharacterTreeCache != null)
        {
            if (character != CharacterTreeCache.Character ||
                character.ObjectId != CharacterTreeCache.Character.ObjectId)
            {
                CharacterTreeCache = null;
            }
        }

        if (refresh)
        {
            CharacterTreeCache = null;
        }

        if (CharacterTreeCache == null)
        {
            var address = (CSCharacter*)character.Address;
            var tree = new CharacterTree(address);
            CharacterTreeCache = new CharacterTreeSet(
                character,
                tree,
                new ExportLogger(log),
                DateTime.Now,
                new bool[tree.Models.Count],
                new bool[tree.AttachedChildren.Count]);
        }

        return CharacterTreeCache;
    }

    private void DrawCharacterTree(Character character)
    {
        var tree = InitTree(character, false);
        using (var d = ImRaii.Disabled(ExportManager.IsExporting))
        {
            if (ImGui.Button("Refresh") && !ExportManager.IsExporting)
            {
                tree = InitTree(character, true);
            }
        }

        DrawCharacterTree(tree);
    }

    private void DrawCharacterTree(CharacterTreeSet set)
    {
        ImGui.Text($"At: {set.Time}");
        var tree = set.Tree;

        IExportRequest? exportRequest = null;

        if (DrawExportButton("Export", ExportManager.IsExporting))
        {
            exportRequest = new ExportTreeRequest(tree);
        }

        ImGui.SameLine();
        var selectedCount = set.EnabledModels.Count(x => x) + set.EnabledAttaches.Count(x => x);
        using (var s = ImRaii.Disabled(selectedCount == 0))
        {
            if (DrawExportButton($"Export {selectedCount} Selected", ExportManager.IsExporting))
            {
                exportRequest = new ExportPartialTreeRequest(tree, set.SelectedModels, set.SelectedAttaches);
            }
        }

        DrawCancelExportButton(ExportManager.IsExporting, ExportManager.CancellationTokenSource);

        ImGui.SameLine();
        if (ImGui.Button("Open export folder"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }

        DrawLogMessage(set.Logger);
        if (ImGui.CollapsingHeader("Customize Parameters"))
        {
            DrawCustomizeParameters(tree);
        }

        var enabledModels = set.EnabledModels;
        var enabledAttaches = set.EnabledAttaches;
        DrawModelView(tree, ref enabledModels, out var modelViewExport);
        DrawAttachView(tree, ref enabledAttaches, out var attachViewExport);
        HandleExportRequest(exportRequest ?? modelViewExport ?? attachViewExport, set.Logger,
                            ExportManager,
                            configuration);
    }

    private void DrawAttachView(CharacterTree tree, ref bool[] set, out IExportRequest? exportRequest)
    {
        exportRequest = null;
        if (tree.AttachedChildren.Count > 0)
        {
            ImGui.Text("Attached Children");
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Attached children are typically weapons, fashion accessories, etc.");
            }

            using var attTable = ImRaii.Table("AttachedChildren", 1, ImGuiTableFlags.Borders);
            for (var i = 0; i < tree.AttachedChildren.Count; i++)
            {
                var child = tree.AttachedChildren[i];
                var skeleton = tree.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx];
                var bone = skeleton.HkSkeleton!.BoneNames[child.Attach.BoneIdx];

                ImGui.TableNextColumn();

                // other execute types are a lil broken still
                if (child.Attach.ExecuteType == 4)
                {
                    var check = set[i];
                    if (ImGui.Checkbox($"##{child.GetHashCode()}", ref check))
                    {
                        set[i] = check;
                    }

                    ImGui.SameLine();
                }

                if (ImGui.CollapsingHeader($"Attach at {bone ?? "unknown"}##{child.GetHashCode()}"))
                {
                    if (child.Attach.OffsetTransform is { } ct)
                    {
                        ImGui.Text($"Position: {ct.Translation}");
                        ImGui.Text($"Rotation: {ct.Rotation}");
                        ImGui.Text($"Scale: {ct.Scale}");
                    }

                    ImGui.Text($"Execute Type: {child.Attach.ExecuteType}");
                    if (DrawExportButton($"Export##{child.GetHashCode()}", ExportManager.IsExporting))
                    {
                        exportRequest = new ExportAttachRequest(child);
                    }

                    using var mainTable = ImRaii.Table("AttachedChildren", 1, ImGuiTableFlags.Borders);
                    foreach (var model in child.Models)
                    {
                        ImGui.TableNextColumn();
                        DrawModel(model, ExportManager.IsExporting, ExportManager.CancellationTokenSource, out var me,
                                  out var er);
                        if (me != null)
                        {
                            exportRequest = new MaterialExportRequest(me);
                        }

                        if (er != null)
                        {
                            exportRequest = new ExportModelRequest(er, child.Skeleton);
                        }
                    }
                }
            }
        }
    }

    private void DrawModelView(CharacterTree tree, ref bool[] set, out IExportRequest? exportRequest)
    {
        exportRequest = null!;
        if (tree.Models.Count == 0)
        {
            ImGui.Text("No models found. Try refreshing.");
            return;
        }

        ImGui.Text("Models");
        using var mainTable = ImRaii.Table("Models", 1, ImGuiTableFlags.Borders);
        for (var i = 0; i < tree.Models.Count; i++)
        {
            var model = tree.Models[i];
            var check = set[i];
            ImGui.TableNextColumn();
            if (ImGui.Checkbox($"##{model.GetHashCode()}", ref check))
            {
                set[i] = check;
            }

            ImGui.SameLine();
            DrawModel(model, ExportManager.IsExporting, ExportManager.CancellationTokenSource, out var me, out var er);

            if (me != null)
            {
                exportRequest = new MaterialExportRequest(me, tree.CustomizeParameter);
            }

            if (er != null)
            {
                exportRequest = new ExportModelRequest(er, tree.Skeleton);
            }
        }
    }
}
