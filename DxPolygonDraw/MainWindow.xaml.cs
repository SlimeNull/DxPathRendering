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
            PathMeshBuilder pathMeshBuilder = new PathMeshBuilder();
            pathMeshBuilder.BeginFigure(true, true, new MeshColor(0, 0, 255, 255), new MeshColor(255, 0, 0, 255), 0.1f);
            pathMeshBuilder.AddPoint(-0.5f, -0.5f);
            pathMeshBuilder.AddPoint(0.5f, -0.5f);
            pathMeshBuilder.AddPoint(0.5f, 0.5f);
            pathMeshBuilder.AddPoint(-0.5f, 0.5f);
            pathMeshBuilder.CloseFigure();

            pathMeshBuilder.Build(out var verticesAndColors, out var indices);

            WriteableBitmap bitmap = new WriteableBitmap(800, 800, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Lock();

            MeshRenderer renderer = new MeshRenderer(800, 800);
            renderer.SetMesh(verticesAndColors, indices);
            renderer.Render(new Span<byte>((void*)bitmap.BackBuffer, bitmap.BackBufferStride * bitmap.PixelHeight));

            bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            bitmap.Unlock();

            image.Source = bitmap;
        }
    }
}