// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using DxPathRendering;

Console.WriteLine("Hello, World!");

var canvasWidth = 2000;
var canvasHeight = 2000;

// create a buffer for rendering
byte[] renderingBuffer = new byte[canvasWidth * canvasHeight * 4];

// create a path mesh builder
PathMeshBuilder pathMeshBuilder = new PathMeshBuilder();

// create a mesh renderer
MeshRenderer renderer = new MeshRenderer(canvasWidth, canvasHeight);

int renderCount = 0;
Stopwatch stopwatch = Stopwatch.StartNew();

while (true)
{
    for (int i = 0; i < 100000; i++)
    {
        var baseX = Random.Shared.Next(50, canvasWidth - 50);
        var baseY = Random.Shared.Next(50, canvasHeight - 50);
        var radius = Random.Shared.Next(10, 100);
        var strokeThickness = radius * 0.1f;

        pathMeshBuilder.BeginFigure(true, true, new MeshColor(0, 0, 255, 255), new MeshColor(255, 0, 0, 255), strokeThickness);

        for (int j = 0; j < 5; j++)
        {
            var rad = MathF.PI / 2 + MathF.PI * 2 / 5 * j;
            pathMeshBuilder.AddPoint(baseX + MathF.Cos(rad) * radius, baseY + MathF.Sin(rad) * radius);
        }

        pathMeshBuilder.CloseFigure();
    }

    pathMeshBuilder.Reset();
    //pathMeshBuilder.Build(out var verticesAndColors, out var indices);

    //renderer.SetAntialiasing(false);
    //renderer.SetMesh(verticesAndColors, indices);

    // render
    //renderer.Render(new Span<byte>(renderingBuffer));

    renderCount++;
    if (stopwatch.ElapsedMilliseconds >= 1000)
    {
        Console.WriteLine($"FPS: {renderCount}");
        renderCount = 0;
        stopwatch.Restart();
    }
}