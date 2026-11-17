// Compiled only with build.ps1 -Qa. Never included in the delivered application.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaperStudio;

class DesktopQaHandler : HttpMessageHandler
{
    public static int Calls;
    public static bool FailDraftOnce;
    public static int FailImageAt;
    public static int ImageCalls {get{return imageCalls;}}
    public static int AuditCalls;
    public static int UnderstandingCalls,ReviewCalls;
    static DesignUnderstanding currentUnderstanding;
    static List<Concept> currentConcepts;
    public static int SketchCalls;
    public static string ExpectedGuide;
    static int imageCalls;
    static string lastApprovedSketch;
    public sealed class PromptEvidence { public string EvidenceId,Quote; }
    public sealed class PromptSource { public string SourceId,StableId; public List<PromptEvidence> Evidence; }
    static List<PromptSource> ReadCitationCatalog(string prompt)
    {
        const string begin="【本次引用目录JSON开始】",end="【本次引用目录JSON结束】";
        int start=prompt.IndexOf(begin,StringComparison.Ordinal);
        if(start<0)throw new Exception("Actual request citation catalogue missing");
        start+=begin.Length;int finish=prompt.IndexOf(end,start,StringComparison.Ordinal);
        if(finish<0)throw new Exception("Actual request citation catalogue incomplete");
        var catalog=Json.Read<List<PromptSource>>(prompt.Substring(start,finish-start).Trim());
        if(catalog==null||catalog.Count==0||catalog.Any(s=>s.Evidence==null||s.Evidence.Count==0))throw new Exception("Actual request citation catalogue has no evidence");
        return catalog;
    }
    static PromptSource CatalogSource(List<PromptSource> catalog,List<SourceRecord> records,string sourceId)
    {
        var record=records.Single(r=>r.Id==sourceId);
        return catalog.Single(s=>s.StableId==record.StableId);
    }
    static string Resource(string name)
    {using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var output=new MemoryStream()){stream.CopyTo(output);return Convert.ToBase64String(output.ToArray());}}
    static HttpResponseMessage ChatResponse(string text)
    {
        string stream="data: "+Json.Write(new{choices=new[]{new{index=0,delta=new{content=text},finish_reason="stop"}}})+"\n\n";
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(stream,System.Text.Encoding.UTF8,"text/event-stream")};
    }
    public static Concept ConceptFixture(int i)
    {
        var concept=new Concept{Name="QA 测试方案 "+i,Idea="接口流程测试",Structure="折叠纸盒方向 "+i,Opening="翻开方式 "+i,Layout="4组12支，展开每组单排全部可见",Materials="白卡纸",Innovation="测试字段",Difficulty="待评估",CostNote="待核价",Risks="待验证",Checks="打样后检查",ImagePrompt="QA packaging fixture",PackageDimensions=new PackageDimensions{Longest=300,Middle=200,Shortest=100},PenLayout=new PenLayout{BoxWidth=300,BoxDepth=100,BoxHeight=200,Orientation="vertical",LengthAxis="Height"},Assembly=new AssemblyPlan{Assumptions="总余量4mm、壁厚1mm为设计假设",LengthClearance=22,Units=Enumerable.Range(0,4).Select(j=>new DisplayUnit{Id="U"+(j+1),Count=12,Rows=1,Columns=12,Motion=i==0?"取下盖板后取出托盘":i==1?"抽离纸套后取出托盘":"展开背部撑片形成桌面展示",Closed=new Placement{Y=j*20},Open=new Placement{X=j*166.4m}}).ToList(),Parts=new List<AssemblyPart>{new AssemblyPart{Id="盖板",Role=i==0?"可取盖板":i==1?"抽离纸套":"折叠桌面撑片",Width=160.4m,Depth=80.7m,Height=1}}}};
        concept.ConceptType=i==0?DesignStrategy.Mature:i==1?DesignStrategy.Functional:DesignStrategy.Exploratory;
        concept.StructuralFamily=i==0?"可取盖模块":i==1?"抽离纸套": "联动折叠支撑";
        concept.OpeningMechanism=i==0?"掀盖取托盘":i==1?"抽套取托盘":"背部撑片展开";
        concept.InternalLayout=i==0?"四组横向并列":i==1?"四组分层抽离":"四组联动展开";
        concept.DesktopUse=i==0?"分盒平放":i==1?"托盘平放":"背部支撑陈列";
        concept.Portability=i==0?"箱体提携":i==1?"纸套收纳":"折叠后携带";
        concept.CreativityLevel=i==0?"适度":i==1?"中等":"高";
        concept.FeasibilityStatus=i==2?"创意探索，需进一步工程验证":"概念可打样，工程仍需验证";
        concept.ReferenceSources=new List<string>();concept.SketchReferenceId="";
        return concept;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Calls++;string body=await request.Content.ReadAsStringAsync();string result;
        if(request.RequestUri.AbsolutePath.EndsWith("chat/completions"))
        {
            dynamic data=Json.Serializer().DeserializeObject(body);
            if(((string)data["messages"][0]["content"]).Contains("检查生成包装图")){
                AuditCalls++;
                string auditText=(string)data["messages"][1]["content"][0]["text"];
                const string marker="\n方案：";
                int start=auditText.LastIndexOf(marker,StringComparison.Ordinal);
                if(start<0)throw new Exception("Calculated audit concept missing");
                var audited=Json.Read<Concept>(auditText.Substring(start+marker.Length));
                return ChatResponse(Json.Write(WorkflowFixtures.ImageReview(audited)));
            }
            var parts=(object[])data["messages"][1]["content"];
            string prompt=String.Join("\n",parts.Cast<Dictionary<string,object>>().Where(p=>(string)p["type"]=="text").Select(p=>(string)p["text"]));
            List<SourceRecord> records;
            using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("knowledge.json")))records=Json.Read<List<SourceRecord>>(reader.ReadToEnd());
            using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("web-knowledge.json")))records.AddRange(Json.Read<List<SourceRecord>>(reader.ReadToEnd()));
            string stage=(string)data["messages"][0]["content"];
            if(stage.Contains("阶段：方案复核")){
                ReviewCalls++;return ChatResponse(Json.Write(WorkflowFixtures.Review(currentConcepts,currentUnderstanding)));
            }
            if(stage.Contains("阶段：资料理解")){
                UnderstandingCalls++;
                var clauseMatches=System.Text.RegularExpressions.Regex.Matches(prompt,@"(?m)^R\d+: (.*)$");
                var selected=records.Where(r=>prompt.Contains("记录ID："+r.Id+"；")).ToList();
                currentUnderstanding=WorkflowFixtures.Understanding(clauseMatches.Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Groups[1].Value.TrimEnd('\r')).ToList(),selected);
                if(selected.Count>=records.Count/2||selected.Any(r=>!prompt.Contains(r.Text))||!selected.Any(r=>r.Group=="材料与工艺")||!selected.Any(r=>r.Group=="历史问题")||!selected.Any(r=>r.Title.Contains("合规"))||!selected.Any(ReferenceSearch.IsCase))throw new Exception("Understanding source selection omitted required evidence");
                if(body.Contains(TextPicture.DataUrl(Resource("ohuhu-style-board.png"))))throw new Exception("Internal pictures repeated in interpretation phase");
                var incomplete=Json.Copy(currentUnderstanding);incomplete.Requirements.RemoveAt(0);
                var catalog=ReadCitationCatalog(prompt);
                incomplete.Evidence=incomplete.Evidence.Select(e=>new SourceEvidence{EvidenceId=CatalogSource(catalog,selected,e.SourceId).Evidence.First().EvidenceId,Application=e.Application}).ToList();
                return ChatResponse(Json.Write(incomplete));
            }
            if(FailDraftOnce){FailDraftOnce=false;return new HttpResponseMessage(HttpStatusCode.GatewayTimeout){Content=new StringContent("QA draft timeout")};}
            var supplied=records.Where(r=>prompt.Contains("记录ID："+r.Id+"；")).ToList();
            if(supplied.Count>=records.Count/2||supplied.Count<20||supplied.Any(r=>!prompt.Contains(r.Text)))throw new Exception("Retrieval must include complete selected source rows without repeating the full corpus");
            if(!prompt.Contains("本地预分析知识库")||!prompt.Contains("已存图片分析")||!prompt.Contains("sketch-01.png")||!prompt.Contains("ohuhu-01.png"))throw new Exception("Stored source and picture analysis missing from design request");
            if(!prompt.Contains("材料与工艺白名单"))throw new Exception("Material whitelist missing from text request");
            if(body.Contains(TextPicture.DataUrl(Resource("ohuhu-style-board.png")))||body.Contains(TextPicture.DataUrl(Resource("sketch-style-board.png"))))throw new Exception("Fixed internal images should use stored analysis during planning");
            if(!body.Contains("407.2")||!body.Contains("150.5")||!body.Contains("12.7")||!body.Contains("48支"))throw new Exception("Required marker data, diameter and reduced bounds missing from text request");
            int count=prompt.Contains("本次修改：")?1:3;
            var response=new DesignResponse{Brief="QA接口响应：只用于验证窗口流程",Missing="QA固定响应，非模型生成",Concepts=new List<Concept>()};
            string system=(string)data["messages"][0]["content"];
            int clauses=System.Text.RegularExpressions.Regex.Matches(system,@"(?m)^R\d+:").Count;
            if(clauses<1||!system.Contains("12×15+4=184"))throw new Exception("Calculated protocol or natural-language clauses absent");
            for(int i=0;i<count;i++){var concept=ConceptFixture(i);WorkflowFixtures.Shell(concept);concept.RequirementChecks=Enumerable.Range(1,clauses).Select(j=>new RequirementCheck{RequirementId="R"+j,Implementation="QA clause "+j}).ToList();concept.Manufacturing=WorkflowFixtures.Manufacturing(supplied,concept.Assembly.WallThickness);response.Concepts.Add(concept);}

            currentConcepts=Json.Copy(response.Concepts);
            var designCatalog=ReadCitationCatalog(prompt);
            foreach(var concept in response.Concepts){
                foreach(var material in concept.Manufacturing.Materials){
                    material.SourceId=CatalogSource(designCatalog,supplied,material.SourceId).SourceId;
                    material.ProcessSourceIds=material.ProcessSourceIds.Select(id=>CatalogSource(designCatalog,supplied,id).SourceId).ToList();
                }
                foreach(var risk in concept.Manufacturing.Risks.Concat(concept.Manufacturing.Tests))risk.SourceId=CatalogSource(designCatalog,supplied,risk.SourceId).SourceId;
            }
            return ChatResponse(Json.Write(response));
        }
        else
        {
            var form=request.Content as MultipartFormDataContent;
            if(form==null)throw new Exception("Expected references through image edits");
            if(!body.Contains("407.2")||!body.Contains("150.5")||!body.Contains("12.7")||!body.Contains("48支"))throw new Exception("Marker parameters, diameter and reduced bounds missing from image request");
            if(!body.Contains("材料与工艺白名单"))throw new Exception("Material whitelist missing from image request");
            var images=new List<string>();
            foreach(var part in form)if(part.Headers.ContentType!=null&&part.Headers.ContentType.MediaType=="image/png")images.Add(Convert.ToBase64String(await part.ReadAsByteArrayAsync()));
            bool sketch=body.Contains("View: 设计草图");
            if(!images.Contains(Resource("ohuhu-logo.png"))||!images.Contains(Resource("ohuhu-skateboard-mascot.png"))||images.Count>16)throw new Exception("Brand artwork absent or excessive image references");
            if(sketch&&(!images.Contains(Resource("ohuhu-style-board.png"))||!body.Contains("计算结构总览")||!images.Contains(Resource("sketch-style-board.png"))||!body.Contains("逐部件尺寸引线")))throw new Exception("Sketch structure and style references absent");
            if(!sketch&&(images.Contains(Resource("ohuhu-style-board.png"))||images.Contains(ExpectedGuide)||body.Contains("整体效果表达参考")))throw new Exception("Effect request contains competing layout or presentation references");
            if(body.Contains("View: 设计草图")){SketchCalls++;if(!body.Contains("background")||!body.Contains("transparent")||!body.Contains("用户原始透明草图")||!body.Contains("industrial packaging design sketch")||!body.Contains("transformation arrows")||!body.Contains("每一个托盘"))throw new Exception("Sketch style or per-unit dimension instructions absent");}
            else if(body.Contains("View: 整体效果")&&lastApprovedSketch!=null&&(!images.Any()||!body.Contains("草图：整体效果的首要结构依据")))throw new Exception("Overall image did not receive the accepted sketch as its structural reference");
            // Real reference pixels exercise image sizing; these QA fixtures are not generated designs.
            imageCalls++;
            if(imageCalls==FailImageAt)return new HttpResponseMessage(HttpStatusCode.GatewayTimeout){Content=new StringContent("QA image timeout")};
            string generated=Resource("sketch-0"+((imageCalls-1)%3+1)+".png");
            if(body.Contains("View: 设计草图"))lastApprovedSketch=generated;
            result=Json.Write(new {data=new[]{new {b64_json=generated}}});
        }
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(result)};
    }
}
