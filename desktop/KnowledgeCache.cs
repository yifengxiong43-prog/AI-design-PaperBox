using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace PaperStudio
{
    public class KnowledgeFact { public string Label,Quote; }
    public class KnowledgeEntry
    {
        public string SourceId,Title,Kind,Summary;
        public List<string> Tags,Cautions,ImageResources;
        public List<KnowledgeFact> Facts;
    }
    public class KnowledgeRule { public string Id,Text,Provenance; public List<string> SourceIds,Tags; }
    public class KnowledgeImage { public string Id,Resource,SourceFile,Sha256,Summary,Provenance; public List<string> Tags,Cautions; }
    public class KnowledgeAnalysis
    {
        public string Version,CorpusHash,Method;
        public List<KnowledgeEntry> Entries;
        public List<KnowledgeRule> Rules;
        public List<KnowledgeImage> Images;
    }
    // Source-linked reference analysis; stale analysis falls back to current raw text only.
    public class KnowledgeCache
    {
        KnowledgeAnalysis analysis;
        Dictionary<string,KnowledgeEntry> entries;
        List<SourceRecord> currentSources;
        Dictionary<string,string> sourceStamps;
        readonly HashSet<string> removedSourceIds=new HashSet<string>();
        Action<string> warning;
        public readonly List<string> Warnings=new List<string>();
        readonly Dictionary<string,HashSet<string>> index=new Dictionary<string,HashSet<string>>();
        readonly Dictionary<string,int> frequency=new Dictionary<string,int>();
        KnowledgeCache(KnowledgeAnalysis value,List<SourceRecord> sources)
        {
            Initialize(value,sources);
        }
        static string Stamp(SourceRecord source)
        {return Json.Write(new{source.Id,source.StableId,source.Title,source.Group,source.Source,source.Text,source.Images});}
        static void ValidateSources(List<SourceRecord> sources)
        {
            if(sources==null||sources.Any(s=>s==null||String.IsNullOrWhiteSpace(s.Id)||s.Text==null)||sources.Select(s=>s.Id).Distinct().Count()!=sources.Count)
                throw new InvalidOperationException("当前资料原文缺失或记录ID重复，不能建立可追溯检索。");
        }
        void Initialize(KnowledgeAnalysis value,List<SourceRecord> sources)
        {
            var ids=new HashSet<string>(sources.Select(s=>s.Id));
            if(sourceStamps!=null)foreach(var id in sourceStamps.Keys.Where(id=>!ids.Contains(id)))removedSourceIds.Add(id);
            removedSourceIds.ExceptWith(ids);
            analysis=value;entries=value.Entries.ToDictionary(e=>e.SourceId);
            currentSources=sources;sourceStamps=sources.ToDictionary(s=>s.Id,Stamp);
            index.Clear();frequency.Clear();
            foreach(var source in sources){
                var terms=Terms(source.Title+" "+source.Text+" "+String.Join(" ",entries[source.Id].Tags));index[source.Id]=terms;
                foreach(var term in terms){int n;frequency.TryGetValue(term,out n);frequency[term]=n+1;}
            }
        }
        public static KnowledgeCache Load(string analysisJson,byte[] knowledgeBytes,byte[] webBytes,List<SourceRecord> sources)
        {
            ValidateSources(sources);
            var value=Json.Read<KnowledgeAnalysis>(analysisJson);string hash;
            using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(knowledgeBytes.Concat(webBytes).ToArray())).Replace("-","").ToLowerInvariant();
            if(value==null||value.Version!="1.0"||value.CorpusHash!=hash)throw new InvalidOperationException("内置资料分析与原文版本不一致，请重新构建资料库。");
            var ids=new HashSet<string>(sources.Select(s=>s.Id));
            if(value.Entries==null||value.Entries.Count!=ids.Count||value.Entries.Any(e=>e==null||!ids.Contains(e.SourceId)||e.Tags==null||e.Cautions==null||e.Facts==null)||value.Entries.Select(e=>e.SourceId).Distinct().Count()!=ids.Count||value.Rules==null||value.Images==null)throw new InvalidOperationException("内置资料分析不完整，请重新构建资料库。");
            var originals=sources.ToDictionary(s=>s.Id);
            if(value.Entries.Any(e=>e.Facts.Any(f=>f==null||String.IsNullOrWhiteSpace(f.Quote)||!originals[e.SourceId].Text.Contains(f.Quote)))||value.Rules.Any(r=>r==null||r.SourceIds==null||r.SourceIds.Any(id=>!ids.Contains(id)))||value.Images.Any(i=>i==null||i.Cautions==null))throw new InvalidOperationException("内置资料分析的来源校验失败。");
            return new KnowledgeCache(value,sources);
        }
        public static KnowledgeCache LoadOrRefresh(string analysisJson,byte[] knowledgeBytes,byte[] webBytes,List<SourceRecord> sources,Action<string> warning=null)
        {
            ValidateSources(sources);
            try{var loaded=Load(analysisJson,knowledgeBytes,webBytes,sources);loaded.warning=warning;return loaded;}
            catch(InvalidOperationException error){return FromCurrentSources(sources,warning,error.Message);}
            catch(ArgumentException error){return FromCurrentSources(sources,warning,error.Message);}
        }
        static KnowledgeCache FromCurrentSources(List<SourceRecord> sources,Action<string> warning,string reason)
        {
            var cache=new KnowledgeCache(RawAnalysis(sources),sources);cache.warning=warning;
            cache.Warn("资料分析未复用："+reason+" 已按当前原文重建临时检索，未沿用旧摘要、规则或图片分析。");return cache;
        }
        void Warn(string message){Warnings.Add(message);if(warning!=null)warning(message);}
        static List<KnowledgeFact> RawFacts(string text)
        {
            return text.Split('\n').Select(s=>s.Trim()).Where(s=>s.Length>0).Take(4)
                .Select(s=>new KnowledgeFact{Label="原文片段",Quote=s.Substring(0,Math.Min(s.Length,180))}).ToList();
        }
        static KnowledgeAnalysis RawAnalysis(List<SourceRecord> sources)
        {
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n",sources.OrderBy(s=>s.Id,StringComparer.Ordinal).Select(Stamp))))).Replace("-","").ToLowerInvariant();
            return new KnowledgeAnalysis{Version="原文重建",CorpusHash=hash,Method="当前原文检索；无缓存推导",Rules=new List<KnowledgeRule>(),Images=new List<KnowledgeImage>(),
                Entries=sources.Select(s=>new KnowledgeEntry{SourceId=s.Id,Title=s.Title,Kind=s.Group,Summary="",Tags=new List<string>(),Cautions=new List<string>(),ImageResources=new List<string>(),Facts=RawFacts(s.Text)}).ToList()};
        }
        public void Refresh(List<SourceRecord> sources)
        {
            ValidateSources(sources);Initialize(RawAnalysis(sources),sources);
            Warn("已按当前资料原文重新建立检索索引；旧摘要、规则和图片分析不再使用。");
        }
        void EnsureCurrent(List<SourceRecord> sources)
        {
            ValidateSources(sources);
            if(sources.Count!=sourceStamps.Count||sources.Any(s=>!sourceStamps.ContainsKey(s.Id)||sourceStamps[s.Id]!=Stamp(s)))Refresh(sources);
            else currentSources=sources;
        }
        static HashSet<string> Terms(string text)
        {
            var terms=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(Match match in Regex.Matches(text??"",@"[a-zA-Z][a-zA-Z0-9-]+|[\u4e00-\u9fff]+")){
                string word=match.Value.ToLowerInvariant();if(word[0]<128)terms.Add(word);
                else for(int i=0;i<word.Length-1;i++)terms.Add(word.Substring(i,2));
            }
            return terms;
        }
        static string QueryQuantity(string query)
        {
            // Current form values are authoritative. A mixed narrative without them is ambiguous.
            var current=Regex.Matches(query,@"笔支数\s*[：:]\s*(\d+)\s*支");
            if(current.Count>0)return current[current.Count-1].Groups[1].Value;
            var counts=Regex.Matches(query,@"(?<!\d)(\d+)\s*支").Cast<Match>().Select(m=>m.Groups[1].Value).Distinct().ToList();
            return counts.Count==1?counts[0]:null;
        }
        static bool NamedMaterial(SourceRecord source,string query)
        {
            return (source.Text??"").Split('\n').Any(line=>{
                if(!new[]{"材料/类型：","材料/配件：","保护材料：","工艺："}.Any(line.StartsWith))return false;
                return line.Substring(line.IndexOf('：')+1).Split(new[]{'/','、','，'},StringSplitOptions.RemoveEmptyEntries).Any(value=>{
                    string name=value.Trim();
                    if(name.Length>=2&&query.IndexOf(name,StringComparison.OrdinalIgnoreCase)>=0)return true;
                    // Names such as EVA泡棉 are often requested by their material abbreviation.
                    return Regex.Matches(name,@"[A-Za-z]{2,}").Cast<Match>().Any(m=>Regex.IsMatch(query,@"(?<![A-Za-z0-9])"+Regex.Escape(m.Value)+@"(?![A-Za-z0-9])",RegexOptions.IgnoreCase));
                });
            });
        }
        public List<SourceRecord> Select(List<SourceRecord> sources,string query)
        {
            EnsureCurrent(sources);
            var terms=Terms(query);query=query??"";string quantity=QueryQuantity(query);
            // Negations remain in the full request. Retrieval does not interpret them as permission.
            var ranked=sources.Select(s=>new {Source=s,Score=index[s.Id].Where(terms.Contains).Sum(t=>1.0/Math.Sqrt(frequency[t]))
                +entries[s.Id].Tags.Where(t=>t.Length>=2&&query.IndexOf(t,StringComparison.OrdinalIgnoreCase)>=0).Sum(t=>3.0)
                +(quantity!=null&&s.Group=="产品案例"&&Regex.IsMatch(s.Text,@"产品的数量：\s*"+quantity+@"\s*支(?!\d)")?4.0:0)})
                .OrderByDescending(x=>x.Score).ThenBy(x=>x.Source.Id,StringComparer.Ordinal).Select(x=>x.Source).ToList();
            var selected=new List<SourceRecord>();
            selected.AddRange(sources.Where(s=>s.Group=="历史问题"||s.Group=="测试与项目"));
            // Supply a viable core material/process set, even for an unusual or empty query.
            string[] core={"/材料汇总/5","/材料汇总/6","/工艺适配/11","/工艺适配/17","/工艺适配/19","/保护材料/14"};
            selected.AddRange(sources.Where(s=>core.Any(s.Id.EndsWith)));
            selected.AddRange(ranked.Where(s=>s.Group=="材料与工艺").Take(24));
            // Explicit names must not disappear behind a top-k limit, including forbidden names.
            selected.AddRange(sources.Where(s=>s.Group=="材料与工艺"&&NamedMaterial(s,query)));
            selected.AddRange(ranked.Where(s=>s.Group=="产品案例").Take(2));
            selected.AddRange(ranked.Where(s=>s.Group=="案例分析").Take(2));
            selected.AddRange(ranked.Where(s=>s.Group=="网络案例").Take(2));
            selected.AddRange(ranked.Where(s=>ReferenceSearch.IsCase(s)&&s.Group!="产品案例"&&s.Group!="案例分析"&&s.Group!="网络案例").Take(4));
            selected.AddRange(ranked.Where(s=>s.Group!="材料与工艺"&&s.Group!="历史问题"&&s.Group!="测试与项目"&&!ReferenceSearch.IsCase(s)).Take(3));
            return selected.GroupBy(s=>s.Id).Select(g=>g.First()).ToList();
        }
        public string Context(List<SourceRecord> selected)
        {
            ValidateSources(selected);EnsureCurrent(currentSources);
            if(selected.Any(s=>removedSourceIds.Contains(s.Id))){selected=selected.Where(s=>!removedSourceIds.Contains(s.Id)).ToList();Warn("本轮引用中已删除的资料已排除，未重新加入旧记录。");}
            // Context normally receives a subset. Changed/new selected records invalidate derived analysis,
            // but a smaller unchanged selection is not a deletion from the full corpus.
            if(selected.Any(s=>!sourceStamps.ContainsKey(s.Id)||sourceStamps[s.Id]!=Stamp(s))){
                var replacement=selected.ToDictionary(s=>s.Id);var merged=currentSources.Where(s=>!replacement.ContainsKey(s.Id)).Concat(selected).ToList();Refresh(merged);
            }
            var text=new StringBuilder("本地预分析知识库 "+analysis.Version+"（原文指纹 "+analysis.CorpusHash.Substring(0,12)+"）。摘要Summary与适用边界仅供理解，不能作为Quote；引用请从本轮引用目录选择EvidenceId，程序填入对应原文，不要抄写或编写Quote。不是当前产品实测或上次方案；当前需求仍须独立理解。检索命中不代表允许使用：禁止项的资料也会保留供核验；案例支数相近只用于结构比较，不能覆盖当前笔长、笔径、支数和保留/修改要求。\n");
            foreach(var source in selected){var e=entries[source.Id];text.Append("分析来源：").Append(e.SourceId).Append("\n引用ID：").Append(source.StableId);
                if(!String.IsNullOrEmpty(e.Summary))text.Append("\n摘要Summary（不可用作Quote）：").Append(e.Summary);
                if(e.Cautions.Count>0)text.Append("\n适用边界：").Append(String.Join("；",e.Cautions));
                text.Append("\n可引用原文片段（用于阅读理解；生成引用请选择本轮目录短编号，以上持久ID仅供追溯）：\n");
                foreach(var fact in e.Facts.Take(3))text.Append("Quote：").Append(fact.Quote.Substring(0,Math.Min(fact.Quote.Length,180))).Append('\n');
            }
            text.Append("已存设计应用规则（提炼建议，不能替代原文证据）：\n");
            foreach(var rule in analysis.Rules)text.Append(rule.Text).Append(" [").Append(rule.Provenance).Append("]\n");
            text.Append("已存图片分析（图像实物在出图阶段传入；这些描述不证明尺寸或机构已验证）：\n");
            foreach(var picture in analysis.Images)text.Append(picture.Resource).Append("：").Append(picture.Summary).Append("；").Append(String.Join("；",picture.Cautions)).Append('\n');
            return text.ToString();
        }
    }
}
