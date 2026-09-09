$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -File $PSCommandPath
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    return
}
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class PulseIconBuilder {
    static Bitmap Render(int size) {
        using(var master = new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb)) {
            using(var g = Graphics.FromImage(master)) {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size*4/256f,size*4/256f);
                using(var tile = new GraphicsPath()) {
                    tile.AddArc(8,8,64,64,180,90);tile.AddArc(184,8,64,64,270,90);
                    tile.AddArc(184,184,64,64,0,90);tile.AddArc(8,184,64,64,90,90);tile.CloseFigure();
                    using(var fill = new SolidBrush(Color.FromArgb(22,31,46)))g.FillPath(fill,tile);
                    using(var border = new Pen(Color.FromArgb(65,88,111),5))g.DrawPath(border,tile);
                }
                using(var pulse = new Pen(Color.FromArgb(89,239,189),18)) {
                    pulse.StartCap=LineCap.Round;pulse.EndCap=LineCap.Round;pulse.LineJoin=LineJoin.Round;
                    g.DrawLines(pulse,new[]{new PointF(39,137),new PointF(76,137),new PointF(100,80),new PointF(133,181),new PointF(161,116),new PointF(184,137),new PointF(211,137)});
                }
                using(var point = new SolidBrush(Color.FromArgb(106,192,255)))g.FillEllipse(point,199,125,24,24);
            }
            var result = new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g = Graphics.FromImage(result)) {
                g.CompositingMode=CompositingMode.SourceCopy;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.DrawImage(master,new Rectangle(0,0,size,size),0,0,master.Width,master.Height,GraphicsUnit.Pixel);
            }
            return result;
        }
    }
    static byte[] Frame(Bitmap b) {
        using(var stream = new MemoryStream())using(var writer=new BinaryWriter(stream)) {
            int n=b.Width,maskStride=((n+31)/32)*4;
            writer.Write(40);writer.Write(n);writer.Write(n*2);writer.Write((ushort)1);writer.Write((ushort)32);
            writer.Write(0);writer.Write(n*n*4+maskStride*n);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
            for(int y=n-1;y>=0;y--)for(int x=0;x<n;x++){Color c=b.GetPixel(x,y);writer.Write(c.B);writer.Write(c.G);writer.Write(c.R);writer.Write(c.A);}
            for(int y=n-1;y>=0;y--) {
                var mask=new byte[maskStride];
                for(int x=0;x<n;x++)if(b.GetPixel(x,y).A==0)mask[x/8]|=(byte)(128>>(x%8));
                writer.Write(mask);
            }
            return stream.ToArray();
        }
    }
    public static void Build(string directory) {
        Directory.CreateDirectory(directory);
        int[] sizes={16,20,24,32,40,48,64,128,256};var frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(var b=Render(sizes[i])) {
            frames[i]=Frame(b);
            if(sizes[i]==256)b.Save(Path.Combine(directory,"PulseWidget.png"),ImageFormat.Png);
        }
        using(var stream=File.Create(Path.Combine(directory,"PulseWidget.ico")))using(var writer=new BinaryWriter(stream)) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;
            }
            foreach(var frame in frames)writer.Write(frame);
        }
    }
}
'@
[PulseIconBuilder]::Build((Join-Path $PSScriptRoot 'assets'))
