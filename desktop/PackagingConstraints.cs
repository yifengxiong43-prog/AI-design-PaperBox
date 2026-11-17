using System;
using System.Collections.Generic;
using System.Globalization;

namespace PaperStudio
{
    public class PenTypeChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }

    public class PackagingTier
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string WeightNote;
        public decimal OuterLongest, OuterMiddle, OuterShortest;
        public decimal? MaxGirth;
        public override string ToString() { return Name; }
    }

    public class PackagingInput
    {
        public string Count = "", Length = "", Diameter = "", TrayHeight = "", Tier = "large-standard", AllowanceCm = "5";
        public string OuterLongest = "", OuterMiddle = "", OuterShortest = "";
        public string PenType = "alcohol";

        public PackagingConstraints Build()
        {
            int count;
            if (!Int32.TryParse((Count ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count <= 0)
                throw new InvalidOperationException("请填写笔的支数，必须为正整数。");
            var length = PositiveNumber(Length, "完整单支笔长（mm）");
            var diameter = PositiveNumber(Diameter, "单支笔直径（mm）");
            decimal? trayHeight = String.IsNullOrWhiteSpace(TrayHeight) ? (decimal?)null : PositiveNumber(TrayHeight, "托盘高度（mm）");
            if (AllowanceCm != "3" && AllowanceCm != "4" && AllowanceCm != "5")
                throw new InvalidOperationException("请选择每边外包装预留量：3、4或5 cm。");
            var tier = PackagingConstraints.FindTier(Tier);
            var result = new PackagingConstraints {
                MarkerCount = count, MarkerLength = length, MarkerDiameter = diameter, TrayHeightMm = trayHeight, PenType = PenType, Tier = tier.Id, AllowanceMm = Decimal.Parse(AllowanceCm, CultureInfo.InvariantCulture) * 10m,
                OuterLongest = tier.OuterLongest, OuterMiddle = tier.OuterMiddle, OuterShortest = tier.OuterShortest,
                MaxGirth = tier.MaxGirth, WeightNote = tier.WeightNote
            };
            if (tier.Id == "custom")
            {
                result.OuterLongest = PositiveNumber(OuterLongest, "外包装最长边（mm）");
                result.OuterMiddle = PositiveNumber(OuterMiddle, "外包装次长边（mm）");
                result.OuterShortest = PositiveNumber(OuterShortest, "外包装最短边（mm）");
            }
            result.Validate();
            return result;
        }

        private static decimal PositiveNumber(string text, string label)
        {
            decimal value;
            if (!Decimal.TryParse((text ?? "").Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) || value <= 0)
                throw new InvalidOperationException("请填写" + label + "，必须为正数。");
            return value;
        }
    }

    public class PackageDimensions
    {
        public decimal Longest, Middle, Shortest;
    }

    public class PenLayout
    {
        public decimal BoxWidth, BoxDepth, BoxHeight;
        public int Rows, Columns;
        public string Orientation, LengthAxis;
        public decimal PenAxisLength()
        {
            if (LengthAxis == "Width") return BoxWidth;
            if (LengthAxis == "Depth") return BoxDepth;
            if (LengthAxis == "Height") return BoxHeight;
            throw new InvalidOperationException("笔长方向必须为Width、Depth或Height。");
        }
    }

    public class PackagingConstraints
    {
        public int MarkerCount;
        public decimal MarkerLength;
        public decimal MarkerDiameter;
        public decimal? TrayHeightMm;
        public string PenType;
        public decimal AllowanceMm = 50m;
        public string Tier, WeightNote;
        public decimal OuterLongest, OuterMiddle, OuterShortest;
        public decimal? MaxGirth;
        public decimal MaxLongest { get { return OuterLongest - AllowanceMm; } }
        public decimal MaxMiddle { get { return OuterMiddle - AllowanceMm; } }
        public decimal MaxShortest { get { return OuterShortest - AllowanceMm; } }
        public string PenTypeName { get { var choice = PenTypes.Find(p => p.Id == PenType); return choice == null ? "未指定笔类" : choice.Name; } }
        public string PlacementRule { get { return PenType == "acrylic" ? "丙烯笔只能笔身直立收纳，闭合收纳和打开展示时都不能横躺；竖放指实际笔身姿态，不是图片或画布的纵向。" : PenType == "fineliner" ? "针管笔可先按横放或竖放做概念排布；笔尖保护、漏墨风险和实际收纳姿态须用指定实物确认。" : "酒精笔、彩铅可横放或笔身直立收纳。"; } }
        public decimal SlotClearanceMm { get { return MarkerDiameter <= 0 ? 2m : Math.Max(2m, Decimal.Round(MarkerDiameter * 0.2m, 1)); } }
        public decimal PanelClearanceMm { get { return 3m; } }
        public decimal EndClearanceMm { get { return MarkerDiameter <= 0 ? 15m / 2m : Math.Max(10m, Decimal.Round(MarkerDiameter * 0.75m, 1)); } }
        public decimal MinimumLengthClearanceMm { get { return 2m * EndClearanceMm; } }

        public static readonly List<PenTypeChoice> PenTypes = new List<PenTypeChoice> {
            new PenTypeChoice { Id = "alcohol", Name = "酒精笔" },
            new PenTypeChoice { Id = "pencil", Name = "彩铅" },
            new PenTypeChoice { Id = "acrylic", Name = "丙烯笔" },
            new PenTypeChoice { Id = "fineliner", Name = "针管笔 / gel pen" }
        };

        public static readonly List<PackagingTier> Tiers = new List<PackagingTier> {
            new PackagingTier { Id = "small-standard", Name = "小号标准尺寸", OuterLongest = 381m, OuterMiddle = 304.8m, OuterShortest = 19.05m, WeightNote = "发货重量不超过16盎司；重量待核验" },
            new PackagingTier { Id = "large-standard", Name = "大号标准尺寸", OuterLongest = 457.2m, OuterMiddle = 355.6m, OuterShortest = 203.2m, WeightNote = "发货重量不超过20磅；重量待核验" },
            new PackagingTier { Id = "small-bulky", Name = "小号大件", OuterLongest = 939.8m, OuterMiddle = 711.2m, OuterShortest = 508m, MaxGirth = 3302m, WeightNote = "发货重量不超过50磅；重量待核验" },
            new PackagingTier { Id = "large-bulky", Name = "大号大件", OuterLongest = 1498.6m, OuterMiddle = 838.2m, OuterShortest = 838.2m, MaxGirth = 3302m, WeightNote = "发货重量不超过50磅；重量待核验" },
            new PackagingTier { Id = "custom", Name = "自定义外包装尺寸", WeightNote = "自定义尺寸不代表商品分段达标；重量及分段规则待核验" }
        };

        public static PackagingTier FindTier(string id)
        {
            var tier = Tiers.Find(t => t.Id == id);
            if (tier == null) throw new InvalidOperationException("请选择有效的商品尺寸分段。");
            return tier;
        }

        public void Validate()
        {
            if (PenType != null && !PenTypes.Exists(p => p.Id == PenType))
                throw new InvalidOperationException("请选择酒精笔、彩铅、丙烯笔或针管笔。");
            if (MarkerCount <= 0) throw new InvalidOperationException("马克笔支数必须为正整数。");
            if (MarkerLength <= 0) throw new InvalidOperationException("马克笔长度必须为正数（mm）。");
            if (MarkerDiameter < 0) throw new InvalidOperationException("单支笔直径不能为负数（mm）。");
            if (TrayHeightMm.HasValue && TrayHeightMm.Value <= 0) throw new InvalidOperationException("托盘高度（mm）必须为正数；留空则自动计算。");
            if (AllowanceMm != 30m && AllowanceMm != 40m && AllowanceMm != 50m)
                throw new InvalidOperationException("每边外包装预留量必须为3、4或5 cm。");
            var tier = FindTier(Tier);
            if (Tier != "custom" && (OuterLongest != tier.OuterLongest || OuterMiddle != tier.OuterMiddle || OuterShortest != tier.OuterShortest))
                throw new InvalidOperationException("商品尺寸分段与外包装限值不一致，请重新选择分段。");
            if (MaxGirth != tier.MaxGirth)
                throw new InvalidOperationException("商品尺寸分段的长度加围长限制不一致，请重新选择分段。");
            if (OuterLongest < OuterMiddle || OuterMiddle < OuterShortest)
                throw new InvalidOperationException("请按最长边 ≥ 次长边 ≥ 最短边填写外包装尺寸。");
            if (OuterLongest <= AllowanceMm || OuterMiddle <= AllowanceMm || OuterShortest <= AllowanceMm)
                throw new InvalidOperationException("所选外包装尺寸无法在三个边各预留" + Number(AllowanceMm) + " mm；每条边必须大于预留量，请选择其他尺寸分段。");
            if (PenType != null && MarkerLength >= MaxLongest)
                throw new InvalidOperationException("单支笔长度必须小于预留外包装空间后的最长边上限，并为纸板、内托及装取间隙留出空间。请调整笔长或商品尺寸。");
            if (PenType != null && MarkerDiameter > 0)
            {
                if (MarkerDiameter >= MarkerLength)
                    throw new InvalidOperationException("请核对完整单支笔长和笔径，完整笔长应大于直径；两个输入均使用mm。");
                if (MarkerLength >= MaxLongest - MinimumLengthClearanceMm)
                    throw new InvalidOperationException("笔长加两端最少" + Number(MinimumLengthClearanceMm) + " mm装取余量已达到包装最长边上限，尚未计入纸板厚度。请增大外包装尺寸或核对产品参数。");
                // Supported geometry reserves diameter x diameter x length for every pen; no diagonal/interlaced packing.
                // This is a necessary bounding-space check, not a claim about circular/hexagonal material volume.
                double penVolume=(double)MarkerCount*(double)MarkerDiameter*(double)MarkerDiameter*(double)MarkerLength;
                double available=(double)MaxLongest*(double)MaxMiddle*(double)MaxShortest;
                if (penVolume>available*(1d+1e-10d))
                    throw new InvalidOperationException("当前直排几何模型按每支笔的直径×直径×完整笔长预留空间（不采用斜放或交错套叠）。总装载空间已超过包装上限，尚未计入纸板和空隙，请核对支数、mm单位或增大外包装尺寸。");
            }
        }

        public void ValidateBox(PackageDimensions box)
        {
            Validate();
            if (box == null) throw new InvalidOperationException("模型未提供产品包装三边数值，请重新生成。");
            if (box.Longest <= 0 || box.Middle <= 0 || box.Shortest <= 0)
                throw new InvalidOperationException("产品包装三边必须为正数（mm）。");
            if (box.Longest < box.Middle || box.Middle < box.Shortest)
                throw new InvalidOperationException("产品包装尺寸必须按最长边、次长边、最短边排序。");
            if (box.Longest > MaxLongest || box.Middle > MaxMiddle || box.Shortest > MaxShortest)
                throw new InvalidOperationException((TrayHeightMm.HasValue?"按用户指定托盘高度 " + Number(TrayHeightMm.Value) + " mm 计算的闭合包装":"模型返回的产品包装") + "尺寸 " + Dimensions(box.Longest,box.Middle,box.Shortest) + " mm 超出预留外包装空间后的上限 " + Dimensions(MaxLongest, MaxMiddle, MaxShortest) + " mm；请调整层数、堆叠方式或包装尺寸，程序不会更改手填高度。");
            if (MaxGirth.HasValue && (box.Longest + AllowanceMm) + 2m * ((box.Middle + AllowanceMm) + (box.Shortest + AllowanceMm)) > MaxGirth.Value)
                throw new InvalidOperationException("模型返回的包装加上三边各" + Number(AllowanceMm) + " mm外包装预留后，长度加围长超过" + Number(MaxGirth.Value) + " mm，请缩小包装后重新生成。");
        }

        public string Summary()
        {
            Validate();
            return "闭合包装上限：" + Dimensions(MaxLongest, MaxMiddle, MaxShortest) + " mm";
        }

        string TechnicalSummary()
        {
            return "外包装上限：" + Dimensions(OuterLongest, OuterMiddle, OuterShortest) + " mm\n"
                + "每边预留 " + Number(AllowanceMm / 10m) + " cm\n"
                + "闭合包装上限：" + Dimensions(MaxLongest, MaxMiddle, MaxShortest) + " mm"
                + (MaxGirth.HasValue ? "\n加回外包装后，长度加围长 ≤ " + Number(MaxGirth.Value) + " mm" : "")
                + "\n重量提示：" + (WeightNote ?? "待核验");
        }

        public void ValidateLayout(PenLayout layout, PackageDimensions box, bool calculatedAssembly=false, bool combinedLengthClearance=false)
        {
            ValidateBox(box);
            if (PenType == null) return;
            if (layout == null) throw new InvalidOperationException("模型未提供笔的摆放方向与包装宽深高，请重新生成。");
            if (layout.BoxWidth <= 0 || layout.BoxDepth <= 0 || layout.BoxHeight <= 0)
                throw new InvalidOperationException("包装宽、深、高必须为正数（mm）。");
            var edges = new[] { layout.BoxWidth, layout.BoxDepth, layout.BoxHeight };
            Array.Sort(edges);
            if (edges[2] != box.Longest || edges[1] != box.Middle || edges[0] != box.Shortest)
                throw new InvalidOperationException("包装宽深高与最长边、次长边、最短边不一致，请重新生成。");
            if (layout.Orientation != "vertical" && layout.Orientation != "horizontal")
                throw new InvalidOperationException("笔的摆放姿态必须为vertical或horizontal。");
            if (PenType == "acrylic" && layout.Orientation != "vertical")
                throw new InvalidOperationException("丙烯笔必须笔身直立收纳，不能横放。");
            if ((layout.Orientation == "vertical" && layout.LengthAxis != "Height") ||
                (layout.Orientation == "horizontal" && layout.LengthAxis != "Width" && layout.LengthAxis != "Depth"))
                throw new InvalidOperationException("直立笔长必须沿Height；横放笔长必须沿Width或Depth。");
            decimal requiredLengthClearance=combinedLengthClearance?5m:MinimumLengthClearanceMm;
            if (layout.PenAxisLength() < MarkerLength + requiredLengthClearance)
                throw new InvalidOperationException("笔长方向的包装外径至少要比单支笔长" + Number(requiredLengthClearance) + " mm，不能把笔刚好塞满或通过缩短笔的比例装入；纸板、内托和装取间隙仍需核验。");
            if (MarkerDiameter > 0 && !calculatedAssembly)
            {
                decimal first, second;
                if (layout.LengthAxis == "Height") { first = layout.BoxWidth; second = layout.BoxDepth; }
                else if (layout.LengthAxis == "Width") { first = layout.BoxDepth; second = layout.BoxHeight; }
                else { first = layout.BoxWidth; second = layout.BoxHeight; }
                long capacity=(long)CrossSectionCapacity(first)*CrossSectionCapacity(second);
                if (capacity < MarkerCount)
                    throw new InvalidOperationException("包装的宽深截面按笔直径" + Number(MarkerDiameter) + " mm只能容纳" + capacity + "支，少于当前" + MarkerCount + "支；请增大截面或调整排列。");
            }
        }

        public PenLayout EstimateLayout()
        {
            Validate();
            int rows, columns; Grid(MarkerCount, out rows, out columns);
            decimal first=CrossSectionSize(columns), second=CrossSectionSize(rows), length=MarkerLength+MinimumLengthClearanceMm;
            if (PenType == "acrylic")
                return new PenLayout{BoxWidth=first,BoxDepth=second,BoxHeight=length,Orientation="vertical",LengthAxis="Height",Rows=rows,Columns=columns};
            return new PenLayout{BoxWidth=length,BoxDepth=first,BoxHeight=second,Orientation="horizontal",LengthAxis="Width",Rows=rows,Columns=columns};
        }

        public string EstimateSummary()
        {
            var layout=EstimateLayout();
            return "按"+layout.Rows+"×"+layout.Columns+"排列，预计盒体约"+Dimensions(layout.BoxWidth,layout.BoxDepth,layout.BoxHeight)+" mm；已计入直径、槽位间隙和边缘余量，纸板厚度与实际内托仍需打样。";
        }

        int CrossSectionCapacity(decimal size)
        {
            if (MarkerDiameter <= 0) return Int32.MaxValue;
            decimal usable=size-2m*PanelClearanceMm+SlotClearanceMm;
            if (usable < MarkerDiameter+SlotClearanceMm) return 0;
            return (int)Math.Min(Int32.MaxValue,Decimal.Floor(usable/(MarkerDiameter+SlotClearanceMm)));
        }

        decimal CrossSectionSize(int slots)
        { return slots*MarkerDiameter+(slots+1)*SlotClearanceMm+2m*PanelClearanceMm; }

        static void Grid(int count,out int rows,out int columns)
        {
            rows=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(count)));columns=(int)Math.Ceiling((decimal)count/rows);
            int bestRows=rows,bestColumns=columns,bestDelta=Math.Abs(columns-rows);long bestArea=(long)rows*columns;
            for(int r=1;r<=rows;r++){int c=(int)Math.Ceiling((decimal)count/r),delta=Math.Abs(c-r);long area=(long)r*c;if(area<bestArea||(area==bestArea&&delta<bestDelta)){bestRows=r;bestColumns=c;bestArea=area;bestDelta=delta;}}
            rows=bestRows;columns=bestColumns;
        }

        public const string SizeScopeRule="尺寸档位与扣除中盒/黄盒预留后的包装尺寸上限仅约束闭合状态的最终包装外径；展开后允许超出该闭合上限。展开时另报包含支撑/盖板的整体占地，不将展开宽度代入闭合包装校验。除非用户原文明示桌面占地或展开尺寸限制，否则无需追问是否允许展开超出。用户明确的展开限制仍是独立硬要求，不得删除或用本默认规则覆盖。最长边、次长边、最短边是排序比较，不是固定的宽深高；竖直笔的Height可以是最长或次长边，不能直接把最短边上限当作可用高度。每个展示单元单排、展开后全部笔身同时可见，不等于闭合时所有笔必须排成一整排；可设计相连的分组模块并核对开合、支撑和遮挡。";

        public string ToPrompt()
        {
            Validate();
            string estimate=MarkerDiameter>0?"\n单排净宽示例（整排余量4mm，未含壁厚/隔板）："+Number(MarkerCount*MarkerDiameter+4)+"mm。按实际分组计算；展开后每组单排，所有笔应一次可见，不能沿用紧凑方阵。":"\n当前项目未记录笔径；旧项目只能做长度校验，不能声称盒体截面已算出。";
            return "【当前结构化产品参数与尺寸硬约束】\n笔类：" + PenTypeName + "；笔支数：" + MarkerCount + "支；完整单支笔长度（笔帽或笔尖均计入）：" + Number(MarkerLength)
                 + " mm；单支笔直径：" + (MarkerDiameter>0?Number(MarkerDiameter):"未填写") + " mm；托盘高度：" + (TrayHeightMm.HasValue?Number(TrayHeightMm.Value)+" mm（用户指定，每层相同，不得修改）":"未指定，由程序按笔尺寸及当前结构计算") + "。以上参数优先于自由文本、历史方案和参考案例中的支数、长度、直径；不得套用示例的数量。" + estimate + "\n"
                + PlacementRule + "约束实际收纳姿态，不锁死盒型、开合、展开、展示或交互创意。笔径按整支笔含笔帽的最大横截面包络宽度使用；六角彩铅也按此包络宽度预留，不能当作材料实体体积。当前计算支持直排，未模拟斜放或不同方向交错套叠。\n"
                + TechnicalSummary() + "\n外包装每边预留可选3–5 cm，本次选定" + Number(AllowanceMm / 10m) + " cm；三个边必须分别减" + Number(AllowanceMm) + " mm，不是三边合计减这个数，也不是每侧减这个数。"
                + "尺寸按最长边、次长边、最短边排列；上限是产品包装外径，不是内径，也不是推荐固定尺寸。\n"+SizeScopeRule+"\n"
                + "需为纸板厚度、内托、装取间隙保留空间，并核验包装内尺寸。程序估算仅用于给模型一个可计算的起点，不是刀模或实测合规结论。"
                + (MaxGirth.HasValue ? "\n设产品包装外径为L、W、H，A=" + Number(AllowanceMm) + " mm，必须同时满足 (L+A)+2*((W+A)+(H+A)) <= " + Number(MaxGirth.Value) + " mm；即加回外包装预留后检查长度加围长，不得只逐边检查。" : "")
                + "\n每套方案必须返回 PackageDimensions 对象，Longest、Middle、Shortest 均为mm数值、正数，且 Longest >= Middle >= Shortest，逐边不超过上述产品包装外径上限；方案说明和图片提示词中的尺寸必须与该对象一致。"
                + (PenType == null ? "" : "\n每套方案还必须返回 PenLayout 对象：{\"BoxWidth\":宽mm数值,\"BoxDepth\":深mm数值,\"BoxHeight\":高mm数值,\"Orientation\":\"vertical或horizontal\",\"LengthAxis\":\"Width或Depth或Height\"}。宽深高均为正数，排序后必须逐一等于PackageDimensions的Longest、Middle、Shortest；高度不一定是最短边。vertical表示笔身直立，LengthAxis必须为Height；horizontal表示笔身横放，LengthAxis必须为Width或Depth。丙烯笔必须vertical。LengthAxis对应的包装外径必须严格大于单支笔长度，并另行核验纸板、内托和装取间隙，不能只等于笔长。图片必须使用同一组宽深高、笔长与摆放方向，以笔长/对应包装轴长的真实比例作图；每支笔都必须是完整单支笔（含笔帽/笔尖），不得缩短笔身、画成短棒、只露出笔头或用露出部分代替整支笔；若分格收纳，每个笔位也必须保留完整笔长和装取间隙。")
                + "不得将尺寸初步校验描述为重量、承载、容纳能力或实测合规已通过；仍需打样及实际外包装测量。";
        }

        private static string Number(decimal value) { return value.ToString("0.############################", CultureInfo.InvariantCulture); }
        private static string Dimensions(decimal longest, decimal middle, decimal shortest)
        { return Number(longest) + " × " + Number(middle) + " × " + Number(shortest); }
    }
}
