using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PaperStudio
{
    public static class SketchOutput
    {
        public static void RequireTransparent(string data)
        {
            try{
                using(var stream=new MemoryStream(Convert.FromBase64String(data)))using(var original=Image.FromStream(stream))
                {
                    if(original.RawFormat.Guid!=ImageFormat.Png.Guid)throw new InvalidOperationException("草图必须返回PNG格式。");
                    using(var bitmap=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb))
                    {
                        using(var g=Graphics.FromImage(bitmap)){g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;g.DrawImageUnscaled(original,0,0);}
                        var locked=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                        try{
                            var bytes=new byte[locked.Stride*bitmap.Height];Marshal.Copy(locked.Scan0,bytes,0,bytes.Length);
                            long clear=0,ink=0,edgeClear=0,edges=0,total=(long)bitmap.Width*bitmap.Height;
                            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){
                                int pixel=y*locked.Stride+x*4;byte alpha=bytes[pixel+3];if(alpha<16)clear++;
                                // White product faces are allowed, but a white-only transparent cutout is not a sketch.
                                if(alpha>64&&(bytes[pixel]<245||bytes[pixel+1]<245||bytes[pixel+2]<245))ink++;
                                if(x==0||y==0||x==bitmap.Width-1||y==bitmap.Height-1){edges++;if(alpha<16)edgeClear++;}
                            }
                            if(clear<total*0.02||edgeClear<edges*0.5||ink<total*0.0005)throw new InvalidOperationException("图片服务未返回有效透明草图（白底、假棋盘背景或空图均不采纳）。需要支持transparent PNG的图片模型；可在设置中更换后重试。");
                        }finally{bitmap.UnlockBits(locked);}
                    }
                }
            }catch(InvalidOperationException){throw;}catch{throw new InvalidOperationException("草图PNG无法读取，未保存该图。");}
        }
        // Composite only the review copy. Keep the exported PNG's alpha and white artwork intact.
        public static string ReviewCopy(string data)
        {
            using(var input=new MemoryStream(Convert.FromBase64String(data)))using(var original=Image.FromStream(input))using(var bitmap=new Bitmap(original.Width,original.Height))
            {
                using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.White);graphics.DrawImageUnscaled(original,0,0);}
                using(var output=new MemoryStream()){bitmap.Save(output,ImageFormat.Png);return Convert.ToBase64String(output.ToArray());}
            }
        }
    }
}
