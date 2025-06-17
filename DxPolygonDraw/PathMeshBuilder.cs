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

        // 缓存集合 - 重用这些集合以避免GC压力
        private List<Point> _innerPointsCache = new List<Point>();
        private List<Point> _outerPointsCache = new List<Point>();

        private static bool IsClockwise(List<Point> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point current = points[i];
                Point next = points[(i + 1) % points.Count];
                sum += (next.X - current.X) * (next.Y + current.Y);
            }
            return sum > 0;
        }

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

                // 清空并重用缓存集合
                _innerPointsCache.Clear();
                _outerPointsCache.Clear();

                // 预分配容量以避免动态增长
                if (_innerPointsCache.Capacity < _figurePoints.Count)
                {
                    _innerPointsCache.Capacity = _figurePoints.Count;
                    _outerPointsCache.Capacity = _figurePoints.Count;
                }

                // 首先判断多边形的方向（顺时针或逆时针）
                bool isClockwise = IsClockwise(_figurePoints);

                // 生成内外点 - 使用多边形偏移算法
                for (int i = 0; i < _figurePoints.Count; i++)
                {
                    int prevIndex = (i + _figurePoints.Count - 1) % _figurePoints.Count;
                    int nextIndex = (i + 1) % _figurePoints.Count;

                    Point prev = _figurePoints[prevIndex];
                    Point current = _figurePoints[i];
                    Point next = _figurePoints[nextIndex];

                    // 计算当前边的方向向量
                    float dx1 = current.X - prev.X;
                    float dy1 = current.Y - prev.Y;

                    // 计算下一条边的方向向量
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

                    // 计算两条边的法向量（始终指向多边形外部）
                    float nx1 = -dy1;
                    float ny1 = dx1;
                    float nx2 = -dy2;
                    float ny2 = dx2;

                    if (!isClockwise)
                    {
                        // 如果是逆时针，翻转法向量方向
                        nx1 = -nx1;
                        ny1 = -ny1;
                        nx2 = -nx2;
                        ny2 = -ny2;
                    }

                    // 计算角平分线向量
                    float nx = nx1 + nx2;
                    float ny = ny1 + ny2;
                    float nlen = (float)Math.Sqrt(nx * nx + ny * ny);

                    if (nlen > 0.0001f)  // 避免除以零
                    {
                        nx /= nlen;
                        ny /= nlen;

                        // 计算平分线长度修正因子
                        float sinHalfAngle = nlen / 2;
                        float offsetFactor = (sinHalfAngle != 0) ? (1.0f / sinHalfAngle) : 1.0f;

                        // 应用偏移 - 外点是向外偏移，内点是向内偏移
                        float offsetX = nx * halfThickness * offsetFactor;
                        float offsetY = ny * halfThickness * offsetFactor;

                        _outerPointsCache.Add(new Point(current.X + offsetX, current.Y + offsetY));
                        _innerPointsCache.Add(new Point(current.X - offsetX, current.Y - offsetY));
                    }
                    else
                    {
                        // 如果角平分线无法计算，则使用前一条边的法向量
                        _outerPointsCache.Add(new Point(current.X + nx1 * halfThickness, current.Y + ny1 * halfThickness));
                        _innerPointsCache.Add(new Point(current.X - nx1 * halfThickness, current.Y - ny1 * halfThickness));
                    }
                }

                // 预分配顶点和索引容量以避免动态增长
                int requiredVertexCapacity = _finalVerticesAndColors.Count + _figurePoints.Count * 2;
                if (_figureFill && _figureFillColor != _figureStrokeColor)
                    requiredVertexCapacity += _figurePoints.Count; // 额外的填充点

                if (_finalVerticesAndColors.Capacity < requiredVertexCapacity)
                    _finalVerticesAndColors.Capacity = requiredVertexCapacity;

                int requiredIndicesCapacity = _finalIndices.Count + _figurePoints.Count * 2;
                if (_figureFill)
                    requiredIndicesCapacity += (_figurePoints.Count - 2) * (_figureFillColor != _figureStrokeColor ? 2 : 1);

                if (_finalIndices.Capacity < requiredIndicesCapacity)
                    _finalIndices.Capacity = requiredIndicesCapacity;

                // 添加所有内外点顶点
                for (int i = 0; i < _figurePoints.Count; i++)
                {
                    // 外点
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        (float)_outerPointsCache[i].X, (float)_outerPointsCache[i].Y,
                        _figureStrokeColor.R, _figureStrokeColor.G, _figureStrokeColor.B, _figureStrokeColor.A
                    ));

                    // 内点
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        (float)_innerPointsCache[i].X, (float)_innerPointsCache[i].Y,
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

                // 如果有填充，使用已经添加的内点做填充
                if (_figureFill)
                {
                    // 计算内点的起始索引
                    uint fillBaseIndex = baseIndex + 1; // 内点从baseIndex+1开始，步长为2

                    // 生成填充的三角形索引
                    // 使用扇形三角化，第一个内点作为中心点
                    for (uint i = 1; i < _figurePoints.Count - 1; i++)
                    {
                        _finalIndices.Add(new MeshTriangleIndices(
                            fillBaseIndex,  // 第一个内点
                            fillBaseIndex + i * 2,  // 其他内点，步长为2
                            fillBaseIndex + (i + 1) * 2
                        ));
                    }

                    // 如果填充颜色与描边颜色不同，需要额外处理
                    if (_figureFillColor.R != _figureStrokeColor.R ||
                        _figureFillColor.G != _figureStrokeColor.G ||
                        _figureFillColor.B != _figureStrokeColor.B ||
                        _figureFillColor.A != _figureStrokeColor.A)
                    {
                        // 添加相同位置但使用填充颜色的点
                        uint colorFillBaseIndex = (uint)_finalVerticesAndColors.Count;

                        // 只添加内点，使用填充颜色
                        for (int i = 0; i < _figurePoints.Count; i++)
                        {
                            _finalVerticesAndColors.Add(new MeshVertexAndColor(
                                (float)_innerPointsCache[i].X, (float)_innerPointsCache[i].Y,
                                _figureFillColor.R, _figureFillColor.G, _figureFillColor.B, _figureFillColor.A
                            ));
                        }

                        // 用新添加的填充颜色点生成三角形
                        for (uint i = 1; i < _figurePoints.Count - 1; i++)
                        {
                            _finalIndices.Add(new MeshTriangleIndices(
                                colorFillBaseIndex,
                                colorFillBaseIndex + i,
                                colorFillBaseIndex + i + 1
                            ));
                        }
                    }
                }
            }
            else if (_figureFill)
            {
                // 直接使用用户添加的点
                uint baseIndex = (uint)_finalVerticesAndColors.Count;

                // 预分配容量
                int requiredVertexCapacity = _finalVerticesAndColors.Count + _figurePoints.Count;
                if (_finalVerticesAndColors.Capacity < requiredVertexCapacity)
                    _finalVerticesAndColors.Capacity = requiredVertexCapacity;

                int requiredIndicesCapacity = _finalIndices.Count + _figurePoints.Count - 2;
                if (_finalIndices.Capacity < requiredIndicesCapacity)
                    _finalIndices.Capacity = requiredIndicesCapacity;

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
