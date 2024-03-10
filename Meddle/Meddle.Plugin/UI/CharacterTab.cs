using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Havok;
using ImGuiNET;
using Lumina.Data;
using Meddle.Xande;
using Penumbra.Api;
using SharpGLTF.Transforms;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xande;
using Xande.Enums;
using Xande.Files;
using Xande.Havok;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CSTransform = FFXIVClientStructs.FFXIV.Client.Graphics.Transform;

namespace Meddle.Plugin.UI;

public unsafe class CharacterTab : ITab
{
    public string Name => "Character";

    public int Order => 0;

    private DalamudPluginInterface PluginInterface { get; }
    private IObjectTable ObjectTable { get; }
    private IClientState ClientState { get; }
    private ModelConverter ModelConverter { get; }
    private LuminaManager LuminaManager { get; }
    public HavokConverter HavokConverter { get; }
    
    private Character? SelectedCharacter { get; set; }

    private Task? ExportTask { get; set; }
    private CancellationTokenSource? ExportCts { get; set; }
    
    public CharacterTab(DalamudPluginInterface pluginInterface, IObjectTable objectTable, IClientState clientState, ModelConverter modelConverter, LuminaManager luminaManager, HavokConverter havokConverter)
    {
        PluginInterface = pluginInterface;
        ObjectTable = objectTable;
        ClientState = clientState;
        ModelConverter = modelConverter;
        LuminaManager = luminaManager;
        HavokConverter = havokConverter;
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
            var chara = CharaSelectCharacterList.GetCurrentCharacter();
            if (chara != null)
                objects = new[] { (Character)Activator.CreateInstance(typeof(Character), BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { (object?)(nint)chara }, null)! };
            else
                objects = Array.Empty<Character>();
        }

        if (ClientState.IsGPosing)
        {
            // Within gpose, only show characters that are gpose actors
            objects = objects.Where(x => x.ObjectIndex is >= 201 and < 239);
        }

        if (SelectedCharacter != null && !SelectedCharacter.IsValid())
            SelectedCharacter = null;

        if (SelectedCharacter == null)
            SelectedCharacter = ClientState.LocalPlayer ?? objects.FirstOrDefault();

        ImGui.Text("Select Character");
        using (var combo = ImRaii.Combo("##Character", SelectedCharacter != null ? GetCharacterDisplayText(SelectedCharacter) : "None"))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(GetCharacterDisplayText(character)))
                        SelectedCharacter = character;
                }
            }
        }

        if (SelectedCharacter != null)
            DrawCharacterInfo(SelectedCharacter);
        else
            ImGui.Text("No character selected");
    }

    private void DrawCharacterInfo(Character character)
    {
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;
        var mainPose = GetPose(human->CharacterBase.Skeleton)!;

        //var modelMetas = new List<ModelMeta>();
        //foreach(var modelPtr in human->CharacterBase.ModelsSpan)
        //{
        //    var model = modelPtr.Value;
        //    if (model == null)
        //        continue;
        //    if (model->ModelResourceHandle == null)
        //        continue;
        //    var shapes = model->ModelResourceHandle->Shapes.ToDictionary(kv => MemoryHelper.ReadStringNullTerminated((nint)kv.Item1.Value), kv => kv.Item2);
        //    var attributes = model->ModelResourceHandle->Attributes.ToDictionary(kv => MemoryHelper.ReadStringNullTerminated((nint)kv.Item1.Value), kv => kv.Item2);
        //    modelMetas.Add(new()
        //    {
        //        ModelPath = model->ModelResourceHandle->ResourceHandle.FileName.ToString(),
        //        EnabledShapes = shapes.Where(kv => ((1 << kv.Value) & model->EnabledShapeKeyIndexMask) != 0).Select(kv => kv.Key).ToArray(),
        //        EnabledAttributes = attributes.Where(kv => ((1 << kv.Value) & model->EnabledAttributeIndexMask) != 0).Select(kv => kv.Key).ToArray(),
        //        ShapesMask = model->EnabledShapeKeyIndexMask,
        //        AttributesMask = model->EnabledAttributeIndexMask,
        //    });
        //}
        
        //var weaponInfos = GetWeaponData(character);
        
        using (var d = ImRaii.Disabled(!(ExportTask?.IsCompleted ?? true)))
        {
            if (ImGui.Button("Export"))
            {
                //var tree = GetCharacterResourceTree(character);
                //if (tree == null)
                //    throw new InvalidOperationException("No resource tree found");
                
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ModelConverter.ExportResourceTree(
                    new(charPtr),
                    true,
                    ExportType.Glb,
                    Plugin.TempDirectory,
                    false,
                    ExportCts.Token);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(ModelConverter.GetLastMessage().Split("\n").FirstOrDefault() ?? string.Empty);
        }
        var weaponData = charPtr->DrawData.WeaponDataSpan;

        var t = human->CharacterBase.Skeleton->Transform;
        ImGui.TextUnformatted($"Position: {t.Position:0.00}");
        ImGui.TextUnformatted($"Rotation: {t.Rotation.EulerAngles:0.00}");
        ImGui.TextUnformatted($"Scale: {t.Scale:0.00}");

        //if (ImGui.CollapsingHeader("Model Metas"))
        //{
        //    foreach (var meta in modelMetas)
        //    {
        //        ImGui.TextUnformatted($"{meta.ModelPath}");
        //        ImGui.TextUnformatted($"Shapes ({meta.ShapesMask:X8}): {string.Join(", ", meta.EnabledShapes)}");
        //        ImGui.TextUnformatted($"Attributes ({meta.AttributesMask:X8}): {string.Join(", ", meta.EnabledAttributes)}");
        //        ImGui.Separator();
        //    }
        //}

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
            ImGui.SetClipboardText(JsonSerializer.Serialize(new NewTree(charPtr), new JsonSerializerOptions() { WriteIndented = true, IncludeFields = true }));

        if (ImGui.Button("Copy Model"))
        {
            var model = LuminaManager.GetModel("chara/equipment/e5037/model/c0101e5037_met.mdl");
            var newModel = new NewModel(model, LuminaManager.GameData);
            ImGui.SetClipboardText(JsonSerializer.Serialize(newModel, new JsonSerializerOptions() { WriteIndented = true, IncludeFields = true }));
        }

        if (ImGui.CollapsingHeader("New Tree"))
            DrawNewTree(new(charPtr));
    }
    
    private void DrawNewTree(NewTree tree)
    {
        var l = JsonSerializer.Serialize(tree, new JsonSerializerOptions() { WriteIndented = true, IncludeFields = true });
        ImGui.TextUnformatted(l);
    }

    private HavokXml LoadSkeleton(string path)
    {
        var file = LuminaManager.GetFile<FileResource>(path)
            ?? throw new Exception("GetFile returned null");

        var sklb = SklbFile.FromStream(file.Reader.BaseStream);

        var xml = HavokConverter.HkxToXml(sklb.HkxData);
        var ret = new HavokXml(xml);
        return ret;
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

    private static Dictionary<string, hkQsTransformf>? GetPose(Skeleton* skeleton)
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

                //ret[boneName] = pose->LocalPose[j];
                var model = pose->AccessBoneLocalSpace(j);
                ret[boneName] = *model;
            }
        }

        return ret;
    }
    
    [Obsolete]
    private Ipc.ResourceTree? GetCharacterResourceTree(Character obj)
    {
        return Ipc.GetGameObjectResourceTrees.Subscriber(PluginInterface).Invoke(true, obj.ObjectIndex)[0];
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
    }
}
