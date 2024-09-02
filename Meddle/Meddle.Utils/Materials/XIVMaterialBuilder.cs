﻿using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public class XivMaterialBuilder : MaterialBuilder
{
    public string Shpk;

    public XivMaterialBuilder(string name, string shpk) : base(name)
    {
        this.Shpk = shpk;
    }
}

public interface IVertexPaintMaterialBuilder
{
    public bool VertexPaint { get; }
}
