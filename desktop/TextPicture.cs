using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace PaperStudio
{
    // Compress only the copy sent to text vision models. Original source/export pixels stay untouched.
    public static class TextPicture
    {
        public static string DataUrl(string data)
        {
            byte[] bytes=Convert.FromBase64String(data);
            bool jpeg=bytes.Length>2&&bytes[0]==255&&bytes[1]==216;
            string original="data:image/"+(jpeg?"jpeg":"png")+";base64,"+data;
            if(bytes.Length<128000)return original;
            using(var input=new MemoryStream(bytes))using(var source=Image.FromStream(input))
            {
                double scale=Math.Min(1,2048.0/Math.Max(source.Width,source.Height));
                using(var bitmap=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale))))
                {
                    using(var graphics=Graphics.FromImage(bitmap)){
                        graphics.Clear(Color.White);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(source,0,0,bitmap.Width,bitmap.Height);
                    }
                    using(var output=new MemoryStream())using(var parameters=new EncoderParameters(1)){
                        parameters.Param[0]=new EncoderParameter(Encoder.Quality,90L);
                        bitmap.Save(output,ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid),parameters);
                        byte[] compressed=output.ToArray();
                        if(scale==1&&compressed.Length>=bytes.Length)return original;
                        return "data:image/jpeg;base64,"+Convert.ToBase64String(compressed);
                    }
                }
            }
        }
    }
}
