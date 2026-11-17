using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PaperStudio
{
    public class CitationIssue
    {
        public string Code, SourceId, Quote, StableId, SourceText;
        public bool Optional;
        internal SourceEvidence Evidence;
    }
    public static class CitationGuard
    {
        sealed class CitationFailure:DesignValidationException
        {
            public readonly string Code;
            public CitationFailure(string code,string message):base(message){Code=code;}
        }
        static bool Has(string value){return !String.IsNullOrWhiteSpace(value);}
        static void Require(bool value,string code,string message){if(!value)throw new CitationFailure(code,message);}
        public static string Key(SourceRecord source)
        {
            Require(source!=null&&Has(source.StableId),"missing-stable-id","资料缺少持久稳定标识，需修复资料库。");
            return source.StableId;
        }
        public static string Hash(SourceRecord source)
        {
            Require(source!=null&&source.Text!=null,"missing-source-text","资料正文缺失。");
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source.Text))).Replace("-","").ToLowerInvariant();
        }
        public static SourceRecord Resolve(List<SourceRecord> sources,string id,string stableId=null,string hash=null)
        {
            var records=(sources??new List<SourceRecord>()).Where(s=>s!=null).ToList();
            string key=Has(stableId)?stableId:id;
            var stable=Has(key)?records.Where(s=>s.StableId==key).ToList():new List<SourceRecord>();
            Require(stable.Count<=1,"ambiguous-stable-id","资料稳定标识重复，无法确定引用。");
            Require(!Has(stableId)||stable.Count==1,"unknown-source","稳定资料标识不存在于可用资料中。");
            var aliases=Has(id)?records.Where(s=>s.Id==id).ToList():new List<SourceRecord>();
            Require(aliases.Count<=1,"ambiguous-id","资料旧标识重复，无法确定引用。");
            var source=stable.Count==1?stable[0]:aliases.SingleOrDefault();
            Require(source!=null,"unknown-source","资料标识不存在于可用资料中。");
            Require(aliases.Count==0||aliases[0]==source,"identity-mismatch","资料稳定标识与旧标识指向不同记录。");
            Key(source);
            Require(records.Count(s=>s.StableId==source.StableId)==1,"ambiguous-stable-id","资料稳定标识重复，无法确定引用。");
            Require(Has(source.Id)&&records.Count(s=>s.Id==source.Id)==1,"ambiguous-id","资料旧标识缺失或重复，无法确定引用。");
            Require(!Has(hash)||String.Equals(Hash(source),hash,StringComparison.OrdinalIgnoreCase),"version-mismatch","资料正文版本已变化，须重新核验引用。");
            return source;
        }
        static bool Critical(SourceRecord source)
        {return source.Group=="材料与工艺"||source.Group=="历史问题"||(source.Group??"").Contains("测试")||(source.Title??"").Contains("合规")||(source.Title??"").Contains("测试");}
        static CitationIssue Issue(string code,SourceEvidence evidence,List<SourceRecord> selected,List<SourceRecord> all)
        {
            var known=(all??new List<SourceRecord>()).Where(s=>s!=null&&evidence!=null&&
                ((Has(evidence.SourceId)&&(s.Id==evidence.SourceId||s.StableId==evidence.SourceId))||(Has(evidence.StableSourceId)&&s.StableId==evidence.StableSourceId))).ToList();
            SourceRecord classified=null;
            if(evidence!=null){try{classified=Resolve(all,evidence.SourceId,evidence.StableSourceId);}catch(CitationFailure){}}
            // A deleted record is identifiable only by the stable key actually sent in this request.
            if(known.Count==0&&evidence!=null&&(Has(evidence.StableSourceId)||(selected??new List<SourceRecord>()).Any(s=>s!=null&&Has(evidence.SourceId)&&s.StableId==evidence.SourceId)))
            {
                try{classified=Resolve(selected,evidence.SourceId,evidence.StableSourceId);known.Add(classified);}catch(CitationFailure){}
            }
            return new CitationIssue{Code=code,Evidence=evidence,SourceId=evidence==null?null:evidence.SourceId,Quote=evidence==null?null:evidence.Quote,
                StableId=known.Count==1?known[0].StableId:evidence==null?null:evidence.StableSourceId,SourceText=known.Count==1?known[0].Text:null,
                Optional=classified!=null&&!Critical(classified)&&ReferenceSearch.IsCase(classified)};
        }
        public static List<CitationIssue> Inspect(DesignUnderstanding brief,List<SourceRecord> selected,List<SourceRecord> all)
        {
            var issues=new List<CitationIssue>();var valid=new List<SourceRecord>();
            if(brief==null||brief.Evidence==null){issues.Add(new CitationIssue{Code="invalid-evidence",SourceText="需求理解或引用列表缺失。"});return issues;}
            foreach(var evidence in brief.Evidence)
            {
                try
                {
                    Require(evidence!=null,"invalid-evidence","引用条目为空。");
                    var source=Resolve(all,evidence.SourceId,evidence.StableSourceId,evidence.SourceHash);
                    SourceRecord available;
                    try{available=Resolve(selected,source.Id,Key(source),Hash(source));}
                    catch(CitationFailure error){if(error.Code=="unknown-source")throw new CitationFailure("not-selected","引用资料未包含在本次实际检索结果中。");throw;}
                    Require(Has(evidence.Quote)&&available.Text.IndexOf(evidence.Quote,StringComparison.Ordinal)>=0,"quote-mismatch","引用不是该资料的连续原文。");
                    Require(Has(evidence.Application),"invalid-evidence","引用缺少适用说明。");
                    evidence.SourceId=available.Id;evidence.StableSourceId=Key(available);evidence.SourceHash=Hash(available);valid.Add(available);
                }
                catch(CitationFailure error){issues.Add(Issue(error.Code,evidence,selected,all));}
            }
            var missing=new List<string>();
            if(!valid.Any(s=>s.Group=="材料与工艺"))missing.Add("材料与工艺");
            if(!valid.Any(s=>s.Group=="历史问题"))missing.Add("历史问题");
            if(!valid.Any(s=>(s.Title??"").Contains("合规")))missing.Add("合规与测试");
            if(missing.Count>0)issues.Add(new CitationIssue{Code="missing-critical-coverage",SourceText="缺少有效原文依据："+String.Join("、",missing)});
            return issues;
        }
        public static bool SkipUnavailable(DesignUnderstanding brief,List<CitationIssue> issues,List<SourceRecord> selected,List<SourceRecord> all)
        {
            // Reinspect current inputs: a stale issue list must not authorize dropping a new critical failure.
            var current=Inspect(brief,selected,all);
            if(current.Count==0)return true;
            if(current.Any(issue=>!issue.Optional||issue.Evidence==null))return false;
            if(brief.ReferenceWarnings==null)brief.ReferenceWarnings=new List<string>();
            foreach(var issue in current)
            {
                issue.Evidence.Application=null;brief.Evidence.Remove(issue.Evidence);
                string warning="部分创意参考未通过原文核验，已排除，相关内容不作为设计依据。";
                if(!brief.ReferenceWarnings.Contains(warning))brief.ReferenceWarnings.Add(warning);
            }
            return true;
        }
        public static void BindManufacturing(Concept concept,List<SourceRecord> selected)
        {
            var plan=concept==null?null:concept.Manufacturing;
            Require(plan!=null&&plan.Materials!=null&&plan.Materials.Count>0&&plan.Risks!=null&&plan.Risks.Count>0&&plan.Tests!=null&&plan.Tests.Count>0,"missing-manufacturing","制造计划缺少材料、风险或测试引用。");
            var bindings=new List<Action>();
            foreach(var material in plan.Materials)
            {
                Require(material!=null,"invalid-material","材料引用为空。");
                var source=Resolve(selected,material.SourceId,material.StableSourceId,material.SourceHash);
                Require(source.Group=="材料与工艺","invalid-material","材料须来自本次检索的材料与工艺记录。");
                var ids=material.ProcessSourceIds??new List<string>();var keys=material.StableProcessIds??new List<string>();
                Require(Math.Max(ids.Count,keys.Count)>0&&(keys.Count==0||ids.Count==0||keys.Count==ids.Count),"invalid-process","工艺引用缺失或稳定标识数量不一致。");
                var processes=new List<SourceRecord>();
                for(int i=0;i<Math.Max(ids.Count,keys.Count);i++)
                {
                    var process=Resolve(selected,ids.Count==0?null:ids[i],keys.Count==0?null:keys[i]);
                    Require(process.Id.Contains("/工艺适配/"),"invalid-process","工艺须来自本次检索的工艺适配记录。");processes.Add(process);
                }
                var target=material;
                bindings.Add(()=>{target.SourceId=source.Id;target.StableSourceId=Key(source);target.SourceHash=Hash(source);target.ProcessSourceIds=processes.Select(p=>p.Id).ToList();target.StableProcessIds=processes.Select(Key).ToList();});
            }
            foreach(var risk in plan.Risks.Concat(plan.Tests))
            {
                Require(risk!=null,"invalid-risk","风险或测试引用为空。");
                var source=Resolve(selected,risk.SourceId,risk.StableSourceId,risk.SourceHash);var target=risk;
                bindings.Add(()=>{target.SourceId=source.Id;target.StableSourceId=Key(source);target.SourceHash=Hash(source);});
            }
            foreach(var bind in bindings)bind();
        }
    }
}
