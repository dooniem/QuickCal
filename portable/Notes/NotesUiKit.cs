using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace QuickCal.Notes
{
    /// <summary>Small drawing helpers for the notes: icons (the same drawings as the web app), colors and pictures.</summary>
    public static class NotesUiKit
    {
        public static readonly Color Blue = Color.FromRgb(0x14, 0x32, 0x75);

        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ---------- Icons, drawn on a 24 x 24 grid ----------

        public const string NotepadPage = "M 6,3.5 L 15,3.5 L 18.5,7 L 18.5,20.5 L 6,20.5 Z";
        public const string NotepadLines = "M 8.8,10 L 15.8,10 M 8.8,13.5 L 15.8,13.5 M 8.8,17 L 13.3,17";
        public const string Bin = "M 4,6.5 L 20,6.5 M 9.5,6.5 L 9.5,4 L 14.5,4 L 14.5,6.5 M 6.5,6.5 L 7.5,20 L 16.5,20 L 17.5,6.5 M 10,10 L 10,17 M 14,10 L 14,17";
        public const string DropboxLogo = "M 6,1.807 L 0,5.629 L 6,9.451 L 12.001,5.629 L 6,1.807 Z M 18,1.807 L 12,5.629 L 18,9.451 L 24,5.629 L 18,1.807 Z M 0,13.274 L 6,17.096 L 12.001,13.274 L 6,9.452 L 0,13.274 Z M 18,9.452 L 12,13.274 L 18,17.096 L 24,13.274 L 18,9.452 Z M 6,18.371 L 12.001,22.193 L 18.001,18.371 L 12.001,14.549 L 6,18.371 Z";
        public const string Tray = "M 4,14 L 4,19.5 L 20,19.5 L 20,14";
        public const string ArrowDown = "M 12,3.5 L 12,14.5 M 7.5,10 L 12,14.5 L 16.5,10";
        public const string ArrowUp = "M 12,15 L 12,4 M 7.5,8.5 L 12,4 L 16.5,8.5";
        public const string BackupTray = "M 3.5,14 L 3.5,19.5 L 20.5,19.5 L 20.5,14";
        public const string BackupDown = "M 8.5,3.5 L 8.5,14.5 M 5,11 L 8.5,14.5 L 12,11";
        public const string BackupUp = "M 15.5,15 L 15.5,4 M 12,7.5 L 15.5,4 L 19,7.5";
        public const string Funnel = "M 3.5,5 L 20.5,5 L 14,12.5 L 14,18.5 L 10,20.5 L 10,12.5 Z";
        public const string Palette = "M 12,3 A 9,9 0 1 0 12,21 C 13.2,21 13.8,20.2 13.8,19.3 C 13.8,18.3 12.9,17.9 12.9,16.9 C 12.9,15.9 13.7,15.2 14.7,15.2 L 17,15.2 A 4,4 0 0 0 21,11.2 C 21,6.6 17,3 12,3 Z";
        public const string SearchGlass = "M 10.5,4.5 A 6,6 0 1 0 10.5,16.5 A 6,6 0 1 0 10.5,4.5 Z M 15,15 L 20.5,20.5";

        /// <summary>A path on the 24 x 24 grid.</summary>
        public static Path PathOf(string data, Brush fill, Brush stroke, double thickness, bool round = false)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round
            };
            if (round) { p.StrokeStartLineCap = PenLineCap.Round; p.StrokeEndLineCap = PenLineCap.Round; }
            return p;
        }

        /// <summary>Paths drawn on a 24 x 24 canvas, scaled to size.</summary>
        public static Viewbox Icon(double size, params UIElement[] parts)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (UIElement part in parts) canvas.Children.Add(part);
            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
        }

        public static Viewbox NotepadIcon(double size) => Icon(size,
            PathOf(NotepadPage, Brush("#fff8c4"), Brushes.Black, 1.5),
            PathOf(NotepadLines, null, Brushes.Black, 1.4));

        public static Viewbox BinIcon(double size) => Icon(size, PathOf(Bin, null, Brushes.Black, 1.8, round: true));

        public static Viewbox DropboxIcon(double size) => Icon(size, PathOf(DropboxLogo, Brush("#0061fe"), null, 0));

        public static Viewbox BackupIcon(double size) => Icon(size,
            PathOf(BackupTray, null, Brush("#333333"), 1.8),
            PathOf(BackupDown, null, Brush(Blue), 2, round: true),
            PathOf(BackupUp, null, Brush("#2e9e5b"), 2, round: true));

        public static Viewbox BackupToFileIcon(double size) => Icon(size,
            PathOf(Tray, null, Brush("#333333"), 1.8),
            PathOf(ArrowDown, null, Brush(Blue), 2, round: true));

        public static Viewbox RestoreIcon(double size) => Icon(size,
            PathOf(Tray, null, Brush("#333333"), 1.8),
            PathOf(ArrowUp, null, Brush("#2e9e5b"), 2, round: true));

        /// <summary>The Excel logo, simplified: a green sheet with a white X.</summary>
        public static Viewbox ExcelIcon(double size) => Icon(size,
            PathOf("M 10,2 L 19,2 A 3,3 0 0 1 22,5 L 22,19 A 3,3 0 0 1 19,22 L 10,22 A 3,3 0 0 1 7,19 L 7,5 A 3,3 0 0 1 10,2 Z", Brush("#21734a"), null, 0),
            PathOf("M 15,2 L 19,2 A 3,3 0 0 1 22,5 L 22,9.5 L 15,9.5 Z", Brush("#b5e09f"), null, 0),
            PathOf("M 15,9.5 L 22,9.5 L 22,15.5 L 15,15.5 Z", Brush("#2f8f4e"), null, 0),
            PathOf("M 3.5,7.5 L 11,7.5 A 2,2 0 0 1 13,9.5 L 13,17 A 2,2 0 0 1 11,19 L 3.5,19 A 2,2 0 0 1 1.5,17 L 1.5,9.5 A 2,2 0 0 1 3.5,7.5 Z", Brush("#107c41"), null, 0),
            PathOf("M 4.6,10.4 L 9.9,16.1 M 9.9,10.4 L 4.6,16.1", null, Brushes.White, 2, round: true));

        /// <summary>A Segoe MDL2 Assets symbol (the same icon font as the rest of QuickCal).</summary>
        public static TextBlock Symbol(string glyph, double size, Brush color = null) => new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size,
            Foreground = color ?? Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>A round color dot with its number inside (no color = a white dot with 0).</summary>
        public static Border NumberDot(string colorId, double size = 14)
        {
            string hex = NoteColors.Hex(colorId);
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = hex != null ? Brush(hex) : Brushes.White,
                BorderBrush = hex != null ? null : Brush("#888888"),
                BorderThickness = new Thickness(hex != null ? 0 : 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = NoteColors.Number(colorId).ToString(),
                    FontSize = size * 0.64,
                    FontWeight = FontWeights.Bold,
                    Foreground = hex != null ? Brushes.White : Brush("#555555"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Diagonal lines over a day with a note (dark on a white day, light on a colored one).</summary>
        public static Brush Hatch(bool light)
        {
            var pen = new Pen(light ? new SolidColorBrush(Color.FromArgb(71, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(33, 0, 0, 0)), light ? 1.5 : 1);
            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(new Point(0, 5), new Point(5, 0)));
            geometry.Children.Add(new LineGeometry(new Point(-1, 1), new Point(1, -1)));
            geometry.Children.Add(new LineGeometry(new Point(4, 6), new Point(6, 4)));
            var brush = new DrawingBrush(new GeometryDrawing(null, pen, geometry))
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 5, 5),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 5, 5),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        /// <summary>The small notepad in the top right corner of a day with a note.</summary>
        public static Viewbox CellNotepad()
        {
            var canvas = new Canvas { Width = 10, Height = 12 };
            canvas.Children.Add(PathOf("M 1,1.5 L 7,1.5 L 9,3.5 L 9,11 L 1,11 Z", Brush("#fff8c4"), Brush("#333333"), 0.9));
            canvas.Children.Add(PathOf("M 2.8,5 L 7.2,5 M 2.8,7 L 7.2,7 M 2.8,9 L 5.8,9", null, Brush("#333333"), 0.8));
            return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        }

        // ---------- Pictures ----------

        /// <summary>A picture from a file, decoded small (the file is not kept open).</summary>
        public static BitmapImage Load(string path, int decodeHeight = 0)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                if (decodeHeight > 0) img.DecodePixelHeight = decodeHeight;
                img.UriSource = new Uri(path);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A picture from a file the user picked or pasted (any format WPF reads), or null.</summary>
        public static BitmapSource Decode(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                BitmapDecoder decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Shrink before saving, so notes stay small and fast: at most 1600 pixels on the longest side,
        /// on a white background, as JPEG (quality 85), like the web app.</summary>
        public static byte[] Shrink(BitmapSource source)
        {
            int pw = source.PixelWidth, ph = source.PixelHeight;
            double s = Math.Min(1.0, (double)NoteStore.MaxImageSide / Math.Max(pw, ph));
            int w = Math.Max(1, (int)Math.Round(pw * s)), h = Math.Max(1, (int)Math.Round(ph * s));
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                dc.DrawImage(source, new Rect(0, 0, w, h));
            }
            var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
    }
}
