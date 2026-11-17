using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PaperStudio
{
    public class Placement { public decimal X, Y, Z; }
    public class Size3 { public decimal Width, Depth, Height; }
    public class ShellPlan
    {
        public decimal WallThickness, AssemblyClearance; // Clearance is per side; reserve sizes are totals per axis.
        public Size3 InternalReserve, ExternalReserve;
        public string Basis;
        public Size3 InnerSize, OuterSize; // Derived cavity and shell envelope, excluding external protrusions.
    }
    public class PenBox
    {
        public int Index;
        public decimal X, Y, Z, Width, Depth, Height;
    }
    public class DisplayUnit
    {
        public string Id, Motion;
        public int Count, Rows=1, Columns;
        public Placement Closed, Open;
        public decimal Width, Depth, Height; // Recomputed, never trusted from model output.
    }
    public class AssemblyPart
    {
        public string Id, Role;
        public decimal Width, Depth, Height;
        public Size3 ClosedSize;
        public string SizeBasis, PlacementScope, AssemblyNotes;
    }
    public class RequirementCheck { public string RequirementId, Requirement, Implementation; }
    public class AssemblyPlan
    {
        public bool AllVisible=true;
        public string Orientation="vertical", LengthAxis="Height";
        public string OpenLayout; // Optional generated-response recipe; saved coordinates remain authoritative.
        public ShellPlan Shell; // Null preserves the legacy package-envelope calculation.
        public Size3 OpenReserve;
        public string OpenReserveBasis;
        // Design assumptions, not claims about measured stock. Total end-to-end clearance is distinct from each gap.
        public decimal TotalClearance=4m, BetweenGap=0m, DividerThickness=0m, WallThickness=1m, LengthClearance=20m;
        public bool CombinedLengthClearance=false; // Reference structures may reserve 4–6 mm once across a paired length.
        public List<DisplayUnit> Units=new List<DisplayUnit>();
        public List<AssemblyPart> Parts=new List<AssemblyPart>();
        public decimal OpenWidth, OpenDepth, OpenHeight;
        public decimal OverallOpenWidth, OverallOpenDepth, OverallOpenHeight;
        public string Assumptions;
    }
    public static class AssemblyGeometry
    {
        public static string RowAxis(AssemblyPlan plan)
        {return plan.Orientation=="vertical"||plan.LengthAxis=="Depth"?"Width":"Depth";}
        public static decimal RowExtent(DisplayUnit unit,AssemblyPlan plan)
        {return RowAxis(plan)=="Width"?unit.Width:unit.Depth;}
        public static decimal Span(int count,decimal diameter,decimal totalClearance,decimal gap=0m,decimal divider=0m)
        {
            if(count<=0||diameter<=0||totalClearance<0||gap<0||divider<0)throw new InvalidOperationException("笔数、笔径或间隙无效。");
            return count*diameter+totalClearance+(count-1)*(gap+divider);
        }
        public static void Calculate(Concept concept,PackagingConstraints constraints)
        {
            var p=concept.Assembly;if(p==null)return; // Saved pre-V0.7 projects remain readable.
            if(constraints==null||constraints.MarkerDiameter<=0)throw new InvalidOperationException("计算结构需要完整笔长和直径。");
            constraints.Validate();
            if(!p.AllVisible)throw new InvalidOperationException("展开必须一次看到全部笔，不能隐藏后排。");
            if(p.Units==null||p.Units.Count==0||p.Units.Count>100||p.Units.Any(u=>u==null||u.Count<=0)||p.Units.Sum(u=>(long)u.Count)!=constraints.MarkerCount)
                throw new InvalidOperationException("各组支数之和必须等于产品支数。");
            bool combined=p.CombinedLengthClearance;
            if(p.TotalClearance<0||p.TotalClearance>30||p.BetweenGap<0||p.BetweenGap>20||p.DividerThickness<0||p.DividerThickness>5||p.WallThickness<=0||p.WallThickness>5||p.LengthClearance>(combined?6m:60m)||(combined?(p.LengthClearance<4m):(p.LengthClearance<constraints.MinimumLengthClearanceMm)))
                throw new InvalidOperationException("尺寸余量不合理；总余量、每间隙、隔板和壁厚必须分别填写，长度方向留足空间。");
            if(String.IsNullOrWhiteSpace(p.Assumptions))throw new InvalidOperationException("必须说明所选间隙和纸板厚度是设计假设及选择理由。");
            if(p.Orientation!="vertical"&&p.Orientation!="horizontal")throw new InvalidOperationException("必须提供实际笔身姿态。");
            if(p.Orientation=="vertical"&&p.LengthAxis!="Height"||p.Orientation=="horizontal"&&p.LengthAxis!="Width"&&p.LengthAxis!="Depth")throw new InvalidOperationException("笔长方向与实际姿态不符。");
            if(p.Units.Select(u=>(u.Id??"").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.Units.Count)throw new InvalidOperationException("每组单元必须有不同编号。");
            foreach(var u in p.Units){
                if(u.Closed==null)throw new InvalidOperationException("单元 "+u.Id+" 缺少 Closed 闭合位置，必须返回完整的 X、Y、Z 毫米坐标。");
                if(u.Open==null)throw new InvalidOperationException("单元 "+u.Id+" 缺少 Open 展开位置，必须返回完整的 X、Y、Z 毫米坐标。");
            }
            // A centred origin is valid. Translate each complete state, never clamp individual units.
            NormalizeOrigin(p.Units.Select(u=>u.Closed));NormalizeOrigin(p.Units.Select(u=>u.Open));
            foreach(var u in p.Units)
            {
                if(String.IsNullOrWhiteSpace(u.Id)||String.IsNullOrWhiteSpace(u.Motion)||u.Rows!=1||u.Columns!=u.Count)
                    throw new InvalidOperationException("每个可见单元必须是单排：12支应为12×1；6×2须拆成两个可见单元并分别计算。");
                SizeUnit(u,p,constraints);
            }
            for(int i=0;i<p.Units.Count;i++)for(int j=0;j<i;j++)
            {
                var a=p.Units[i];var b=p.Units[j];
                if(Overlap(a.Closed.X,a.Width,b.Closed.X,b.Width)&&Overlap(a.Closed.Y,a.Depth,b.Closed.Y,b.Depth)&&Overlap(a.Closed.Z,a.Height,b.Closed.Z,b.Height))throw new InvalidOperationException((constraints.TrayHeightMm.HasValue?"托盘高度 "+N(constraints.TrayHeightMm.Value)+" mm 与当前闭合排布冲突：":"闭合状态单元相交：")+a.Id+" / "+b.Id+"；请调整层间位置或结构，不会改动输入高度。");
                bool visible=p.Orientation=="vertical"?!Overlap(a.Open.X,a.Width,b.Open.X,b.Width)||!Overlap(a.Open.Z,a.Height,b.Open.Z,b.Height):!Overlap(a.Open.X,a.Width,b.Open.X,b.Width)||!Overlap(a.Open.Y,a.Depth,b.Open.Y,b.Depth);
                if(!visible)throw new InvalidOperationException((constraints.TrayHeightMm.HasValue?"托盘高度 "+N(constraints.TrayHeightMm.Value)+" mm 下展开单元遮挡：":"展开仍遮挡后排：")+a.Id+" / "+b.Id+"。竖笔按正面XZ投影，横笔按顶面XY投影检查；请调整展开位置或开合结构，不会改动输入高度。\n"+OpenLayoutAdvice(p));
            }
            if(p.Parts==null||p.Parts.Count==0||p.Parts.Any(x=>x==null||String.IsNullOrWhiteSpace(x.Id)||String.IsNullOrWhiteSpace(x.Role)||x.Width<=0||x.Depth<=0||x.Height<=0))throw new InvalidOperationException("外壳、盖板及连接片必须分别填写设计尺寸和作用。");
            if(p.Parts.Select(x=>x.Id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.Parts.Count)throw new InvalidOperationException("外壳、盖板及连接片必须使用不同编号，避免图纸尺寸对应到多个部件。");
            CalculateEnvelope(concept);
            // Stock thickness and articulated motion still require prototypes; numerics are not a physical certification.
            constraints.ValidateLayout(concept.PenLayout,concept.PackageDimensions,true,p.CombinedLengthClearance);
            concept.Layout=String.Join("\n",p.Units.Select(u=>u.Id+"："+u.Count+"支，"+u.Count+"×1单排；"+u.Motion+"；单元外尺寸 "+N(u.Width)+"×"+N(u.Depth)+"×"+N(u.Height)+" mm。"))+"\n展开后各单元在"+(p.Orientation=="vertical"?"正面":"顶面")+"几何投影互不遮挡；盖板和挡片应避开笔身，实际可见性另须图像核验。";
        }
        static void ValidateSize(Size3 size,string label,bool positive)
        {
            if(size==null||size.Width<0||size.Depth<0||size.Height<0||positive&&(size.Width==0||size.Depth==0||size.Height==0))
                throw new InvalidOperationException(label+"须明确提供三个"+(positive?"正数":"非负数")+"毫米值。");
        }
        static bool Fits(Size3 part,Size3 space)
        {return part.Width<=space.Width&&part.Depth<=space.Depth&&part.Height<=space.Height;}
        static void CalculateEnvelope(Concept concept)
        {
            var p=concept.Assembly;
            try
            {
                decimal w=p.Units.Max(u=>u.Closed.X+u.Width),d=p.Units.Max(u=>u.Closed.Y+u.Depth),h=p.Units.Max(u=>u.Closed.Z+u.Height);
                if(p.Shell==null){w+=2*p.WallThickness;d+=2*p.WallThickness;h+=2*p.WallThickness;}
                else
                {
                    var s=p.Shell;
                    if(s.WallThickness<=0||s.WallThickness>5||s.AssemblyClearance<0||s.AssemblyClearance>30||String.IsNullOrWhiteSpace(s.Basis))throw new InvalidOperationException("Shell须说明独立壳厚（0–5mm）、每侧装配余量（0–30mm）及各项预算依据；不能将壳体余量塞入笔槽尺寸。");
                    ValidateSize(s.InternalReserve,"Shell.InternalReserve 内置折页/底板占用总量",false);
                    ValidateSize(s.ExternalReserve,"Shell.ExternalReserve 闭合外部突出总量",false);
                    if(p.OpenReserve==null||String.IsNullOrWhiteSpace(p.OpenReserveBasis))throw new InvalidOperationException("须明确OpenReserve三轴展开附加预算及OpenReserveBasis；没有附加占用时可为0，但须说明理由。");
                    s.InnerSize=new Size3{Width=w+s.InternalReserve.Width+2*s.AssemblyClearance,Depth=d+s.InternalReserve.Depth+2*s.AssemblyClearance,Height=h+s.InternalReserve.Height+2*s.AssemblyClearance};
                    s.OuterSize=new Size3{Width=s.InnerSize.Width+2*s.WallThickness,Depth=s.InnerSize.Depth+2*s.WallThickness,Height=s.InnerSize.Height+2*s.WallThickness};
                    w=s.OuterSize.Width+s.ExternalReserve.Width;d=s.OuterSize.Depth+s.ExternalReserve.Depth;h=s.OuterSize.Height+s.ExternalReserve.Height;
                    var package=new Size3{Width=w,Depth=d,Height=h};
                    foreach(var part in p.Parts)
                    {
                        if(part.SizeBasis!="flat"&&part.SizeBasis!="formed")throw new InvalidOperationException("部件 "+part.Id+" 的SizeBasis须为flat（平片）或formed（成形），不能混用板材厚度和安装高度。");
                        if(part.PlacementScope!="internal"&&part.PlacementScope!="shell"&&part.PlacementScope!="external")throw new InvalidOperationException("部件 "+part.Id+" 的PlacementScope须为internal、shell或external。");
                        if(String.IsNullOrWhiteSpace(part.AssemblyNotes))throw new InvalidOperationException("部件 "+part.Id+" 须在AssemblyNotes说明安装轴、连接端、折线/胶接区和支撑关系。");
                        ValidateSize(part.ClosedSize,"部件 "+part.Id+" 的ClosedSize闭合安装包络",true);
                        var space=part.PlacementScope=="internal"?s.InnerSize:package;
                        if(!Fits(part.ClosedSize,space))throw new InvalidOperationException("部件 "+part.Id+" 的闭合安装包络 "+SizeText(part.ClosedSize)+" 超过"+(part.PlacementScope=="internal"?"外壳内空 ":"最终包装外径 ")+SizeText(space)+"；请修正安装轴/折叠关系或对应预算，不能缩短笔身。");
                    }
                }
                p.OpenWidth=p.Units.Max(u=>u.Open.X+u.Width);p.OpenDepth=p.Units.Max(u=>u.Open.Y+u.Depth);p.OpenHeight=p.Units.Max(u=>u.Open.Z+u.Height);
                if(p.OpenReserve!=null){ValidateSize(p.OpenReserve,"OpenReserve展开附加总量",false);if(String.IsNullOrWhiteSpace(p.OpenReserveBasis))throw new InvalidOperationException("OpenReserve须同时提供预算依据OpenReserveBasis。");}
                p.OverallOpenWidth=p.OpenWidth+(p.OpenReserve==null?0:p.OpenReserve.Width);p.OverallOpenDepth=p.OpenDepth+(p.OpenReserve==null?0:p.OpenReserve.Depth);p.OverallOpenHeight=p.OpenHeight+(p.OpenReserve==null?0:p.OpenReserve.Height);
                concept.PenLayout=new PenLayout{BoxWidth=w,BoxDepth=d,BoxHeight=h,Orientation=p.Orientation,LengthAxis=p.LengthAxis};
                var edges=new[]{w,d,h}.OrderByDescending(x=>x).ToArray();concept.PackageDimensions=new PackageDimensions{Longest=edges[0],Middle=edges[1],Shortest=edges[2]};
            }
            catch(OverflowException){throw new InvalidOperationException("外壳或展开预算超出可计算数值范围，请核对毫米单位和尺寸。");}
        }
        static void SizeUnit(DisplayUnit u,AssemblyPlan p,PackagingConstraints c)
        {
            decimal row=Span(u.Count,c.MarkerDiameter,p.TotalClearance,p.BetweenGap,p.DividerThickness)+2*p.WallThickness;
            decimal thin=c.MarkerDiameter+p.TotalClearance+2*p.WallThickness,length=c.MarkerLength+p.LengthClearance+2*p.WallThickness;
            u.Width=p.LengthAxis=="Width"?length:row;
            u.Depth=p.LengthAxis=="Depth"?length:p.LengthAxis=="Width"?row:thin;
            decimal minimumHeight=p.LengthAxis=="Height"?length:thin;
            if(c.TrayHeightMm.HasValue&&c.TrayHeightMm.Value<minimumHeight)
                throw new InvalidOperationException("单元 "+u.Id+" 的托盘高度 "+N(c.TrayHeightMm.Value)+" mm 小于当前"+(p.LengthAxis=="Height"?"竖放完整笔长及装取余量":"笔径、排布余量和两侧壁厚")+"所需的最低 "+N(minimumHeight)+" mm；请修改高度或结构，程序不会更改手填值。");
            u.Height=c.TrayHeightMm??minimumHeight;
        }
        // Only called on fresh API responses, never by load/export/review of saved coordinates.
        public static void PrepareGeneratedLayout(Concept concept,PackagingConstraints constraints)
        {
            var p=concept.Assembly;if(p==null||String.IsNullOrEmpty(p.OpenLayout)||p.OpenLayout=="manual")return;
            if(p.OpenLayout!="side-by-side")throw new InvalidOperationException("OpenLayout仅支持manual或side-by-side，不能猜测未知展开方式。");
            constraints.Validate();
            if(p.Units==null||p.Units.Count==0||p.Units.Count>100||p.Units.Any(u=>u==null||u.Count<=0)||p.Units.Sum(u=>(long)u.Count)!=constraints.MarkerCount)throw new InvalidOperationException("先正确填写各单元支数再计算展开位置。");
            decimal x=0;
            foreach(var u in p.Units){SizeUnit(u,p,constraints);u.Open=new Placement{X=x};x+=u.Width+4m;}
        }
        static string OpenLayoutAdvice(AssemblyPlan p)
        {
            decimal x=0;var lines=new List<string>();
            foreach(var u in p.Units){lines.Add(u.Id+" 外径="+N(u.Width)+"×"+N(u.Depth)+"×"+N(u.Height)+" mm，可用Open={X:"+N(x)+",Y:0,Z:0}");x+=u.Width+4m;}
            return "程序计算的并排候选（单元间4mm设计间距，不是每支笔间隙）："+String.Join("；",lines)+"。仅证明单元投影不遮挡，不证明铰链能到达；如采用此候选须同步修改Motion、连接/盖板Parts、开合步骤和支撑，仍需独立复核及白样。也可返回其他经计算互不遮挡的展开位置。";
        }
        static void NormalizeOrigin(IEnumerable<Placement> positions)
        {
            var items=positions.Distinct().ToList();
            decimal x=Math.Min(0,items.Min(p=>p.X)),y=Math.Min(0,items.Min(p=>p.Y)),z=Math.Min(0,items.Min(p=>p.Z));
            foreach(var p in items){p.X-=x;p.Y-=y;p.Z-=z;}
        }
        public static IEnumerable<PenBox> PenBoxes(DisplayUnit unit,AssemblyPlan plan,PackagingConstraints constraints,bool open)
        {
            var position=open?unit.Open:unit.Closed;
            decimal side=plan.WallThickness+plan.TotalClearance/2m;
            decimal end=plan.WallThickness+plan.LengthClearance/2m;
            decimal pitch=constraints.MarkerDiameter+plan.BetweenGap+plan.DividerThickness;
            for(int i=0;i<unit.Count;i++)
            {
                decimal row=side+i*pitch;
                yield return new PenBox{
                    Index=i+1,
                    X=position.X+(plan.LengthAxis=="Width"?end:row),
                    Y=position.Y+(plan.LengthAxis=="Depth"?end:plan.LengthAxis=="Width"?row:side),
                    Z=position.Z+(plan.LengthAxis=="Height"?end:side),
                    Width=plan.LengthAxis=="Width"?constraints.MarkerLength:constraints.MarkerDiameter,
                    Depth=plan.LengthAxis=="Depth"?constraints.MarkerLength:constraints.MarkerDiameter,
                    Height=plan.LengthAxis=="Height"?constraints.MarkerLength:constraints.MarkerDiameter
                };
            }
        }
        static bool Overlap(decimal a,decimal sizeA,decimal b,decimal sizeB){return a<b+sizeB-0.01m&&b<a+sizeA-0.01m;}
        static string N(decimal n){return n.ToString("0.##",CultureInfo.InvariantCulture);}
        public static string SizeText(Size3 size)
        {return size==null?"未记录":N(size.Width)+" × "+N(size.Depth)+" × "+N(size.Height)+" mm";}
        public static string PartSizeLabel(AssemblyPart part)
        {return part.SizeBasis=="flat"?"平片 W×D×厚":part.SizeBasis=="formed"?"成形 W×D×H":"旧版未注明尺寸状态";}
        public static string ShellDescription(AssemblyPlan p)
        {
            var s=p.Shell;
            if(s==null)return "旧方案未声明独立外壳预算；保留原单元包络加两侧内托壁厚的尺寸算法，不代表已计入折页、底板或胶口占用。";
            return "外壳预算（W×D×H）：独立壳厚 "+N(s.WallThickness)+" mm；每侧装配净余量 "+N(s.AssemblyClearance)+" mm（每轴加两次）。\n内置结构占用总量 "+SizeText(s.InternalReserve)+"；闭合外部突出总量 "+SizeText(s.ExternalReserve)+"。\n壳内空 "+SizeText(s.InnerSize)+"；壳本体外径 "+SizeText(s.OuterSize)+"；最终包装外径另加外部突出预算。\n预算依据："+s.Basis+"。预算不是部件布置或运动学验证，部件之间是否占用同一区域仍须复核。";
        }
        public static string Describe(Concept c,PackagingConstraints constraints)
        {
            if(c.Assembly==null)return "旧方案尚无逐组计算数据；重新生成可建立。";
            var p=c.Assembly;
            return "整排净宽 = 支数×笔径 + 整排总余量 + (支数−1)×(笔间隙+隔板厚度)。\n本方案：总余量 "+N(p.TotalClearance)+" mm；每间隙 "+N(p.BetweenGap)+" mm；隔板 "+N(p.DividerThickness)+" mm；壁厚 "+N(p.WallThickness)+" mm。\n"+
                String.Join("\n",p.Units.Select(u=>u.Id+" · "+u.Count+"支 / 单排；净排宽 "+N(Span(u.Count,constraints.MarkerDiameter,p.TotalClearance,p.BetweenGap,p.DividerThickness))+" mm；单元净尺寸 "+N(u.Width-2*p.WallThickness)+" × "+N(u.Depth-2*p.WallThickness)+" × "+N(u.Height-2*p.WallThickness)+" mm；单元外尺寸 "+N(u.Width)+" × "+N(u.Depth)+" × "+N(u.Height)+" mm"))+
                "\n单槽净长 "+N(constraints.MarkerLength+p.LengthClearance)+" mm；槽宽 "+N(constraints.MarkerDiameter+p.BetweenGap)+" mm。\n笔组展开包络 "+N(p.OpenWidth)+" × "+N(p.OpenDepth)+" × "+N(p.OpenHeight)+" mm（不含盖板/支撑）。\n"+
                (p.OpenReserve==null?"旧方案未声明全结构展开附加预算。\n":"展开附加总量 "+SizeText(p.OpenReserve)+"；全结构展开预算范围 "+N(p.OverallOpenWidth)+" × "+N(p.OverallOpenDepth)+" × "+N(p.OverallOpenHeight)+" mm。依据："+p.OpenReserveBasis+"。\n")+
                ShellDescription(p)+"\n"+
                String.Join("\n",p.Parts.Select(x=>x.Id+" / "+x.Role+"："+PartSizeLabel(x)+" "+N(x.Width)+" × "+N(x.Depth)+" × "+N(x.Height)+" mm；闭合安装包络 "+SizeText(x.ClosedSize)+"；归属 "+(x.PlacementScope??"未记录")+"；安装说明："+(x.AssemblyNotes??"未记录")))+"\n假设："+p.Assumptions+"。尺寸均为设计选值；部件安装包络只校验三轴上限，未计算部件相交、转轴轨迹或稳定性，不代表生产刀模。";
        }
        public static string Contract(PackagingConstraints c)
        {
            return "\n【可计算结构协议：独立外壳与安装预算】每个Concept返回Assembly及RequirementChecks。Assembly格式：{AllVisible:true,Orientation:vertical或horizontal,LengthAxis:Height/Width/Depth,TotalClearance:4,BetweenGap:0,DividerThickness:0,WallThickness:1,LengthClearance:"+N(c.MinimumLengthClearanceMm)+",CombinedLengthClearance:false（参考结构明确两组笔长总余量只加一次时才为true，此时LengthClearance必须4–6mm）,Assumptions:说明设计选值理由,Shell:{WallThickness:独立壳厚,AssemblyClearance:每侧装配净余量,InternalReserve:{Width:数值,Depth:数值,Height:数值},ExternalReserve:{Width:数值,Depth:数值,Height:数值},Basis:各轴占用与间隙依据},OpenReserve:{Width:数值,Depth:数值,Height:数值},OpenReserveBasis:盖板及支撑展开预算依据,Units:[{Id:A,Count:12,Rows:1,Columns:12,Motion:具体翻转/滑移与连接动作,Closed:{X:0,Y:0,Z:0},Open:{X:0,Y:0,Z:0}}],Parts:[{Id:盖板,Role:盖板连接与闭合方式,Width:数值,Depth:数值,Height:数值,SizeBasis:flat或formed,ClosedSize:{Width:数值,Depth:数值,Height:数值},PlacementScope:internal或shell或external,AssemblyNotes:安装轴、折线、胶接区、接端及支撑尺寸说明}]}。全部用严格JSON。单位mm，坐标是各单元轴对齐包围盒左前下角；各视图不允许改变分组和单元尺寸。\n"+
                "程序将根据Assembly计算每组净宽、单元外径、闭合盒宽深高及展开范围；PackageDimensions/PenLayout由程序据此重算。结构文本不编造其他数值。总余量默认4mm，可按笔型选不同值并说明；12×15+4=184mm是净排宽，不是每个缝加4mm；再加隔板和两侧壁厚才是外径。\n"+
                "每个单元的Closed和Open均必须明确返回X、Y、Z三个数值；为0也要写0，不得省略坐标或只写动作。建议同一状态共用左前下角原点；如果采用中心原点，程序会整体平移负坐标，不改变单元相对位置。闭合与展开各自统一原点，不允许逐单元自行归零。\n"+
                "唯一的Open坐标例外：若设计本身选择各单排单元左右并排、同一底面展开，请在Assembly明确返回OpenLayout:\"side-by-side\"，此时可省略Units的Open，程序按实际单元宽度依次计算X=前单元X+前单元宽+4mm，Y=Z=0，覆盖响应中的Open。4mm是单元之间设计间距，不是每支笔间隙；Closed仍须完整填写。其他布局用OpenLayout:\"manual\"或省略该字段，并自行返回完整Open坐标。不要为通过校验随意改为并排：必须同步设计对应的连接、Motion、Parts、OpeningSequence及稳定支撑；并排位置不证明固定铰链可到达，也不自动允许拆开独立摆放。用户要求一体连接时必须保留一体连接。不同方案可用不同分组和开合机制，不能只换名称。\n"+
                 "每组单元外宽=Count×笔径+TotalClearance+(Count-1)×(BetweenGap+DividerThickness)+2×WallThickness；薄边=笔径+TotalClearance+2×WallThickness；长边=笔长+LengthClearance+2×WallThickness。LengthClearance至少"+N(c.MinimumLengthClearanceMm)+"mm。竖直时外宽/薄边/长边对应Width/Depth/Height；横向Width轴对应长边/外宽/薄边，Depth轴对应外宽/长边/薄边。"+(c.TrayHeightMm.HasValue?"用户指定所有单元托盘高度均为"+N(c.TrayHeightMm.Value)+"mm；程序按此高度计算碰撞、外壳和展开尺寸。不得改成自动值；若低于装载最低高度或与闭合层位冲突，须报告原因。":"未指定托盘高度时，程序按笔长或笔径、余量及两侧壁厚自动计算每单元高度。")+"此为装载空间包络，不意味着内托前挡板必须做成全笔长的高墙；展示侧采用低挡片、开口/镂空或可翻开面板，使每支完整笔身可见。\n"+
                "外壳与笔槽独立计算：每轴壳内空=该轴单元最大闭合终点+Shell.InternalReserve该轴总量+2×Shell.AssemblyClearance；壳本体外径=内空+2×Shell.WallThickness；最终包装外径=壳本体外径+Shell.ExternalReserve该轴突出总量。Shell.InnerSize/OuterSize由程序派生，无需返回。不得用增大TotalClearance/LengthClearance、虚移单元原点或改小笔尺寸代替壳体装配预算。三轴Reserve均是总量，不再乘2。可为0但Basis必须说明为什么没有额外占用；内置折页、底板、胶口的实料叠层与独立装配净余量不能重复计入，也不能遗漏。壳厚0<厚度≤5mm；每侧装配净余量0–30mm，按结构选值而非统一放大。\n"+
                "Parts每件W/D/H必须声明SizeBasis：flat表示平片W×D×厚度，formed表示成形W×D×H；ClosedSize一律是安装方向下闭合态W×D×H，不能将平片长边误作板厚或安装高度。internal部件ClosedSize三轴不得超过Shell.InnerSize，shell/external不得超过最终包装外径；不允许靠平片自动旋转或折叠猜测通过。AssemblyNotes写具体安装轴、接到哪些单元/零件的哪条边、压痕位置、胶接宽度、底座与支撑关系及数值；与Manufacturing连接/开合说明一致。此校验只检查部件上限，不能据此声称部件无相交或铰链可达，仍须独立评审及白样。\n"+
                "OpenWidth/Depth/Height仅为笔组展开包络；OverallOpenWidth/Depth/Height由程序按笔组包络+OpenReserve三轴总量计算，表示全结构展开预算范围。OpenReserve须明确盖板、支撑等额外占用，OpenReserveBasis说明方向和依据；为0也须有理由。图纸不能把笔组范围标成整体占地，也不要把预算图画成已验证机械刀模。\n"+
                "必须一次性完整看到所有笔：每单元Rows=1且Columns=Count；6×2只能拆成6支+6支两个单排单元，展开并排或完全错开，不能躲在前一排后。竖直笔展开按正面XZ投影不得重叠，横放按顶面XY投影不得重叠；不接受仅从顶部看到笔帽。封闭时各单元XYZ实体不得交叉。闭合、展开必须是同一结构、数量、铰链与图案位置。连接片和面板Parts逐一标注数值（设计假设，不得用待确认替代）。\n"+
                "可计算参考（整排总余量4、无隔板/额外间隙，未含壁厚）："+String.Join("；",new[]{1,6,11,12}.Select(n=>n+"支="+N(Span(n,c.MarkerDiameter,4))+"mm"))+"。不要把默认分组当成固定模板；根据用户的开合、造型和体验选择不同分组与连接。";
        }
        public static List<string> Requirements(string request,string details,string feedback)
        {return Regex.Split((request??"")+"\n"+(details??"")+"\n"+(feedback??""),@"[\r\n。；;!?！？]+").Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();}
        public static string RequirementPrompt(List<string> clauses)
        {return "\n逐条需求（必须结合语境理解否定、保留和修改；新修改优先。不能只匹配主题词）：\n"+String.Join("\n",clauses.Select((x,i)=>"R"+(i+1)+": "+x))+"\n每个方案返回RequirementChecks:[{RequirementId:R1,Implementation:说明本方案如何具体落实此条及与其他方案的不同}]。每条必须有对应实现；不能遗漏详细要求或只换同义词。";}
        public static void ValidateRequirements(Concept c,int count)
        {
            if(c.RequirementChecks==null||Enumerable.Range(1,count).Any(i=>!c.RequirementChecks.Any(r=>r!=null&&r.RequirementId=="R"+i&&!String.IsNullOrWhiteSpace(r.Implementation))))throw new InvalidOperationException("模型遗漏需求的逐项实现说明；未进入出图。");
        }
    }
}
