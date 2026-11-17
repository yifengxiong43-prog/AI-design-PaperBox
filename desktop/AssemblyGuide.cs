using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperStudio
{
    // Geometric evidence, never a substitute for the API creative sketch or a production dieline.
    public static class AssemblyGuide
    {
        static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(48,55,53));
        static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(112,120,116));
        static readonly Pen Outline=new Pen(Ink,1.6), Dimension=new Pen(Muted,1);
        static string N(decimal x){return x.ToString("0.##",CultureInfo.InvariantCulture);}
        static string Size(decimal w,decimal d,decimal h){return N(w)+" × "+N(d)+" × "+N(h)+" mm";}
        static string Preview(string text,int limit)
        {return String.IsNullOrEmpty(text)?"未记录":text.Length<=limit?text:text.Substring(0,limit)+"…（完整说明见文字账本）";}
        static void Text(DrawingContext dc,string value,double x,double y,double size=20,double width=1650)
        {
            var text=new FormattedText(value??"",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,Ink,1.0){MaxTextWidth=Math.Max(1,width)};
            dc.DrawText(text,new Point(x,y));
        }
        static void Header(DrawingContext dc,string title,string subtitle)
        {
            Text(dc,title,48,30,30);Text(dc,subtitle,48,80,19);
            dc.DrawLine(new Pen(Brushes.LightGray,1),new Point(48,122),new Point(1752,122));
        }
        static void Horizontal(DrawingContext dc,double left,double right,double edge,double y,string label)
        {
            dc.DrawLine(Dimension,new Point(left,edge),new Point(left,y+7));dc.DrawLine(Dimension,new Point(right,edge),new Point(right,y+7));
            dc.DrawLine(Dimension,new Point(left,y),new Point(right,y));
            foreach(double x in new[]{left,right})dc.DrawLine(Outline,new Point(x-4,y+5),new Point(x+4,y-5));
            var text=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),18,Ink,1.0);
            double x0=(left+right-text.Width)/2;
            dc.DrawRectangle(Brushes.White,null,new Rect(x0-5,y-13,text.Width+10,26));dc.DrawText(text,new Point(x0,y-13));
        }
        static void Vertical(DrawingContext dc,double top,double bottom,double edge,double x,string label)
        {
            dc.DrawLine(Dimension,new Point(edge,top),new Point(x-7,top));dc.DrawLine(Dimension,new Point(edge,bottom),new Point(x-7,bottom));
            dc.DrawLine(Dimension,new Point(x,top),new Point(x,bottom));
            foreach(double y in new[]{top,bottom})dc.DrawLine(Outline,new Point(x-5,y+4),new Point(x+5,y-4));
            var text=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),18,Ink,1.0);
            double y0=(top+bottom+text.Width)/2;
            dc.PushTransform(new RotateTransform(-90,x,y0));dc.DrawRectangle(Brushes.White,null,new Rect(x-5,y0-13,text.Width+10,26));dc.DrawText(text,new Point(x,y0-13));dc.Pop();
        }
        static Rect Fitted(Rect area,decimal width,decimal height)
        {
            double scale=Math.Min(area.Width/(double)width,area.Height/(double)height);
            return new Rect(area.X+(area.Width-(double)width*scale)/2,area.Y+(area.Height-(double)height*scale)/2,(double)width*scale,(double)height*scale);
        }
        static void Box(DrawingContext dc,Rect box,decimal width,decimal height,string widthName,string heightName)
        {
            dc.DrawRectangle(null,Outline,box);Horizontal(dc,box.Left,box.Right,box.Bottom,box.Bottom+30,widthName+" "+N(width)+" mm");
            Vertical(dc,box.Top,box.Bottom,box.Left,box.Left-30,heightName+" "+N(height)+" mm");
        }
        static void Unit(DrawingContext dc,DisplayUnit u,AssemblyPlan p,PackagingConstraints c,Rect box)
        {
            bool vertical=p.Orientation=="vertical";double scale=box.Width/(double)u.Width;
            // Container outline is see-through evidence, not a tall opaque front wall.
            dc.DrawRectangle(null,Outline,box);
            foreach(var pen in AssemblyGeometry.PenBoxes(u,p,c,true)){
                double x=box.Left+(double)(pen.X-u.Open.X)*scale;
                double y=vertical?box.Top+(double)(u.Height-(pen.Z-u.Open.Z)-pen.Height)*scale:box.Top+(double)(pen.Y-u.Open.Y)*scale;
                var body=new Rect(x,y,(double)pen.Width*scale,(double)(vertical?pen.Height:pen.Depth)*scale);
                dc.DrawRoundedRectangle(Brushes.WhiteSmoke,new Pen(Muted,Math.Max(0.5,Math.Min(1,scale))),body,Math.Min(3,body.Width/5),Math.Min(3,body.Height/5));
                bool lengthVertical=p.LengthAxis!="Width";
                var cap=lengthVertical?new Rect(body.X,body.Y,body.Width,body.Height*0.12):new Rect(body.X,body.Y,body.Width*0.12,body.Height);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(144,169,153)),null,cap);
            }
        }
        static Picture Render(int height,string name,Action<DrawingContext> draw)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,1800,height));draw(dc);}
            var bitmap=new RenderTargetBitmap(1800,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=new MemoryStream()){encoder.Save(stream);return new Picture{Name=name,Data=Convert.ToBase64String(stream.ToArray())};}
        }
        public static Picture Create(PackagingConstraints c,Concept concept)
        {
            AssemblyGeometry.Calculate(concept,c);var p=concept.Assembly;
            if(p==null)throw new InvalidOperationException("请先重新生成方案，建立逐组结构后再制作尺寸参考。");
            bool vertical=p.Orientation=="vertical";decimal projectedHeight=vertical?p.OpenHeight:p.OpenDepth;
            string ids=String.Join("、",p.Units.Select(u=>u.Id));
            return Render(1400,"计算结构总览："+ids+"；所有笔均按完整笔长和直径绘制；同一视图等比例，视图之间比例独立。框线为包络投影，不是实际挡笔的前壁，也不是生产刀模。",dc=>{
                Header(dc,"结构尺寸参考 · 全部笔身与同一组单元",c.MarkerCount+" 支 · 完整笔长 "+N(c.MarkerLength)+" mm · 最大外径 "+N(c.MarkerDiameter)+" mm · 以下尺寸均为 mm");
                Text(dc,vertical?"01  笔组完全展开 · 正面 XZ 投影":"01  笔组完全展开 · 顶面 XY 投影",48,145,24);
                Rect area=Fitted(new Rect(130,210,1540,510),p.OpenWidth,projectedHeight);double scale=area.Width/(double)p.OpenWidth;
                foreach(var u in p.Units){
                    double left=area.Left+(double)u.Open.X*scale,top=area.Top+(double)(vertical?p.OpenHeight-u.Open.Z-u.Height:u.Open.Y)*scale;
                    var box=new Rect(left,top,(double)u.Width*scale,(double)(vertical?u.Height:u.Depth)*scale);Unit(dc,u,p,c,box);
                    if(box.Width>=100){int limit=Math.Max(1,(int)(box.Width/18)-5);string id=u.Id.Length>limit?u.Id.Substring(0,limit)+"…":u.Id;Text(dc,id+" · "+u.Count+"支",box.Left,box.Top-28,17,box.Width);}
                }
                Horizontal(dc,area.Left,area.Right,area.Bottom,770,"笔组 W "+N(p.OpenWidth)+" mm");Vertical(dc,area.Top,area.Bottom,area.Left,75,(vertical?"笔组 H ":"笔组 D ")+N(projectedHeight)+" mm");
                Text(dc,"笔组展开包络 W × D × H："+Size(p.OpenWidth,p.OpenDepth,p.OpenHeight)+"（不含盖板/支撑）",48,810,22);
                Text(dc,p.OpenReserve==null?"旧方案未记录全结构展开预算；此图不能代表整体占地。":"全结构展开预算范围："+Size(p.OverallOpenWidth,p.OverallOpenDepth,p.OverallOpenHeight)+"；附加预算及依据见后页。",48,850,19);
                Text(dc,"02  最终包装闭合外包络 · 三个正交方向",48,905,24);
                var b=concept.PenLayout;
                var front=Fitted(new Rect(120,995,440,230),b.BoxWidth,b.BoxHeight);Box(dc,front,b.BoxWidth,b.BoxHeight,"W","H");Text(dc,"正面 W × H",120,955,20);
                var topView=Fitted(new Rect(730,995,440,230),b.BoxWidth,b.BoxDepth);Box(dc,topView,b.BoxWidth,b.BoxDepth,"W","D");Text(dc,"顶面 W × D",730,955,20);
                var side=Fitted(new Rect(1340,995,330,230),b.BoxDepth,b.BoxHeight);Box(dc,side,b.BoxDepth,b.BoxHeight,"D","H");Text(dc,"侧面 D × H",1340,955,20);
                Text(dc,"最终包装外径 "+Size(b.BoxWidth,b.BoxDepth,b.BoxHeight)+"   |   内托壁厚 "+N(p.WallThickness)+" mm"+(p.Shell==null?"（旧版未分列壳厚）":"；独立壳厚 "+N(p.Shell.WallThickness)+" mm"),48,1290,20);
                Text(dc,"此页只约束尺寸、数量与位置；外观、连接动作和线稿表达请参考原始设计草图。装配与纸板实际厚度需打样。",48,1335,18);
            });
        }
        class Detail
        {
            public List<DisplayUnit> Units;public List<AssemblyPart> Parts;
            public string Ids {get{return Units!=null?String.Join("、",Units.Select(u=>u.Id)):String.Join("、",Parts.Select(p=>p.Id));}}
            public decimal Width {get{return Units!=null?Units[0].Width:Parts[0].Width;}}
            public decimal Depth {get{return Units!=null?Units[0].Depth:Parts[0].Depth;}}
            public decimal Height {get{return Units!=null?Units[0].Height:Parts[0].Height;}}
        }
        public static List<Picture> CreatePages(PackagingConstraints c,Concept concept)
        {
            var pages=new List<Picture>{Create(c,concept)};var p=concept.Assembly;
            bool hasBudget=p.Shell!=null||p.OpenReserve!=null;
            if(hasBudget)pages.Add(BudgetPage(p,concept.PenLayout));
            int detailPageLimit=hasBudget?1:2;
            // Same geometry is drawn once, explicitly shared by every listed ID; quantities are never merged.
            var details=p.Units.GroupBy(u=>u.Count+"|"+Size(u.Width,u.Depth,u.Height)).Select(g=>new Detail{Units=g.ToList()})
                .Concat(p.Parts.GroupBy(x=>x.SizeBasis+"|"+Size(x.Width,x.Depth,x.Height)+"|"+AssemblyGeometry.SizeText(x.ClosedSize)+"|"+x.PlacementScope).Select(g=>new Detail{Parts=g.ToList()})).ToList();
            for(int page=0;page<Math.Min(detailPageLimit,(details.Count+3)/4);page++){
                var selected=details.Skip(page*4).Take(4).ToList();int pageNumber=page+1;
                string caption="逐部件尺寸引线 · "+String.Join("；",selected.Select(d=>d.Ids+" = "+(d.Units!=null?"单元外径 ":AssemblyGeometry.PartSizeLabel(d.Parts[0])+" ")+Size(d.Width,d.Depth,d.Height)+(d.Parts==null?"":"；闭合安装 "+AssemblyGeometry.SizeText(d.Parts[0].ClosedSize)+"；"+String.Join("；",d.Parts.Select(a=>a.Id+"："+a.AssemblyNotes)))))+"。同规格共享图示，每个编号在最终草图独立标注。图示不是设计外形或刀模。";
                pages.Add(Render(1500,caption,dc=>{
                    Header(dc,"单元与部件 · 尺寸引线 "+pageNumber,"每个编号必须在最终草图出现；同规格共享这一组尺寸。各小图独立缩放，数值优先。框线是包络，外形按所选方案。");
                    for(int i=0;i<selected.Count;i++){
                        var d=selected[i];double x=48+(i%2)*880,y=155+(i/2)*650;
                        string names=d.Ids.Length>40?d.Ids.Substring(0,38)+"…（全部编号见本图说明）":d.Ids;Text(dc,names,x,y,24,800);
                        bool isUnit=d.Units!=null,vertical=p.Orientation=="vertical";
                        decimal projectedHeight=isUnit&&vertical?d.Height:d.Depth;
                        Rect box=Fitted(new Rect(x+90,y+100,610,295),d.Width,projectedHeight);
                        if(isUnit)Unit(dc,d.Units[0],p,c,box);
                        Box(dc,box,d.Width,projectedHeight,"W",isUnit&&vertical?"H":"D");
                        Text(dc,(isUnit?"每组外尺寸 ":AssemblyGeometry.PartSizeLabel(d.Parts[0])+" ")+Size(d.Width,d.Depth,d.Height),x,y+455,20,815);
                        if(isUnit){
                            var u=d.Units[0];Text(dc,"每组 "+u.Count+" × 1 单排 · 净尺寸 "+Size(u.Width-2*p.WallThickness,u.Depth-2*p.WallThickness,u.Height-2*p.WallThickness),x,y+492,18,815);
                            Text(dc,"完整笔长 "+N(c.MarkerLength)+" · 笔径 "+N(c.MarkerDiameter)+" · 槽净长 "+N(c.MarkerLength+p.LengthClearance)+" mm",x,y+529,18,815);
                            Text(dc,"总排宽余量 "+N(p.TotalClearance)+" · 每间隙 "+N(p.BetweenGap)+" · 隔板 "+N(p.DividerThickness)+" · 壁厚 "+N(p.WallThickness)+" mm",x,y+566,17,815);
                        }else{
                            var part=d.Parts[0];Text(dc,"闭合安装 W×D×H："+AssemblyGeometry.SizeText(part.ClosedSize)+"；归属 "+(part.PlacementScope??"未记录"),x,y+492,18,815);
                            string roles=String.Join("；",d.Parts.Select(a=>a.Role).Distinct());Text(dc,roles.Length>70?roles.Substring(0,68)+"…":roles,x,y+529,17,815);
                            string notes=String.Join("；",d.Parts.Select(a=>a.Id+"："+(a.AssemblyNotes??"安装状态未记录")));Text(dc,notes.Length>72?notes.Substring(0,70)+"…（完整说明见账本）":notes,x,y+566,17,815);
                        }
                    }
                    Text(dc,"尺寸线必须准确指向对应单元；每个展开件都应有编号与尺寸。效果图、展开图和草图保持同一结构，不另造盒型。",48,1450,18);
                }));
            }
            if(details.Count>detailPageLimit*4||details.Any(d=>d.Ids.Length>40))pages.Add(Ledger(p));return pages;
        }
        static Picture Ledger(AssemblyPlan p)
        {
            var rows=p.Units.Select(u=>u.Id+" | "+u.Count+"支 | "+Size(u.Width,u.Depth,u.Height)).Concat(p.Parts.Select(a=>a.Id+" | "+AssemblyGeometry.PartSizeLabel(a)+" "+Size(a.Width,a.Depth,a.Height)+" | 安装 "+AssemblyGeometry.SizeText(a.ClosedSize))).ToList();
            return Render(1800,"完整逐件尺寸账本（W×D×H）："+String.Join("；",rows)+"。前页为部分代表几何；本页及文字账本列出所有单元和部件。最终草图仍须逐件标注。",dc=>{
                Header(dc,"完整逐件尺寸账本","部件较多时，前页画代表几何，本页补齐全部编号与尺寸。所有数值为 W × D × H（mm），最终草图仍须逐件引线标注。");
                int perColumn=(rows.Count+1)/2;double line=Math.Min(32,1580.0/Math.Max(1,perColumn)),font=Math.Min(19,Math.Max(9,line-4));
                for(int i=0;i<rows.Count;i++)Text(dc,rows[i],48+(i/perColumn)*880,160+(i%perColumn)*line,font,820);
                Text(dc,"纸板厚度、连接尺寸是设计假设；结构、承载与实际开合需白样验证。",48,1755,18);
            });
        }
        static Picture BudgetPage(AssemblyPlan p,PenLayout package)
        {
            string caption=AssemblyGeometry.ShellDescription(p)+"\n笔组展开 "+Size(p.OpenWidth,p.OpenDepth,p.OpenHeight)+"；展开附加预算 "+AssemblyGeometry.SizeText(p.OpenReserve)+"；全结构展开预算 "+Size(p.OverallOpenWidth,p.OverallOpenDepth,p.OverallOpenHeight)+"；依据："+p.OpenReserveBasis;
            return Render(1500,"外壳内空与全结构预算："+caption,dc=>{
                Header(dc,"外壳内空与全结构预算","尺寸为设计假设；本页区分装载、装配和附加结构占用，不绘制未经验证的折线或转轴。");
                Text(dc,"01  外壳 · W × D × H",48,155,25);
                if(p.Shell==null)Text(dc,AssemblyGeometry.ShellDescription(p),48,210,21,1660);
                else
                {
                    var s=p.Shell;
                    Text(dc,"独立壳厚 "+N(s.WallThickness)+" mm；每侧装配净余量 "+N(s.AssemblyClearance)+" mm（每轴加两次）",48,210,21);
                    Text(dc,"内置结构占用总量："+AssemblyGeometry.SizeText(s.InternalReserve)+"；外部突出总量："+AssemblyGeometry.SizeText(s.ExternalReserve),48,260,21);
                    Text(dc,"壳内空："+AssemblyGeometry.SizeText(s.InnerSize),48,310,23);
                    Text(dc,"壳本体外径（未加外部突出）："+AssemblyGeometry.SizeText(s.OuterSize),48,360,23);
                    Text(dc,"预算依据："+Preview(s.Basis,220),48,420,20,1660);
                }
                Text(dc,"最终包装外径（含闭合突出）："+Size(package.BoxWidth,package.BoxDepth,package.BoxHeight),48,560,24);
                Text(dc,"内空 = 单元闭合包络 + 内置结构总量 + 2 × 每侧装配净余量",48,620,21);
                Text(dc,"壳本体外径 = 内空 + 2 × 独立壳厚；最终包装外径 = 壳本体外径 + 外部突出总量",48,665,21);
                Text(dc,"02  展开 · 笔组与全结构分列",48,765,25);
                Text(dc,"笔组展开包络："+Size(p.OpenWidth,p.OpenDepth,p.OpenHeight),48,825,22);
                Text(dc,"盖板/底座/支撑附加总量："+AssemblyGeometry.SizeText(p.OpenReserve),48,875,22);
                Text(dc,"全结构展开预算范围："+Size(p.OverallOpenWidth,p.OverallOpenDepth,p.OverallOpenHeight),48,925,24);
                Text(dc,"展开预算依据："+Preview(p.OpenReserveBasis,220),48,985,20,1660);
                Text(dc,"03  部件尺寸的状态",48,1155,25);
                Text(dc,"flat = 平片 W × D × 厚度；formed = 成形 W × D × H。ClosedSize始终为安装轴向下的闭合包络。",48,1210,21);
                Text(dc,"安装说明必须给出接端、折线、胶接与支撑关系。尺寸上限通过不代表零件无相交、铰链可达或支撑稳定。",48,1270,20);
                Text(dc,"部件的具体位置与展开运动仍需独立评审及白样验证；这不是生产刀模。",48,1415,19);
            });
        }
    }
}
