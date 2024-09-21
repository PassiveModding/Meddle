using System.Numerics;
using Meddle.Utils.Export;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public class CharacterOcclusionMaterialBuilder : GenericMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;

    public CharacterOcclusionMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider) : base(name, set, dataProvider)
    {
        this.set = set;
        this.dataProvider = dataProvider;
    }

    public override MeddleMaterialBuilder Apply()
    {
        base.Apply();
        WithDoubleSide(set.RenderBackfaces);
        WithBaseColor(new Vector4(1, 1, 1, 0f));
        WithAlpha(AlphaMode.BLEND, 0.5f);
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
        Extras = set.ComposeExtrasNode();
        return this;
    }
}
