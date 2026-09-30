using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

string outputRoot = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Musique", "Assets"));
string iconFolder = Path.Combine(outputRoot, "icons");
Directory.CreateDirectory(iconFolder);
Console.WriteLine($"Writing icons to {iconFolder}");

const int Size = 96;
var white = Color.White;
int written = 0;

void Icon(string name, Action<Graphics> draw) {
    using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap)) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        draw(g);
    }
    bitmap.Save(Path.Combine(iconFolder, name + ".png"), ImageFormat.Png);
    written++;
}

Pen Stroke(float width) => new(white, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

void Fill(Graphics g, params PointF[] points) {
    using var brush = new SolidBrush(white);
    using var path = new GraphicsPath();
    path.AddPolygon(points);
    using var pen = new Pen(white, 4) { LineJoin = LineJoin.Round };
    g.FillPath(brush, path);
    g.DrawPath(pen, path);
}

void RoundRect(Graphics g, RectangleF r, float radius, Brush brush) {
    using var path = new GraphicsPath();
    float d = radius * 2;
    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    g.FillPath(brush, path);
}

Icon("play", g => Fill(g, new(30, 20), new(30, 76), new(76, 48)));

Icon("pause", g => {
    using var brush = new SolidBrush(white);
    RoundRect(g, new RectangleF(26, 20, 15, 56), 4, brush);
    RoundRect(g, new RectangleF(55, 20, 15, 56), 4, brush);
});

Icon("next", g => {
    Fill(g, new(22, 24), new(22, 72), new(60, 48));
    using var brush = new SolidBrush(white);
    RoundRect(g, new RectangleF(64, 24, 10, 48), 3, brush);
});

Icon("previous", g => {
    Fill(g, new(74, 24), new(74, 72), new(36, 48));
    using var brush = new SolidBrush(white);
    RoundRect(g, new RectangleF(22, 24, 10, 48), 3, brush);
});

Icon("close", g => {
    using var pen = Stroke(8);
    g.DrawLine(pen, 26, 26, 70, 70);
    g.DrawLine(pen, 70, 26, 26, 70);
});

Icon("note", g => {
    using var brush = new SolidBrush(white);
    g.FillEllipse(brush, 18, 56, 30, 22);
    g.FillRectangle(brush, 40, 16, 8, 50);
    Fill(g, new(46, 16), new(76, 28), new(76, 40), new(46, 30));
});

Icon("windows", g => {
    using var pen = Stroke(7);
    g.DrawRectangle(pen, 14, 18, 68, 46);
    g.DrawLine(pen, 48, 64, 48, 76);
    g.DrawLine(pen, 32, 78, 64, 78);
});

Icon("logo", g => {
    using var red = new SolidBrush(Color.FromArgb(255, 255, 0, 51));
    g.FillEllipse(red, 2, 2, 92, 92);
    using var pen = new Pen(white, 5);
    g.DrawEllipse(pen, 22, 22, 52, 52);
    Fill(g, new(40, 34), new(40, 62), new(62, 48));
});

Console.WriteLine($"Wrote {written} icons.");
