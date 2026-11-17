using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PaperStudio
{
    internal static class KnowledgeCacheChecks
    {
        const string Resources = "desktop/resources/";
        static void Check(bool condition,string message){if(!condition)throw new Exception("Knowledge cache: "+message);}
        static void Reject(Action action,string message)
        {
            bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}
            Check(rejected,message);
        }
        static string Hash(byte[] bytes)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static Dictionary<string,object> ReadObject(string filename)
        {return (Dictionary<string,object>)Json.Serializer().DeserializeObject(File.ReadAllText(Resources+filename));}
        static string SourceText(List<SourceRecord> sources)
        {return String.Join("\n\n",sources.Select(s=>s.Id+"\n"+s.Source+"\n"+s.Text));}

        public static void Run()
        {
            byte[] knowledge=File.ReadAllBytes(Resources+"knowledge.json"),web=File.ReadAllBytes(Resources+"web-knowledge.json");
            var sources=Json.Read<List<SourceRecord>>(Encoding.UTF8.GetString(knowledge)).Concat(Json.Read<List<SourceRecord>>(Encoding.UTF8.GetString(web))).ToList();
            string json=File.ReadAllText(Resources+"knowledge-analysis.json");
            var analysis=Json.Read<KnowledgeAnalysis>(json);
            var cache=KnowledgeCache.Load(json,knowledge,web,sources);
            Check(Json.Read<List<SourceRecord>>(Encoding.UTF8.GetString(knowledge)).Count==259&&sources.Count==276&&analysis.Entries.Count==276,"259 original records, 8 previous research records and 9 new case analyses are embedded");
            Check(new HashSet<string>(analysis.Entries.Select(e=>e.SourceId)).SetEquals(sources.Select(s=>s.Id)),"analysis covers every source exactly once");
            foreach(var entry in analysis.Entries){
                var original=sources.Single(s=>s.Id==entry.SourceId);
                Check(entry.Facts.Count>0&&entry.Facts.All(f=>original.Text.Contains(f.Quote)),"cached facts are contiguous source quotes: "+entry.SourceId);
                Check(!String.IsNullOrWhiteSpace(entry.Summary),"each source has a reusable summary");
            }
            Check(analysis.Rules.Count>=20&&analysis.Rules.All(r=>r.SourceIds.All(id=>sources.Any(s=>s.Id==id))&&!String.IsNullOrWhiteSpace(r.Provenance)),"derived design rules identify provenance and valid sources");

            byte[] changed=(byte[])knowledge.Clone();changed[changed.Length-1]^=1;
            Reject(()=>KnowledgeCache.Load(json,changed,web,sources),"changed document corpus invalidates cached analysis");
            changed=(byte[])web.Clone();changed[changed.Length-1]^=1;
            Reject(()=>KnowledgeCache.Load(json,knowledge,changed,sources),"changed web corpus invalidates cached analysis");
            var invalid=Json.Read<KnowledgeAnalysis>(json);invalid.Entries[0].Facts[0].Quote="invented uncited cache fact";
            Reject(()=>KnowledgeCache.Load(Json.Write(invalid),knowledge,web,sources),"invented evidence cannot be loaded");
            invalid=Json.Read<KnowledgeAnalysis>(json);invalid.Entries.RemoveAt(0);
            Reject(()=>KnowledgeCache.Load(Json.Write(invalid),knowledge,web,sources),"missing source analysis cannot be loaded");
            invalid=Json.Read<KnowledgeAnalysis>(json);invalid.Version="999";
            Reject(()=>KnowledgeCache.Load(Json.Write(invalid),knowledge,web,sources),"unknown cache format cannot be silently reused");
            RecoveryChecks(json,knowledge,web,sources);

            VerifyOriginals(analysis);
            var magnet=cache.Select(sources,"12支酒精马克笔，磁吸翻盖，磁铁固定，白卡纸内托，保留Ohuhu滑板变色龙");
            var drawer=cache.Select(sources,"300支彩铅，抽屉式模块纸盒，不要磁铁，不要塑料，展开后所有笔身都能一次看见");
            Check(magnet.Any(s=>s.Group=="材料与工艺"&&s.Text.Contains("磁铁")),"magnetic closure query retrieves its actual accessory record");
            Check(drawer.Any(s=>ReferenceSearch.IsCase(s)&&(s.Text+s.Title).Contains("抽屉")),"drawer query retrieves a supporting packaging case");
            Check(drawer.Any(ReferenceSearch.IsCase),"colored-pencil request has structure examples even without exact product match");
            Check(!new HashSet<string>(magnet.Select(s=>s.Id)).SetEquals(drawer.Select(s=>s.Id)),"changed natural-language requirements select different source sets");
            Check(cache.Select(sources,"36支丙烯笔彩盒").First(s=>s.Group=="产品案例").Id.EndsWith("/现有产品包装/6"),"36-pen query prioritizes the actual 36-pen case, not the generic 48-pen example");
            Check(cache.Select(sources,"旧例48支仅供参考；当前笔支数：72支；丙烯笔彩盒").First(s=>s.Group=="产品案例").Id.EndsWith("/现有产品包装/5"),"structured current quantity outranks a previous example when selecting cases");
            Check(magnet.Count(s=>s.Group=="产品案例")<=2&&drawer.Count(s=>s.Group=="产品案例")<=2,"near-identical existing-product cases cannot consume the entire case budget");
            var forbidden=cache.Select(sources,"不要EVA，不要磁铁，保留纸盒");
            Check(forbidden.Any(s=>s.Id.EndsWith("/保护材料/10"))&&forbidden.Any(s=>s.Group=="材料与工艺"&&s.Text.Contains("磁铁")),"forbidden material records remain available for checking aliases and limitations, not as permission to use them");
            Check(cache.Context(forbidden).Contains("不代表允许使用"),"retrieved forbidden names are explicitly distinguished from design permission");
            var fullVisible=cache.Select(sources,"36支丙烯笔竖放，150mm长15mm直径，便携纸盒打开全部笔身一次可见，展开不要遮挡");
            Check(fullVisible.Count(s=>s.Group=="案例分析")==2&&fullVisible.Count(s=>s.Group=="网络案例")==2,"Local analyzed cases and official web cases both reach the generation context");
            Check(fullVisible.Any(s=>s.Id=="local-analysis/acrylic36-caps-counterexample"),"Actual 36-pen cap-only case is retrieved with its limitation");
            Check(cache.Context(fullVisible).Contains("OpenLayout=side-by-side")&&cache.Context(fullVisible).Contains("一体连接"),"Stored corrective method is loaded alongside cases, including mechanism limits");
            Check(magnet.Select(s=>s.Id).SequenceEqual(cache.Select(sources,"12支酒精马克笔，磁吸翻盖，磁铁固定，白卡纸内托，保留Ohuhu滑板变色龙").Select(s=>s.Id)),"retrieval is deterministic for the same requirements");
            foreach(var selected in new[]{magnet,drawer,cache.Select(sources,""),cache.Select(sources,"完全陌生的要求")}){
                Check(selected.Count<sources.Count&&selected.Select(s=>s.Id).Distinct().Count()==selected.Count,"retrieval returns unique source records without resending the full library");
                Check(sources.Where(s=>s.Group=="历史问题"||s.Group=="测试与项目").All(s=>selected.Any(r=>r.Id==s.Id)),"historical failures and compliance remain mandatory for every query");
                Check(selected.Any(s=>s.Group=="材料与工艺")&&selected.Any(ReferenceSearch.IsCase),"unusual requests retain material and structure grounding");
                string context=cache.Context(selected);
                Check(selected.All(s=>context.Contains(s.Id)),"each selected source's cached analysis is actually included");
                Check(context.Contains("12×15+4=184")&&context.Contains("否定")&&context.Contains("30–50mm"),"cached guidance preserves dimensions, negative requirements and outer-box allowances");
                Check(analysis.Images.All(p=>context.Contains(p.Resource)),"all saved sketch and brand observations remain available");
                int fullBytes=Encoding.UTF8.GetByteCount(SourceText(sources));
                int selectedBytes=Encoding.UTF8.GetByteCount(SourceText(selected)+context);
                Check(selectedBytes<fullBytes*0.8,"source-linked summaries plus selected originals must reduce total text payload");
                Console.WriteLine("Knowledge context: "+selected.Count+"/"+sources.Count+" records, "+selectedBytes+"/"+fullBytes+" UTF-8 bytes including cached guidance.");
            }
            Console.WriteLine("PASS: complete original files, source-linked cached analysis, hash invalidation, grounded query retrieval and reduced context.");
        }

        static void RecoveryChecks(string json,byte[] knowledge,byte[] web,List<SourceRecord> originals)
        {
            const string stale="STALE_ANALYSIS_MUST_NOT_REACH_API";
            foreach(string fault in new[]{"hash","missing","fact"}){
                var broken=Json.Read<KnowledgeAnalysis>(json);
                foreach(var item in broken.Entries)item.Summary=stale;
                foreach(var item in broken.Rules)item.Text=stale;
                foreach(var item in broken.Images)item.Summary=stale;
                if(fault=="hash")broken.CorpusHash="old-corpus";
                if(fault=="missing")broken.Entries.RemoveAt(0);
                if(fault=="fact")broken.Entries[0].Facts[0].Quote=stale;
                var warnings=new List<string>();var cache=KnowledgeCache.LoadOrRefresh(Json.Write(broken),knowledge,web,originals,warnings.Add);
                string context=cache.Context(cache.Select(originals,"36支丙烯笔竖放"));
                Check(warnings.Count==1&&cache.Warnings.Count==1,"fallback reports the precise cache failure once: "+fault);
                Check(!context.Contains(stale)&&!context.Contains("OpenLayout=side-by-side"),"fallback never retains stale summaries, rules, image descriptions or invented facts: "+fault);
                Check(context.Contains("原文重建")&&context.Contains("可引用原文片段"),"fallback supplies traceable original-only context: "+fault);
            }
            var sources=Json.Copy(originals);var changing=KnowledgeCache.Load(json,knowledge,web,sources);
            var selected=sources.First(s=>s.Group=="市场参考");string id=selected.Id;
            selected.Text="新原文：竖放笔架，所有笔身可见。";
            string changedContext=changing.Context(new List<SourceRecord>{selected});
            Check(changedContext.Contains(selected.Text)&&!changedContext.Contains("摘要Summary（不可用作Quote）"),"Context detects in-place original text edits and drops cached analysis");
            var added=new SourceRecord{Id="test/current/new",Title="新案例",Group="市场参考",Source="本次测试原文",Text="新案例原文：可抽取竖放模块。"};
            sources.Add(added);
            var retrieved=changing.Select(sources,"可抽取竖放模块");
            Check(retrieved.Any(s=>s.Id==added.Id)&&changing.Context(new List<SourceRecord>{added}).Contains(added.Text),"added source is indexed and quoted without KeyNotFound");
            sources.Remove(selected);
            changing.Select(sources,"竖放笔架");
            Check(!changing.Context(new List<SourceRecord>{selected}).Contains(selected.Text),"deleted source in a stale selection is not revived");
            var replacement=Json.Copy(added);replacement.Text="替换后的原文：完整长度与单排宽度。";
            Check(changing.Context(new List<SourceRecord>{replacement}).Contains(replacement.Text),"changed record supplied as a new selected object refreshes its original text");
            changing.Refresh(sources);
            Check(changing.Context(new List<SourceRecord>{added}).Contains(added.Text),"explicit refresh follows the supplied authoritative original records");
            var badSources=Json.Copy(sources);badSources.Add(Json.Copy(sources[0]));
            Reject(()=>KnowledgeCache.LoadOrRefresh("{}",knowledge,web,badSources),"raw duplicate IDs cannot be concealed by cache recovery");
            var valid=KnowledgeCache.Load(json,knowledge,web,originals);var one=originals.First(s=>s.Group=="市场参考");
            string validContext=valid.Context(new List<SourceRecord>{one});
            Check(valid.Warnings.Count==0&&validContext.Contains("Summary与适用边界仅供理解，不能作为Quote")&&validContext.Contains("Quote："),"valid cached context distinguishes non-quotable summaries from verbatim facts without needless refresh");
            foreach(string line in validContext.Split('\n').Where(s=>s.StartsWith("Quote：")))Check(one.Text.Contains(line.Substring(6)),"exposed short facts remain contiguous original quotes");
            var identities=Json.Copy(originals);var identityCache=KnowledgeCache.Load(json,knowledge,web,identities);
            identities[0].StableId="11111111-2222-4333-8444-555555555555";
            string identityContext=identityCache.Context(new List<SourceRecord>{identities[0]});
            Check(identityCache.Warnings.Count==1&&identityContext.Contains("引用ID："+identities[0].StableId)&&identityContext.Contains("分析来源："+identities[0].Id),"stable identity changes refresh context while retaining the legacy source locator");
        }

        static void VerifyOriginals(KnowledgeAnalysis analysis)
        {
            var cacheData=ReadObject("knowledge-analysis.json");
            var manifest=ReadObject("source-manifest.json");
            var files=((object[])cacheData["SourceFiles"]).Cast<Dictionary<string,object>>().ToList();
            Check(files.Count==5,"five original documents retained");
            using(var archive=ZipFile.OpenRead(Resources+"original-sources.zip")){
                foreach(var file in files){
                    string name=(string)file["File"];
                    var entry=archive.GetEntry(name);Check(entry!=null,"original archived file exists: "+name);
                    using(var input=entry.Open())using(var output=new MemoryStream()){
                        input.CopyTo(output);byte[] bytes=output.ToArray();
                        Check(Hash(bytes)==(string)file["Sha256"]&&bytes.Length==Convert.ToInt32(file["Bytes"]),"original document bytes match analysis fingerprint: "+name);
                    }
                    var source=((object[])manifest["sources"]).Cast<Dictionary<string,object>>().Single(s=>(string)s["file"]==name);
                    Check((string)source["sha256"]==(string)file["Sha256"],"cache fingerprint agrees with original import manifest");
                }
            }
            Check(analysis.Images.Count(p=>Regex.IsMatch(p.SourceFile??"",@"^[a-f0-9]{64}\.png$"))==11,"all five sketch and six Ohuhu originals have saved observations");
            foreach(var picture in analysis.Images)
                Check(Hash(File.ReadAllBytes(Resources+picture.Resource))==picture.Sha256,"stored reference pixels remain unchanged: "+picture.Resource);
            foreach(var pair in (Dictionary<string,object>)cacheData["InputHashes"])
                Check(Hash(File.ReadAllBytes(Resources+pair.Key))==(string)pair.Value,"analysis input fingerprint is current: "+pair.Key);
        }
    }
}
