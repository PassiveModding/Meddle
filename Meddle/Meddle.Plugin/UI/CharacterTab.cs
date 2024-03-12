using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Havok;
using ImGuiNET;
using SharpGLTF.Transforms;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Interface;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CSTransform = FFXIVClientStructs.FFXIV.Client.Graphics.Transform;
using Skeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Plugin.UI;

public unsafe class CharacterTab : ITab
{
    public string Name => "Character";

    public int Order => 0;

    private DalamudPluginInterface PluginInterface { get; }
    private IObjectTable ObjectTable { get; }
    private IClientState ClientState { get; }
    private ModelManager ModelConverter { get; }

    private (IntPtr, CharacterTree)? CharacterTreeCache { get; set; }
    private Character? SelectedCharacter { get; set; }

    private Task? ExportTask { get; set; }
    private CancellationTokenSource? ExportCts { get; set; }
    
    public CharacterTab(DalamudPluginInterface pluginInterface, IObjectTable objectTable, IClientState clientState, ModelManager modelConverter)
    {
        PluginInterface = pluginInterface;
        ObjectTable = objectTable;
        ClientState = clientState;
        ModelConverter = modelConverter;
    }

    public void Draw()
    {
        DrawObjectPicker();
    }

    private void DrawObjectPicker()
    {
        IEnumerable<Character> objects;
        if (ClientState.LocalPlayer != null)
            objects = ObjectTable.OfType<Character>().Where(obj => obj.IsValid() && IsHuman(obj)).OrderBy(c => GetCharacterDistance(c).LengthSquared());
        else
        {
            try
            {
                // Lobby :)
                // TODO: Maybe don't call this until post tick is done
                var chara = CharaSelectCharacterList.GetCurrentCharacter();
                if (chara != null)
                    objects = new[] { (Character)Activator.CreateInstance(typeof(Character), BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { (object?)(nint)chara }, null)! };
                else
                    objects = Array.Empty<Character>();
            }
            catch
            {
                return;
            }

        }

        if (ClientState.IsGPosing)
        {
            // Within gpose, only show characters that are gpose actors
            objects = objects.Where(x => x.ObjectIndex is >= 201 and < 239);
        }

        if (SelectedCharacter != null && !SelectedCharacter.IsValid())
        {
            SelectedCharacter = null;
        }

        if (SelectedCharacter == null)
        {
            SelectedCharacter = ClientState.LocalPlayer ?? objects.FirstOrDefault();
        }

        ImGui.Text("Select Character");
        using (var combo = ImRaii.Combo("##Character", SelectedCharacter != null ? GetCharacterDisplayText(SelectedCharacter) : "None"))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(GetCharacterDisplayText(character)))
                    {
                        SelectedCharacter = character;
                    }
                }
            }
        }

        if (SelectedCharacter != null)
        {
            var address = (CSCharacter*)SelectedCharacter.Address;
            if (CharacterTreeCache == null)
            {
                CharacterTreeCache = new ValueTuple<IntPtr, CharacterTree>((IntPtr)address, new(address));
            }
            else if (CharacterTreeCache!.Value.Item1 != (IntPtr)address)
            {
                CharacterTreeCache = new ValueTuple<IntPtr, CharacterTree>((IntPtr)address, new(address));
            }
            
            DrawCharacterInfo(SelectedCharacter);
        }
        else
        {
            ImGui.Text("No character selected");
            CharacterTreeCache = null;
        }
    }

    private void DrawCharacterInfo(Character character)
    {
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;
        var mainPose = GetPose(human->CharacterBase.Skeleton)!;
        
        using (var d = ImRaii.Disabled(!(ExportTask?.IsCompleted ?? true)))
        {
            if (ImGui.Button("Export"))
            {
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ModelConverter.Export(
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    },
                    new(charPtr),
                    ExportCts.Token);
            }
        }
        var weaponData = charPtr->DrawData.WeaponDataSpan;

        var t = human->CharacterBase.Skeleton->Transform;
        ImGui.TextUnformatted($"Position: {t.Position:0.00}");
        ImGui.TextUnformatted($"Rotation: {t.Rotation.EulerAngles:0.00}");
        ImGui.TextUnformatted($"Scale: {t.Scale:0.00}");

        if (ImGui.CollapsingHeader("Main Pose"))
        {
            foreach (var bone in mainPose)
            {
                var v = new Transform(bone.Value);
                ImGui.TextUnformatted($"{bone.Key} => {v}");
            }
        }

        if (ImGui.CollapsingHeader("Main Pose Names"))
        {
            var sk = human->CharacterBase.Skeleton;
            ImGui.Text($"{(nint)sk:X8}");
            for (var i = 0; i < sk->PartialSkeletonCount; ++i)
            {
                var p = sk->PartialSkeletons[i].GetHavokPose(0);
                if (p != null && p->Skeleton != null)
                {
                    for (var j = 0; j < p->Skeleton->Bones.Length; ++j)
                        ImGui.TextUnformatted($"[{i:X}, {j:X}] => {p->Skeleton->Bones[j].Name.String ?? $"Bone {j}"}");
                }
                ImGui.Separator();
            }
        }
        
        if (ImGui.CollapsingHeader("Main Hand"))
            DrawWeaponData(weaponData[0]);

        if (ImGui.CollapsingHeader("Off Hand"))
            DrawWeaponData(weaponData[1]);
        
        if (ImGui.CollapsingHeader("Prop"))
            DrawWeaponData(weaponData[2]);

        if (ImGui.Button("Copy"))
            ImGui.SetClipboardText(JsonSerializer.Serialize(new CharacterTree(charPtr), new JsonSerializerOptions() { WriteIndented = true, IncludeFields = true }));

        if (ImGui.CollapsingHeader("Models") && CharacterTreeCache != null)
        {
            DrawCharacterTree(CharacterTreeCache.Value.Item2!);
        }
    }

    private void DrawCharacterTree(CharacterTree tree)
{
    
        using var mainTable = ImRaii.Table("Models", 1, ImGuiTableFlags.Borders);
        foreach (var model in tree.Models)
        {

            ImGui.TableNextColumn();
            using var modelNode = ImRaii.TreeNode($"{model.HandlePath}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
            if (modelNode.Success)
            {
                // Export icon
                if (ImGui.SmallButton($"Export##{model.GetHashCode()}"))
                {
                    ExportCts?.Cancel();
                    ExportCts = new();
                    ExportTask = ModelConverter.Export(new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    }, model, tree.Skeleton, tree.RaceCode!.Value, ExportCts.Token);
                }
                
                if (model.Shapes.Count > 0)
                {
                    ImGui.Text($"Shapes: {string.Join(", ", model.Shapes.Select(x => x.Name))}");
                    ImGui.Text($"Enabled Shapes: {string.Join(", ", model.EnabledShapes)}");
                }

                if (model.EnabledAttributes.Length > 0)
                {
                    ImGui.Text($"Enabled Attributes: {string.Join(", ", model.EnabledAttributes)}");
                }

                // Display Materials
                using (var table = ImRaii.Table("MaterialsTable", 2,
                                                ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
                {
                    ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthFixed,
                                           0.75f * ImGui.GetWindowWidth());
                    ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);

                    foreach (var material in model.Materials)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"{material.HandlePath}");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(material.HandlePath);
                        }
                        ImGui.TableNextColumn();
                        ImGui.Text($"Shader: {material.ShaderPackage.Name} Textures: {material.Textures.Count}");
                        ImGui.Indent();
                        // Display Material Textures in the same table
                        foreach (var texture in material.Textures)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text($"{texture.HandlePath}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{texture.Usage}");
                        }

                        ImGui.Unindent();
                    }
                }


                ImGui.Spacing();

                // Display Meshes in a single table
                using var tableMeshes = ImRaii.Table("MeshesTable", 3,
                                                     ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
                ImGui.TableSetupColumn("Mesh", ImGuiTableColumnFlags.WidthFixed, 0.5f * ImGui.GetWindowWidth());
                ImGui.TableSetupColumn("Vertices", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Indices", ImGuiTableColumnFlags.WidthStretch);

                for (var i = 0; i < model.Meshes.Count; ++i)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    ImGui.Text($"Mesh {i}");
                    var mesh = model.Meshes[i];
                    ImGui.TableNextColumn();
                    ImGui.Text($"Vertices: {mesh.Vertices.Count}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"Indices: {mesh.Indices.Count}");
                    for (var j = 0; j < mesh.Submeshes.Count; j++)
                    {
                        var submesh = mesh.Submeshes[j];
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"[{j}] Submesh attributes: {string.Join(", ", submesh.Attributes)}");
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn(); // Leave an empty column for spacing
                    }
                }
            }
        }
    }

    private void DrawWeaponData(DrawObjectData data)
    {
        var skeleton = GetWeaponSkeleton(data);
        var pose = GetPose(skeleton);
        
        if (pose != null)
        {
            ImGui.TextUnformatted($"{new Transform(data.Model->CharacterBase.DrawObject.Object.Transform)} {(nint)data.Model:X8}");
            ImGui.TextUnformatted($"{new Transform(data.Model->CharacterBase.Skeleton->Transform)}");
            {
                var attach = &data.Model->CharacterBase.Attach;
                var skele = attach->OwnerSkeleton;
                var attachments = attach->SkeletonBoneAttachments;
                for (var i = 0; i < attach->AttachmentCount; ++i)
                {
                    var attachment = &attachments[i];
                    ImGui.TextUnformatted($"[{attachment->SkeletonIdx:X}, {attachment->BoneIdx:X}] " +
                        $"{skele->PartialSkeletonsSpan[attachment->SkeletonIdx].GetHavokPose(0)->Skeleton->Bones[attachment->BoneIdx].Name.String ?? "Unknown"} => " +
                        $"{new Transform(attachment->ChildTransform)}");
                    var poseMatrix = GetMatrix(*skele->PartialSkeletonsSpan[attachment->SkeletonIdx].GetHavokPose(0)->CalculateBoneModelSpace(attachment->BoneIdx));
                    var offsetMatrix = GetMatrix(attachment->ChildTransform);
                    ImGui.TextUnformatted($"{new Transform(offsetMatrix * poseMatrix * GetMatrix(skele->Transform))}");
                }
            }
            foreach (var bone in pose)
            {
                var v = new Transform(bone.Value);
                ImGui.TextUnformatted($"{bone.Key} => {v.Translation:0.00} {new EulerAngles(v.Rotation).Angles:0.00} {v.Scale:0.00}");
            }
        }
        else
            ImGui.TextUnformatted("No pose");
    }

    private static Skeleton* GetWeaponSkeleton(DrawObjectData data)
    {
        if (data.Model == null)
            return null;

        return data.Model->CharacterBase.Skeleton;
    }

    private static Vector3 AsVector(hkVector4f hkVector) =>
        new(hkVector.X, hkVector.Y, hkVector.Z);
    
    private static Vector4 AsVector(hkQuaternionf hkVector) =>
        new(hkVector.X, hkVector.Y, hkVector.Z, hkVector.W);
    
    private static Quaternion AsQuaternion(hkQuaternionf hkQuaternion) =>
        new(hkQuaternion.X, hkQuaternion.Y, hkQuaternion.Z, hkQuaternion.W);

    private static Matrix4x4 GetMatrix(CSTransform transform) =>
        new Transform(transform).AffineTransform.Matrix;

    private static unsafe Matrix4x4 GetMatrix(hkQsTransformf transform)
    {
        var transformPtr = (hkQsTransformf*)NativeMemory.AlignedAlloc((nuint)sizeof(hkQsTransformf), 32);
        var matrixPtr = (Matrix4x4*)NativeMemory.AlignedAlloc((nuint)sizeof(Matrix4x4), 32);
        *transformPtr = transform;
        *matrixPtr = new();
        transformPtr->get4x4ColumnMajor((float*)matrixPtr);
        var ret = *matrixPtr;
        NativeMemory.AlignedFree(transformPtr);
        NativeMemory.AlignedFree(matrixPtr);
        return ret;
    }

    private static AffineTransform AsAffineTransform(hkQsTransformf hkTransform)
    {
        return new(GetMatrix(hkTransform));
    }

    private Dictionary<string, hkQsTransformf>? GetPose(Skeleton* skeleton)
    {
        if (skeleton == null)
            return null;
        
        var ret = new Dictionary<string, hkQsTransformf>();
        
        for(var i = 0; i < skeleton->PartialSkeletonCount; ++i)
        {
            var partial = skeleton->PartialSkeletons[i];
            var pose = partial.GetHavokPose(0);
            if (pose == null)
                continue;

            var partialSkele = pose->Skeleton;
            
            for (var j = 0; j < partialSkele->Bones.Length; ++j)
            {
                if (j == partial.ConnectedBoneIndex)
                    continue;

                var boneName = pose->Skeleton->Bones[j].Name.String;
                if (string.IsNullOrEmpty(boneName))
                   continue;
                
                if (ClientState.IsGPosing)
                {
                    var model = pose->AccessBoneLocalSpace(j);
                    ret[boneName] = *model;
                }
                else
                {
                    ret[boneName] = pose->LocalPose[j];
                }
            }
        }

        return ret;
    }

    private Vector3 GetCharacterDistance(Character obj) {
        if (ClientState.LocalPlayer is { Position: var charPos })
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    private string GetCharacterDisplayText(Character obj) =>
        $"{obj.Address:X8}:{obj.ObjectId:X} - {obj.ObjectKind} - {(string.IsNullOrWhiteSpace(obj.Name.TextValue) ? "Unnamed" : obj.Name.TextValue)} - {GetCharacterDistance(obj).Length():0.00}y";

    private static bool IsHuman(Character obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;
        if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
            return false;
        return true;
    }

    public void Dispose()
    {
        ExportCts?.Cancel();
        ExportCts?.Dispose();
    }
}