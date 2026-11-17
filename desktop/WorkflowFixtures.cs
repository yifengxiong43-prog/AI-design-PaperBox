// Only compiled into tests and QA. These fixtures are not AI output.
using System;
using System.Linq;
using System.Collections.Generic;
namespace PaperStudio
{
    public static class WorkflowFixtures
    {
        public static void Shell(Concept concept)
        {
            var p=concept.Assembly;
            p.Shell=new ShellPlan{WallThickness=1,AssemblyClearance=2,InternalReserve=new Size3{Width=4,Depth=4,Height=4},ExternalReserve=new Size3(),Basis="QA占用预算，非实际工程方案"};
            p.OpenReserve=new Size3{Width=4,Depth=20,Height=4};p.OpenReserveBasis="QA盖板/支撑预算";
            foreach(var part in p.Parts){part.ClosedSize=new Size3{Width=part.Width,Depth=part.Depth,Height=part.Height};part.SizeBasis="formed";part.PlacementScope="internal";part.AssemblyNotes="QA安装态轴向与预算占位，不代表经过实物验证";}
        }
        public static ImageInspectionReport ImageReview(Concept concept)
        {
            // Protocol fixture only: real images still need an API vision inspection.
            return new ImageInspectionReport{Pass=true,SameStructure=true,ProportionsMatch=true,DimensionsPresent=true,RequirementsMet=true,ObservedVisiblePens=concept.Assembly.Units.Sum(u=>u.Count),Issues="QA mock only",SketchStyleMatches=true,
                Units=concept.Assembly.Units.Select(u=>new ImageUnitInspection{Id=u.Id,VisiblePens=u.Count,FullLengthVisible=true,DimensionsMatch=true,WidthMm=u.Width,DepthMm=u.Depth,HeightMm=u.Height,Evidence="QA单元观察占位，不代表实际图片质量"}).ToList(),
                Parts=concept.Assembly.Parts.Select(p=>new ImagePartInspection{Id=p.Id,DimensionsMatch=true,WidthMm=p.Width,DepthMm=p.Depth,HeightMm=p.Height,Evidence="QA部件尺寸占位，不代表实际图片质量"}).ToList()};
        }
        public static DesignUnderstanding Understanding(List<string> clauses,List<SourceRecord> records)
        {
            var evidence=new[]{records.First(r=>r.Group=="材料与工艺"),records.First(r=>r.Group=="历史问题"),records.First(r=>r.Group=="产品案例"),records.First(r=>r.Title.Contains("合规"))};
            return new DesignUnderstanding{Goal="QA验证资料理解",UseScene="桌面展开",Style="按原文",Assumptions="纸板厚度为设计值，毛重/报价未知",Questions=new List<string>(),Requirements=clauses.Select((s,i)=>new InterpretedRequirement{Id="R"+(i+1),Original=s,Meaning="QA解释："+s,Kind=s.Contains("不要")?"avoid":s.Contains("保留")?"keep":"must",Status="active",SupersededBy=""}).ToList(),Evidence=evidence.Select(r=>new SourceEvidence{SourceId=r.Id,Quote=r.Text.Substring(0,Math.Min(35,r.Text.Length)),Application="用于QA引用校验，非实际设计决定"}).ToList()};
        }
        public static ManufacturingPlan Manufacturing(List<SourceRecord> records,decimal thickness)
        {
            var material=records.First(r=>r.Id.Contains("/材料汇总/5"));var process=records.First(r=>r.Id.EndsWith("/工艺适配/17"));
            return new ManufacturingPlan{Materials=new List<MaterialChoice>{new MaterialChoice{Part="单排内托",Role="unit-wall",SourceId=material.Id,MaterialName="白卡纸",Quantity=4,ThicknessMm=thickness,ThicknessBasis="QA假设",Purpose="定位",ProcessSourceIds=new List<string>{process.Id}},new MaterialChoice{Part="外壳",Role="shell",SourceId=material.Id,MaterialName="白卡纸",Quantity=1,ThicknessMm=thickness,ThicknessBasis="QA独立壳厚",Purpose="保护",ProcessSourceIds=new List<string>{process.Id}}},AssemblySteps=new List<string>{"模切并压痕","粘合背板并固定低位托底","组装连接片并检查开合"},SampleChecks=new List<string>{"取放试验","展开稳定性","开合干涉"},Connections="背板纸连接片与留白糊口粘合",OpeningSequence="左右依次展开并由底翼支撑",Stability="底翼展开后支撑，需白样倾倒试验",Protection="运输内托与护角定位，非测试认证",CostDrivers="纸材面积、装配工序，报价未知",Risks=new List<RiskControl>{new RiskControl{SourceId=records.First(r=>r.Group=="历史问题").Id,Risk="运输挤压",Action="加强护角并固定内托",Test="跌落与振动后检查"}},Tests=new List<RiskControl>{new RiskControl{SourceId=records.First(r=>r.Title.Contains("合规")).Id,Risk="运输损伤",Action="称重后确定跌落高度",Test="1角3棱6面，企业振动条件250转/分3小时"}}};
        }
        public static DesignReview Review(List<Concept> concepts,DesignUnderstanding understanding)
        {
            return new DesignReview{DistinctDirections=true,Concepts=concepts.Select(c=>new ConceptVerdict{Name=c.Name,RowAxis=c.Assembly==null?null:AssemblyGeometry.RowAxis(c.Assembly),Pass=true,ManufacturableConcept=true,SourceGrounded=true,Findings="QA模拟通过，不代表实际可制造",Requirements=understanding.Requirements.Where(r=>r.Status=="active").Select(r=>new RequirementVerdict{Id=r.Id,Met=true,Evidence="QA测试落实说明"}).ToList()}).ToList()};
        }
    }
}
