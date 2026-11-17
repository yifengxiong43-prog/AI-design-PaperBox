using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PaperStudio
{
    public class ImageUnitInspection
    {
        public string Id,Evidence;
        public int VisiblePens;
        public bool FullLengthVisible,DimensionsMatch;
        public decimal? WidthMm,DepthMm,HeightMm;
    }
    public class ImagePartInspection
    {
        public string Id,Evidence;
        public bool DimensionsMatch;
        public decimal? WidthMm,DepthMm,HeightMm;
    }
    public class ImageInspectionReport
    {
        public bool Pass,SameStructure,ProportionsMatch,DimensionsPresent,RequirementsMet,SketchStyleMatches,ReferenceStructureMatch,PaperMaterialMatch=true;
        public string ReferenceIssues;
        public int ObservedVisiblePens;
        public string Issues;
        public List<ImageUnitInspection> Units;
        public List<ImagePartInspection> Parts;
    }
    public static class ImageInspection
    {
        public static string Contract(bool sketch)
        {
            string dimensions=",DimensionsMatch:boolean,WidthMm:number|null,DepthMm:number|null,HeightMm:number|null";
            string material=",PaperMaterialMatch:boolean";
            return "只输出严格JSON：{Pass:boolean,SameStructure:boolean,ProportionsMatch:boolean"+material+",DimensionsPresent:boolean,RequirementsMet:boolean,ObservedVisiblePens:integer,Issues:string,Units:[{Id:账本单元编号,VisiblePens:integer,FullLengthVisible:boolean,Evidence:实际观察到的位置/数量/遮挡/比例依据"+(sketch?dimensions:"")+"}]"+(sketch?",SketchStyleMatches:boolean,Parts:[{Id:账本部件编号,Evidence:观察到的部件与尺寸引线依据"+dimensions+"}]":"")+"}。\n"+
                "必须逐个检查账本Assembly.Units，每个Id只出现一次；无法找到的单元也返回其Id，VisiblePens=0、FullLengthVisible=false。只统计主展开视图内实际能辨识完整笔身的笔，不累加闭合图、局部图、单支比例示意或重复视图；不要相信数量文字，不凭方案账本补齐看不见的笔。Evidence描述图中证据，不得只抄账本或写‘已满足’。ObservedVisiblePens是这些单元在同一个主展开视图中的可见总数。整体通过不能覆盖某一单元失败。\n"+
                (sketch?"草图还必须逐个检查Assembly.Parts，每个Id只出现一次。Units的WidthMm/DepthMm/HeightMm读取图中对应单元的外宽/外深/外高引线，Parts读取对应部件三边（含板厚）；不要拿净尺寸、整体尺寸替代单元外尺寸。缺标注、看不清、无法对应轴时返回null及DimensionsMatch=false，不能把账本数字当作读到的数值。尺寸比较容差0.01mm。SketchStyleMatches仅判断细线工业草图、少量局部上色、协调的闭合/展开/内部多视图与动作箭头；尺寸引线缺漏归DimensionsPresent和DimensionsMatch，不单独影响SketchStyleMatches。不能把摄影渲染当草图。审阅副本的白底不代表原图不透明，透明通道另由程序检查。":"普通效果图无需尺寸引线，DimensionsPresent可为true，不要求Units的尺寸字段和Parts。")+
                (sketch?"背景不可画棋盘格、模拟透明网格或大块背景底板；即使外围有透明像素，只要主体背景仍为画出来的棋盘格，也必须SketchStyleMatches=false并在Issues说明。真实纸盒表面的图案不应误判为背景。草图的RequirementsMet只判断可见要求，例如指定的三角杆、盒型、笔数、配件与视觉风格；若用户只笼统要求‘配件’而未指定独立配件，已绘出的提手、盖板、可拆托盘和取笔缺口按实际可见部件判断，不要求凭空增加另一件。儿童开启力、人体工学、承重、安全和合规测试无法由图片确认，写入Issues并保留工程待验证，不可单独因此设为false，也不可宣称已通过。":"普通效果图的RequirementsMet和Pass只判断图中可见的要求：支数、完整笔身、盒型、开合、明确指定的配件与视觉风格。儿童开启力、人体工学、提携承重、安全和合规测试无法仅凭效果图确认；应写入Issues并保留工程待验证，不可单独因此判图片失败，也不可宣称已通过。未指定具体额外配件时，已绘出的提手、可拆托盘等应按实际可见部件判断，不要求凭空增加配件。")+
                (sketch?"DimensionsPresent还要求整体闭合外宽/外深/外高与PenLayout一致、整体展开宽/深/高与Assembly.OpenWidth/OpenDepth/OpenHeight一致，并标出完整单支笔长；这些标注必须实际可读，缺失或与账本不符则DimensionsPresent=false，Issues逐项说明。ProportionsMatch必须根据实际绘制的笔身与其对应盒边的比例判断，不能只因标注数字相同就通过。":"")+
                "不能判断就返回相应false并说明。此检查验证可见一致性，不构成打样通过或量产认证。";
        }
        static InvalidOperationException Invalid(){return new InvalidOperationException("图片检查字段不完整或类型不符，保留图片等待重新复核，不重新出图。");}
        static Dictionary<string,object> Object(object value)
        {var result=value as Dictionary<string,object>;if(result==null)throw Invalid();return result;}
        static object Field(Dictionary<string,object> value,string key)
        {object result;if(!value.TryGetValue(key,out result))throw Invalid();return result;}
        static string Text(Dictionary<string,object> value,string key)
        {var result=Field(value,key) as string;if(result==null)throw Invalid();return result;}
        static bool Bool(Dictionary<string,object> value,string key)
        {object result=Field(value,key);if(!(result is bool))throw Invalid();return (bool)result;}
        static int Count(Dictionary<string,object> value,string key)
        {object result=Field(value,key);if(!(result is int))throw Invalid();return (int)result;}
        static decimal? Measure(Dictionary<string,object> value,string key)
        {
            object result=Field(value,key);if(result==null)return null;
            if(!(result is int)&&!(result is long)&&!(result is decimal)&&!(result is double))throw Invalid();
            try{return Convert.ToDecimal(result,CultureInfo.InvariantCulture);}catch{throw Invalid();}
        }
        static object[] Array(Dictionary<string,object> value,string key)
        {var result=Field(value,key) as object[];if(result==null)throw Invalid();return result;}
        public static ImageInspectionReport Parse(string json,bool sketch)
        {
            Dictionary<string,object> data;
            try{data=Object(Json.Serializer().DeserializeObject(json));}catch{throw Invalid();}
            var report=new ImageInspectionReport{Pass=Bool(data,"Pass"),SameStructure=Bool(data,"SameStructure"),ProportionsMatch=Bool(data,"ProportionsMatch"),DimensionsPresent=Bool(data,"DimensionsPresent"),RequirementsMet=Bool(data,"RequirementsMet"),ObservedVisiblePens=Count(data,"ObservedVisiblePens"),Issues=Text(data,"Issues"),Units=new List<ImageUnitInspection>(),Parts=new List<ImagePartInspection>()};
            if(data.ContainsKey("PaperMaterialMatch"))report.PaperMaterialMatch=Bool(data,"PaperMaterialMatch");
            if(data.ContainsKey("ReferenceStructureMatch"))report.ReferenceStructureMatch=Bool(data,"ReferenceStructureMatch");
            if(data.ContainsKey("ReferenceIssues")&&data["ReferenceIssues"]!=null)report.ReferenceIssues=Text(data,"ReferenceIssues");
            if(sketch||data.ContainsKey("SketchStyleMatches"))report.SketchStyleMatches=Bool(data,"SketchStyleMatches");
            foreach(var item in Array(data,"Units")){
                var unit=Object(item);var parsed=new ImageUnitInspection{Id=Text(unit,"Id"),VisiblePens=Count(unit,"VisiblePens"),FullLengthVisible=Bool(unit,"FullLengthVisible"),Evidence=Text(unit,"Evidence")};
                if(sketch||unit.ContainsKey("DimensionsMatch"))parsed.DimensionsMatch=Bool(unit,"DimensionsMatch");
                if(sketch||unit.ContainsKey("WidthMm"))parsed.WidthMm=Measure(unit,"WidthMm");
                if(sketch||unit.ContainsKey("DepthMm"))parsed.DepthMm=Measure(unit,"DepthMm");
                if(sketch||unit.ContainsKey("HeightMm"))parsed.HeightMm=Measure(unit,"HeightMm");
                report.Units.Add(parsed);
            }
            if(sketch||data.ContainsKey("Parts"))foreach(var item in Array(data,"Parts")){
                var part=Object(item);report.Parts.Add(new ImagePartInspection{Id=Text(part,"Id"),Evidence=Text(part,"Evidence"),DimensionsMatch=Bool(part,"DimensionsMatch"),WidthMm=Measure(part,"WidthMm"),DepthMm=Measure(part,"DepthMm"),HeightMm=Measure(part,"HeightMm")});
            }
            return report;
        }
        static bool Matches(decimal? observed,decimal expected)
        {return observed.HasValue&&observed.Value>0&&Math.Abs(observed.Value-expected)<=0.01m;}
        public static bool VisualReferenceUsable(ImageInspectionReport report,DesignVersion version)
        {
            if(report==null||version==null||version.Concept==null||version.Concept.Assembly==null||version.Constraints==null||report.Units==null)return false;
            var units=version.Concept.Assembly.Units;
            return (version.Concept.Understanding==null||version.Concept.Understanding.ReferenceDesign==null||report.ReferenceStructureMatch)&&report.SameStructure&&report.ProportionsMatch&&report.RequirementsMet&&report.SketchStyleMatches
                &&report.ObservedVisiblePens==version.Constraints.MarkerCount
                &&report.Units.Sum(u=>u==null?0:(long)u.VisiblePens)==report.ObservedVisiblePens
                &&report.Units.Count==units.Count&&report.Units.All(u=>u!=null&&units.Any(p=>p.Id==u.Id)
                    &&u.FullLengthVisible&&!String.IsNullOrWhiteSpace(u.Evidence)
                    &&u.VisiblePens==units.Single(p=>p.Id==u.Id).Count)
                &&report.Units.Select(u=>u.Id).Distinct().Count()==units.Count;
        }
        public static void Validate(ImageInspectionReport report,DesignVersion version,string view)
        {
            if(report==null||report.Units==null||report.Units.Any(u=>u==null)||version==null||version.Concept==null||version.Concept.Assembly==null||version.Constraints==null)throw Invalid();
            var plan=version.Concept.Assembly;bool sketch=view=="设计草图";var errors=new List<string>();
            var paperIssues=DesignMethod.PaperManufacturingIssues(version.Concept);
            if(paperIssues.Any(i=>i.Contains("明确结构冲突")))errors.Add("方案制造数据明确依赖非纸类主体材料："+String.Join("；",paperIssues.Where(i=>i.Contains("明确结构冲突"))));
            if(!report.PaperMaterialMatch)errors.Add("效果图材质未呈现为纸板/纸基结构");
            if(!report.SameStructure||!report.ProportionsMatch||!report.RequirementsMet)errors.Add("整体结构、笔身比例或可见用户要求未通过");
            if(version.Concept.Understanding!=null&&version.Concept.Understanding.ReferenceDesign!=null&&!report.ReferenceStructureMatch)errors.Add("与已确认的原始Reference核心结构不一致："+(report.ReferenceIssues??"缺少逐项对照"));
            if(!report.DimensionsPresent)errors.Add(sketch?"草图整体尺寸或展开尺寸标注缺失、错误":"图片视图复核未通过");
            if(report.ObservedVisiblePens!=version.Constraints.MarkerCount||report.Units.Sum(u=>(long)u.VisiblePens)!=report.ObservedVisiblePens)errors.Add("主展开视图可见笔数与产品支数或逐单元统计不一致");
            if(report.Units.Count!=plan.Units.Count||report.Units.Any(u=>u==null||!plan.Units.Any(p=>p.Id==u.Id))||report.Units.Select(u=>u.Id).Distinct().Count()!=report.Units.Count)errors.Add("单元编号遗漏、重复或与方案不符");
            foreach(var expected in plan.Units){
                var units=report.Units.Where(u=>u!=null&&u.Id==expected.Id).ToList();if(units.Count!=1)continue;var unit=units[0];
                if(unit.VisiblePens!=expected.Count||!unit.FullLengthVisible||String.IsNullOrWhiteSpace(unit.Evidence))errors.Add("单元 "+expected.Id+" 的完整笔身未逐支可见或缺少观察依据");
                if(sketch&&(!unit.DimensionsMatch||!Matches(unit.WidthMm,expected.Width)||!Matches(unit.DepthMm,expected.Depth)||!Matches(unit.HeightMm,expected.Height)))errors.Add("单元 "+expected.Id+" 缺少可读的外宽×外深×外高或标注与计算不符");
            }
            if(sketch){
                if(!report.SketchStyleMatches)errors.Add("未采用要求的多视图细线草图、局部上色和动作箭头");
                if(report.Parts==null||report.Parts.Any(p=>p==null))throw Invalid();
                if(report.Parts.Count!=plan.Parts.Count||report.Parts.Any(p=>p==null||!plan.Parts.Any(e=>e.Id==p.Id))||report.Parts.Select(p=>p.Id).Distinct().Count()!=report.Parts.Count)errors.Add("草图部件编号遗漏、重复或与方案不符");
                foreach(var expected in plan.Parts){
                    var parts=report.Parts.Where(p=>p!=null&&p.Id==expected.Id).ToList();if(parts.Count!=1)continue;var part=parts[0];
                    if(!part.DimensionsMatch||!Matches(part.WidthMm,expected.Width)||!Matches(part.DepthMm,expected.Depth)||!Matches(part.HeightMm,expected.Height)||String.IsNullOrWhiteSpace(part.Evidence))errors.Add("部件 "+expected.Id+" 缺少可读的三边尺寸或与设计选值不符");
                }
            }
            if(!report.Pass&&errors.Count==0)errors.Add("图片整体复核未通过，原因需人工检查");
            if(errors.Count>0)throw new ImageQualityException("图片逐项检查未通过，保留计算方案，未采纳该图："+String.Join("；",errors.Distinct())+(String.IsNullOrWhiteSpace(report.Issues)?"":"。复核观察："+report.Issues));
        }
    }
}
