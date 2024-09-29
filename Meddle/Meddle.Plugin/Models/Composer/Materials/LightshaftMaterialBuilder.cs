using System.Numerics;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public class LightshaftMaterialBuilder : GenericMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;

    public LightshaftMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider) : base(name, set, dataProvider)
    {
        this.set = set;
        this.dataProvider = dataProvider;
    }

    public override MeddleMaterialBuilder Apply()
    {
        base.Apply();
        this.WithBaseColor(new Vector4(1, 1, 1, 0));
        WithAlpha(AlphaMode.BLEND, 0.5f);

        Extras = set.ComposeExtrasNode();
        return this;
    }
}
