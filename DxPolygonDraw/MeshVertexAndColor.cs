using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxPathRendering
{
    public record struct MeshVertexAndColor(MeshVertex Vertex, MeshColor Color)
    {
        public MeshVertexAndColor(float x, float y, byte r, byte g, byte b, byte a)
            : this(new MeshVertex(x, y), new MeshColor(r, g, b, a))
        {

        }
    }

    public record struct MeshVertex(float X, float Y);
    public record struct MeshColor(byte R, byte G, byte B, byte A);
}
