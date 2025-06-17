using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxPathRendering
{
    public record struct MeshVertexAndColor(float X, float Y, byte R, byte G, byte B, byte A)
    {

    }

    public record struct MeshVertex(float X, float Y);
    public record struct MeshColor(byte R, byte G, byte B, byte A);
}
