using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;

namespace PaperStudio
{
    public static class SamplePackage
    {
        public static void Write(string path,DesignVersion version,string explanation,List<Picture> guides)
        {
            if(version==null||version.Concept==null||version.Concept.Assembly==null||version.Constraints==null)
                throw new InvalidOperationException("当前方案还没有完整的逐组计算数据，请先生成新版方案后导出打样资料包。");
            // Recompute a snapshot: exporting must not change the current project or its accepted images.
            var concept=Json.Copy(version.Concept);var constraints=Json.Copy(version.Constraints);
            AssemblyGeometry.Calculate(concept,constraints);
            string target=Path.GetFullPath(path),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,false,Encoding.UTF8))
                {
                    Text(zip,"README.txt","纸盒包装创意工作台 · 打样沟通资料包\r\n\r\n"+
                        "用途：把当前方案、毫米尺寸、物料来源和需求落实情况交给设计师/供应商讨论并制作白样。\r\n"+
                        "这是概念与计算资料，不是生产刀模、结构认证、报价或实测合规结论。\r\n"+
                        "units.csv：逐组支数、单元净/外尺寸、完整笔长、笔径、间隙和闭合/展开位置；坐标为单元左前下角。\r\n"+
                        "parts.csv：逐件区分平片/成形尺寸、闭合安装包络、内外归属及安装说明；均为设计选值，不能据此断言无干涉。\r\n"+
                        "envelope.csv：独立壳厚、每侧装配余量、内置/外突预算、壳内空、壳本体及最终外径；笔组与全结构展开预算分列。\r\n"+
                        "materials.csv：物料、数量、来源ID、工艺ID及厚度假设。克重不等于厚度，须向供应商核实实料。\r\n"+
                        "requirements.csv：原文、解释、当前生效/已被替代状态和本方案实现。空白表示当前版本未记录，不代表通过。\r\n"+
                        "images/只包含当前方案已接受的视图；待复核或被拒绝的图片不在此包中。AI图片仍需人工核对。\r\n"+
                        "guides/为程序尺寸参考图，提供比例和分组依据，不代替AI创意图或生产刀模。\r\n"+
                        "CSV使用UTF-8 BOM，所有尺寸单位为mm、小数点为点；以公式符号开头的外来文本已加单引号，避免Excel执行。\r\n\r\n"+
                        "打样前：核对实笔(含笔帽/笔尖)最大尺寸、纸材厚度、糊口/压痕/连接位置及装配顺序。\r\n"+
                        "白样后：检查全部笔一次可见、装取、开合干涉、展开稳定性和运输固定；再按适用测试要求验证。\r\n"+
                        "几何假设："+concept.Assembly.Assumptions+"\r\n");
                    Text(zip,"current-plan.txt","方案："+concept.Name+"\r\n版本创建："+version.Created+"\r\n本次修改："+version.Instruction+
                        "\r\n\r\n当前版本原始需求\r\n"+version.Requirements+"\r\n\r\n"+(explanation??"")+
                        "\r\n\r\n导出时重新计算的尺寸\r\n"+AssemblyGeometry.Describe(concept,constraints));
                    WriteUnits(zip,concept.Assembly,constraints);
                    WriteEnvelope(zip,concept);
                    WriteParts(zip,concept.Assembly);
                    WriteMaterials(zip,concept.Manufacturing);
                    WriteRequirements(zip,concept);
                    WriteEvidence(zip,concept.Understanding);
                    var imageIndex=new StringBuilder(Csv("分类","名称","文件"));
                    string[] views={"整体效果","打开状态","内部布局","设计草图"};
                    for(int i=0;i<views.Length;i++)
                    {
                        string data;
                        if(version.Pictures==null||!version.Pictures.TryGetValue(views[i],out data))continue;
                        string imagePath=AddImage(zip,"images/view-"+(i+1).ToString("00",CultureInfo.InvariantCulture),data,false);
                        imageIndex.Append(Csv("已接受视图",views[i],imagePath));
                    }
                    if(guides!=null)for(int i=0;i<guides.Count;i++)
                    {
                        var guide=guides[i];if(guide==null)throw new InvalidOperationException("尺寸参考图内容为空，未导出资料包。");
                        string imagePath=AddImage(zip,"guides/guide-"+(i+1).ToString("00",CultureInfo.InvariantCulture),guide.Data,true);
                        imageIndex.Append(Csv("程序尺寸参考",guide.Name,imagePath));
                    }
                    Text(zip,"image-index.csv",imageIndex.ToString());
                }
                if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
            }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
        static void WriteUnits(ZipArchive zip,AssemblyPlan plan,PackagingConstraints c)
        {
            var text=new StringBuilder(Csv("单元编号","支数","排数","列数","姿态","笔长轴","完整笔长_mm","最大笔径_mm","净宽_mm","净深_mm","净高_mm","外宽_mm","外深_mm","外高_mm","整排总余量_mm","每笔间隙_mm","隔板厚度_mm","壁厚_mm","两端总余量_mm","闭合X_mm","闭合Y_mm","闭合Z_mm","展开X_mm","展开Y_mm","展开Z_mm","开合动作"));
            foreach(var u in plan.Units)text.Append(Csv(u.Id,u.Count,u.Rows,u.Columns,plan.Orientation,plan.LengthAxis,c.MarkerLength,c.MarkerDiameter,u.Width-2*plan.WallThickness,u.Depth-2*plan.WallThickness,u.Height-2*plan.WallThickness,u.Width,u.Depth,u.Height,plan.TotalClearance,plan.BetweenGap,plan.DividerThickness,plan.WallThickness,plan.LengthClearance,u.Closed.X,u.Closed.Y,u.Closed.Z,u.Open.X,u.Open.Y,u.Open.Z,u.Motion));
            Text(zip,"units.csv",text.ToString());
        }
        static void WriteParts(ZipArchive zip,AssemblyPlan plan)
        {
            var text=new StringBuilder(Csv("部件编号","作用","尺寸状态","宽_mm","深_mm","平片厚度或成形高_mm","归属","闭合安装宽_mm","闭合安装深_mm","闭合安装高_mm","安装轴及连接折线胶接说明","尺寸依据"));
            foreach(var part in plan.Parts)text.Append(Csv(part.Id,part.Role,AssemblyGeometry.PartSizeLabel(part),part.Width,part.Depth,part.Height,part.PlacementScope??"未记录",part.ClosedSize==null?"":(object)part.ClosedSize.Width,part.ClosedSize==null?"":(object)part.ClosedSize.Depth,part.ClosedSize==null?"":(object)part.ClosedSize.Height,part.AssemblyNotes??"旧方案未记录安装状态","设计选值；三轴上限校验不证明零件无相交或转轴可达，需白样核对"));
            Text(zip,"parts.csv",text.ToString());
        }
        static void WriteEnvelope(ZipArchive zip,Concept concept)
        {
            var p=concept.Assembly;var text=new StringBuilder(Csv("项目","宽_mm","深_mm","高_mm","语义及依据"));
            text.Append(Csv("单元内托壁厚",p.WallThickness,p.WallThickness,p.WallThickness,"单元计算使用，不代替独立壳厚"));
            if(p.Shell!=null)
            {
                var s=p.Shell;
                text.Append(Csv("独立壳厚",s.WallThickness,s.WallThickness,s.WallThickness,"每轴外径加两侧壳厚"));
                text.Append(Csv("每侧装配净余量",s.AssemblyClearance,s.AssemblyClearance,s.AssemblyClearance,"每轴内空加两次；"+s.Basis));
                text.Append(Csv("内置结构占用总量",s.InternalReserve.Width,s.InternalReserve.Depth,s.InternalReserve.Height,"各轴总量，只加一次；"+s.Basis));
                text.Append(Csv("壳内空",s.InnerSize.Width,s.InnerSize.Depth,s.InnerSize.Height,"单元闭合包络+内置总量+两侧装配余量"));
                text.Append(Csv("壳本体外径",s.OuterSize.Width,s.OuterSize.Depth,s.OuterSize.Height,"壳内空+两侧独立壳厚；尚未计入外部突出"));
                text.Append(Csv("闭合外部突出总量",s.ExternalReserve.Width,s.ExternalReserve.Depth,s.ExternalReserve.Height,"各轴总量，只加一次；"+s.Basis));
            }
            else text.Append(Csv("旧方案外壳预算未记录","","","保留原包络算法，不能视为已计入折页底板或胶口"));
            text.Append(Csv("最终包装外径",concept.PenLayout.BoxWidth,concept.PenLayout.BoxDepth,concept.PenLayout.BoxHeight,"包含已声明的闭合外部突出，受包装上限约束"));
            text.Append(Csv("笔组展开包络",p.OpenWidth,p.OpenDepth,p.OpenHeight,"仅笔组，不包含盖板和支撑"));
            if(p.OpenReserve!=null)
            {
                text.Append(Csv("展开附加总量",p.OpenReserve.Width,p.OpenReserve.Depth,p.OpenReserve.Height,p.OpenReserveBasis));
                text.Append(Csv("全结构展开预算范围",p.OverallOpenWidth,p.OverallOpenDepth,p.OverallOpenHeight,"笔组包络+展开附加总量；未模拟实际位置及运动轨迹"));
            }
            else text.Append(Csv("全结构展开预算未记录","","","不能用笔组包络代替整体占地"));
            Text(zip,"envelope.csv",text.ToString());
        }
        static void WriteMaterials(ZipArchive zip,ManufacturingPlan plan)
        {
            var text=new StringBuilder(Csv("部位","角色","材料名","数量","厚度_mm","厚度依据","用途","材料来源ID","工艺来源ID"));
            if(plan!=null&&plan.Materials!=null)foreach(var m in plan.Materials.Where(x=>x!=null))
                text.Append(Csv(m.Part,m.Role,m.MaterialName,m.Quantity,m.ThicknessMm,m.ThicknessBasis,m.Purpose,m.SourceId,String.Join(" | ",m.ProcessSourceIds??new List<string>())));
            Text(zip,"materials.csv",text.ToString());
        }
        static void WriteRequirements(ZipArchive zip,Concept concept)
        {
            var text=new StringBuilder(Csv("需求编号","原文","解释","类别","状态","替代者编号","本方案落实"));
            var checks=concept.RequirementChecks??new List<RequirementCheck>();
            var requirements=concept.Understanding==null?null:concept.Understanding.Requirements;
            if(requirements!=null)foreach(var r in requirements.Where(x=>x!=null))
                text.Append(Csv(r.Id,r.Original,r.Meaning,r.Kind,r.Status,r.SupersededBy,String.Join("\n",checks.Where(x=>x!=null&&x.RequirementId==r.Id).Select(x=>x.Implementation))));
            else foreach(var r in checks.Where(x=>x!=null))text.Append(Csv(r.RequirementId,r.Requirement,"","","当前版本未记录解释状态","",r.Implementation));
            Text(zip,"requirements.csv",text.ToString());
        }
        static void WriteEvidence(ZipArchive zip,DesignUnderstanding understanding)
        {
            var text=new StringBuilder(Csv("来源ID","引用原文","本方案用途"));
            if(understanding!=null&&understanding.Evidence!=null)foreach(var e in understanding.Evidence.Where(x=>x!=null))text.Append(Csv(e.SourceId,e.Quote,e.Application));
            Text(zip,"source-evidence.csv",text.ToString());
        }
        static string Csv(params object[] values)
        {
            return String.Join(",",values.Select(value=>{
                string result=value==null?"":value is IFormattable?((IFormattable)value).ToString(null,CultureInfo.InvariantCulture):value.ToString();
                if(value is string)
                {
                    int start=0;while(start<result.Length&&(Char.IsWhiteSpace(result[start])||Char.IsControl(result[start])||result[start]=='\uFEFF'))start++;
                    if(start<result.Length&&"=+-@".IndexOf(result[start])>=0)result="'"+result;
                    else if(result.Length>0&&(result[0]=='\t'||result[0]=='\r'||result[0]=='\n'))result="'"+result;
                }
                return "\""+result.Replace("\"","\"\"")+"\"";
            }))+"\r\n";
        }
        static void Text(ZipArchive zip,string name,string contents)
        {
            using(var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Optimal).Open(),new UTF8Encoding(true)))writer.Write(contents);
        }
        static string AddImage(ZipArchive zip,string name,string data,bool requirePng)
        {
            byte[] bytes;string extension;
            try
            {
                bytes=Convert.FromBase64String(data??"");
                using(var input=new MemoryStream(bytes))using(var image=Image.FromStream(input,true,true))
                    extension=image.RawFormat.Guid==ImageFormat.Png.Guid?".png":image.RawFormat.Guid==ImageFormat.Jpeg.Guid?".jpg":null;
            }
            catch(Exception error)
            {
                if(!(error is FormatException)&&!(error is ArgumentException)&&!(error is OutOfMemoryException))throw;
                throw new InvalidOperationException("资料包图片无法读取，原有导出文件未被覆盖。",error);
            }
            if(extension==null||requirePng&&extension!=".png")throw new InvalidOperationException("方案图片须为PNG/JPEG，程序尺寸参考须为PNG，原有导出文件未被覆盖。");
            string path=name+extension;
            using(var output=zip.CreateEntry(path,CompressionLevel.NoCompression).Open())output.Write(bytes,0,bytes.Length);
            return path;
        }
    }
}
