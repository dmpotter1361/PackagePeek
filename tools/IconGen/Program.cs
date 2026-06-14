using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Generates the app icon: a sky-blue badge with a delivery box + smile arrow,
// a paper plane (air) and a little car (ground). Outputs a multi-size .ico and
// a PNG preview.

static GraphicsPath Rounded(Rectangle r, int radius)
{
    int d = radius * 2;
    var p = new GraphicsPath();
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
}

static Bitmap DrawMaster()
{
    const int S = 256;
    var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.Clear(Color.Transparent);

    // --- sky badge background ---
    var bg = new Rectangle(8, 8, 240, 240);
    using (var path = Rounded(bg, 52))
    using (var sky = new LinearGradientBrush(bg, Color.FromArgb(0xBF, 0xE6, 0xFF), Color.FromArgb(0xEC, 0xF8, 0xFF), 90f))
    {
        g.FillPath(sky, path);
        using var border = new Pen(Color.FromArgb(40, 0, 60, 90), 3);
        g.DrawPath(border, path);
    }

    // --- paper plane (air), upper-left ---
    PointF[] plane =
    {
        new(40, 84), new(104, 54), new(70, 100), new(64, 86)
    };
    g.FillPolygon(Brushes.White, plane);
    using (var pgray = new Pen(Color.FromArgb(120, 90, 110, 130), 2))
        g.DrawPolygon(pgray, plane);
    // dotted contrail behind it
    using (var trail = new Pen(Color.FromArgb(150, 255, 255, 255), 4) { DashStyle = DashStyle.Dot, StartCap = LineCap.Round, EndCap = LineCap.Round })
        g.DrawLine(trail, 20, 92, 58, 84);

    // --- little car (ground), upper-right ---
    var carColor = Color.FromArgb(0xE0, 0x53, 0x3D);
    using (var cb = new SolidBrush(carColor))
    {
        using var body = Rounded(new Rectangle(150, 78, 74, 26), 9);
        g.FillPath(cb, body);
        using var cabin = Rounded(new Rectangle(164, 62, 40, 22), 8);
        g.FillPath(cb, cabin);
    }
    using (var win = new SolidBrush(Color.FromArgb(225, 225, 245, 255)))
    using (var wpath = Rounded(new Rectangle(170, 66, 28, 15), 5))
        g.FillPath(win, wpath);
    g.FillEllipse(Brushes.DimGray, 158, 96, 20, 20);
    g.FillEllipse(Brushes.DimGray, 196, 96, 20, 20);
    g.FillEllipse(Brushes.Gainsboro, 164, 102, 8, 8);
    g.FillEllipse(Brushes.Gainsboro, 202, 102, 8, 8);

    // --- delivery box (hero), center-bottom ---
    var kraft = Color.FromArgb(0xCB, 0x97, 0x5A);
    var kraftDark = Color.FromArgb(0xB0, 0x7C, 0x40);
    var tape = Color.FromArgb(0xEA, 0xCF, 0xA6);
    var box = new Rectangle(74, 120, 108, 96);
    using (var kb = new SolidBrush(kraft)) g.FillRectangle(kb, box);
    using (var kd = new SolidBrush(kraftDark)) g.FillRectangle(kd, new Rectangle(box.X, box.Y, box.Width, 20)); // top flap shadow
    using (var tb = new SolidBrush(tape)) g.FillRectangle(tb, new Rectangle(box.X + box.Width / 2 - 9, box.Y, 18, box.Height)); // center tape
    using (var edge = new Pen(kraftDark, 2)) g.DrawRectangle(edge, box);

    // --- orange smile arrow across the box face ---
    using (var smile = new Pen(Color.FromArgb(0xFF, 0x99, 0x00), 9) { StartCap = LineCap.Round, EndCap = LineCap.Round })
    {
        var arcRect = new Rectangle(box.X + 14, box.Y + 30, box.Width - 28, 56);
        g.DrawArc(smile, arcRect, 15, 120);
        // arrowhead at the right tip of the smile
        PointF tip = new(arcRect.Right - 6, arcRect.Y + arcRect.Height - 14);
        PointF[] head = { tip, new(tip.X - 16, tip.Y - 4), new(tip.X - 5, tip.Y - 16) };
        using var ob = new SolidBrush(Color.FromArgb(0xFF, 0x99, 0x00));
        g.FillPolygon(ob, head);
    }

    return bmp;
}

static byte[] Png(Bitmap b)
{
    using var ms = new MemoryStream();
    b.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

var master = DrawMaster();

int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };
var frames = new List<Bitmap>();
foreach (var s in sizes)
{
    var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(master, new Rectangle(0, 0, s, s));
    }
    frames.Add(bmp);
}

var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var icoPath = Path.Combine(outDir, "appicon.ico");
var pngPath = Path.Combine(outDir, "appicon-preview.png");

using (var fs = new FileStream(icoPath, FileMode.Create))
using (var bw = new BinaryWriter(fs))
{
    var pngs = frames.Select(Png).ToList();
    bw.Write((short)0);            // reserved
    bw.Write((short)1);            // type = icon
    bw.Write((short)frames.Count); // image count
    int offset = 6 + 16 * frames.Count;
    for (int i = 0; i < frames.Count; i++)
    {
        var f = frames[i];
        bw.Write((byte)(f.Width >= 256 ? 0 : f.Width));
        bw.Write((byte)(f.Height >= 256 ? 0 : f.Height));
        bw.Write((byte)0);   // palette
        bw.Write((byte)0);   // reserved
        bw.Write((short)1);  // color planes
        bw.Write((short)32); // bits per pixel
        bw.Write(pngs[i].Length);
        bw.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var png in pngs) bw.Write(png);
}

master.Save(pngPath, ImageFormat.Png);
Console.WriteLine("Wrote: " + icoPath);
Console.WriteLine("Wrote: " + pngPath);
