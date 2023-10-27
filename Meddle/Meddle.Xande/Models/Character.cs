using Dalamud.Plugin;
using Meddle.Xande.Utility;
using Penumbra.Api.Enums;
using Xande.Enums;

namespace Meddle.Xande.Models;

public class Character
{
    public Dictionary<string, Model> Models { get; set; } = new();
    public GenderRace GenderRace { get; set; } = GenderRace.Unknown;
    public Dictionary<string, Skeleton> Skeletons { get; set; } = new();
    public ushort SelectedObjectObjectIndex { get; set; }

    public class Texture
    {
        public string ActivePath { get; set; }
        public string GamePath { get; set; }
    }

    public class Shaderpack
    {
        public string ActivePath { get; set; }
    }

    public class Material
    {
        public string ActivePath { get; set; }
        public string GamePath { get; set; }
        public List<Texture> Textures { get; set; }
        public List<Shaderpack> Shaderpacks { get; set; }
    }

    public class Model
    {
        public string Path { get; set; }
        public Dictionary<string, Material> Materials { get; set; }
    }

    public class Skeleton
    {
        public string ActivePath { get; set; }
        public Dictionary<string, SkeletonParameters> SkeletonParameters { get; set; }
    }

    public class SkeletonParameters
    {
        public string ActivePath { get; set; }
    }


    public void AddSkeleton(string skeletonPath)
    {
        if (!Skeletons.ContainsKey(skeletonPath))
        {
            Skeletons[skeletonPath] = new Skeleton
            {
                ActivePath = skeletonPath,
                SkeletonParameters = new Dictionary<string, SkeletonParameters>()
            };
        }
    }

    public void AddSkeletonParameters(string skeletonPath, string skeletonParametersPath)
    {
        if (Skeletons.TryGetValue(skeletonPath, out var skeleton) &&
            !skeleton.SkeletonParameters.ContainsKey(skeletonParametersPath))
        {
            Skeletons[skeletonPath].SkeletonParameters[skeletonParametersPath] = new SkeletonParameters
            {
                ActivePath = skeletonParametersPath
            };
        }
    }

    public void AddModel(string modelPath)
    {
        if (!Models.ContainsKey(modelPath))
        {
            Models[modelPath] = new Model
            {
                Path = modelPath,
                Materials = new Dictionary<string, Material>()
            };
        }
    }

    public void AddMaterial(string modelPath, string materialPath, string luminaPath)
    {
        if (Models.TryGetValue(modelPath, out var model) && !model.Materials.ContainsKey(materialPath))
        {
            Models[modelPath].Materials[materialPath] = new Material
            {
                ActivePath = materialPath,
                GamePath = luminaPath,
                Textures = new List<Texture>(),
                Shaderpacks = new List<Shaderpack>()
            };
        }
    }

    public void AddTexture(string modelPath, string materialPath, string texturePath, string luminaPath)
    {
        if (Models.TryGetValue(modelPath, out var model) && model.Materials.TryGetValue(materialPath, out var material))
        {
            material.Textures.Add(new Texture
            {
                ActivePath = texturePath,
                GamePath = luminaPath
            });
        }
    }

    public void AddShaderpack(string modelPath, string materialPath, string shaderpackPath)
    {
        if (Models.TryGetValue(modelPath, out var model) && model.Materials.TryGetValue(materialPath, out var material))
        {
            material.Shaderpacks.Add(new Shaderpack
            {
                ActivePath = shaderpackPath
            });
        }
    }

    public ResourceTree AsResourceTree(string name, DalamudPluginInterface pluginInterface)
    {
        // Create a new ResourceTree object with the given name and GenderRace.
        var resourceTree = new ResourceTree(name, GenderRace);

        // Get an IPC subscriber for a specific IPC message.
        var ipcSubscriber =
            pluginInterface.GetIpcSubscriber<ResourceType, bool, ushort[],
                IReadOnlyDictionary<long, (string, string, ChangedItemIcon)>?[]>(
                "Penumbra.GetGameObjectResourcesOfType");

        // Invoke the IPC message to get model metadata.
        var modelMeta = ipcSubscriber.InvokeFunc(ResourceType.Mdl, true, new[] {SelectedObjectObjectIndex});

        // Extract and convert the model metadata.
        (string gamepath, string name)[]? meta = modelMeta?[0]?
            .Select(x => x.Value)
            .Select(x => (x.Item1, x.Item2))
            .ToArray();

        // Throw an exception if model metadata is not available.
        if (meta == null)
        {
            throw new Exception("Failed to get model meta");
        }

        var nodes = new List<Node>();

        // Iterate through each model and its associated data.
        foreach (var (modelFullPath, model) in Models)
        {
            var modelGamePath = CharacterUtility.ResolveGameObjectPath(modelFullPath, SelectedObjectObjectIndex, null, pluginInterface);
            var children = new List<Node>();

            foreach (var (materialFullPath, material) in model.Materials)
            {
                // Resolve the game path for the material.
                var materialGamePath = CharacterUtility.ResolveGameObjectPath(materialFullPath,
                    SelectedObjectObjectIndex, material.GamePath, pluginInterface);
                var materialChildren = new List<Node>();

                foreach (var texture in material.Textures)
                {
                    // Resolve the game path for the texture.
                    var textureGamePath = CharacterUtility.ResolveGameObjectPath(texture.ActivePath,
                        SelectedObjectObjectIndex, texture.GamePath, pluginInterface);
                    materialChildren.Add(new Node(texture.ActivePath, textureGamePath ?? texture.GamePath,
                        ResourceType.Tex.ToString(),
                        ResourceType.Tex));
                }

                foreach (var shaderpack in material.Shaderpacks)
                {
                    // Resolve the game path for the shaderpack.
                    var shaderpackGamePath = CharacterUtility.ResolveGameObjectPath(shaderpack.ActivePath,
                        SelectedObjectObjectIndex, null, pluginInterface);
                    materialChildren.Add(new Node(shaderpack.ActivePath, shaderpackGamePath ?? shaderpack.ActivePath,
                        ResourceType.Shpk.ToString(), ResourceType.Shpk));
                }

                children.Add(new Node(material.ActivePath, materialGamePath ?? material.GamePath,
                    ResourceType.Mtrl.ToString(), ResourceType.Mtrl, materialChildren.ToArray()));
            }

            // Find the matching metadata entry for the model.
            var metaEntry = meta.FirstOrDefault(x =>
                string.Equals(x.gamepath, modelFullPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.gamepath, modelGamePath, StringComparison.OrdinalIgnoreCase));

            nodes.Add(new Node(modelFullPath, modelGamePath ?? modelFullPath,
                metaEntry.name ?? modelGamePath ?? modelFullPath, ResourceType.Mdl, children.ToArray()));
        }

        // Iterate through each skeleton.
        foreach (var skeleton in Skeletons)
        {
            var children = new List<Node>();

            foreach (var (parameterPath, value) in skeleton.Value.SkeletonParameters)
            {
                children.Add(new Node(value.ActivePath, value.ActivePath,
                    ResourceType.Skp.ToString(), ResourceType.Skp));
            }

            nodes.Add(new Node(skeleton.Key, skeleton.Key, ResourceType.Sklb.ToString(), ResourceType.Sklb,
                children.ToArray()));
        }

        resourceTree.Nodes = nodes.ToArray();
        return resourceTree;
    }
}