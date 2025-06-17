using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxPathRendering
{
    public class PathMeshBuilder
    {
        private bool _figureStroke;
        private bool _figureFill;

        private MeshColor _figureStrokeColor;
        private MeshColor _figureFillColor;

        private float _figureStrokeThickness;
        private List<Point> _figurePoints = new List<Point>();

        private List<MeshVertexAndColor> _finalVerticesAndColors = new List<MeshVertexAndColor>();
        private List<MeshTriangleIndices> _finalIndices = new List<MeshTriangleIndices>();

        public void BeginFigure(bool stroke, bool fill, MeshColor strokeColor, MeshColor fillColor, float strokeThickness)
        {
            _figureStroke = stroke;
            _figureFill = fill;
            _figureStrokeColor = strokeColor;
            _figureFillColor = fillColor;
            _figureStrokeThickness = strokeThickness;
            _figurePoints.Clear();
        }

        public void AddPoint(float x, float y)
        {
            _figurePoints.Add(new Point(x, y));
        }

        public void CloseFigure()
        {
            // 没有点或少于3个点时无法形成有效的多边形
            if (_figurePoints.Count < 3)
                return;

            if (_figureStroke)
            {
                uint baseIndex = (uint)_finalVerticesAndColors.Count;
                float halfThickness = _figureStrokeThickness / 2f;
                List<Point> innerPoints = new List<Point>();
                List<Point> outerPoints = new List<Point>();

                // 生成内外点
                for (int i = 0; i < _figurePoints.Count; i++)
                {
                    Point current = _figurePoints[i];
                    Point prev = _figurePoints[(i + _figurePoints.Count - 1) % _figurePoints.Count];
                    Point next = _figurePoints[(i + 1) % _figurePoints.Count];

                    // 计算前后向量的平均方向作为法向量
                    float dx1 = current.X - prev.X;
                    float dy1 = current.Y - prev.Y;
                    float dx2 = next.X - current.X;
                    float dy2 = next.Y - current.Y;

                    // 归一化向量
                    float len1 = (float)Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                    float len2 = (float)Math.Sqrt(dx2 * dx2 + dy2 * dy2);

                    if (len1 > 0)
                    {
                        dx1 /= len1;
                        dy1 /= len1;
                    }
                    if (len2 > 0)
                    {
                        dx2 /= len2;
                        dy2 /= len2;
                    }

                    // 计算法向量（垂直于线段）
                    float nx1 = -dy1;
                    float ny1 = dx1;
                    float nx2 = -dy2;
                    float ny2 = dx2;

                    // 平均法向量
                    float nx = (nx1 + nx2) * 0.5f;
                    float ny = (ny1 + ny2) * 0.5f;

                    // 调整法向量长度
                    float norm = (float)Math.Sqrt(nx * nx + ny * ny);
                    if (norm > 0)
                    {
                        nx /= norm;
                        ny /= norm;
                    }

                    // 内外点
                    innerPoints.Add(new Point(current.X - nx * halfThickness, current.Y - ny * halfThickness));
                    outerPoints.Add(new Point(current.X + nx * halfThickness, current.Y + ny * halfThickness));
                }

                // 添加所有内外点顶点
                for (int i = 0; i < _figurePoints.Count; i++)
                {
                    // 外点
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        (float)outerPoints[i].X, (float)outerPoints[i].Y,
                        _figureStrokeColor.R, _figureStrokeColor.G, _figureStrokeColor.B, _figureStrokeColor.A
                    ));

                    // 内点
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        (float)innerPoints[i].X, (float)innerPoints[i].Y,
                        _figureStrokeColor.R, _figureStrokeColor.G, _figureStrokeColor.B, _figureStrokeColor.A
                    ));
                }

                // 生成stroke对应的三角形索引
                for (uint i = 0; i < _figurePoints.Count; i++)
                {
                    uint next = (i + 1) % (uint)_figurePoints.Count;

                    uint outerCurrent = baseIndex + i * 2;
                    uint innerCurrent = baseIndex + i * 2 + 1;
                    uint outerNext = baseIndex + next * 2;
                    uint innerNext = baseIndex + next * 2 + 1;

                    // 两个三角形组成一个四边形
                    _finalIndices.Add(new MeshTriangleIndices(outerCurrent, innerCurrent, innerNext));
                    _finalIndices.Add(new MeshTriangleIndices(outerCurrent, innerNext, outerNext));
                }

                // 如果有填充，使用内点做填充
                if (_figureFill)
                {
                    uint fillBaseIndex = (uint)_finalVerticesAndColors.Count;

                    // 添加所有内点作为填充顶点
                    foreach (var point in innerPoints)
                    {
                        _finalVerticesAndColors.Add(new MeshVertexAndColor(
                            (float)point.X, (float)point.Y,
                            _figureFillColor.R, _figureFillColor.G, _figureFillColor.B, _figureFillColor.A
                        ));
                    }

                    // 生成填充的三角形索引（假设是凸多边形，使用扇形三角化）
                    for (uint i = 1; i < innerPoints.Count - 1; i++)
                    {
                        _finalIndices.Add(new MeshTriangleIndices(
                            fillBaseIndex,
                            fillBaseIndex + i,
                            fillBaseIndex + i + 1
                        ));
                    }
                }
            }
            else if (_figureFill)
            {
                // 直接使用用户添加的点
                uint baseIndex = (uint)_finalVerticesAndColors.Count;

                // 添加所有点作为顶点
                foreach (var point in _figurePoints)
                {
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        (float)point.X, (float)point.Y,
                        _figureFillColor.R, _figureFillColor.G, _figureFillColor.B, _figureFillColor.A
                    ));
                }

                // 生成填充的三角形索引（假设是凸多边形，使用扇形三角化）
                for (uint i = 1; i < _figurePoints.Count - 1; i++)
                {
                    _finalIndices.Add(new MeshTriangleIndices(
                        baseIndex,
                        baseIndex + i,
                        baseIndex + i + 1
                    ));
                }
            }

            // 清空当前图形点集，为下一次做准备
            _figurePoints.Clear();
        }

        public void Build(out MeshVertexAndColor[] verticesAndColors, out MeshTriangleIndices[] indices)
        {
            verticesAndColors = _finalVerticesAndColors.ToArray();
            indices = _finalIndices.ToArray();

            // 清空最终结果，为下一次构建做准备
            _finalVerticesAndColors.Clear();
            _finalIndices.Clear();
        }
    }
}
