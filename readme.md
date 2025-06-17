# DxPathRendering

DirectX based path rendering.

![preview](/Assets/preview.webp)



## Sample

Here is a sample of using this library in WPF.

```cs
// create a WriteableBitmap for rendering
WriteableBitmap bitmap = new WriteableBitmap(800, 800, 96, 96, PixelFormats.Bgra32, null);

// create a path mesh builder
PathMeshBuilder pathMeshBuilder = new PathMeshBuilder();
pathMeshBuilder.BeginFigure(true, true, new MeshColor(0, 0, 255, 255), new MeshColor(255, 0, 0, 255), 10f);

for (int i = 0; i < 5; i++)
{
    var rad = MathF.PI / 2 + MathF.PI * 2 / 5 * i;
    pathMeshBuilder.AddPoint(400 + MathF.Cos(rad) * 100f, 400 + MathF.Sin(rad) * 100f);
}

pathMeshBuilder.CloseFigure();
pathMeshBuilder.Build(out var verticesAndColors, out var indices);

// create a rotation transform
RotateTransform rotateTransform = new RotateTransform(180, 400, 400);

// create a mesh renderer
MeshRenderer renderer = new MeshRenderer(800, 800);

// transform, antialiasing, and mesh setup
renderer.SetTransform(
    new MatrixTransform(
        (float)rotateTransform.Value.M11, 
        (float)rotateTransform.Value.M12, 
        (float)rotateTransform.Value.M21, 
        (float)rotateTransform.Value.M22, 
        (float)rotateTransform.Value.OffsetX, 
        (float)rotateTransform.Value.OffsetY));

renderer.SetAntialiasing(true);
renderer.SetMesh(verticesAndColors, indices);

// render
bitmap.Lock();
renderer.Render(new Span<byte>((void*)bitmap.BackBuffer, bitmap.BackBufferStride * bitmap.PixelHeight));
bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
bitmap.Unlock();
```