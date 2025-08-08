using System.Diagnostics;
using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Services;

public class AnimationExportService : IDisposable, IService
{
    private readonly ILogger<AnimationExportService> logger;
    private readonly Configuration config;

    public AnimationExportService(ILogger<AnimationExportService> logger, Configuration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing ExportUtil");
    }

    public void ExportAnimation(
        List<(DateTime, AttachSet[])> frames,
        bool includePositionalData,
        string path,
        CancellationToken token = default)
    {
        try
        {
            var boneSets = SkeletonUtils.GetAnimatedBoneMap(frames.ToArray());
            var startTime = frames.Min(x => x.Item1);
            //var folder = GetPathForOutput();
            var folder = path;
            Directory.CreateDirectory(folder);
            foreach (var (id, (bones, root, timeline)) in boneSets)
            {
                var scene = new SceneBuilder();
                if (root == null) throw new InvalidOperationException("Root bone not found");
                logger.LogInformation("Adding bone set {Id}", id);
                var rootNode = new NodeBuilder(id);
                rootNode.AddNode(root);

                if (includePositionalData)
                {
                    var startPos = timeline.First().Attach.Transform.Translation;
                    foreach (var frameTime in timeline)
                    {
                        if (token.IsCancellationRequested) return;
                        var pos = frameTime.Attach.Transform.Translation;
                        var rot = frameTime.Attach.Transform.Rotation;
                        var scale = frameTime.Attach.Transform.Scale;
                        var time = SkeletonUtils.TotalSeconds(frameTime.Time, startTime);
                        root.UseTranslation().UseTrackBuilder("pose").WithPoint(time, pos - startPos);
                        root.UseRotation().UseTrackBuilder("pose").WithPoint(time, rot);
                        root.UseScale().UseTrackBuilder("pose").WithPoint(time, scale);
                    }
                }

                scene.AddNode(rootNode);
                scene.AddSkinnedMesh(GetDummyMesh(), Matrix4x4.Identity, bones.Cast<NodeBuilder>().ToArray());
                var sceneGraph = scene.ToGltf2();
                var outputPath = Path.Combine(folder, $"motion_{id}.gltf");
                sceneGraph.SaveGLTF(outputPath);
            }

            if (config.OpenFolderOnExport)
            {
                Process.Start("explorer.exe", folder);
            }
            logger.LogInformation("Export complete");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to export animation");
            throw;
        }
    }

    // https://github.com/0ceal0t/Dalamud-VFXEditor/blob/be00131b93b3c6dd4014a4f27c2661093daf3a85/VFXEditor/Utils/Gltf/GltfSkeleton.cs#L132
    public static MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4> GetDummyMesh(string name = "DUMMY_MESH")
    {
        var dummyMesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4>(name);
        var material = new MaterialBuilder("material");

        var p1 = new VertexPosition
        {
            Position = new Vector3(0.000001f, 0, 0)
        };
        var p2 = new VertexPosition
        {
            Position = new Vector3(0, 0.000001f, 0)
        };
        var p3 = new VertexPosition
        {
            Position = new Vector3(0, 0, 0.000001f)
        };

        dummyMesh.UsePrimitive(material).AddTriangle(
            (p1, new VertexEmpty(), new VertexJoints4(0)),
            (p2, new VertexEmpty(), new VertexJoints4(0)),
            (p3, new VertexEmpty(), new VertexJoints4(0))
        );

        return dummyMesh;
    }
}
