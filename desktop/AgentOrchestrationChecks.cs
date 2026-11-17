using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PaperStudio
{
    internal static class AgentOrchestrationChecks
    {
        static void Check(bool value,string message){if(!value)throw new Exception("Agent orchestration: "+message);}
        public static void Run()
        {
            PythonGeometryTool.RootOverride=Path.GetFullPath("desktop/python-tools");
            Check(AgentToolCatalog.Prompt().Contains("retrieve_references")&&AgentToolCatalog.Prompt().Contains("calculate_geometry")&&AgentToolCatalog.Prompt().Contains("inspect_image"),"tool catalog exposes the bounded packaging tools");
            try{if(File.Exists(AgentDiagnostics.LogPath))File.Delete(AgentDiagnostics.LogPath);}catch(IOException){}
            var events=new System.Collections.Generic.List<string>();
            var run=new AgentRun("agent-check"){Changed=call=>events.Add(call.Tool+":"+call.Status)};
            Check(run.Invoke("retrieve_references","planning",0,"query-hash=abc; source-count=2",()=>"stable-1,stable-2")=="stable-1,stable-2","successful tool returns its typed result");
            bool failed=false;try{run.Invoke<object>("calculate_geometry","draft",1,"concept=C1",()=>{throw new InvalidOperationException("bad geometry");});}catch(InvalidOperationException){failed=true;}
            Check(failed,"tool errors propagate to the workflow");
            bool cancelled=false;try{run.InvokeAsync("inspect_image","image-review",1,"view=整体效果",()=>Task.FromCanceled(new CancellationToken(true))).GetAwaiter().GetResult();}catch(AggregateException error){cancelled=error.InnerExceptions.Any(e=>e is TaskCanceledException||e is OperationCanceledException);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cancellation propagates without retrying internally");
            Check(run.Calls.Count==3&&run.Calls.Any(c=>c.Status=="succeeded")&&run.Calls.Any(c=>c.Status=="failed")&&run.Calls.Any(c=>c.Status=="cancelled"),"success, failure and cancellation are traced");
            Check(events.SequenceEqual(new[]{"retrieve_references:running","retrieve_references:succeeded","calculate_geometry:running","calculate_geometry:failed","inspect_image:running","inspect_image:cancelled"}),"desktop receives actual start and terminal tool events");
            string credentialPath=Path.Combine(Path.GetTempPath(),"PaperStudio-QA","credentials-check.dat");
            ApiSecretStore.PathOverride=credentialPath;
            try{
                var stored=new ApiSettings{TextKey="TEST_TEXT_SECRET",ImageKey="TEST_IMAGE_SECRET"};ApiSecretStore.Save(stored);
                Check(!File.ReadAllText(credentialPath).Contains("TEST_TEXT_SECRET")&&!File.ReadAllText(credentialPath).Contains("TEST_IMAGE_SECRET"),"saved credentials are encrypted");
                var loaded=new ApiSettings();ApiSecretStore.Load(loaded);
                Check(loaded.TextKey==stored.TextKey&&loaded.ImageKey==stored.ImageKey,"current Windows user can reload saved credentials");
            }finally{ApiSecretStore.PathOverride=null;if(File.Exists(credentialPath))File.Delete(credentialPath);}
            var sample36=new PackagingInput{Count="36",Length="150",Diameter="15",Tier="large-standard",AllowanceCm="5",PenType="acrylic"}.Build();
            Check(sample36.EstimateLayout().Orientation=="vertical"&&sample36.MarkerCount==36&&sample36.MarkerLength==150&&sample36.MarkerDiameter==15,"specified 36 acrylic marker case enters deterministic geometry");
            var design36=new Concept{Assembly=new AssemblyPlan{Assumptions="4mm整排余量与1mm纸板为设计假设",LengthClearance=sample36.MinimumLengthClearanceMm,
                Units=Enumerable.Range(0,3).Select(i=>new DisplayUnit{Id="U"+(i+1),Count=12,Rows=1,Columns=12,Motion="打开后并排展示",Closed=new Placement{Y=i*21},Open=new Placement{X=i*190}}).ToList(),
                Parts=new System.Collections.Generic.List<AssemblyPart>{new AssemblyPart{Id="盖板",Role="闭合外盖",Width=186,Depth=63,Height=1}}}};
            AssemblyGeometry.Calculate(design36,sample36);PythonGeometryTool.Verify(design36,sample36);
            Check(design36.Assembly.Units.Sum(u=>u.Count)==36&&design36.Assembly.Units.All(u=>u.Rows==1&&u.Columns==12)&&design36.PenLayout.BoxHeight>150,"36-marker layout preserves all full-length pens in three visible rows");
            var localBytes=File.ReadAllBytes("desktop/resources/knowledge.json");var webBytes=File.ReadAllBytes("desktop/resources/web-knowledge.json");
            var sources=Json.Read<System.Collections.Generic.List<SourceRecord>>(System.Text.Encoding.UTF8.GetString(localBytes));
            sources.AddRange(Json.Read<System.Collections.Generic.List<SourceRecord>>(System.Text.Encoding.UTF8.GetString(webBytes)));
            var cache=KnowledgeCache.LoadOrRefresh(File.ReadAllText("desktop/resources/knowledge-analysis.json"),localBytes,webBytes,sources);
            var selected=PackagingAgentTools.RetrieveReferences(sources,"36支丙烯马克笔，打开后成为桌面展示架\n"+sample36.ToPrompt(),cache);
            Check(selected.Count>0&&selected.Count<sources.Count&&selected.All(s=>!String.IsNullOrWhiteSpace(s.StableId))&&selected.Any(s=>s.Group=="材料与工艺")&&selected.Any(s=>s.Group=="历史问题")&&selected.Any(s=>s.Title.Contains("合规"))&&selected.Any(ReferenceSearch.IsCase),"36-marker task retrieves grounded materials, risks, compliance and case sources");
            var limits=new PackagingInput{Count="12",Length="150",Diameter="15",Tier="large-standard",AllowanceCm="4",PenType="acrylic"}.Build();
            var concept=new Concept{Assembly=new AssemblyPlan{Assumptions="4mm总余量与1mm纸板为设计假设",LengthClearance=24,Units=new System.Collections.Generic.List<DisplayUnit>{new DisplayUnit{Id="U1",Motion="单排竖直展示",Count=12,Rows=1,Columns=12,Closed=new Placement(),Open=new Placement()}},Parts=new System.Collections.Generic.List<AssemblyPart>{new AssemblyPart{Id="盖板",Role="外壳连接",Width=10,Depth=10,Height=1}}}};
            AssemblyGeometry.Calculate(concept,limits);
            PythonGeometryTool.Verify(concept,limits);
            concept.Assembly.Units[0].Width-=1;
            failed=false;try{PythonGeometryTool.Verify(concept,limits);}catch(InvalidOperationException){failed=true;}
            Check(failed,"Python tool rejects a shortened marker row independently of the model response");
            var raisedTray=Json.Copy(concept);
            raisedTray.Assembly.Orientation="horizontal";raisedTray.Assembly.LengthAxis="Width";
            var raisedLimits=Json.Copy(limits);raisedLimits.PenType="pencil";raisedLimits.TrayHeightMm=25m;
            AssemblyGeometry.Calculate(raisedTray,raisedLimits);
            PythonGeometryTool.Verify(raisedTray,raisedLimits);
            Check(raisedTray.Assembly.Units[0].Height==25m,"User-specified tray height is verified independently without reverting to minimum height");
            var paired=Json.Copy(raisedTray);paired.Assembly.CombinedLengthClearance=true;paired.Assembly.LengthClearance=5m;AssemblyGeometry.Calculate(paired,raisedLimits);Check(paired.Assembly.Units[0].Width==157m,"Paired reference structures use one 5mm total length allowance");
            var imageReferences=new System.Collections.Generic.List<Picture>{new Picture{Name="用户草图.png",Data=Convert.ToBase64String(new byte[]{1,2,3})}};
            var imageBrief=new DesignUnderstanding();
            failed=false;try{PackagingAgentTools.ValidateReferenceImages(imageBrief,imageReferences);}catch(DesignValidationException){failed=true;}
            Check(failed,"uploaded sketches cannot be silently ignored by understanding");
            imageBrief.ReferenceImages=new System.Collections.Generic.List<ReferenceImageConstraint>{new ReferenceImageConstraint{ImageName="用户草图.png",BoxType="翻盖盒",Opening="向两侧展开",Layout="单排陈列",VisualStyle="细线草图",Constraints="保持滑板图案"}};
            PackagingAgentTools.ValidateReferenceImages(imageBrief,imageReferences);
            if(File.Exists(AgentDiagnostics.LogPath)){
                var lines=File.ReadAllLines(AgentDiagnostics.LogPath);Check(lines.Length>=6,"start and finish events are persisted");
                Check(lines.All(line=>!line.Contains("secret")&&!line.Contains("api_key")&&!line.Contains("data:image")),"trace excludes credentials, prompts and image data");
                Check(lines.All(line=>line.Contains("agent-check")&&line.Contains("Tool")),"trace lines contain task and tool identity");
            }
            Console.WriteLine("PASS: bounded Agent tool dispatch, typed failure propagation and redacted JSONL tracing.");
        }
    }
}
