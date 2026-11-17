using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PaperStudio
{
    public class InterpretedRequirement
    {
        public string Id, Original, Meaning, Kind, Status, SupersededBy;
    }
    public class SourceEvidence { public string EvidenceId, SourceId, StableSourceId, SourceHash, Quote, Application; }
    public class DesignUnderstanding
    {
        public string Goal, UseScene, Style, Assumptions;
        public List<InterpretedRequirement> Requirements;
        public List<SourceEvidence> Evidence;
        public List<ReferenceImageConstraint> ReferenceImages;
        public ReferenceDesignSpecification ReferenceDesign;
        public List<string> Questions;
        public List<string> ReferenceWarnings=new List<string>();
    }
    public class MaterialChoice
    {
        public string Part, Role, SourceId, StableSourceId, SourceHash, MaterialName, ThicknessBasis, Purpose;
        public int Quantity;
        public decimal ThicknessMm;
        public List<string> ProcessSourceIds;
        public List<string> StableProcessIds;
    }
    public class RiskControl { public string SourceId, StableSourceId, SourceHash, Risk, Action, Test; }
    public class ManufacturingPlan
    {
        public List<MaterialChoice> Materials;
        public List<string> AssemblySteps, SampleChecks;
        public string Connections, OpeningSequence, Stability, Protection, CostDrivers;
        public List<RiskControl> Risks, Tests;
    }
    public class RequirementVerdict { public string Id, Evidence; public bool Met; }
    public class ConceptVerdict
    {
        public string Name, Findings, RowAxis;
        public bool Pass, ManufacturableConcept, SourceGrounded;
        public List<RequirementVerdict> Requirements;
    }
    public class DesignReview { public bool DistinctDirections; public List<ConceptVerdict> Concepts; }
    public class DesignValidationException:InvalidOperationException
    {
        public string RejectedResponse;
        public DesignValidationException(string message):base(message){}
    }
    public static class DesignMethod
    {
        public static string Read()
        {
            var texts=new List<string>();
            foreach(string name in new[]{"packaging-designer.SKILL.md","packaging-design-method.md"})
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {if(stream==null)throw new InvalidOperationException("内置包装设计Skill或资料方法缺失，请使用完整新版客户端。");using(var reader=new StreamReader(stream))texts.Add(reader.ReadToEnd());}
            return String.Join("\n\n",texts);
        }
        static void Require(bool condition,string error){if(!condition)throw new DesignValidationException(error);}
        static bool Has(string s){return !String.IsNullOrWhiteSpace(s);}
        static bool Filled(List<string> values,int min){return values!=null&&values.Count>=min&&values.All(Has);}
        // User text is authoritative; model omissions must never delete requirements.
        public static int ReconcileRequirements(DesignUnderstanding brief,List<string> clauses)
        {
            Require(brief!=null,"需求理解未返回有效内容。");
            var returned=brief.Requirements??new List<InterpretedRequirement>();
            var canonical=new List<InterpretedRequirement>();var aligned=new List<InterpretedRequirement>();int pending=0;
            for(int i=0;i<clauses.Count;i++)
            {
                string id="R"+(i+1),original=clauses[i];
                var matches=returned.Where(r=>r!=null&&r.Original!=null&&r.Original.Trim()==original).ToList();
                if(matches.Count>1)matches=matches.Where(r=>String.Equals(r.Id==null?null:r.Id.Trim(),id,StringComparison.OrdinalIgnoreCase)).ToList();
                if(matches.Count==0)matches=returned.Where(r=>r!=null&&String.Equals(r.Id==null?null:r.Id.Trim(),id,StringComparison.OrdinalIgnoreCase)&&!Has(r.Original)).ToList();
                var match=matches.Count==1?matches[0]:null;
                bool valid=match!=null&&Has(match.Meaning)&&new[]{"must","avoid","keep","change","preference"}.Contains(match.Kind)&&new[]{"active","superseded"}.Contains(match.Status);
                canonical.Add(new InterpretedRequirement{Id=id,Original=original,Meaning=valid?match.Meaning:"模型未单独解释；设计和复核须按此原文及上下文执行："+original,Kind=valid?match.Kind:"must",Status=valid?match.Status:"active",SupersededBy=valid&&match.Status=="superseded"?match.SupersededBy:""});
                aligned.Add(valid?match:null);
                if(!valid)pending++;
            }
            // Resolve successive edits against the original model mapping, not already modified statuses.
            var superseded=canonical.Select(r=>r.Status=="superseded").ToArray();
            for(int i=0;i<canonical.Count;i++)if(canonical[i].Status=="superseded")
            {
                int target=i;
                while(target>=0&&superseded[target])
                {
                    var match=aligned[target];
                    var targets=returned.Where(r=>r!=null&&match!=null&&Has(match.SupersededBy)&&String.Equals(r.Id==null?null:r.Id.Trim(),match.SupersededBy.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();
                    int next=targets.Count==1?aligned.IndexOf(targets[0]):-1;
                    target=next>target?next:-1;
                }
                if(target>i)canonical[i].SupersededBy=canonical[target].Id;
                else{canonical[i].Status="active";canonical[i].SupersededBy="";canonical[i].Meaning="替代关系未确认，须结合后续修改复核原文："+canonical[i].Original;pending++;}
            }
            brief.Requirements=canonical;
            if(pending>0)brief.Assumptions=(brief.Assumptions??"")+"\n有 "+pending+" 条需求解释未对齐，已保留完整原文；后续设计与独立复核逐条核对，不能视作已满足。";
            return pending;
        }
        static bool MaterialNameMatches(SourceRecord source,string name)
        {
            if(source==null)return false;
            foreach(var line in source.Text.Split('\n'))
            {
                if(!new[]{"材料/类型：","材料/配件：","保护材料："}.Any(line.StartsWith))continue;
                var value=line.Substring(line.IndexOf('：')+1).Trim();
                if(value==name||value.Split(new[]{'/','、','，'},StringSplitOptions.RemoveEmptyEntries).Any(part=>part.Trim()==name))return true;
            }
            return false;
        }
        public static void ValidateUnderstanding(DesignUnderstanding brief,List<string> clauses,List<SourceRecord> sources)
        {
            Require(brief!=null&&Has(brief.Goal)&&Has(brief.UseScene)&&Has(brief.Style)&&Has(brief.Assumptions),"需求理解缺少目标、场景、风格或假设。");
            Require(brief.Requirements!=null&&brief.Requirements.Count==clauses.Count,"需求理解必须覆盖所有原文条目。");
            for(int i=0;i<clauses.Count;i++)
            {
                var matches=brief.Requirements.Where(item=>item!=null&&item.Id=="R"+(i+1)).ToList();Require(matches.Count==1,"需求编号遗漏或重复。");var r=matches[0];
                Require(r.Original==clauses[i]&&Has(r.Meaning)&&new[]{"must","avoid","keep","change","preference"}.Contains(r.Kind),"需求原文或否定/保留/修改分类不完整。");
                Require(r.Status=="active"||r.Status=="superseded","需求必须区分当前有效与已被修改替代。");
                if(r.Status=="superseded")Require(brief.Requirements.Any(n=>n!=null&&n.Id==r.SupersededBy&&n.Status=="active"&&brief.Requirements.IndexOf(n)>i),"被替代的旧要求须指向后续有效修改。");
            }
            Require(brief.Evidence!=null&&brief.Evidence.Count>=3,"材料、历史问题或测试依据不足，相关结论尚未验证。");
            foreach(var evidence in brief.Evidence)
            {
                Require(evidence!=null&&Has(evidence.Quote)&&Has(evidence.Application),"资料理解缺少摘录或适用说明。");
                var source=CitationGuard.Resolve(sources,evidence.SourceId,evidence.StableSourceId,evidence.SourceHash);
                Require(source.Text.Contains(evidence.Quote),"引用摘录与原文不一致，尚未作为设计依据。");
            }
            var cited=sources.Where(s=>brief.Evidence.Any(e=>e.SourceId==s.Id)).ToList();
            Require(cited.Any(s=>s.Group=="材料与工艺")&&cited.Any(s=>s.Group=="历史问题")&&cited.Any(s=>s.Title.Contains("合规")),"材料限制、历史风险或合规测试依据不足；相关设计结论尚未验证，请检查内置资料。");
            Require(brief.Questions!=null,"需要明确是否存在阻断设计的问题。");
        }
        public static void ValidateManufacturing(Concept concept,List<SourceRecord> sources)
        {
            ValidateManufacturingStructure(concept);
            var p=concept.Manufacturing;
            foreach(var m in p.Materials)
            {
                var source=sources.FirstOrDefault(s=>s.Id==m.SourceId&&s.Group=="材料与工艺");
                Require(MaterialNameMatches(source,m.MaterialName),"物料未命中原材料表名称列："+m.MaterialName);
                Require(m.ProcessSourceIds!=null&&m.ProcessSourceIds.Count>0&&m.ProcessSourceIds.All(id=>sources.Any(s=>s.Id==id&&s.Id.Contains("/工艺适配/"))),"部件需要原表中可追溯的加工工艺。");
            }
            foreach(var risk in p.Risks.Concat(p.Tests))Require(sources.Any(s=>s.Id==risk.SourceId),"风险/测试必须有真实来源。");
            Require(p.Risks.Any(r=>sources.Any(s=>s.Id==r.SourceId&&s.Group=="历史问题"))&&p.Tests.Any(r=>sources.Any(s=>s.Id==r.SourceId&&s.Title.Contains("合规"))),"须应用已有历史问题和企业测试要求。");
            concept.Materials=String.Join("\n\n",p.Materials.Select(m=>m.Part+" · "+m.MaterialName+" × "+m.Quantity+"；厚度 "+m.ThicknessMm+" mm（"+m.ThicknessBasis+"）\n资料来源："+sources.First(s=>s.Id==m.SourceId).Source+"\n用途："+m.Purpose));
        }
        public static void ValidateManufacturingStructure(Concept concept)
        {
            var p=concept==null?null:concept.Manufacturing;
            Require(p!=null&&p.Materials!=null&&p.Materials.Count>0,"方案缺少物料清单。");
            foreach(var m in p.Materials)Require(m!=null&&Has(m.Part)&&Has(m.Role)&&Has(m.MaterialName)&&m.Quantity>0&&m.ThicknessMm>0&&Has(m.ThicknessBasis)&&Has(m.Purpose),"部件材料、数量、厚度假设和作用不能为空。");
            Require(concept.Assembly!=null&&p.Materials.Any(m=>m.Role=="unit-wall")&&p.Materials.Where(m=>m.Role=="unit-wall").All(m=>m.ThicknessMm==concept.Assembly.WallThickness),"所有内托壁厚须与几何计算一致，物料Role=unit-wall。");
            if(concept.Assembly.Shell!=null)Require(p.Materials.Any(m=>m.Role=="shell")&&p.Materials.Where(m=>m.Role=="shell").All(m=>m.ThicknessMm==concept.Assembly.Shell.WallThickness),"外壳须有Role=shell的材料记录，全部外壳板厚须与Shell.WallThickness一致；不能沿用内托板厚。");
            Require(Filled(p.AssemblySteps,3)&&Filled(p.SampleChecks,3)&&Has(p.Connections)&&Has(p.OpeningSequence)&&Has(p.Stability)&&Has(p.Protection)&&Has(p.CostDrivers),"需要具体装配顺序、连接、开合、稳定性、保护、成本因素与打样检查。");
            Require(p.Risks!=null&&p.Risks.Count>0&&p.Tests!=null&&p.Tests.Count>0,"必须说明历史风险应对与测试计划。");
            foreach(var risk in p.Risks.Concat(p.Tests))Require(risk!=null&&Has(risk.Risk)&&Has(risk.Action)&&Has(risk.Test),"风险/测试必须有预防动作和验证方法。");
        }
        public static List<string> PaperManufacturingIssues(Concept concept)
        {
            var issues=new List<string>();
            if(concept==null){issues.Add("缺少方案结构");return issues;}
            var plan=concept.Manufacturing;
            if(plan==null||plan.Materials==null){issues.Add("未提供纸类主体材料清单，工程待验证");return issues;}
            var forbidden=new[]{"塑料","注塑","abs","pp","pet","pvc","树脂","亚克力"};
            Func<string,bool> isPaper=name=>{
                var text=(name??"").ToLowerInvariant();
                return new[]{"纸","白卡","白板","灰板","瓦楞","特种纸","裱纸","paper","cardboard","corrugated"}.Any(text.Contains);
            };
            foreach(var material in plan.Materials.Where(m=>m!=null)){
                var name=material.MaterialName??"";var lower=name.ToLowerInvariant();
                if(forbidden.Any(lower.Contains))issues.Add("明确结构冲突：材料“"+name+"”依赖塑料/注塑主体");
                if((material.Role=="shell"||material.Role=="unit-wall")&&!isPaper(name))issues.Add("纸类主体材料未确认："+name);
            }
            if(!plan.Materials.Any(m=>m!=null&&m.Role=="shell"&&isPaper(m.MaterialName)))issues.Add("缺少纸类外壳主体材料（shell）");
            if(!plan.Materials.Any(m=>m!=null&&m.Role=="unit-wall"&&isPaper(m.MaterialName)))issues.Add("缺少纸类内托/分隔材料（unit-wall）");
            if(concept.Assembly==null||concept.Assembly.Parts==null||concept.Assembly.Parts.Count==0)issues.Add("缺少可解释的纸板部件组成");
            else foreach(var part in concept.Assembly.Parts.Where(p=>p!=null)){
                if(!Has(part.SizeBasis)||!Has(part.AssemblyNotes))issues.Add("部件“"+part.Id+"”缺少裁切/折叠/连接说明");
            }
            if(!Has(plan.Connections))issues.Add("未说明纸板面板的插接、粘合或折页连接");
            if(!Filled(plan.AssemblySteps,3))issues.Add("未提供至少三步纸板装配路径");
            if(!Has(plan.OpeningSequence)||!Has(plan.Stability))issues.Add("未说明闭合开合或纸板支撑稳定方式");
            return issues.Distinct().ToList();
        }
        public static void ValidateReview(DesignReview review,List<Concept> concepts,DesignUnderstanding understanding)
        {
            Require(review!=null&&review.DistinctDirections&&review.Concepts!=null&&review.Concepts.Count==concepts.Count,"独立方案复核未确认方向差异与完整性。");
            foreach(var c in concepts)
            {
                var checks=review.Concepts.Where(r=>r!=null&&r.Name==c.Name).ToList();Require(checks.Count==1,"复核方案名称缺失或重复。");var check=checks[0];
                Require(check.Pass&&check.ManufacturableConcept&&(check.SourceGrounded||(c.EngineeringIssues!=null&&c.EngineeringIssues.Count>0)),"方案复核未通过："+c.Name+"。"+check.Findings);
                Require(check.Requirements!=null&&check.Requirements.All(v=>v!=null&&understanding.Requirements.Any(r=>r.Id==v.Id))&&check.Requirements.Select(v=>v.Id).Distinct().Count()==check.Requirements.Count,"复核需求编号不存在或重复，不能用相互矛盾的结论判定通过："+c.Name);
                Require(understanding.Requirements.Where(r=>r.Status=="active").All(r=>check.Requirements.Any(v=>v.Id==r.Id&&v.Met&&Has(v.Evidence))),"有效需求尚未逐条通过复核："+c.Name);
            }
        }
        public static string Summary(DesignUnderstanding brief)
        {
            if(brief==null)return "旧方案尚未经过资料理解流程。";
            return brief.Goal+"\n场景："+brief.UseScene+"\n风格："+brief.Style+"\n\n"+String.Join("\n",brief.Requirements.Select(r=>r.Id+" · "+r.Original+"\n→ "+r.Meaning+(r.Status=="superseded"?"（被 "+r.SupersededBy+" 替代）":"")))+"\n\n假设："+brief.Assumptions+(brief.ReferenceWarnings==null||brief.ReferenceWarnings.Count==0?"":"\n\n资料核验："+String.Join("；",brief.ReferenceWarnings));
        }
        public static string ManufacturingSummary(ManufacturingPlan p)
        {
            if(p==null)return "旧方案尚无制造推导。";
            return "连接方式\n"+p.Connections+"\n\n装配步骤\n"+String.Join("\n",p.AssemblySteps.Select((s,i)=>(i+1)+". "+s))+"\n\n开合\n"+p.OpeningSequence+"\n\n展开稳定性\n"+p.Stability+"\n\n运输保护\n"+p.Protection+"\n\n打样验证\n"+String.Join("\n",p.SampleChecks)+"\n\n成本因素\n"+p.CostDrivers+"\n\n历史风险与测试\n"+String.Join("\n\n",p.Risks.Concat(p.Tests).Select(r=>r.Risk+" → "+r.Action+"；验证："+r.Test+"\n依据："+(Has(r.SourceId)?r.SourceId:"未核验")));
        }
    }
}
