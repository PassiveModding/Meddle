using System.Diagnostics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Numerics;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Models.ExportRequest;
using Meddle.Plugin.Utility;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;

namespace Meddle.Plugin.UI;

public partial class CharacterTab
{
    private Task? ExportTask { get; set; }
    private CancellationTokenSource? ExportCts { get; set; }
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
                new ExportLogger(Log),
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

        if (DrawExportButton("Export"))
        {
            exportRequest = new ExportTreeRequest(set);
        }

        ImGui.SameLine();
        var selectedCount = set.EnabledModels.Count(x => x) + set.EnabledAttaches.Count(x => x);
        using (var s = ImRaii.Disabled(selectedCount == 0))
        {
            if (DrawExportButton($"Export {selectedCount} Selected"))
            {
                exportRequest = new ExportTreeRequest(set, true);
            }
        }

        DrawCancelExportButton();

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
        HandleExportRequest(exportRequest ?? modelViewExport ?? attachViewExport, set.Logger, tree);
    }

    private void HandleExportRequest(IExportRequest? request, ExportLogger logger, CharacterTree tree)
    {
        if (request == null)
        {
            return;
        }

        ExportCts?.Cancel();
        ExportCts = new();

        switch (request)
        {
            case ExportAttachRequest ear:
                ExportTask = ExportManager.Export(
                    logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true,
                        ParallelBuild = Configuration.ParallelBuild
                    },
                    ear.Child.Models.ToArray(),
                    [],
                    ear.Child.Skeleton,
                    tree.RaceCode ?? 0,
                    tree.CustomizeParameter,
                    ExportCts.Token);
                break;
            case ExportTreeRequest etr:
                if (etr.ApplySettings)
                {
                    ExportTask = ExportManager.Export(
                        logger,
                        new ExportConfig
                        {
                            ExportType = ExportType.Gltf,
                            IncludeReaperEye = false,
                            OpenFolderWhenComplete = true,
                            ParallelBuild = Configuration.ParallelBuild
                        },
                        etr.Set.SelectedModels,
                        etr.Set.SelectedAttaches,
                        tree.Skeleton,
                        tree.RaceCode ?? 0,
                        tree.CustomizeParameter,
                        ExportCts.Token);
                }
                else
                {
                    ExportTask = ExportManager.Export(
                        logger,
                        new ExportConfig
                        {
                            ExportType = ExportType.Gltf,
                            IncludeReaperEye = false,
                            OpenFolderWhenComplete = true,
                            ParallelBuild = Configuration.ParallelBuild
                        },
                        tree,
                        ExportCts.Token);
                }

                break;
            case ExportModelRequest emr:
                ExportTask = ExportManager.Export(
                    logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true,
                        ParallelBuild = Configuration.ParallelBuild
                    }, [emr.Model], [],
                    emr.SkeletonOverride ?? tree.Skeleton,
                    tree.RaceCode ?? 0,
                    tree.CustomizeParameter, ExportCts.Token);
                break;
            case MaterialExportRequest mer:
                ExportTask = ExportManager.ExportMaterial(
                    mer.Material,
                    logger,
                    tree.CustomizeParameter,
                    ExportCts.Token);
                break;
        }
    }

    private static void DrawCustomizeParameters(CharacterTree tree)
    {
        ImGui.Text("Customize");
        if (tree.CustomizeParameter == null)
        {
            ImGui.Text("No customize parameters");
            return;
        }

        var c = tree.CustomizeParameter;

        var sk3 = new Vector3(c.SkinColor.X, c.SkinColor.Y, c.SkinColor.Z);
        if (DrawColorEdit3("Skin Color", sk3, out var sc3))
        {
            c.SkinColor = new Vector4(sc3.X, sc3.Y, sc3.Z, c.SkinColor.W);
        }

        if (DrawColorEdit4("Lip Color", c.LipColor, out var lc4))
        {
            c.LipColor = lc4;
        }

        ImGui.SameLine();
        ImGui.Checkbox("Apply Lip Color", ref c.ApplyLipColor);

        // info hover for lip
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Lip color does not apply to Hrothgar");
        }

        if (DrawColorEdit3("Hair Color", c.MainColor, out var hc3))
        {
            c.MainColor = hc3;
        }

        if (DrawColorEdit3("Hair Highlight", c.MeshColor, out var hh3))
        {
            c.MeshColor = hh3;
        }

        if (DrawColorEdit4("Left Iris", c.LeftColor, out var ic4))
        {
            c.LeftColor = ic4;
        }

        if (DrawColorEdit3("Race Feature", c.OptionColor, out var oc3))
        {
            c.OptionColor = oc3;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Tattoo Color, Limbal Ring Color, Ear Clasp Color; Varies by race");
        }


        // Not sure about applying separate eye colours yet
        /*
        var irisCol2 = c.RightColor;
        var ic42 = new Vector4(irisCol2.X, irisCol2.Y, irisCol2.Z, irisCol2.W);
        if (ImGui.ColorEdit4("Right Iris", ref ic42))
        {
            c.RightColor = ic42;
        }*/

        tree.CustomizeParameter = c;
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
                var check = set[i];
                if (ImGui.Checkbox($"##{child.GetHashCode()}", ref check))
                {
                    set[i] = check;
                }

                ImGui.SameLine();

                if (ImGui.CollapsingHeader($"Attach at {bone ?? "unknown"}##{child.GetHashCode()}"))
                {
                    ImGui.Text($"Position: {child.Attach.OffsetTransform.Translation}");
                    ImGui.Text($"Rotation: {child.Attach.OffsetTransform.Rotation}");
                    ImGui.Text($"Scale: {child.Attach.OffsetTransform.Scale}");
                    if (DrawExportButton($"Export##{child.GetHashCode()}"))
                    {
                        exportRequest = new ExportAttachRequest(child);
                    }

                    using var mainTable = ImRaii.Table("AttachedChildren", 1, ImGuiTableFlags.Borders);
                    foreach (var model in child.Models)
                    {
                        ImGui.TableNextColumn();
                        DrawModel(model, out var er);
                        if (er != null)
                        {
                            exportRequest = er;
                            if (er is ExportModelRequest emr)
                            {
                                emr.SkeletonOverride = child.Skeleton;
                            }
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
            DrawModel(model, out var er);

            if (er != null)
            {
                exportRequest = er;
            }
        }
    }

    private void DrawModel(Model model, out IExportRequest? exportRequest)
    {
        exportRequest = null!;
        var displayPath = model.ResolvedPath;
        if (displayPath != null && model.HandlePath != model.ResolvedPath)
        {
            displayPath = $"{model.ResolvedPath} -> {model.HandlePath}";
        }
        else
        {
            displayPath = model.HandlePath;
        }

        using var modelNode =
            ImRaii.TreeNode($"{displayPath}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
        if (!modelNode.Success) return;

        if (DrawExportButton($"Export##{model.GetHashCode()}"))
        {
            exportRequest = new ExportModelRequest(model);
        }

        DrawCancelExportButton();

        ImGui.Text($"Handle Path: {model.HandlePath}");
        ImGui.Text($"Resolved Path: {model.ResolvedPath}");

        if (model.Shapes.Count > 0)
        {
            ImGui.TextWrapped($"Shapes: {string.Join(", ", model.Shapes.Select(x => x.Name))}");
            ImGui.TextWrapped($"Enabled Shapes: {string.Join(", ", model.EnabledShapes)}");
        }

        if (model.EnabledAttributes.Count > 0)
        {
            ImGui.TextWrapped($"Enabled Attributes: {string.Join(", ", model.EnabledAttributes)}");
        }

        // Display Materials
        for (var m = 0; m < model.Materials.Count; m++)
        {
            var material = model.Materials[m];
            ImGui.Text($"Material #{m}");
            ImGui.Indent();
            var export = DrawExportButton($"Export Textures##{material.GetHashCode()}");
            DrawCancelExportButton();
            DrawMaterial(material);
            if (export)
            {
                exportRequest = new MaterialExportRequest(material);
            }

            ImGui.Unindent();
            ImGui.Separator();
        }

        for (var m = 0; m < model.Meshes.Count; m++)
        {
            var mesh = model.Meshes[m];
            ImGui.Text($"Mesh #{m}");
            ImGui.Indent();
            DrawMesh(mesh);
            ImGui.Unindent();
            ImGui.Separator();
        }
    }
}
