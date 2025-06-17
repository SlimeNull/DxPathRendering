using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DxPathRendering
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private unsafe void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // create a WriteableBitmap for rendering
            WriteableBitmap bitmap = new WriteableBitmap(800, 800, 96, 96, PixelFormats.Bgra32, null);

            // create a path mesh builder
            PathMeshBuilder pathMeshBuilder = new PathMeshBuilder();

            for (int i = 0; i < 5; i++)
            {
                var baseX = Random.Shared.Next(50, 750);
                var baseY = Random.Shared.Next(50, 750);
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

            // set the WriteableBitmap as the source of the Image control
            image.Source = bitmap;
        }
    }
}