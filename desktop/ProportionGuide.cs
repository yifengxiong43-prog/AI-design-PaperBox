using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperStudio
{
    // A measured reference sent to the image API, never presented as a generated design.
    public static class ProportionGuide
    {
        static string N(decimal value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
        public static Picture Create(PackagingConstraints constraints,Concept concept)
        {
            constraints.ValidateLayout(concept.PenLayout,concept.PackageDimensions,concept.Assembly!=null);
            var layout=concept.PenLayout;
            bool vertical=layout.Orientation=="vertical";
            decimal projectedWidth=layout.LengthAxis=="Depth"?layout.BoxDepth:layout.BoxWidth;
            double scale=Math.Min(820.0/(double)projectedWidth,430.0/(double)layout.BoxHeight);
            double width=(double)projectedWidth*scale,height=(double)layout.BoxHeight*scale;
            double x=190+(820-width)/2,y=210+(430-height)/2;
            double penLength=(double)constraints.MarkerLength*scale;
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,1200,820));
                Action<string,double,double,double,Brush> label=(text,left,top,size,color)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,color,1.0),new Point(left,top));
                label("MEASURED PROPORTION REFERENCE",40,28,30,Brushes.DarkSlateGray);
                label("MANDATORY GEOMETRY REFERENCE. Dashed envelope is not a required box shape or artwork.",40,75,21,Brushes.DimGray);
                label("Full pen length: "+N(constraints.MarkerLength)+" mm  |  "+constraints.PenType+"  |  "+layout.Orientation,40,112,22,Brushes.DarkSlateGray);
                label("Show the selected concept's actual layout; do not infer a fixed grid or recommended box size.",40,145,20,Brushes.DarkSlateGray);
                var outline=new Pen(Brushes.SlateGray,2){DashStyle=DashStyles.Dash};
                dc.DrawRectangle(null,outline,new Rect(x,y,width,height));
                label((layout.LengthAxis=="Depth"?"Depth":"Width")+" "+N(projectedWidth)+" mm",x,y-35,20,Brushes.DimGray);
                label("Height",28,y+height/2-22,20,Brushes.DimGray);
                label(N(layout.BoxHeight)+" mm",28,y+height/2+4,20,Brushes.DimGray);
                var pen=new Pen(Brushes.Crimson,2);var tick=new Pen(Brushes.Crimson,2);
                double penThickness=Math.Max(0.5, (double)(constraints.MarkerDiameter>0?constraints.MarkerDiameter: (constraints.PenType=="acrylic"?18m:17m))*scale);
                if(vertical)
                {
                    double px=x+width/2-penThickness/2,py=y+(height-penLength)/2;
                    dc.DrawRoundedRectangle(Brushes.MistyRose,pen,new Rect(px,py,penThickness,penLength),penThickness/3,penThickness/3);
                    dc.DrawRectangle(Brushes.IndianRed,null,new Rect(px,py+penLength*0.8,penThickness,penLength*0.2));
                    dc.DrawLine(tick,new Point(px-15,py),new Point(px+penThickness+15,py));
                    dc.DrawLine(tick,new Point(px-15,py+penLength),new Point(px+penThickness+15,py+penLength));
                }
                else
                {
                    double px=x+(width-penLength)/2,py=y+height/2-penThickness/2;
                    dc.DrawRoundedRectangle(Brushes.MistyRose,pen,new Rect(px,py,penLength,penThickness),penThickness/3,penThickness/3);
                    dc.DrawRectangle(Brushes.IndianRed,null,new Rect(px+penLength*0.8,py,penLength*0.2,penThickness));
                    dc.DrawLine(tick,new Point(px,py-15),new Point(px,py+penThickness+15));
                    dc.DrawLine(tick,new Point(px+penLength,py-15),new Point(px+penLength,py+penThickness+15));
                }
                label("Red full marker = FULL pen length including cap; diameter follows the entered measurement.",40,675,21,Brushes.Crimson);
                label("Pen / "+layout.LengthAxis+" = "+N(constraints.MarkerLength)+" / "+N(layout.PenAxisLength())+" = "+N(constraints.MarkerLength/layout.PenAxisLength()),40,714,24,Brushes.DarkSlateGray);
                label("Box W x D x H: "+N(layout.BoxWidth)+" x "+N(layout.BoxDepth)+" x "+N(layout.BoxHeight)+" mm. Design allowances are listed in the assembly ledger.",40,758,20,Brushes.DimGray);
            }
            var bitmap=new RenderTargetBitmap(1200,820,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=new MemoryStream())
            {
                encoder.Save(output);
                return new Picture{Name="程序按毫米等比绘制的笔长比例参考（最高比例依据）：红色完整笔示意为整支笔长，粗细遵循输入直径；虚线是盒体外径范围，不是指定盒型或图案。笔长/"+layout.LengthAxis+"="+N(constraints.MarkerLength/layout.PenAxisLength())+"。效果图和草图保留此比例，不能缩短笔来塞入盒内。",Data=Convert.ToBase64String(output.ToArray())};
            }
        }
    }
}
