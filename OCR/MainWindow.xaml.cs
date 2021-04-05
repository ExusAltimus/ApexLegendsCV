
using AForge.Imaging;
using AForge.Imaging.Filters;
using AForge.Math.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OCR
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public class Shield
        {
            public System.Drawing.Color Color { get; }
            public string Name { get; }
            public int Tolerance { get; }
            public int? EvoChunks { get; }

            public Shield(string name)
            {
                Name = name;
            }

            public Shield(string name, int r, int g, int b, int tolerance, int? evoChunks)
            {
                Name = name;
                Color = System.Drawing.Color.FromArgb(255, r, g, b);
                Tolerance = tolerance;
                EvoChunks = evoChunks;
            }
        }

        private System.Drawing.Rectangle? HealthbarRect { get; set; }
        private IntPtr? WindowHandle { get; set; }

        public static Shield[] Shields = new[]
        {
            new Shield("None"),
            new Shield("White", 190, 190, 190, 40, 2), // white, 0
            new Shield("Blue", 30, 145, 253, 40, 3),
            new Shield("Purple", 158, 46, 248, 40, 4),
            new Shield("Red", 244, 3, 3, 40, 5),
            new Shield("Gold", 253, 205, 60, 40, null),
            new Shield("Healing", 43, 115, 89, 20, null)
        };


        public MainWindow()
        {
            InitializeComponent();

            // rgb(190, 190, 190) white
            // rgb(33, 144, 246) blue
            // rgb(158, 46, 248) purple
            // rgb(253, 205, 60) gold
            // rgb(244, 3, 3) red
            // rgb(43, 115, 89) healing

        }

        public bool IsColorCloseEnough(System.Drawing.Color color, System.Drawing.Color expectedColor, int tolerance)
        {
            if (color == expectedColor)
                return true;

            if (Math.Abs((int)color.R - (int)expectedColor.R) < tolerance
                && Math.Abs((int)color.G - (int)expectedColor.G) < tolerance
                && Math.Abs((int)color.B - (int)expectedColor.B) < tolerance)
                return true;

            return false;
        }

        public System.Drawing.Rectangle GetBoundingBox(List<System.Drawing.Point> points)
        {
            // Add checks here, if necessary, to make sure that points is not null,
            // and that it contains at least one (or perhaps two?) elements
            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxX = points.Max(p => p.X);
            var maxY = points.Max(p => p.Y);

            return new System.Drawing.Rectangle(new System.Drawing.Point(minX, minY), new System.Drawing.Size(maxX - minX, maxY - minY));
        }

        private IntPtr? GetApexWindowHandle()
        {
            var apexWindow = FindWindow.GetWindows("Apex Legends");
            var apexWindowHandle = apexWindow.FirstOrDefault();
            return apexWindowHandle;
        }

        private Bitmap CaptureApexWindow()
        {
            var apexWindowHandle = GetApexWindowHandle();
            var screenCapture = new ScreenCapture();
            var window = screenCapture.CaptureWindow(apexWindowHandle.Value, new System.Drawing.Rectangle(0, 800, 400, 100));
            return window.Image;
        }

        private System.Drawing.Rectangle? FindHealthbarRect()
        {
            var healthArea = CaptureApexWindow();
            // locating objects
            var blobCounter = new BlobCounter();
            blobCounter.CoupledSizeFiltering = true;
            blobCounter.FilterBlobs = true;
            blobCounter.MinHeight = 6;
            blobCounter.MinWidth = 100;
            blobCounter.MaxHeight = 15;

            //grayscale
            var bmp = Grayscale.CommonAlgorithms.BT709.Apply(healthArea);
            //Invert invert = new Invert();
            //bmp = invert.Apply(bmp);

            var filter = new IterativeThreshold(2, 4);
            // apply the filter
            filter.ApplyInPlace(bmp);
            blobCounter.ProcessImage(bmp);
            Blob[] blobs = blobCounter.GetObjectsInformation();

            // check for rectangles
            var shapeChecker = new SimpleShapeChecker();
            shapeChecker.AngleError = 5.0f;
            shapeChecker.LengthError = 0.5f;

            foreach (var blob in blobs)
            {
                List<AForge.IntPoint> edgePoints = blobCounter.GetBlobsEdgePoints(blob);
                List<AForge.IntPoint> cornerPoints;

                try
                {
                    // use the shape checker to extract the corner points
                    if (shapeChecker.IsQuadrilateral(edgePoints, out cornerPoints))
                    {
                        PolygonSubType subType = shapeChecker.CheckPolygonSubType(cornerPoints);
                        if (subType == PolygonSubType.Trapezoid)
                        {
                            // here i use the graphics class to draw an overlay, but you
                            // could also just use the cornerPoints list to calculate your
                            // x, y, width, height values.
                            List<System.Drawing.Point> Points = new List<System.Drawing.Point>();
                            foreach (var point in cornerPoints)
                            {
                                Points.Add(new System.Drawing.Point(point.X, point.Y));
                            }
                            var boundingBox = GetBoundingBox(Points);
                            var ratio = (boundingBox.Width / (float)boundingBox.Height);
                            if (ratio > 21.0f && ratio < 24.0f)
                            {
                                return boundingBox;
                            }
                        }
                    }
                }
                catch (Exception e) { }
            }

            return null;
        }

        public (Bitmap Bitmap, System.Drawing.Rectangle StripRect, System.Drawing.Rectangle Rect) GetShieldStrip(Bitmap capture, System.Drawing.Rectangle healthRect)
        {
            var shieldRect = new System.Drawing.Rectangle(healthRect.X, (healthRect.Y - 9), healthRect.Width, (healthRect.Height - 3));
            var shieldRectStrip = new System.Drawing.Rectangle(healthRect.X, (healthRect.Y - 9) + ((healthRect.Height - 3) / 2), healthRect.Width - 7, 1);
            var cloneBitmap = capture.Clone(shieldRectStrip, capture.PixelFormat);
            return (cloneBitmap, shieldRectStrip, shieldRect);
        }

        public Shield GetShield(Bitmap shieldStrip)
        {
            long[] totals = new long[] { 0, 0, 0 };

            var width = 5;// (cloneBitmap.Width / 5);
            for (int i = 0; i < width; i++)
            {
                var pixel = shieldStrip.GetPixel(i, 0);
                totals[0] += pixel.R;
                totals[1] += pixel.G;
                totals[2] += pixel.B;
            }

            byte avgB = (byte)(totals[2] / (width));
            byte avgG = (byte)(totals[1] / (width));
            byte avgR = (byte)(totals[0] / (width));
            var colorIndex = 0;
            var averageColor = System.Drawing.Color.FromArgb(255, avgR, avgG, avgB);

            for (var j = 1; j < Shields.Length; j++)
            {
                if (IsColorCloseEnough(averageColor, Shields[j].Color, Shields[j].Tolerance))
                {
                    colorIndex = j;
                    break;
                }
            }

            return Shields[colorIndex];
        }

        public (double Health, int Width) GetShieldHealth(Bitmap shieldStrip, Shield shield)
        {
            if (shield.Name == "None" || shield.Name == "Healing")
                return (0, 0);

            var maxWidth = shieldStrip.Width;
            if (shield.EvoChunks.HasValue) // Evo calc
                maxWidth = (shieldStrip.Width / 5 * (shield.EvoChunks.Value)) - 1;

            double health;
            var deadPixelCount = 0L;
            var shieldPixelCount = 0L;
            var shieldSpacerCount = 0L;

            for (int i = 0; i < shieldStrip.Width; i++)
            {
                var pixel = shieldStrip.GetPixel(i, 0);
                if (IsColorCloseEnough(pixel, shield.Color, shield.Tolerance))
                {
                    shieldPixelCount++;
                    shieldPixelCount += shieldSpacerCount;
                    //deadPixelCount -= shieldSpacerCount;
                    shieldSpacerCount = 0;
                }
                else
                {
                    deadPixelCount++;
                    shieldSpacerCount++;
                }
            }
            health = Math.Ceiling((shieldPixelCount / ((double)(maxWidth))) * 100.0d);
            if (health > 100)
                health = 100;

            return (health, maxWidth);
        }

        private async Task RecalibrateAsync()
        {
            HealthbarRect = await Observable.Interval(TimeSpan.FromMilliseconds(200))
                .Select((o) => FindHealthbarRect())
                .Where(o => o != null)
                .Select(o => o.Value)
                .FirstAsync();

        }

        private async Task WaitForApexWindowAsync()
        {
            WindowHandle = await Observable.Interval(TimeSpan.FromMilliseconds(200))
                .Select((o) => GetApexWindowHandle())
                .Where(o => o != null)
                .Select(o => o.Value)
                .FirstAsync();
        }

        private async void btnRecalibrate_Click(object sender, RoutedEventArgs e)
        {
           await RecalibrateAsync();
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            btnStart.IsEnabled = false;
            lblOutput.Content = $"Waiting to find Apex Legends window...";
            await WaitForApexWindowAsync();
            lblOutput.Content = $"Waiting for healthbar to initialize...";
            await RecalibrateAsync();
            Observable.Interval(TimeSpan.FromMilliseconds(50))
                .Where(o => HealthbarRect.HasValue)
                .Subscribe((time) =>
                {
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();
                    var apexCapture = CaptureApexWindow();
                    var shieldStrip = GetShieldStrip(apexCapture, HealthbarRect.Value);
                    var shield = GetShield(shieldStrip.Bitmap);
                    var shieldHealth = GetShieldHealth(shieldStrip.Bitmap, shield);
                    using (var ms = new MemoryStream())
                    {
                        using (var g = Graphics.FromImage(apexCapture))
                        {
                            g.DrawRectangle(new System.Drawing.Pen(System.Drawing.Color.DeepPink, 2.0f), new System.Drawing.Rectangle(shieldStrip.Rect.X, shieldStrip.Rect.Y, shieldHealth.Width, shieldStrip.Rect.Height));
                            g.DrawRectangle(new System.Drawing.Pen(System.Drawing.Color.DeepPink, 1.0f), HealthbarRect.Value);
                        }
                        this.Dispatcher.Invoke(() =>
                        {
                        //grdShield.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(avgR, avgG, avgB));
                            apexCapture.Save(ms, ImageFormat.Bmp);
                            ms.Seek(0, SeekOrigin.Begin);
                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.StreamSource = ms;
                            bitmapImage.EndInit();
                            imgCapture.Source = bitmapImage;
                            stopwatch.Stop();
                            lblOutput.Content = $"Shield: {shield.Name}, Shield HP%: {shieldHealth.Health}, ms: {stopwatch.ElapsedMilliseconds}";
                        });

                    }

                });
        }
    }
}
