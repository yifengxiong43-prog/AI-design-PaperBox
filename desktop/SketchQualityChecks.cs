// Test-only native WPF checks; never compiled into the distributed application.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace PaperStudio
{
    public static class SketchQualityChecks
    {
        static void Check(bool pass,string message){if(!pass)throw new Exception(message);}
        public static void Run(string output)
        {
            Directory.CreateDirectory(output);
            // A transparent canvas with a white rectangle is empty on the intended white review surface.
            using(var bitmap=new System.Drawing.Bitmap(80,80))using(var graphics=System.Drawing.Graphics.FromImage(bitmap))using(var stream=new MemoryStream()){
                graphics.Clear(System.Drawing.Color.Transparent);graphics.FillRectangle(System.Drawing.Brushes.White,10,10,60,60);
                bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);bool rejected=false;
                try{SketchOutput.RequireTransparent(Convert.ToBase64String(stream.ToArray()));}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"Transparent white-only image must not pass as a line-art sketch");
            }
            foreach(var name in Enumerable.Range(1,5).Select(i=>"desktop/resources/sketch-"+i.ToString("00")+".png")){
                string original=Convert.ToBase64String(File.ReadAllBytes(name));SketchOutput.RequireTransparent(original);
                bool rejected=false;try{SketchOutput.RequireTransparent(SketchOutput.ReviewCopy(original));}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"Only the review copy is composited; original reference keeps real alpha");
            }
            var c=new PackagingInput{Count="12",Length="150",Diameter="15",Tier="large-standard",AllowanceCm="4",PenType="alcohol"}.Build();
            var concept=new Concept{Assembly=new AssemblyPlan{LengthClearance=24,Assumptions="QA设计假设",Units=new List<DisplayUnit>{
                new DisplayUnit{Id="A",Count=6,Columns=6,Motion="左侧展开",Closed=new Placement(),Open=new Placement()},
                new DisplayUnit{Id="B",Count=6,Columns=6,Motion="右侧展开",Closed=new Placement{Y=24},Open=new Placement{X=104}}
            },Parts=new List<AssemblyPart>{new AssemblyPart{Id="盖板",Role="闭合保护",Width=98,Depth=47,Height=1},new AssemblyPart{Id="连接片",Role="连接两组",Width=20,Depth=40,Height=1}}}};
            foreach(string axis in new[]{"Height","Width","Depth"})
            {
                var item=Json.Copy(concept);item.Assembly.LengthAxis=axis;item.Assembly.Orientation=axis=="Height"?"vertical":"horizontal";
                if(axis!="Height"){item.Assembly.Units[1].Closed=new Placement{Z=24};item.Assembly.Units[1].Open=axis=="Width"?new Placement{Y=104}:new Placement{X=104};}
                var pages=AssemblyGuide.CreatePages(c,item);Check(pages.Count>=2&&pages.Count<=4,"Bounded readable reference sheets");
                Check(pages.Any(p=>p.Name.Contains("A")&&p.Name.Contains("B"))&&pages.Any(p=>p.Name.Contains("连接片")),"Every group and part is identified in reference descriptions");
                for(int i=0;i<pages.Count;i++){
                    var bytes=Convert.FromBase64String(pages[i].Data);File.WriteAllBytes(Path.Combine(output,axis+"-"+i+".png"),bytes);
                    using(var input=new MemoryStream(bytes)){
                        var frame=BitmapFrame.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                        Check(frame.PixelWidth<=1800&&frame.PixelHeight<=1800,"Text model compression must not shrink guides below their intended resolution");
                        Check(frame.PixelWidth>=1600&&frame.PixelHeight>=1000,"Dimension arrows and numbers have enough space");
                    }
                }
                var unit=item.Assembly.Units[0];var boxes=AssemblyGeometry.PenBoxes(unit,item.Assembly,c,true).ToList();
                Check(boxes.Count==6&&boxes.All(b=>(axis=="Height"?b.Height:axis=="Width"?b.Width:b.Depth)==150),"Every guide pen uses the complete physical input length");
            }
            var many=Json.Copy(concept);many.Assembly.Parts=Enumerable.Range(1,40).Select(i=>new AssemblyPart{Id="测试部件"+i,Role="QA尺寸标注",Width=20+i,Depth=40+i,Height=1}).ToList();
            var dense=AssemblyGuide.CreatePages(c,many);Check(dense.Count==4,"Complex diagrams use a complete ledger instead of unbounded images");
            Check(dense.Last().Name.Contains("测试部件40"),"Dense fallback retains the last part, without silent truncation");
            File.WriteAllBytes(Path.Combine(output,"complex-ledger.png"),Convert.FromBase64String(dense.Last().Data));
        }
    }
}
