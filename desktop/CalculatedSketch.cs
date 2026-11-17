using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace PaperStudio
{
    public static class CalculatedSketch
    {
        static readonly Color Ink=Color.FromArgb(36,48,43);
        static readonly Color Green=Color.FromArgb(52,137,98);
        static readonly Color[] Caps={Color.FromArgb(228,108,116),Color.FromArgb(236,164,75),Color.FromArgb(225,202,77),Color.FromArgb(91,169,113),Color.FromArgb(81,142,190),Color.FromArgb(160,113,185)};

        static string N(decimal value){return value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);}
        static void Label(Graphics g,string text,float x,float y,int size=20,Color? color=null)
        {
            using(var font=new Font("Microsoft YaHei",size,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(color??Ink))g.DrawString(text,font,brush,x,y);
        }
        static void Round(Graphics g,Pen pen,Brush fill,float x,float y,float w,float h,float radius)
        {
            using(var path=new GraphicsPath()){
                float d=Math.Min(radius*2,Math.Min(w,h));
                path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);
                path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();
                if(fill!=null)g.FillPath(fill,path);g.DrawPath(pen,path);
            }
        }
        static void Arrow(Graphics g,Pen pen,float x1,float y1,float x2,float y2)
        {
            g.DrawLine(pen,x1,y1,x2,y2);
            float dx=x2-x1,dy=y2-y1,length=(float)Math.Sqrt(dx*dx+dy*dy);
            if(length<1)return;
            dx/=length;dy/=length;
            g.DrawLine(pen,x2,y2,x2-8*dx-5*dy,y2-8*dy+5*dx);
            g.DrawLine(pen,x2,y2,x2-8*dx+5*dy,y2-8*dy-5*dx);
        }
        static void Closed(Graphics g,Concept concept,float x,float y)
        {
            var box=concept.PenLayout;
            float scale=Math.Min(390f/(float)box.BoxWidth,300f/(float)box.BoxHeight);
            float w=(float)box.BoxWidth*scale,h=(float)box.BoxHeight*scale;
            using(var pen=new Pen(Ink,3))using(var light=new SolidBrush(Color.FromArgb(35,211,233,220)))
            using(var accent=new Pen(Green,3))
            {
                Round(g,pen,light,x,y,w,h,22);
                if(concept.DesignOrigin=="controlled-drawer"){
                    Round(g,accent,null,x+w/2-32,y+3,64,11,5);
                    for(int i=0;i<4;i++){
                        float yy=y+18+(h-36)*i/4f;
                        g.DrawLine(pen,x+16,yy,x+w-16,yy);
                        Label(g,"ABCD"[i].ToString(),x+25,yy+3,17);
                        Round(g,accent,null,x+w/2-13,yy+(h-36)/8f,26,7,3);
                    }
                    g.DrawLine(pen,x+16,y+h-18,x+w-16,y+h-18);
                }else if(concept.DesignOrigin=="controlled-wing"){
                    Round(g,accent,null,x+w/2-32,y+4,64,11,5);
                    g.DrawLine(accent,x+w/2-8,y+14,x+w/2-8,y+h-14);
                    g.DrawLine(accent,x+w/2+8,y+14,x+w/2+8,y+h-14);
                    for(int i=1;i<3;i++)g.DrawLine(pen,x+20,y+i*h/3f,x+w-20,y+i*h/3f);
                    Label(g,"连体折页",x+32,y+h/2-18,20,Green);
                }else{
                    g.DrawLine(accent,x+12,y+32,x+w-12,y+32);
                    Round(g,pen,null,x+w/2-43,y+7,86,16,8);
                    Label(g,"可提携箱盖",x+30,y+h/2-12,22,Green);
                }
                Arrow(g,accent,x,y+h+34,x+w,y+h+34);
                Label(g,"W "+N(box.BoxWidth)+" mm",x+w/2-60,y+h+38,18);
                Arrow(g,accent,x-25,y,x-25,y+h);
                Label(g,"H "+N(box.BoxHeight)+" mm",x-20,y-34,18);
                Label(g,"D "+N(box.BoxDepth)+" mm",x+2,y+h+72,18);
            }
        }
        static void Unit(Graphics g,DisplayUnit unit,AssemblyPlan plan,PackagingConstraints constraints,float x,float y,float scale,int unitIndex)
        {
            bool vertical=plan.Orientation=="vertical";
            float width=(float)unit.Width*scale,height=(float)(vertical?unit.Height:unit.Depth)*scale;
            using(var pen=new Pen(Ink,2))using(var inner=new Pen(Color.FromArgb(110,Ink),1))
            using(var fill=new SolidBrush(Color.FromArgb(25,Caps[unitIndex%Caps.Length])))
            using(var cap=new SolidBrush(Caps[unitIndex%Caps.Length]))
            {
                Round(g,pen,fill,x,y,width,height,10);
                for(int k=0;k<unit.Count;k++){
                    float diameter=(float)constraints.MarkerDiameter*scale;
                    float length=(float)constraints.MarkerLength*scale;
                    float clearance=(float)plan.LengthClearance*scale/2f;
                    float px,py,pw,ph;
                    if(vertical){px=x+(float)plan.WallThickness*scale+(float)plan.TotalClearance*scale/2f+k*diameter;py=y+(height-length)/2f;pw=diameter*.78f;ph=length;}
                    else if(plan.LengthAxis=="Depth"){px=x+(float)plan.WallThickness*scale+(float)plan.TotalClearance*scale/2f+k*diameter;py=y+(float)plan.WallThickness*scale+clearance;pw=diameter*.78f;ph=length;}
                    else{px=x+(float)plan.WallThickness*scale+clearance;py=y+(float)plan.WallThickness*scale+(float)plan.TotalClearance*scale/2f+k*diameter;pw=length;ph=diameter*.78f;}
                    Round(g,inner,null,px,py,pw,ph,Math.Min(5,pw/3));
                    if(vertical||plan.LengthAxis=="Depth"){
                        float capHeight=Math.Min(7,ph/9);
                        g.FillRectangle(cap,px+1,py+1,Math.Max(1,pw-2),capHeight);
                        if(constraints.PenType=="alcohol")g.FillRectangle(cap,px+1,py+ph-capHeight-1,Math.Max(1,pw-2),capHeight);
                    }else{
                        float capWidth=Math.Min(7,pw/9);
                        g.FillRectangle(cap,px+1,py+1,capWidth,Math.Max(1,ph-2));
                        if(constraints.PenType=="alcohol")g.FillRectangle(cap,px+pw-capWidth-1,py+1,capWidth,Math.Max(1,ph-2));
                    }
                }
                decimal left=plan.Units.Min(u=>u.Open.X),right=plan.Units.Max(u=>u.Open.X);
                float labelX=unit.Open.X==left?x-77:unit.Open.X==right?x+width+8:x+8;
                float labelY=unit.Open.X==left||unit.Open.X==right?y+height/2f-10:y-28;
                Label(g,unit.Id+" "+unit.Count+"支",labelX,labelY,18,Green);
                if(unitIndex>=0&&unitIndex<plan.Units.Count&&plan.Parts.Any(p=>p.Id=="抽屉导向"+unit.Id)){
                    g.DrawArc(pen,x+width/2f-13,y+height-4,26,16,0,180);
                }
            }
        }
        public static string Create(PackagingConstraints constraints,Concept concept)
        {
            if(constraints==null||concept==null||concept.Assembly==null)throw new InvalidOperationException("缺少可计算结构，不能绘制草图。");
            AssemblyGeometry.Calculate(concept,constraints);
            var plan=concept.Assembly;
            if(plan.Units.Sum(u=>u.Count)!=constraints.MarkerCount)throw new InvalidOperationException("笔数账本不一致，不能绘制草图。");
            bool vertical=plan.Orientation=="vertical";
            float extentX=(float)plan.OpenWidth,extentY=(float)(vertical?plan.OpenHeight:plan.OpenDepth);
            float scale=Math.Min(900f/extentX,590f/extentY);
            using(var bitmap=new Bitmap(1800,1080,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(bitmap))using(var stream=new MemoryStream())
            using(var dimension=new Pen(Green,2))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Label(g,concept.Name,64,35,34);
                Label(g,"概念结构草图  ·  尺寸为设计选值，需实物与白样确认",64,87,18,Green);
                Label(g,"闭合状态",64,158,24);
                Label(g,"完全展开  ·  "+(vertical?"正视":"俯视"),635,115,24);
                Closed(g,concept,100,245);
                for(int i=0;i<plan.Units.Count;i++){
                    var u=plan.Units[i];Unit(g,u,plan,constraints,650+(float)u.Open.X*scale,190+(float)(vertical?u.Open.Z:u.Open.Y)*scale,scale,i);
                }
                if(concept.DesignOrigin=="controlled-wing"){
                    using(var hinge=new Pen(Green,4))for(int i=1;i<plan.Units.Count;i++){
                        float x=650+(float)plan.Units[i].Open.X*scale-2f*scale;
                        g.DrawLine(hinge,x,192,x,190+(float)plan.Units[i].Depth*scale-2);
                    }
                    Label(g,"连体折页 · 笔翼不拆离",1040,144,18,Green);
                }
                Arrow(g,dimension,650,820,650+extentX*scale,820);
                Label(g,"展开笔组 W "+N(plan.OpenWidth)+" mm",700,827,18);
                Arrow(g,dimension,620,190,620,190+extentY*scale);
                Label(g,"展开笔组 "+(vertical?"H "+N(plan.OpenHeight):"D "+N(plan.OpenDepth))+" mm",650,850,18);
                Label(g,"每组尺寸 W×D×H (mm)",64,760,19,Green);
                for(int i=0;i<plan.Units.Count;i++){
                    var u=plan.Units[i];Label(g,u.Id+" · "+u.Count+"支 · "+N(u.Width)+"×"+N(u.Depth)+"×"+N(u.Height),64+(i%2)*330,800+(i/2)*34,17);
                }
                Label(g,"单支完整笔长 "+N(constraints.MarkerLength)+" mm  ·  最大外径 "+N(constraints.MarkerDiameter)+" mm",64,943,19);
                Label(g,"每组单排；总数 "+constraints.MarkerCount+" 支。闭合与展开为同一组部件；纸厚、连接、开启力、承重与合规待打样。",64,980,18);
                bitmap.Save(stream,ImageFormat.Png);
                string data=Convert.ToBase64String(stream.ToArray());SketchOutput.RequireTransparent(data);return data;
            }
        }
    }
}
