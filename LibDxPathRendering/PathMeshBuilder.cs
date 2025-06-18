using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        public static bool IsClockwise(List<Point> points)
        {
            if (points.Count < 3)
                return false;

            // 处理向量长度为4的整数倍部分
            int vectorSize = Vector<float>.Count / 2; // Vector中可以容纳的点对数
            int vectorizableCount = (points.Count / vectorSize) * vectorSize;

            Vector<float> sumVector = Vector<float>.Zero;

            for (int i = 0; i < vectorizableCount; i += vectorSize)
            {
                // 准备向量数据
                float[] xCurrent = new float[Vector<float>.Count];
                float[] yCurrent = new float[Vector<float>.Count];
                float[] xNext = new float[Vector<float>.Count];
                float[] yNext = new float[Vector<float>.Count];

                for (int j = 0; j < vectorSize; j++)
                {
                    int idx = i + j;
                    Point current = points[idx];
                    Point next = points[(idx + 1) % points.Count];

                    int offset = j * 2;
                    xCurrent[offset] = current.X;
                    yCurrent[offset] = current.Y;
                    xNext[offset] = next.X;
                    yNext[offset] = next.Y;

                    // 填充第二个元素
                    xCurrent[offset + 1] = 0;
                    yCurrent[offset + 1] = 0;
                    xNext[offset + 1] = 0;
                    yNext[offset + 1] = 0;
                }

                Vector<float> vXCurrent = new Vector<float>(xCurrent);
                Vector<float> vYCurrent = new Vector<float>(yCurrent);
                Vector<float> vXNext = new Vector<float>(xNext);
                Vector<float> vYNext = new Vector<float>(yNext);

                // (next.X - current.X) * (next.Y + current.Y)
                Vector<float> vDiffX = Vector.Subtract(vXNext, vXCurrent);
                Vector<float> vSumY = Vector.Add(vYNext, vYCurrent);
                Vector<float> vProduct = Vector.Multiply(vDiffX, vSumY);

                sumVector = Vector.Add(sumVector, vProduct);
            }

            // 累加向量中的所有元素
            double sum = 0;
            for (int i = 0; i < Vector<float>.Count; i++)
            {
                sum += sumVector[i];
            }

            // 处理剩余部分
            for (int i = vectorizableCount; i < points.Count; i++)
            {
                Point current = points[i];
                Point next = points[(i + 1) % points.Count];
                sum += (next.X - current.X) * (next.Y + current.Y);
            }

            return sum > 0;
        }
        public static void PolygonStroke(List<Point> points, List<Point> outputInnerPoints,
                                        List<Point> outputOuterPoints, float thickness)
        {
            if (points.Count < 3)
                return;

            // 清空输出集合
            outputInnerPoints.Clear();
            outputOuterPoints.Clear();

            // 首先判断多边形的方向（顺时针或逆时针）
            bool isClockwise = IsClockwise(points);
            float halfThickness = thickness / 2f;
            // 预先计算所有点的前后关系
            int count = points.Count;
            Vector2[] vertices = new Vector2[count];
            Vector2[] prevVectors = new Vector2[count];
            Vector2[] nextVectors = new Vector2[count];
            Vector2[] prevNormals = new Vector2[count];
            Vector2[] nextNormals = new Vector2[count];

            // 转换为Vector2并计算相邻边向量
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector2(points[i].X, points[i].Y);

                int prevIdx = (i + count - 1) % count;
                int nextIdx = (i + 1) % count;

                prevVectors[i] = vertices[i] - new Vector2(points[prevIdx].X, points[prevIdx].Y);
                nextVectors[i] = new Vector2(points[nextIdx].X, points[nextIdx].Y) - vertices[i];

                // 归一化
                if (prevVectors[i] != Vector2.Zero)
                    prevVectors[i] = Vector2.Normalize(prevVectors[i]);

                if (nextVectors[i] != Vector2.Zero)
                    nextVectors[i] = Vector2.Normalize(nextVectors[i]);

                // 计算法向量
                prevNormals[i] = new Vector2(-prevVectors[i].Y, prevVectors[i].X);
                nextNormals[i] = new Vector2(-nextVectors[i].Y, nextVectors[i].X);

                if (!isClockwise)
                {
                    prevNormals[i] = -prevNormals[i];
                    nextNormals[i] = -nextNormals[i];
                }
            }

            // 处理每个顶点
            for (int i = 0; i < count; i++)
            {
                // 计算角平分线向量
                Vector2 normal = prevNormals[i] + nextNormals[i];
                float length = normal.Length();

                if (length > 0.0001f)
                {
                    normal /= length;

                    // 计算平分线长度修正因子
                    float sinHalfAngle = length / 2;
                    float offsetFactor = (sinHalfAngle != 0) ? (1.0f / sinHalfAngle) : 1.0f;

                    // 应用偏移
                    Vector2 offset = normal * halfThickness * offsetFactor;

                    outputOuterPoints.Add(new Point(
                        vertices[i].X + offset.X,
                        vertices[i].Y + offset.Y));

                    outputInnerPoints.Add(new Point(
                        vertices[i].X - offset.X,
                        vertices[i].Y - offset.Y));
                }
                else
                {
                    // 如果角平分线无法计算，则使用前一条边的法向量
                    outputOuterPoints.Add(new Point(
                        vertices[i].X + prevNormals[i].X * halfThickness,
                        vertices[i].Y + prevNormals[i].Y * halfThickness));

                    outputInnerPoints.Add(new Point(
                        vertices[i].X - prevNormals[i].X * halfThickness,
                        vertices[i].Y - prevNormals[i].Y * halfThickness));
                }
            }
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
            {
                throw new InvalidOperationException("A figure must have at least 3 points to be closed.");
            }

            if (_figureStroke)
            {
                uint baseIndex = (uint)_finalVerticesAndColors.Count;
                float halfThickness = _figureStrokeThickness / 2f;

                // 清空并重用缓存集合
                _innerPointsCache.Clear();
                _outerPointsCache.Clear();

                PolygonStroke(_figurePoints, _innerPointsCache, _outerPointsCache, _figureStrokeThickness);

                // 添加所有内外点顶点
                for (int i = 0; i < _figurePoints.Count; i++)
                {
                    // 外点
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        _outerPointsCache[i].X, _outerPointsCache[i].Y,
                        _figureStrokeColor.R, _figureStrokeColor.G, _figureStrokeColor.B, _figureStrokeColor.A
                    ));

                    // 内点
                    _finalVerticesAndColors.Add(new MeshVertexAndColor(
                        _innerPointsCache[i].X, _innerPointsCache[i].Y,
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

                // 如果有填充
                if (_figureFill)
                {
                    // 计算内点的起始索引
                    uint fillBaseIndex = baseIndex + 1; // 内点从 baseIndex+1开始，步长为2
                    uint fillBaseIndexStep = 2;

                    // 如果填充颜色与描边颜色不同，需要额外处理
                    if (_figureFillColor.R != _figureStrokeColor.R ||
                        _figureFillColor.G != _figureStrokeColor.G ||
                        _figureFillColor.B != _figureStrokeColor.B ||
                        _figureFillColor.A != _figureStrokeColor.A)
                    {
                        // 只添加内点，使用填充颜色
                        for (int i = 0; i < _figurePoints.Count; i++)
                        {
                            _finalVerticesAndColors.Add(new MeshVertexAndColor(
                                _innerPointsCache[i].X, _innerPointsCache[i].Y,
                                _figureFillColor.R, _figureFillColor.G, _figureFillColor.B, _figureFillColor.A
                            ));
                        }

                        fillBaseIndex = (uint)(baseIndex + _figurePoints.Count * 2);
                        fillBaseIndexStep = 1;
                    }

                    // 生成填充的三角形索引
                    // 使用扇形三角化，第一个内点作为中心点
                    for (uint i = 1; i < _figurePoints.Count - 1; i++)
                    {
                        _finalIndices.Add(new MeshTriangleIndices(
                            fillBaseIndex,  // 第一个内点
                            fillBaseIndex + i * fillBaseIndexStep,  // 其他内点，步长为 fillBaseIndexStep
                            fillBaseIndex + (i + 1) * fillBaseIndexStep
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
                        point.X, point.Y,
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

            Reset();
        }

        public void BuildInto(List<MeshVertexAndColor> verticesAndColors, List<MeshTriangleIndices> indices)
        {
            verticesAndColors.AddRange(_finalVerticesAndColors);
            indices.AddRange(_finalIndices);

            Reset();
        }

        public void Reset()
        {
            _finalVerticesAndColors.Clear();
            _finalIndices.Clear();
        }
    }
}
