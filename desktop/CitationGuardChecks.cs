using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PaperStudio
{
    internal static class CitationGuardChecks
    {
        static void Check(bool value,string message){if(!value)throw new Exception("Citation guard: "+message);}
        static void Reject(Action action,string message)
        {bool rejected=false;try{action();}catch(DesignValidationException){rejected=true;}Check(rejected,message);}
        static SourceEvidence Evidence(SourceRecord source)
        {return new SourceEvidence{SourceId=source.Id,Quote=source.Text.Substring(0,Math.Min(35,source.Text.Length)),Application="仅用于测试引用验证"};}
        static DesignUnderstanding Brief(IEnumerable<SourceRecord> sources)
        {return new DesignUnderstanding{Evidence=sources.Select(Evidence).ToList()};}
        public static void Run()
        {
            var records=Json.Read<List<SourceRecord>>(File.ReadAllText("desktop/resources/knowledge.json",Encoding.UTF8));
            // Fixed fixture keys are local clones; production keys must come from the persisted corpus.
            for(int i=0;i<records.Count;i++)records[i].StableId="00000000-0000-4000-8000-"+(i+1).ToString("D12");
            var grabie=records.First(s=>s.Title.Contains("Grabie"));
            var material=records.First(s=>s.Id.Contains("/材料汇总/5"));
            var history=records.First(s=>s.Group=="历史问题");
            var compliance=records.First(s=>s.Title.Contains("合规"));
            var process=records.First(s=>s.Id.EndsWith("/工艺适配/17"));
            var selected=new List<SourceRecord>{material,history,compliance,grabie,process};
            var core=new[]{material,history,compliance};
            Check(CitationGuard.Inspect(null,selected,records).Any(i=>i.Code=="invalid-evidence")&&CitationGuard.Inspect(new DesignUnderstanding(),selected,records).Any(i=>i.Code=="invalid-evidence"),"missing understanding or evidence lists cannot silently pass");
            var brief=Brief(core.Concat(new[]{grabie}));
            Check(CitationGuard.Inspect(brief,selected,records).Count==0,"exact continuous source quotes must validate");
            Check(brief.Evidence.All(e=>e.StableSourceId==CitationGuard.Key(records.Single(s=>s.Id==e.SourceId))&&e.SourceHash==CitationGuard.Hash(records.Single(s=>s.Id==e.SourceId))),"successful quotes bind to real stable keys and exact text versions");

            var bad=Brief(core.Concat(new[]{grabie}));var dropped=bad.Evidence.Last();
            dropped.Quote="Grabie纸盒可以立体展开，兼顾展示与趣味。";
            Check(!grabie.Text.Contains(dropped.Quote),"the real Grabie paraphrase is not a source quotation");
            var issues=CitationGuard.Inspect(bad,selected,records);
            Check(issues.Count==1&&issues[0].Code=="quote-mismatch"&&issues[0].SourceText==grabie.Text,"paraphrases expose the actual text without inventing a replacement quote");
            Check(dropped.Quote=="Grabie纸盒可以立体展开，兼顾展示与趣味。","failed quotes remain untouched for repair");
            Check(CitationGuard.SkipUnavailable(bad,issues,selected,records)&&bad.Evidence.Count==3&&String.IsNullOrEmpty(dropped.Application)&&bad.ReferenceWarnings.Count==1,"a bad extra case can be explicitly excluded only after key evidence is intact");
            Check(CitationGuard.Inspect(bad,selected,records).Count==0,"remaining quotes still pass exact-source allowlist checks");

            bad=Brief(core);bad.Evidence.Add(new SourceEvidence{SourceId="不存在的资料",Quote="未核验原文",Application="未经证实的事实"});
            issues=CitationGuard.Inspect(bad,selected,records);
            Check(issues.Single().Code=="unknown-source"&&!issues.Single().Optional&&!CitationGuard.SkipUnavailable(bad,issues,selected,records),"unknown references cannot be proven optional even when key coverage is complete");
            bad=Brief(core);bad.Evidence.Add(new SourceEvidence{SourceId="新材料表/材料汇总/999",Quote="未知材料依据",Application="用作承重材料"});
            Check(!CitationGuard.SkipUnavailable(bad,CitationGuard.Inspect(bad,selected,records),selected,records),"complete key coverage never permits dropping an unidentified material citation");
            bad=Brief(core.Concat(new[]{grabie}));bad.Evidence.Last().StableSourceId=grabie.StableId;
            var withoutGrabie=records.Where(s=>s!=grabie).ToList();
            Check(CitationGuard.SkipUnavailable(bad,CitationGuard.Inspect(bad,selected,withoutGrabie),selected,withoutGrabie),"a deleted pure case remains identifiable through its actual selected stable-key snapshot");
            bad=Brief(core.Concat(new[]{grabie}));
            Check(!CitationGuard.SkipUnavailable(bad,CitationGuard.Inspect(bad,selected,withoutGrabie),selected,withoutGrabie),"a deleted record's legacy ID alone cannot establish a safe category");
            bad=Brief(core.Concat(new[]{grabie}));bad.Evidence.Last().StableSourceId="无法识别的稳定来源";
            Check(!CitationGuard.SkipUnavailable(bad,CitationGuard.Inspect(bad,selected,records),selected,records),"a known case alias cannot make an unknown stable identity optional");
            bad=Brief(core.Concat(new[]{grabie}));
            issues=CitationGuard.Inspect(bad,selected.Where(s=>s!=grabie).ToList(),records);
            Check(issues.Single().Code=="not-selected","a known corpus record not sent to the model is not allowed");
            Check(CitationGuard.SkipUnavailable(bad,issues,selected.Where(s=>s!=grabie).ToList(),records),"an unselected pure case may be excluded without treating it as evidence");

            bad=Brief(core.Concat(new[]{grabie}));bad.Evidence[0].Quote="伪造材料引文";
            issues=CitationGuard.Inspect(bad,selected,records);string before=Json.Write(bad);
            Check(issues.Any(i=>i.Code=="quote-mismatch"&&!i.Optional)&&issues.Any(i=>i.Code=="missing-critical-coverage")&&!CitationGuard.SkipUnavailable(bad,issues,selected,records)&&Json.Write(bad)==before,"failed material evidence must block without deleting or rewriting it");
            bad=Brief(core.Concat(new[]{grabie}));bad.Evidence.Add(Evidence(material));bad.Evidence.Last().Quote="额外的失效材料引文";
            issues=CitationGuard.Inspect(bad,selected,records);
            Check(!CitationGuard.SkipUnavailable(bad,issues,selected,records),"valid material coverage cannot authorize dropping another known critical citation");
            foreach(var critical in new[]{history,compliance})
            {
                bad=Brief(core.Concat(new[]{grabie}));bad.Evidence.Add(Evidence(critical));bad.Evidence.Last().Quote="失效的关键摘录";
                issues=CitationGuard.Inspect(bad,selected,records);
                Check(!CitationGuard.SkipUnavailable(bad,issues,selected,records),"extra known history or test citations cannot be treated as optional cases");
            }
            bad=Brief(new[]{material,history});bad.Evidence.Add(new SourceEvidence{SourceId="未知",Quote="未知",Application="未知"});
            Check(!CitationGuard.SkipUnavailable(bad,CitationGuard.Inspect(bad,selected,records),selected,records),"missing compliance coverage prevents skipping unknown evidence");
            bad=Brief(core.Concat(new[]{grabie}));bad.Evidence.Last().Quote="改写案例";issues=CitationGuard.Inspect(bad,selected,records);bad.Evidence[0].Quote="随后破坏材料";
            Check(!CitationGuard.SkipUnavailable(bad,issues,selected,records),"stale issue lists cannot bypass a newly invalid critical citation");

            var renamed=Json.Copy(grabie);renamed.Id="改名后的案例/7";
            Check(CitationGuard.Resolve(new List<SourceRecord>{renamed},grabie.Id,grabie.StableId,CitationGuard.Hash(grabie))==renamed,"stable identity survives a legacy display ID rename");
            var renamedSources=core.Concat(new[]{renamed}).ToList();var renamedBrief=Brief(core.Concat(new[]{grabie}));renamedBrief.Evidence.Last().StableSourceId=grabie.StableId;
            Check(CitationGuard.Inspect(renamedBrief,renamedSources,renamedSources).Count==0&&renamedBrief.Evidence.Last().SourceId==renamed.Id,"inspection canonicalizes renamed citations for downstream legacy readers");
            renamed.Text+="\n新增资料版本";
            Reject(()=>CitationGuard.Resolve(new List<SourceRecord>{renamed},grabie.Id,grabie.StableId,CitationGuard.Hash(grabie)),"a changed body must invalidate the recorded source hash");
            Reject(()=>CitationGuard.Resolve(selected,material.Id,"伪造的稳定标识"),"a forged stable key cannot fall back to a valid legacy ID");
            Reject(()=>CitationGuard.Resolve(selected,material.Id,grabie.StableId),"stable and legacy IDs cannot name different real records");
            Check(CitationGuard.Resolve(selected,grabie.StableId)==grabie,"a stable key can be supplied directly as the source identifier");
            var missing=Json.Copy(material);missing.StableId=null;Reject(()=>CitationGuard.Key(missing),"missing production identity is never fabricated from text or row names");
            var duplicate=Json.Copy(grabie);duplicate.StableId="00000000-0000-4000-8000-999999999999";
            Reject(()=>CitationGuard.Resolve(selected.Concat(new[]{duplicate}).ToList(),grabie.Id,grabie.StableId),"duplicate legacy IDs remain unsafe for downstream legacy lookups");
            duplicate=Json.Copy(grabie);duplicate.Id="another-case";
            Reject(()=>CitationGuard.Resolve(selected.Concat(new[]{duplicate}).ToList(),grabie.Id),"duplicate stable IDs are ambiguous even with a unique legacy alias");
            Check(CitationGuard.Hash(new SourceRecord{Text="abc"})=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","source version uses SHA256 of exact UTF8 text");

            var concept=new Concept{Manufacturing=WorkflowFixtures.Manufacturing(records,1)};
            CitationGuard.BindManufacturing(concept,selected);
            Check(concept.Manufacturing.Materials.All(m=>m.StableSourceId==material.StableId&&m.SourceHash==CitationGuard.Hash(material)&&m.StableProcessIds.SequenceEqual(new[]{process.StableId})),"materials and processes bind to actual selected stable records");
            Check(concept.Manufacturing.Risks.Concat(concept.Manufacturing.Tests).All(r=>!String.IsNullOrEmpty(r.StableSourceId)&&!String.IsNullOrEmpty(r.SourceHash)),"risk and test sources get versioned bindings too");
            foreach(var unavailable in new[]{material,process,history,compliance})
            {
                var candidate=new Concept{Manufacturing=WorkflowFixtures.Manufacturing(records,1)};
                string original=Json.Write(candidate);
                Reject(()=>CitationGuard.BindManufacturing(candidate,selected.Where(s=>s!=unavailable).ToList()),"no manufacturing source may escape the actual selected set: "+unavailable.Id);
                Check(Json.Write(candidate)==original,"failed manufacturing binding preserves the response for targeted repair");
            }
            var forged=new Concept{Manufacturing=WorkflowFixtures.Manufacturing(records,1)};forged.Manufacturing.Materials[0].StableSourceId="伪造";
            Reject(()=>CitationGuard.BindManufacturing(forged,selected),"manufacturing stable identity is not silently overwritten");
            var changed=Json.Copy(selected);changed.Single(s=>s.Id==material.Id).Text+="版本变化";
            Reject(()=>CitationGuard.BindManufacturing(concept,changed),"saved material bindings cannot silently accept changed source text");
            Console.WriteLine("PASS: exact selected citations, optional case exclusion, critical evidence, stable IDs, source versions and manufacturing bindings.");
        }
    }
}
