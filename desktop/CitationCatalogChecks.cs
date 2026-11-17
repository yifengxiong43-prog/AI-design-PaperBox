using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PaperStudio
{
    internal static class CitationCatalogChecks
    {
        public class Fragment { public string EvidenceId{get;set;} public string Quote{get;set;} }
        public class Entry { public string SourceId{get;set;} public string StableId{get;set;} public string Source{get;set;} public List<Fragment> Evidence{get;set;} }
        static void Check(bool value,string message){if(!value)throw new Exception("Citation catalog: "+message);}
        static void Reject(Action action,string message)
        {bool rejected=false;try{action();}catch(DesignValidationException){rejected=true;}Check(rejected,message);}
        static List<Entry> Entries(CitationCatalog catalog)
        {string prompt=catalog.Prompt();int start=prompt.IndexOf('[');return Json.Read<List<Entry>>(prompt.Substring(start,prompt.IndexOf("\n【本次引用目录JSON结束】",StringComparison.Ordinal)-start));}
        static DesignUnderstanding Brief(string evidenceId)
        {return new DesignUnderstanding{Evidence=new List<SourceEvidence>{new SourceEvidence{EvidenceId=evidenceId,Application="按原文核对展开单排结构"}}};}
        static string Snapshot(List<SourceRecord> sources)
        {return Json.Write(sources.Select(s=>new object[]{s.Id,s.StableId,s.Title,s.Group,s.Source,s.Text,s.Images,s.Brief==null?null:new[]{s.Brief.Name,s.Brief.Size,s.Brief.Quantity,s.Brief.Cost,s.Brief.Market,s.Brief.Style,s.Brief.Needs}}).ToList());}
        public static void Run()
        {
            var records=Json.Read<List<SourceRecord>>(File.ReadAllText("desktop/resources/knowledge.json",Encoding.UTF8));
            records.AddRange(Json.Read<List<SourceRecord>>(File.ReadAllText("desktop/resources/web-knowledge.json",Encoding.UTF8)));
            var hanauma=records.Single(s=>s.StableId=="645dd8a7-b92b-4919-863b-fde373e4d9fa");
            var acceptance=records.Single(s=>s.Id.EndsWith("/选型与验收/6"));
            var material=records.First(s=>s.Id.Contains("/材料汇总/5"));var process=records.First(s=>s.Id.EndsWith("/工艺适配/17"));
            var history=records.First(s=>s.Group=="历史问题");var compliance=records.First(s=>s.Title.Contains("合规"));
            var epe=records.Single(s=>s.StableId.StartsWith("89ad27",StringComparison.Ordinal));
            var selected=new List<SourceRecord>{hanauma,acceptance,material,process,history,compliance,epe};string original=Snapshot(selected);
            var catalog=new CitationCatalog(selected);var entries=Entries(catalog);
            Check(Snapshot(selected)==original,"building the catalog cannot modify source records");
            Check(catalog.Prompt()==new CitationCatalog(selected.AsEnumerable().Reverse().ToList()).Prompt(),"stable-key order makes catalog handles independent of retrieval order");
            Check(entries.Select(e=>e.StableId).SequenceEqual(selected.Select(s=>s.StableId).OrderBy(s=>s,StringComparer.Ordinal)),"sources sort by persistent stable identity");
            foreach(var entry in entries)
            {
                var source=selected.Single(s=>s.StableId==entry.StableId);
                Check(entry.Source==source.Source&&entry.Evidence.Count<=4&&entry.Evidence.All(e=>e.Quote.Length<=240&&source.Text.Contains(e.Quote)&&!e.Quote.StartsWith("原表字段")),"snippets retain exact text, omit table headings, and stay within the source budget");
            }
            var hanaumaEntry=entries.Single(e=>e.StableId==hanauma.StableId);
            var fragment=hanaumaEntry.Evidence.Single(e=>e.Quote.Contains("展开三页，每页单排"));
            var brief=Brief(fragment.EvidenceId);catalog.ResolveUnderstanding(brief);var evidence=brief.Evidence[0];
            Check(evidence.SourceId==hanauma.Id&&evidence.StableSourceId==hanauma.StableId&&evidence.SourceHash==CitationGuard.Hash(hanauma)&&evidence.Quote==fragment.Quote&&evidence.EvidenceId==null,"choosing evidence resolves exact original text and clears transient handles");
            Check(evidence.Application=="按原文核对展开单排结构","binding retains the model's applicability explanation");
            Reject(()=>catalog.ResolveUnderstanding(Brief("E999-99-not-in-this-catalog")),"unknown evidence handles are not guessed");
            var other=new CitationCatalog(selected.Where(s=>s!=acceptance).ToList());
            Reject(()=>other.ResolveUnderstanding(Brief(fragment.EvidenceId)),"an old evidence handle cannot bind in a changed retrieval scope");
            var changed=Json.Copy(selected);changed.Single(s=>s.Id==hanauma.Id).Text+="资料版本变化";
            Reject(()=>new CitationCatalog(changed).ResolveUnderstanding(Brief(fragment.EvidenceId)),"changed source versions invalidate old catalog handles");
            foreach(string field in new[]{"SourceId","StableSourceId","Quote","SourceHash"})
            {
                var conflict=Brief(fragment.EvidenceId);
                if(field=="SourceId")conflict.Evidence[0].SourceId=material.Id;
                if(field=="StableSourceId")conflict.Evidence[0].StableSourceId=material.StableId;
                if(field=="Quote")conflict.Evidence[0].Quote="展开三页，每页单排。";
                if(field=="SourceHash")conflict.Evidence[0].SourceHash="not-the-recorded-version";
                string before=Json.Write(conflict);Reject(()=>catalog.ResolveUnderstanding(conflict),"conflicting "+field+" cannot be silently overwritten");
                Check(Json.Write(conflict)==before,"failed understanding binding preserves the response for repair");
            }
            brief=Brief(fragment.EvidenceId);brief.Evidence[0].Application=" ";Reject(()=>catalog.ResolveUnderstanding(brief),"evidence selection still needs an applicability explanation");
            var legacy=new DesignUnderstanding{Evidence=new List<SourceEvidence>{new SourceEvidence{SourceId="645dd8a7-b92b-4919-863f-fde373e4d9fa",Quote="展开三页，每页单排。",Application="旧协议错误UUID"}}};
            catalog.ResolveUnderstanding(legacy);
            Check(CitationGuard.Inspect(legacy,selected,records).Any(i=>i.Code=="unknown-source"),"legacy UUID typos still fail rather than being corrected from the quotation");
            legacy.Evidence[0].SourceId=epe.StableId;legacy.Evidence[0].Quote="验收方式：白样盒、展开刀线、装配测试";
            catalog.ResolveUnderstanding(legacy);
            Check(CitationGuard.Inspect(legacy,selected,records).Any(i=>i.Code=="quote-mismatch"),"the production acceptance quote wrongly attached to EPE remains invalid");
            var acceptanceFragment=entries.Single(e=>e.StableId==acceptance.StableId).Evidence.Single(e=>e.Quote.Contains("验收方式：白样盒、展开刀线、装配测试"));
            var acceptanceBrief=Brief(acceptanceFragment.EvidenceId);catalog.ResolveUnderstanding(acceptanceBrief);
            Check(acceptanceBrief.Evidence[0].SourceId==acceptance.Id&&acceptanceBrief.Evidence[0].Quote==acceptanceFragment.Quote,"selecting the real acceptance evidence binds the original text to its actual source");
            var partial=Brief(fragment.EvidenceId);partial.Evidence.Add(new SourceEvidence{EvidenceId="E999-99-unknown",Application="未知片段"});string partialBefore=Json.Write(partial);
            Reject(()=>catalog.ResolveUnderstanding(partial),"a later bad fragment blocks the whole binding");
            Check(Json.Write(partial)==partialBefore&&partial.Evidence[0].EvidenceId==fragment.EvidenceId,"a failed response retains all original evidence handles for diagnostics");

            var clauses=new List<string>{"36支丙烯笔，单支150×15mm；展开后各组单排，完整笔身同时可见"};
            var understood=WorkflowFixtures.Understanding(clauses,records);
            understood.Evidence=new[]{material,history,compliance,hanauma}.Select(s=>new SourceEvidence{EvidenceId=entries.Single(e=>e.StableId==s.StableId).Evidence[0].EvidenceId,Application="按本条资料核对实际36支方案"}).ToList();
            catalog.ResolveUnderstanding(understood);
            Check(CitationGuard.Inspect(understood,selected,records).Count==0,"the actual 36-pen evidence can enter the existing strict workflow");
            DesignMethod.ValidateUnderstanding(understood,clauses,selected);
            var concept=new Concept{Manufacturing=WorkflowFixtures.Manufacturing(records,1),Assembly=new AssemblyPlan{WallThickness=1}};
            foreach(var m in concept.Manufacturing.Materials){m.SourceId=entries.Single(e=>e.StableId==material.StableId).SourceId;m.ProcessSourceIds=new List<string>{entries.Single(e=>e.StableId==process.StableId).SourceId};}
            foreach(var r in concept.Manufacturing.Risks.Concat(concept.Manufacturing.Tests))r.SourceId=entries.Single(e=>e.StableId==records.Single(s=>s.Id==r.SourceId).StableId).SourceId;
            var pending=Json.Copy(concept);catalog.BindManufacturing(concept);
            Check(concept.Manufacturing.Materials.All(m=>m.SourceId==material.Id&&m.StableSourceId==material.StableId&&m.ProcessSourceIds.Single()==process.Id&&m.StableProcessIds.Single()==process.StableId),"manufacturing handles bind real materials and processes without transient IDs");
            DesignMethod.ValidateManufacturing(concept,selected);
            var stale=Json.Copy(pending);
            stale.Manufacturing.Materials[0].StableProcessIds=new List<string>{"old-stable-id","another-old-id"};
            catalog.BindManufacturing(stale);
            Check(stale.Manufacturing.Materials[0].ProcessSourceIds.Single()==process.Id&&stale.Manufacturing.Materials[0].StableProcessIds.Single()==process.StableId,"current short process handles replace a stale stable-ID list");
            var unknown=Json.Copy(pending);
            unknown.Manufacturing.Materials[0].ProcessSourceIds=new List<string>{"S999-not-in-this-catalog"};
            unknown.Manufacturing.Materials[0].StableProcessIds=new List<string>{"old-stable-id","another-old-id"};
            Reject(()=>catalog.BindManufacturing(unknown),"unknown current process handles cannot be replaced by stale stable IDs");
            Reject(()=>other.BindManufacturing(Json.Copy(pending)),"manufacturing handles cannot cross retrieval scopes");
            pending.Manufacturing.Materials[0].StableSourceId=hanauma.StableId;
            catalog.BindManufacturing(pending);
            Check(pending.Manufacturing.Materials[0].SourceId==material.Id&&pending.Manufacturing.Materials[0].StableSourceId==material.StableId,"current material handle replaces stale model-supplied stable identity");
            var typo=new Concept{Manufacturing=WorkflowFixtures.Manufacturing(records,1)};typo.Manufacturing.Materials[0].SourceId="645dd8a7-b92b-4919-863f-fde373e4d9fa";
            Reject(()=>catalog.BindManufacturing(typo),"manufacturing UUID typos are not repaired by the catalog");
            var partialConcept=Json.Copy(concept);
            partialConcept.Manufacturing.Materials[0].ProcessSourceIds=new List<string>{process.Id,"S999-not-in-this-catalog"};
            partialConcept.Manufacturing.Materials[0].StableProcessIds=new List<string>();
            var issues=catalog.BindManufacturingForConcept(partialConcept);
            Check(issues.Any(i=>i.Contains("ProcessSourceIds[1]"))&&partialConcept.Manufacturing.Materials[0].ProcessSourceIds.SequenceEqual(new[]{process.Id})&&partialConcept.Manufacturing.Materials[0].StableProcessIds.SequenceEqual(new[]{process.StableId}),"partial binding retains real process evidence and records the unresolved field without a fabricated ID");
            Console.WriteLine("PASS: scoped short evidence handles, exact quotations, strict identity conflicts and manufacturing catalog bindings.");
        }
    }
}
