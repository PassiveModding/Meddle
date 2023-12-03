using Dalamud.Interface.Utility.Raii;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok;
using ImGuiNET;
using Meddle.Xande;
using Penumbra.Api;
using SharpGLTF.Transforms;
using System.Numerics;
using System.Runtime.InteropServices;
using Xande.Enums;
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
    
    private Character? selectedCharacter;
    private Character? SelectedCharacter
    {
        get => selectedCharacter;
        set
        {
            if (selectedCharacter == value)
                return;
            selectedCharacter = value;
            if (selectedCharacter == null)
                ResourceTree = null;
            else
                ResourceTree = Ipc.GetGameObjectResourceTrees.Subscriber(PluginInterface).Invoke(true, selectedCharacter.ObjectIndex)[0];
        }
    }
    private Ipc.ResourceTree? ResourceTree { get; set; }

    private Task? ExportTask { get; set; }
    private CancellationTokenSource? ExportCts { get; set; }

    public CharacterTab(DalamudPluginInterface pluginInterface, IObjectTable objectTable, IClientState clientState, ModelConverter modelConverter)
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
        var objects = ObjectTable.OfType<Character>().Where(obj => obj.IsValid() && IsHuman(obj)).OrderBy(c => GetCharacterDistance(c).LengthSquared());

        if (SelectedCharacter != null && !SelectedCharacter.IsValid())
            SelectedCharacter = null;

        if (SelectedCharacter == null && ClientState.LocalPlayer != null)
            SelectedCharacter = ClientState.LocalPlayer;

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

        var weaponInfos = GetWeaponData(character);

        using (var d = ImRaii.Disabled(ResourceTree == null))
        {
            if (ImGui.Button("Export"))
            {
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ModelConverter.ExportResourceTree(
                    ResourceTree!,
                    Enumerable.Repeat(true, ResourceTree!.Nodes.Count).ToArray(),
                    true,
                    ExportType.Glb,
                    Plugin.TempDirectory,
                    true,
                    mainPose.ToDictionary(kv => kv.Key, kv => AsAffineTransform(kv.Value)),
                    weaponInfos,
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
    }

    private void DrawWeaponData(DrawObjectData data)
    {
        var skeleton = GetWeaponSkeleton(data);
        var pose = GetPose(skeleton);
        
        if (pose != null)
        {
            ImGui.TextUnformatted($"{new Transform(data.Model->CharacterBase.DrawObject.Object.Transform)} {(nint)data.Model:X8}");
            ImGui.TextUnformatted($"{new Transform(data.Model->CharacterBase.Skeleton->Transform)}");
            ImGui.TextUnformatted($"{new Transform(data.Model->CharacterBase.UnkTransform)}");
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
    
    private List<HkSkeleton.WeaponData> GetWeaponData(Character character)
    {
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;

        var ret = new List<HkSkeleton.WeaponData>();
        
        if (ResourceTree == null)
            return ret;

        foreach(var weaponData in charPtr->DrawData.WeaponDataSpan)
        {
            if (weaponData.Model == null)
                continue;
            var attach = &weaponData.Model->CharacterBase.Attach;
            if (attach->ExecuteType != 4)
                PluginLog.Error($"Unsupported executetype {attach->ExecuteType}");
            var att = attach->SkeletonBoneAttachments[0];

            var poseMatrix = GetMatrix(*attach->OwnerSkeleton->PartialSkeletonsSpan[att.SkeletonIdx].GetHavokPose(0)->CalculateBoneModelSpace(att.BoneIdx));
            var offsetMatrix = GetMatrix(att.ChildTransform);
            HkSkeleton.WeaponData data = new()
            {
                SklbPath = weaponData.Model->CharacterBase.Skeleton->SkeletonResourceHandles[0]->ResourceHandle.FileName.ToString(),
                ModelPath = weaponData.Model->CharacterBase.Models[0]->ModelResourceHandle->ResourceHandle.FileName.ToString(),
                BoneName = attach->OwnerSkeleton->PartialSkeletonsSpan[att.SkeletonIdx].GetHavokPose(0)->Skeleton->Bones[att.BoneIdx].Name.String!,
                BoneLookup = GetPose(weaponData.Model->CharacterBase.Skeleton)!.ToDictionary(k => k.Key, v => AsAffineTransform(v.Value)),
                AttachOffset = new(offsetMatrix),
                PoseOffset = new(poseMatrix),
                OwnerOffset = new(GetMatrix(attach->OwnerSkeleton->Transform)),
            };
            ret.Add(data);
        }

        return ret;
    }

    private static Skeleton* GetWeaponSkeleton(DrawObjectData data)
    {
        if (data.Model == null)
            return null;

        // Only true for gpose characters, it seems
        //if ((data.Model->Flags & 0x9) == 0)
        //    return null;

        return data.Model->CharacterBase.Skeleton;
    }

    private static Vector3 AsVector(hkVector4f hkVector) =>
        new(hkVector.X, hkVector.Y, hkVector.Z);

    private static Vector4 AsVector(hkQuaternionf hkVector) =>
        new(hkVector.X, hkVector.Y, hkVector.Z, hkVector.W);

    private static Quaternion AsQuaternion(hkQuaternionf hkQuaternion) =>
        new(hkQuaternion.X, hkQuaternion.Y, hkQuaternion.Z, hkQuaternion.W);

    private readonly record struct EulerAngles
    {
        public Vector3 Angles { get; init; }

        // https://stackoverflow.com/a/70462919
        public EulerAngles(Quaternion q)
        {
            Vector3 angles = new();

            // roll / x
            double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            angles.X = (float)Math.Atan2(sinr_cosp, cosr_cosp);

            // pitch / y
            double sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (Math.Abs(sinp) >= 1)
            {
                angles.Y = (float)Math.CopySign(Math.PI / 2, sinp);
            }
            else
            {
                angles.Y = (float)Math.Asin(sinp);
            }

            // yaw / z
            double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            angles.Z = (float)Math.Atan2(siny_cosp, cosy_cosp);

            Angles = angles * 180 / MathF.PI;
        }

        public static implicit operator Vector3(EulerAngles e) => e.Angles;

        public override string ToString()
        {
            return Angles.ToString();
        }

        public readonly string ToString(string? format)
        {
            return Angles.ToString(format);
        }

        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            return Angles.ToString(format, formatProvider);
        }
    }

    private readonly record struct Transform
    {
        public Vector3 Translation { get; init; }
        public Quaternion Rotation { get; init; }
        public Vector3 Scale { get; init; }

        public AffineTransform AffineTransform => new(Scale, Rotation, Translation);

        public Transform(hkQsTransformf hkTransform)
        {
            Translation = AsVector(hkTransform.Translation);
            Rotation = AsQuaternion(hkTransform.Rotation);
            Scale = AsVector(hkTransform.Scale);
        }

        public Transform(CSTransform transform)
        {
            Translation = transform.Position;
            Rotation = transform.Rotation;
            Scale = transform.Scale;
        }

        public Transform(AffineTransform transform)
        {
            transform = transform.GetDecomposed();
            Translation = transform.Translation;
            Rotation = transform.Rotation;
            Scale = transform.Scale;
        }

        public Transform(Matrix4x4 transform) : this(new AffineTransform(transform))
        {

        }

        public override string ToString()
        {
            return $"{Translation:0.00} {new EulerAngles(Rotation).Angles:0.00} {Scale:0.00}";
        }
    }

    private static Matrix4x4 GetMatrix(CSTransform transform) =>
        new Transform(transform).AffineTransform.Matrix;

    private unsafe static Matrix4x4 GetMatrix(hkQsTransformf transform)
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

                ret[boneName] = pose->LocalPose[j];
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
    }
}
