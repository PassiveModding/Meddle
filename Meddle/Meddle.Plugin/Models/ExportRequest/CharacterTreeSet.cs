using Meddle.Plugin.Utility;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;

namespace Meddle.Plugin.Models.ExportRequest;

public class CharacterTreeSet(
    Character character,
    CharacterTree tree,
    ExportLogger logger,
    DateTime time,
    bool[] selectedModels,
    bool[] selectedAttaches)
{
    public Character Character { get; } = character;
    public CharacterTree Tree { get; } = tree;
    public ExportLogger Logger { get; } = logger;
    public DateTime Time { get; } = time;
    public bool[] EnabledModels { get; } = selectedModels;
    public bool[] EnabledAttaches { get; } = selectedAttaches;
        
    public AttachedChild[] SelectedAttaches => Tree.AttachedChildren.Where((x, i) => EnabledAttaches[i]).ToArray();
    public Model[] SelectedModels => Tree.Models.Where((x, i) => EnabledModels[i]).ToArray();
}
