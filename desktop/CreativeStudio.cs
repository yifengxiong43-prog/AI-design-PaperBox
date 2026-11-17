using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;

namespace PaperStudio
{
    public class CreativeStudio
    {
        Window window;
        LoadingRunner runner;
        DesignProject project=new DesignProject();
        List<SourceRecord> knowledge;
        KnowledgeCache knowledgeCache;
        ApiSettings settings=new ApiSettings();
        CancellationTokenSource cancellation;
        GenerationTask generationTask;
        bool trackingGeneration;
        string lastRemoteTool;
        bool busy,dirty,syncing;
        int imageGenerated,imageChecked,imageFailures,reviewFailures;
        static readonly string[] AgentStepNames={"理解设计需求","检索包装知识库","分析参考草图","计算包装尺寸和排列方式","构思创意方案","生成草图与效果图","检查生成结果"};
        bool detailOpen;
        string view="整体效果";
        double imageZoom=1.0;
        bool dragging;
        Point dragOrigin;
        double scrollOriginX,scrollOriginY;
        DispatcherTimer searchTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(400) };
        DispatcherTimer waitTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(30) };
        DateTime lastStatusUtc,runStartedUtc;
        string lastStatusText;
        static string ConfigPath { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","api-settings.json"); } }
        static string PlanningPath { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","planning-checkpoint.json"); } }
        T C<T>(string name) where T:FrameworkElement {return (T)window.FindName(name);}
        static Brush B(string c){return (Brush)new BrushConverter().ConvertFromString(c);}
        static Stream Resource(string n){return Assembly.GetExecutingAssembly().GetManifestResourceStream(n);}
        static string ReadResource(string n){using(var r=new StreamReader(Resource(n),Encoding.UTF8))return r.ReadToEnd();}
        static byte[] ResourceBytes(string n){using(var input=Resource(n))using(var output=new MemoryStream()){input.CopyTo(output);return output.ToArray();}}
        static string SchemeIdFor(int index){return "scheme-"+(char)('A'+index);}
        void Status(string s){lastStatusUtc=DateTime.UtcNow;lastStatusText=s;C<TextBlock>("Status").Text=s;}
        void On(string n,Action a){C<Button>(n).Click+=delegate{try{a();}catch(Exception e){Alert(e.Message);}};}
        void Async(string n,Func<Task> a){C<Button>(n).Click+=async delegate{try{await a();}catch(Exception e){Alert(e.Message);}};}
        void Alert(string s){Status(s);MessageBox.Show(window,s,"包装工作台",MessageBoxButton.OK,MessageBoxImage.Information);}
        DesignService Service()
        {
#if QA
            var service=new DesignService(settings,new DesktopQaHandler());
#else
            var service=new DesignService(settings);
#endif
            service.PreferEarlyConceptDelivery=true;
            service.ReferenceMode=project.ReferenceMode??"creative";
            service.KnowledgeCache=knowledgeCache;service.Progress=Status;service.ToolEvent=AgentToolChanged;service.UnderstandingReady=brief=>{C<Expander>("UnderstandingSettings").Visibility=Visibility.Visible;C<TextBlock>("UnderstandingPreview").Text=DesignMethod.Summary(brief);};
            service.PlanningState=generationTask==null?null:generationTask.Planning;
            service.PlanningSaved=state=>{
                if(generationTask!=null){generationTask.Planning=state;StoreTask();}
            };
            service.PlanningResumed=state=>{
                if(trackingGeneration&&generationTask!=null){for(int i=0;i<4;i++)if(generationTask.Steps[i].Status=="pending"){generationTask.Steps[i].Status="completed";generationTask.Steps[i].OutputSummary="已核对并恢复旧版检查点";generationTask.Steps[i].FinishedUtc=DateTime.UtcNow;SetAgentStep(i+1,"completed","已恢复");}StoreTask();}
            };
            return service;
        }
        DesignOption Selected {get{return project.Options.Count==0?null:project.Options[project.Selected];}}
        DesignVersion Current {get{return Selected==null?null:Selected.Current;}}
        [STAThread]
        public static void Main(string[] args)
        {
            try {
#if QA
                if(args.Length==3&&args[0]=="--resume-check")GenerationTaskStore.PathOverride=args[1];
#endif
                var app=new Application();var studio=new CreativeStudio();
#if QA
                if(args.Length==3&&args[0]=="--resume-check"){
                    studio.window.Loaded+=delegate{
                        try{
                            studio.RestoreInterruptedTask();
                            if(studio.generationTask==null||studio.project.Options.Count!=3||!studio.project.Options[0].Current.Pictures.ContainsKey("设计草图")||studio.C<Button>("ContinueGeneration").Visibility!=Visibility.Visible)throw new Exception("Restart did not restore the interrupted sketch task and continue action");
                            Directory.CreateDirectory(args[2]);File.WriteAllText(System.IO.Path.Combine(args[2],"restart-passed.txt"),"PASS: fresh WPF process restored interrupted task, first image and continue control.");studio.dirty=false;studio.window.Close();
                        }catch(Exception error){Directory.CreateDirectory(args[2]);File.WriteAllText(System.IO.Path.Combine(args[2],"restart-failed.txt"),error.ToString());Environment.ExitCode=1;studio.dirty=false;studio.window.Close();}
                    };app.Run(studio.window);return;
                }
                if(args.Length==2 && args[0]=="--render-check") {
                    studio.window.Loaded+=async delegate {
                        try{await studio.RenderCheck(args[1]);}
                        catch(Exception error){Directory.CreateDirectory(args[1]);File.WriteAllText(System.IO.Path.Combine(args[1],"failed.txt"),error.ToString());Environment.ExitCode=1;studio.dirty=false;studio.window.Close();}
                    };
                    app.Run(studio.window);return;
                }
#endif
                app.Run(studio.window);
            } catch(Exception e){if(args.Length==2&&args[0]=="--render-check"){Directory.CreateDirectory(args[1]);File.WriteAllText(System.IO.Path.Combine(args[1],"failed.txt"),e.ToString());Environment.ExitCode=1;}else MessageBox.Show(e.Message,"包装工作台启动提示");}
        }
        public CreativeStudio()
        {
            Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(ReadResource("DesignSystem.xaml")));
            window=(Window)XamlReader.Parse(ReadResource("Workspace.xaml"));
            window.Title="纸盒包装创意工作台 V"+Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            var workArea=SystemParameters.WorkArea;
            window.Width=Math.Min(window.Width,Math.Max(window.MinWidth,workArea.Width*0.88));
            window.Height=Math.Min(window.Height,Math.Max(window.MinHeight,workArea.Height-40));
            window.WindowStartupLocation=WindowStartupLocation.Manual;
            window.Left=workArea.Left+Math.Max(0,(workArea.Width-window.Width)/2);
            window.Top=workArea.Top+Math.Max(0,(workArea.Height-window.Height)/2);
            knowledge=Json.Read<List<SourceRecord>>(ReadResource("knowledge.json"));
            knowledge.AddRange(Json.Read<List<SourceRecord>>(ReadResource("web-knowledge.json")));
            knowledgeCache=KnowledgeCache.LoadOrRefresh(ReadResource("knowledge-analysis.json"),ResourceBytes("knowledge.json"),ResourceBytes("web-knowledge.json"),knowledge,message=>ReferenceDiagnostics.Write("cache-refresh",message));
            using(var stream=Resource("studio-logo.png")){var logo=new BitmapImage();logo.BeginInit();logo.CacheOption=BitmapCacheOption.OnLoad;logo.StreamSource=stream;logo.EndInit();logo.Freeze();C<Image>("StudioLogo").Source=logo;window.Icon=logo;}
            runner=new LoadingRunner(window,C<Canvas>("RunnerTrack"),C<TextBlock>("RunnerScore"),C<TextBlock>("RunnerHint"),Resource("runner-mascot.png"));
            window.Closed+=delegate{runner.Dispose();searchTimer.Stop();waitTimer.Stop();};
            waitTimer.Tick+=delegate{if(busy&&(DateTime.UtcNow-lastStatusUtc).TotalSeconds>=30)C<TextBlock>("Status").Text=(lastStatusText??"正在等待服务商")+" · 已等待 "+Math.Max(1,(int)(DateTime.UtcNow-runStartedUtc).TotalMinutes)+" 分钟，可取消";};
            string credentialWarning=null;
            try{if(File.Exists(ConfigPath))settings=Json.Read<ApiSettings>(File.ReadAllText(ConfigPath))??new ApiSettings();}catch{settings=new ApiSettings();}
            try{ApiSecretStore.Load(settings);}catch(Exception e){credentialWarning=e.Message;}
            settings.TextKey=Environment.GetEnvironmentVariable("PAPER_TEXT_API_KEY")??settings.TextKey;
            settings.ImageKey=Environment.GetEnvironmentVariable("PAPER_IMAGE_API_KEY")??settings.ImageKey;
            On("Settings",Settings);On("Open",Open);On("Save",delegate{Save();});On("Export",Export);
            On("AddPictures",AddPictures);On("Cancel",delegate{if(cancellation!=null)cancellation.Cancel();});
            On("ConfirmReference",ConfirmReference);
            Async("Generate",async delegate{
                await Generate();
            });
            Async("ContinueGeneration",ContinueGeneration);
            Async("Revise",Revise);Async("RetryImage",async delegate{C<Popup>("MorePopup").IsOpen=false;await RetryImage();});
            On("ViewKnowledgeReport",ViewKnowledgeReport);On("RepairKnowledge",RepairKnowledge);
            On("ViewOverall",delegate{view="整体效果";RenderDetail();});On("ViewOpen",delegate{view="打开状态";RenderDetail();});On("ViewLayout",delegate{view="内部布局";RenderDetail();});On("ViewSketch",delegate{view="设计草图";RenderDetail();});
            On("ZoomOut",delegate{SetImageZoom(imageZoom-0.15);});On("ZoomReset",delegate{SetImageZoom(1.0);});On("ZoomIn",delegate{SetImageZoom(imageZoom+0.15);});On("Compare",ShowOverview);
            On("CreateNav",ShowOverview);On("CloseDetails",ShowOverview);
            On("AddReferenceEmpty",delegate{AddPictures();C<Expander>("ReferenceSettings").IsExpanded=true;});
            On("ViewMenu",delegate{C<Popup>("ViewPopup").IsOpen=true;});On("More",delegate{C<Popup>("MorePopup").IsOpen=true;});
            window.SizeChanged+=delegate{UpdateWorkspaceLayout();};
            C<ScrollViewer>("ImageViewport").SizeChanged+=delegate{FitImage();};
            UpdateWorkspaceLayout();
            var viewport=C<ScrollViewer>("ImageViewport");
            viewport.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(imageZoom<=1.01)return;dragging=true;dragOrigin=e.GetPosition(viewport);scrollOriginX=viewport.HorizontalOffset;scrollOriginY=viewport.VerticalOffset;viewport.CaptureMouse();e.Handled=true;};
            viewport.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){if(!dragging)return;var point=e.GetPosition(viewport);viewport.ScrollToHorizontalOffset(scrollOriginX-(point.X-dragOrigin.X));viewport.ScrollToVerticalOffset(scrollOriginY-(point.Y-dragOrigin.Y));e.Handled=true;};
            viewport.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){if(!dragging)return;dragging=false;viewport.ReleaseMouseCapture();e.Handled=true;};
            C<ComboBox>("Versions").SelectionChanged+=delegate {if(!syncing && Selected!=null && C<ComboBox>("Versions").SelectedIndex>=0){Selected.Selected=C<ComboBox>("Versions").SelectedIndex;dirty=true;Render();}};
            C<TextBox>("Request").TextChanged+=delegate{if(!syncing){dirty=true;searchTimer.Stop();searchTimer.Start();}};
            C<TextBox>("Details").TextChanged+=delegate{if(!syncing)dirty=true;};
            C<ComboBox>("SizeTier").ItemsSource=PackagingConstraints.Tiers.Take(2).ToList();C<ComboBox>("SizeTier").DisplayMemberPath="Name";C<ComboBox>("SizeTier").SelectedValuePath="Id";
            C<ComboBox>("SizeTier").SelectedValue="large-standard";
            C<ComboBox>("PenType").ItemsSource=PackagingConstraints.PenTypes;C<ComboBox>("PenType").DisplayMemberPath="Name";C<ComboBox>("PenType").SelectedValuePath="Id";C<ComboBox>("PenType").SelectedValue="alcohol";
            foreach(var name in new[]{"MarkerCount","MarkerLength","MarkerDiameter","TrayHeight","OuterLongest","OuterMiddle","OuterShortest"})
                C<TextBox>(name).TextChanged+=delegate{if(!syncing){dirty=true;UpdatePackagingHint();}};
            C<ComboBox>("SizeTier").SelectionChanged+=delegate{if(!syncing){dirty=true;UpdatePackagingHint();}};
            C<ComboBox>("AllowanceCm").SelectionChanged+=delegate{if(!syncing){dirty=true;UpdatePackagingHint();}};
            C<ComboBox>("PenType").SelectionChanged+=delegate{if(!syncing){dirty=true;UpdatePackagingHint();}};
            C<ComboBox>("ReferenceMode").SelectionChanged+=delegate{if(!syncing){var item=C<ComboBox>("ReferenceMode").SelectedItem as ComboBoxItem;project.ReferenceMode=item==null?"creative":item.Tag.ToString();if(project.ReferenceMode!="creative")foreach(var p in project.Attachments.Where(p=>p.ReferenceType=="creative"))p.ReferenceType="structure";dirty=true;RenderAttachments();RenderReferenceConfirmation();}};
            UpdatePackagingHint();
            searchTimer.Tick+=delegate{searchTimer.Stop();C<TextBlock>("SizeHint").Text=String.IsNullOrWhiteSpace(C<TextBox>("Request").Text)?"":ReferenceSearch.SizeHint(knowledge,C<TextBox>("Request").Text);};
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy){e.Cancel=true;Status("任务正在进行，请先点击取消生成，再关闭窗口。");}else e.Cancel=!CanLeave();};
#if !QA
            RestoreInterruptedTask();
#endif
            CheckStartup(credentialWarning);
        }
        void CheckStartup(string warning)
        {
            string tools=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"python-tools");
            if(!File.Exists(System.IO.Path.Combine(tools,"geometry.py"))||!File.Exists(System.IO.Path.Combine(tools,"runtime","python.exe")))warning="随包几何工具缺失，请使用完整发布文件夹。";
            bool configured=!String.IsNullOrWhiteSpace(settings.TextBase)&&!String.IsNullOrWhiteSpace(settings.TextModel)&&!String.IsNullOrWhiteSpace(settings.TextKey)&&!String.IsNullOrWhiteSpace(settings.ImageBase)&&!String.IsNullOrWhiteSpace(settings.ImageModel)&&!String.IsNullOrWhiteSpace(settings.ImageKey);
            C<TextBlock>("ApiHint").Text=configured?"API 参数已填写，待实际调用验证":"请先在右上角设置文字与图片 API";
            Status(warning??(configured?"Agent 已加载；API 连接和图片结果仍需实际验证。":"Agent 已加载；生成前需配置文字与图片 API。"));
        }
        void SetBusy(bool value,string message)
        {
            busy=value;
            if(value){imageGenerated=trackingGeneration?project.Options.Sum(o=>o.Current.Pictures.Count(p=>p.Key=="设计草图"||p.Key=="整体效果")):0;imageChecked=imageGenerated;imageFailures=0;reviewFailures=0;for(int i=1;i<=7;i++)SetAgentStep(i,"pending",null);C<Border>("AgentProgress").Visibility=Visibility.Visible;C<ScrollViewer>("Results").Padding=new Thickness(0,230,0,16);C<Grid>("DetailView").Margin=new Thickness(0,230,0,0);}
            foreach(var n in new[]{"Generate","ContinueGeneration","ConfirmReference","Revise","RetryImage","ViewKnowledgeReport","RepairKnowledge","Open","Save","Export","Settings","AddPictures","AddReferenceEmpty","ZoomOut","ZoomReset","ZoomIn","Compare","CreateNav","CloseDetails","ViewMenu","More","ViewOverall","ViewOpen","ViewLayout","ViewSketch"})C<Button>(n).IsEnabled=!value;
            C<ComboBox>("ReferenceMode").IsEnabled=!value;
            C<Button>("Revise").IsEnabled=!value && Current!=null;C<Button>("RetryImage").IsEnabled=!value && Current!=null;
            C<TextBox>("Request").IsEnabled=!value;C<TextBox>("Details").IsEnabled=!value;C<TextBox>("Feedback").IsEnabled=!value;
            C<ComboBox>("Versions").IsEnabled=!value;
            foreach(var name in new[]{"MarkerCount","MarkerLength","MarkerDiameter","TrayHeight","OuterLongest","OuterMiddle","OuterShortest"})C<TextBox>(name).IsEnabled=!value;
            C<ComboBox>("SizeTier").IsEnabled=!value;C<ComboBox>("AllowanceCm").IsEnabled=!value;
            C<ComboBox>("PenType").IsEnabled=!value;
            C<UniformGrid>("ConceptCards").IsEnabled=!value;C<WrapPanel>("AttachmentList").IsEnabled=!value;
            C<Button>("Cancel").Visibility=value?Visibility.Visible:Visibility.Collapsed;C<Border>("RunnerPanel").Visibility=value?Visibility.Visible:Visibility.Collapsed;runner.SetActive(value);
            if(!value)UpdateContinueButton();
            Status(message);
        }
        void SetAgentStep(int index,string state,string detail)
        {
            var line=C<TextBlock>("AgentStep"+index);
            string symbol=state=="running"?"◌":state=="succeeded"||state=="completed"?"✓":state=="failed"||state=="warning"?"!":state=="cancelled"?"–":"○";
            line.Text=symbol+" "+AgentStepNames[index-1]+(String.IsNullOrWhiteSpace(detail)?"":" · "+detail);
            line.Foreground=B(state=="failed"?"#B44736":state=="warning"?"#9B6B2E":state=="cancelled"?"#8A8A8A":state=="succeeded"||state=="completed"?"#356B4E":state=="running"?"#1C1C1C":"#8A8A8A");
            line.ToolTip=detail;
        }
        static int AgentStep(string tool)
        {
            switch(tool){
                case "parse_requirements":case "understand_request":case "validate_understanding":return 1;
                case "retrieve_references":case "refresh_references":case "validate_citations":return 2;
                case "analyze_reference_images":case "analyze_reference_structure":return 3;
                case "estimate_geometry":case "calculate_geometry":return 4;
                case "draft_concepts":case "revise_design":case "review_design":case "validate_manufacturing":return 5;
                case "generate_image":return 6;
                case "inspect_image":return 7;
                default:return 0;
            }
        }
        void AgentToolChanged(AgentToolCall call)
        {
            if(!window.Dispatcher.CheckAccess()){window.Dispatcher.BeginInvoke(new Action(()=>AgentToolChanged(call)));return;}
            int index=AgentStep(call.Tool);if(index==0)return;
            if(call.Tool=="generate_image"&&call.Status=="succeeded")imageGenerated++;
            if(call.Tool=="generate_image"&&call.Status=="failed")imageFailures++;
            if(call.Tool=="inspect_image"&&call.Status=="succeeded")imageChecked++;
            if(call.Tool=="inspect_image"&&call.Status=="failed")reviewFailures++;
            if(trackingGeneration&&generationTask!=null){
                if(call.Status=="running"&&(call.Tool=="understand_request"||call.Tool=="draft_concepts"||call.Tool=="revise_design"||call.Tool=="review_design"||call.Tool=="generate_image"||call.Tool=="inspect_image")){lastRemoteTool=call.Tool;generationTask.UnknownTool=call.Tool;}
                else if(call.Tool==generationTask.UnknownTool&&call.Status=="succeeded")generationTask.UnknownTool=null;
                generationTask.Record(index,call);
                int expected=Math.Max(3,project.Options.Count*2);
                if(index==6&&call.Status=="succeeded"&&imageGenerated<expected)generationTask.Steps[5].Status="running";
                if(index==7&&call.Status=="succeeded"&&imageChecked<expected)generationTask.Steps[6].Status="running";
                if(index==6&&call.Status=="failed"&&imageChecked<expected)generationTask.Steps[6].Status="pending";
                StoreTask();
            }
            string error=call.Error==null?null:call.Error.Replace('\r',' ').Replace('\n',' ');
            string detail=call.Status=="failed"||call.Status=="warning"?(error==null?"需要注意":error.Substring(0,Math.Min(48,error.Length))):call.Status=="running"?"进行中":call.Status=="cancelled"?"已取消":null;
            if(index==6&&call.Status=="succeeded")detail="已返回 "+imageGenerated+" / "+Math.Max(3,project.Options.Count*2)+" 张";
            if(index==7&&call.Status=="succeeded")detail="已复核 "+imageChecked+" / "+Math.Max(3,project.Options.Count*2)+" 张";
            SetAgentStep(index,trackingGeneration&&generationTask!=null?generationTask.Steps[index-1].Status:call.Status,detail);
            if(call.Status=="failed"||call.Status=="warning")C<TextBlock>("AgentStep"+index).ToolTip=call.Error;
            if(call.Status=="failed")Status(call.Error??"当前步骤失败，可重试。");
        }
        void HideAgentProgress(){C<Border>("AgentProgress").Visibility=Visibility.Collapsed;C<ScrollViewer>("Results").Padding=new Thickness(0,24,0,16);C<Grid>("DetailView").Margin=new Thickness(0,16,0,0);}
        void StoreTask(){if(generationTask!=null){generationTask.Project=project;generationTask.UpdatedUtc=DateTime.UtcNow;GenerationTaskStore.Save(generationTask);}}
        string RunningRemoteTool(){return lastRemoteTool;}
        string CurrentInputKey(string request,string details,PackagingInput input)
        {
            return GenerationTaskStore.Fingerprint(new{request,details,input,project.ReferenceMode,Attachments=project.Attachments,Method=DesignMethod.Read(),Knowledge=knowledge.Select(s=>new{s.StableId,s.Text})});
        }
        void UpdateContinueButton()
        {
            C<Button>("ContinueGeneration").Visibility=(generationTask!=null&&(generationTask.Status=="failed"||generationTask.Status=="cancelled"))||(generationTask==null&&File.Exists(PlanningPath))?Visibility.Visible:Visibility.Collapsed;
        }
        void RenderReferenceConfirmation()
        {
            var spec=generationTask==null||generationTask.Planning==null?null:generationTask.Planning.ReferenceSpec;
            bool pending=spec!=null&&generationTask.Status=="reference-pending";
            C<Button>("ConfirmReference").Visibility=pending?Visibility.Visible:Visibility.Collapsed;
            C<TextBlock>("ReferenceSummary").Visibility=pending?Visibility.Visible:Visibility.Collapsed;
            if(pending)C<TextBlock>("ReferenceSummary").Text="识别结构："+spec.BodyShape+"；托盘："+spec.TrayCount+"；开合："+spec.OpeningMechanism+"。";
        }
        void ConfirmReference()
        {
            var planning=generationTask==null?null:generationTask.Planning;
            if(planning==null||planning.ReferenceSpec==null)return;
            var spec=planning.ReferenceSpec;
            string summary="盒体："+spec.BodyShape+"\n托盘："+spec.TrayCount+"；"+spec.TrayHierarchy+"\n开合："+spec.OpeningMechanism+"\n连杆："+spec.Linkages+"\n支点："+spec.PivotPoints+"\n笔排列："+spec.ProductLayout+"\n闭合与展开："+spec.ClosedOpenMapping+"\n必须保留："+String.Join("；",spec.LockedFeatures)+"\n待确认："+String.Join("；",spec.Unknowns)+"\n\n以上结构是否与上传的 Reference 一致？不一致时请修改需求或图片后重新识别。";
            if(MessageBox.Show(window,summary,"核对 Reference 结构",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            planning.ReferenceConfirmedHash=ReferenceDesign.Signature(spec);
            generationTask.Status="failed";generationTask.Error="Reference 已确认，等待继续生成。";StoreTask();RenderReferenceConfirmation();
            window.Dispatcher.BeginInvoke(new Action(async()=>await ContinueGeneration()));
        }
        void RenderTaskProgress()
        {
            if(generationTask==null)return;
            C<Border>("AgentProgress").Visibility=Visibility.Visible;
            C<ScrollViewer>("Results").Padding=new Thickness(0,230,0,16);
            C<Grid>("DetailView").Margin=new Thickness(0,230,0,0);
            for(int i=1;i<=7;i++){
                var s=generationTask.Steps[i-1];
                SetAgentStep(i,s.Status,s.Status=="failed"?"已中断":s.Status=="completed"?"已完成":s.Status=="running"?"执行中":s.Status=="pending"&&generationTask.Status=="failed"?"等待继续":null);
                C<TextBlock>("AgentStep"+i).ToolTip=s.Error??s.OutputSummary;
            }
            UpdateContinueButton();
            RenderReferenceConfirmation();
        }
        void RestoreInterruptedTask()
        {
            try{
                generationTask=GenerationTaskStore.Load();
                if(generationTask==null||generationTask.Status=="completed"||generationTask.Status=="concept-pending"){UpdateContinueButton();return;}
                project=generationTask.Project;detailOpen=false;syncing=true;
                C<TextBox>("Request").Text=project.DraftRequest??project.Request;
                C<TextBox>("Details").Text=project.DraftDetails??project.Details;
                RestorePackaging(project.DraftPackaging);SelectReferenceMode();syncing=false;
                RenderAttachments();Render();RenderTaskProgress();dirty=false;
                Status(generationTask.Status=="reference-pending"?"已恢复Reference解析结果，请核对结构后继续。":"已恢复中断任务 "+generationTask.TaskId.Substring(0,8)+"；已完成的方案和图片仍在。点击继续生成。");
            }catch(Exception error){Status("未能恢复上次生成任务："+error.Message);}
            UpdateContinueButton();
        }
        void ShowInterruption(string error)
        {
            int count=generationTask==null?0:generationTask.Steps.Count(s=>s.Status=="completed");
            string notice="生成暂时中断。已完成 "+count+" 个步骤及其结果已保存。";
            if(generationTask!=null&&!String.IsNullOrEmpty(generationTask.UnknownTool))notice+="\n服务端请求状态未知；继续前会确认是否重新发送，可能重复计费。";
            var dialog=new Window{Title="生成暂时中断",Owner=window,Width=460,Height=210,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#FAFAF9"),FontFamily=window.FontFamily,FontSize=13};
            var panel=new StackPanel{Margin=new Thickness(24)};
            panel.Children.Add(new TextBlock{Text=notice,TextWrapping=TextWrapping.Wrap,LineHeight=22,Margin=new Thickness(0,0,0,20)});
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var resume=new Button{Content="继续生成",Width=100,Height=36,Margin=new Thickness(0,0,8,0)};
            var reason=new Button{Content="查看原因",Width=100,Height=36,Margin=new Thickness(0,0,8,0)};
            var close=new Button{Content="取消",Width=70,Height=36};
            bool selected=false;resume.Click+=delegate{selected=true;dialog.Close();};reason.Click+=delegate{MessageBox.Show(dialog,error,"中断原因",MessageBoxButton.OK,MessageBoxImage.Information);};close.Click+=delegate{dialog.Close();};
            actions.Children.Add(resume);actions.Children.Add(reason);actions.Children.Add(close);panel.Children.Add(actions);dialog.Content=panel;dialog.ShowDialog();
            if(selected)window.Dispatcher.BeginInvoke(new Action(async()=>await ContinueGeneration()));
        }
        async Task Run(Func<CancellationToken,Task> work)
        {
            if(busy)return;cancellation=new CancellationTokenSource();runStartedUtc=DateTime.UtcNow;SetBusy(true,"正在整理需求…");waitTimer.Start();if(trackingGeneration)RenderTaskProgress();
            string status="完成。",interruption=null;bool generationInterruption=false;
            try{await work(cancellation.Token);status=C<TextBlock>("Status").Text;}
            catch(OperationCanceledException){status=cancellation.IsCancellationRequested?"已取消。已经完成的方案与图片已保留。":"请求超时，可重试；已完成内容已保留。";if(trackingGeneration&&generationTask!=null){generationTask.Status="cancelled";generationTask.Error=status;StoreTask();}}
            catch(Exception e){status=e.Message;
                if(trackingGeneration&&generationTask!=null){generationTask.Interrupt(e.Message,e is ApiTimeoutException||e is ApiConnectionException,RunningRemoteTool());StoreTask();interruption=e.Message;generationInterruption=true;}
                else interruption=e.Message;
#if QA
                throw;
#endif
            }
            finally{waitTimer.Stop();trackingGeneration=false;cancellation.Dispose();cancellation=null;if(project.Options.Any(o=>o.Versions.Any(v=>v.PendingImages!=null&&v.PendingImages.Count>0)))dirty=true;SetBusy(false,status);Render();RenderTaskProgress();}
#if !QA
            if(interruption!=null){if(generationInterruption)ShowInterruption(interruption);else MessageBox.Show(window,interruption,"生成未完成",MessageBoxButton.OK,MessageBoxImage.Information);}
#endif
        }
        List<SourceRecord> References(string request){return ReferenceSearch.Find(knowledge,request);}
        PackagingInput ReadPackaging()
        {
            var allowance=C<ComboBox>("AllowanceCm").SelectedItem as ComboBoxItem;
            var tier=C<ComboBox>("SizeTier").SelectedItem as PackagingTier;
            var pen=C<ComboBox>("PenType").SelectedItem as PenTypeChoice;
            return new PackagingInput{PenType=pen==null?null:pen.Id,Count=C<TextBox>("MarkerCount").Text.Trim(),Length=C<TextBox>("MarkerLength").Text.Trim(),Diameter=C<TextBox>("MarkerDiameter").Text.Trim(),TrayHeight=C<TextBox>("TrayHeight").Text.Trim(),Tier=tier==null?null:tier.Id,
                AllowanceCm=allowance==null?"":Convert.ToString(allowance.Content),OuterLongest=C<TextBox>("OuterLongest").Text.Trim(),OuterMiddle=C<TextBox>("OuterMiddle").Text.Trim(),OuterShortest=C<TextBox>("OuterShortest").Text.Trim()};
        }
        void RestorePackaging(PackagingInput input)
        {
            input=input??new PackagingInput();
            C<ComboBox>("PenType").SelectedValue=input.PenType??"alcohol";
            C<TextBox>("MarkerCount").Text=input.Count??"";C<TextBox>("MarkerLength").Text=input.Length??"";C<TextBox>("MarkerDiameter").Text=input.Diameter??"";C<TextBox>("TrayHeight").Text=input.TrayHeight??"";
            C<ComboBox>("SizeTier").SelectedValue=input.Tier;C<ComboBox>("AllowanceCm").SelectedIndex=input.AllowanceCm=="3"?0:input.AllowanceCm=="4"?1:2;
            C<TextBox>("OuterLongest").Text=input.OuterLongest??"";C<TextBox>("OuterMiddle").Text=input.OuterMiddle??"";C<TextBox>("OuterShortest").Text=input.OuterShortest??"";
            UpdatePackagingHint();
        }
        void UpdatePackagingHint()
        {
            var input=ReadPackaging();C<StackPanel>("CustomSizePanel").Visibility=input.Tier=="custom"?Visibility.Visible:Visibility.Collapsed;
            C<TextBlock>("PenPlacementHint").Text=input.PenType=="acrylic"?"丙烯笔只能竖直收纳，不能通过横放来压低盒体。":input.PenType=="fineliner"?"针管笔先按实测长度和最大笔帽直径排布，笔尖保护与摆放姿态待实物确认。":"酒精笔和彩铅可横放或竖放，由方案选择合适布局。";
            PackagingConstraints measured=null;
            try{measured=input.Build();C<TextBlock>("PackagingEstimateHint").Text="单排净宽："+AssemblyGeometry.Span(measured.MarkerCount,measured.MarkerDiameter,4).ToString("0.##")+" mm（含整排 4 mm 余量，未含壁厚/隔板）。生成后按各组实际支数计算。";}
            catch(InvalidOperationException){C<TextBlock>("PackagingEstimateHint").Text="填写支数、完整笔长和笔直径后，自动估算盒体尺寸。";}
            // The live product-size limit preview is independent of the entered marker values.
            input.Count="1";input.Length="1";input.Diameter="1";input.PenType=null;
            try{C<TextBlock>("PackagingLimitHint").Text=input.Build().Summary();}
            catch(InvalidOperationException e){C<TextBlock>("PackagingLimitHint").Text=e.Message;}
        }
        List<SourceRecord> TextReferences(List<SourceRecord> relevant)
        {
            var grouped=knowledge.Concat(relevant??new List<SourceRecord>()).GroupBy(s=>s.StableId).ToList();
            if(grouped.Any(g=>String.IsNullOrWhiteSpace(g.Key)||g.Select(s=>s.Id).Distinct().Count()!=1||g.Select(s=>s.Text).Distinct().Count()!=1))
                throw new InvalidOperationException("内置资料与任务快照的稳定ID或正文不一致，需要刷新资料后重新检索。");
            return grouped.Select(g=>g.First()).ToList();
        }
        string WriteKnowledgeReport()
        {
            var aliases=knowledge.GroupBy(s=>s.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToList();
            var stable=knowledge.GroupBy(s=>s.StableId).Where(g=>String.IsNullOrWhiteSpace(g.Key)||g.Count()>1).Select(g=>g.Key).ToList();
            var missing=knowledge.Where(s=>String.IsNullOrWhiteSpace(s.Text)).Select(s=>s.Id).ToList();
            var changed=new List<string>();
            var current=knowledge.Where(s=>!String.IsNullOrWhiteSpace(s.StableId)).GroupBy(s=>s.StableId).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var source in generationTask==null||generationTask.RetrievedSources==null?new List<SourceRecord>():generationTask.RetrievedSources){
                SourceRecord found;if(String.IsNullOrWhiteSpace(source.StableId)||!current.TryGetValue(source.StableId,out found)||found.Text!=source.Text||found.Id!=source.Id)changed.Add(source.Id);
            }
            var path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","knowledge-diagnostic.txt");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var lines=new List<string>{"纸盒包装创意工作台 · 资料诊断",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),"内置资料记录："+knowledge.Count,"旧记录ID重复："+aliases.Count,"稳定ID重复或空值："+stable.Count,"原文缺失："+missing.Count,"任务快照失效："+changed.Count,"索引状态："+(knowledgeCache.Warnings.Count==0?"已加载内置分析":"已从当前原文临时重建"),"当前任务："+(generationTask==null?"无":generationTask.TaskId),""};
            foreach(var id in aliases)lines.Add("旧ID重复："+id);
            foreach(var id in stable)lines.Add("稳定ID异常："+id);
            foreach(var id in missing)lines.Add("原文缺失："+id);
            foreach(var id in changed)lines.Add("任务快照失效，需重新检索："+id);
            foreach(var warning in knowledgeCache.Warnings)lines.Add("索引提醒："+warning);
            lines.Add("内置原始文件、逐记录哈希和摘要的完整检查，请运行随版本提供的 diagnose_knowledge.py；本报告记录当前运行与任务快照。 ");
            File.WriteAllLines(path,lines,Encoding.UTF8);return path;
        }
        void ViewKnowledgeReport()
        {
            C<Popup>("MorePopup").IsOpen=false;
            try{
                var path=WriteKnowledgeReport();
                var directory=System.IO.Path.GetDirectoryName(path);
                var root=AppDomain.CurrentDomain.BaseDirectory;
                var python=System.IO.Path.Combine(root,"python-tools","runtime","python.exe");
                var script=System.IO.Path.Combine(root,"diagnostics","diagnose_knowledge.py");
                if(File.Exists(python)&&File.Exists(script)){
                    var output=System.IO.Path.Combine(directory,"knowledge-integrity.json");
                    var task=System.IO.Path.Combine(directory,"active-generation.json");
                    var arguments="\""+script+"\" --output \""+output+"\""+(File.Exists(task)?" --task \""+task+"\"":"");
                    using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(python,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true})){
                        if(!process.WaitForExit(30000)){process.Kill();throw new InvalidOperationException("资料逐条检查超时，请稍后重试。");}
                        if(process.ExitCode!=0)throw new InvalidOperationException("资料逐条检查失败："+process.StandardError.ReadToEnd());
                    }
                    File.AppendAllText(path,"逐记录报告："+output+Environment.NewLine,Encoding.UTF8);
                }
                System.Diagnostics.Process.Start(path);
            }catch(Exception error){ReferenceDiagnostics.Write("knowledge-diagnostic",error.ToString());MessageBox.Show(window,error.Message,"资料诊断",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        void RepairKnowledge()
        {
            C<Popup>("MorePopup").IsOpen=false;
            try{
                var restored=KnowledgeCache.LoadOrRefresh(ReadResource("knowledge-analysis.json"),ResourceBytes("knowledge.json"),ResourceBytes("web-knowledge.json"),knowledge,message=>ReferenceDiagnostics.Write("cache-refresh",message));
                if(restored.Warnings.Count>0)throw new InvalidOperationException("内置分析与原文不一致，已生成诊断报告；需要用原始资料重建发布包。不能直接覆盖资料。");
                knowledgeCache=restored;
                if(generationTask!=null&&generationTask.RetrievedSources!=null){
                    bool stale=false;try{TextReferences(generationTask.RetrievedSources);}catch(InvalidOperationException){stale=true;}
                    if(stale){generationTask.RetrievedSources=null;generationTask.Planning=null;for(int i=1;i<7;i++){generationTask.Steps[i].Status="pending";generationTask.Steps[i].Error=null;}generationTask.Status="failed";generationTask.UnknownTool=null;generationTask.Error="资料快照已失效；继续生成时将重新检索和规划。";StoreTask();RenderTaskProgress();}
                }
                WriteKnowledgeReport();Status("资料索引已核对。有效任务结果保留；失效快照会在继续生成时重新检索。");
            }catch(Exception error){ReferenceDiagnostics.Write("knowledge-repair",error.ToString());var path=WriteKnowledgeReport();MessageBox.Show(window,error.Message+"\n诊断报告："+path,"知识库需要检查",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        static Picture StoredPng(string resource,string description)
        {
            using(var input=Resource(resource))using(var output=new MemoryStream()){input.CopyTo(output);return new Picture{Name=description,Data=Convert.ToBase64String(output.ToArray())};}
        }
        List<Picture> ContextPictures(List<SourceRecord> sources,bool textOnly=false)
        {
            var result=new List<Picture>(project.Attachments);
            foreach(var id in ReferenceSearch.PictureIds(sources))
                if(textOnly)result.Add(StoredPng(id+".jpg","后台案例结构参考（不代表同款产品）"));else using(var s=Resource(id+".jpg"))result.Add(new Picture{Name="后台案例结构参考（不代表同款产品）",Data=EncodePicture(s)});
            // Planning reuses stored brand/sketch analysis; image generation still receives actual pixels.
            if(textOnly)return result;
            result.Add(StoredPng("ohuhu-style-board.png","Ohuhu风格参考总览：图1黑底热带平涂；图2白底彩色线描；图3绿色Ohuhu标志；图4透明底黑线趣味涂鸦；图5白底户外青蛙涂鸦；图6白底植物线稿。按当前主题选取合适风格，不拼接全部图案；示例12/60色、Hilo/Kalalau不是当前产品参数。"));
            result.Add(StoredPng("ohuhu-logo.png","Ohuhu品牌标志原图：品牌字形、绿色与叶片细节以此图为准。用户明确要求其他品牌或无品牌时不使用。"));
            result.Add(StoredPng("ohuhu-skateboard-mascot.png","Ohuhu滑板变色龙原始图案：用户特别要求保留的品牌元素，保持原角色的卷尾、滑板、姿态和线条特征，不替换为青蛙或无关角色。可融入合适方案，不以重复贴图替代结构创新。"));
            result.Add(StoredPng("sketch-style-board.png","用户提供的5张设计草图：圆角提箱与分盒、竖式多抽屉提箱、多层展开托盘、联动阶梯展开、植物异形侧翼抽屉。参考创意探索与线稿表达；不要机械复制结构，原图300支、尺寸和笔长比例不是当前产品参数。"));
            result.Add(StoredPng("sketch-06-expanded-layout.png","用户新增的全展开分组参考：展开后的每个托盘/分盒/笔组都有数量牌和尺寸引线。借鉴逐单元标注、分组数量和整体展开关系；不得沿用原图72支或旧尺寸。"));
            const string officialDescription="Ohuhu官网产品外观研究：Hilo、Honolulu²、Hanauma及旋转收纳反例。各款笔帽/笔尖、图案不同；采用当前笔型，示例数量不覆盖输入；旋转反例不可用于宣称全部笔可见。尺寸按用户输入。";
            if(textOnly)result.Add(StoredPng("ohuhu-official-board.jpg",officialDescription));else using(var source=Resource("ohuhu-official-board.jpg"))result.Add(new Picture{Name=officialDescription,Data=EncodePicture(source)});
            return result;
        }
        List<Picture> ImageReferences(DesignVersion version,List<SourceRecord> sources,string targetView=null)
        {
            bool sketch=targetView=="设计草图";
            var pictures=new List<Picture>(project.Attachments);
            if(version.Concept.Understanding!=null&&version.Concept.Understanding.ReferenceDesign!=null)return pictures;
            if(targetView=="整体效果"&&version.Concept.Assembly!=null){
                if(version.Constraints!=null&&version.Constraints.PenType=="fineliner")pictures.Add(StoredPng("official-wai-momi.png","Ohuhu Wai Momi 针管笔实物外观；不采用原图数量或包装结构。"));
                if(version.Constraints!=null&&version.Constraints.PenType=="alcohol")using(var source=Resource("official-honolulu-320.jpg"))pictures.Add(new Picture{Name="Ohuhu Honolulu 酒精笔外观与笔帽颜色；不采用原图数量或包装结构。",Data=EncodePicture(source)});
                pictures.Add(StoredPng("ohuhu-logo.png","Ohuhu标志原图；仅用于本方案纸盒印刷，不改变草图结构。"));
                pictures.Add(StoredPng("ohuhu-skateboard-mascot.png","滑板变色龙图案；仅用于本方案纸盒印刷，不改变草图结构。"));
                return pictures;
            }
            bool calculatedEffect=!sketch&&(version.Concept.DesignOrigin??"").StartsWith("controlled-",StringComparison.Ordinal);
            if(version.Concept.Assembly!=null){if(sketch)pictures.InsertRange(0,AssemblyGuide.CreatePages(version.Constraints,version.Concept));else if(!calculatedEffect)pictures.Insert(0,AssemblyGuide.Create(version.Constraints,version.Concept));}
            if(!sketch&&!calculatedEffect&&version.Constraints!=null&&version.Constraints.PenType!=null&&version.Concept.PenLayout!=null)pictures.Insert(0,ProportionGuide.Create(version.Constraints,version.Concept));
            if(version.Constraints!=null&&version.Constraints.PenType=="fineliner")pictures.Add(StoredPng("official-wai-momi.png","Ohuhu Wai Momi Acrylic Gel Pens 官网实物外观：细长白色笔杆、带笔夹的彩色笔帽、0.7mm细头。仅供针管笔外观参考；本次笔长、直径和支数以用户输入为准。"));
            if(version.Constraints!=null&&version.Constraints.PenType=="alcohol")using(var source=Resource("official-honolulu-320.jpg"))pictures.Add(new Picture{Name="Ohuhu Honolulu 320 官网笔帽配色参考：笔帽顶面按色相排列并印有色号；本图的320色和刷头/斜头组合不代表当前支数或尺寸。",Data=EncodePicture(source)});
            pictures.Add(StoredPng("ohuhu-style-board.png","Ohuhu风格参考总览：按用户主题选择合适视觉方向，不拼接全部图案；旧系列名称和色数不覆盖当前产品。"));
            pictures.Add(StoredPng("ohuhu-logo.png","Ohuhu品牌标志原图：字形、比例与叶片以原图为准；要求其他品牌或无品牌时不使用。"));
            pictures.Add(StoredPng("ohuhu-skateboard-mascot.png","Ohuhu滑板变色龙原始图案：保留卷尾、滑板、姿态和线条特征，不用青蛙替代。"));
            if(sketch){
                var words=version.Concept.Structure+version.Concept.Opening;string selected=words.Contains("抽屉")?"02":words.Contains("阶梯")||words.Contains("联动")||words.Contains("连体")||words.Contains("折页")?"04":words.Contains("手提")||words.Contains("提携")?"01":"03";
                pictures.Add(StoredPng("sketch-"+selected+".png","用户原始透明草图：细线、多视图、动作箭头、尺寸引线与局部色块；只借鉴表达，不复制旧数值或盒型。"));
                pictures.Add(StoredPng("sketch-style-board.png","用户5张草图表达总览：同一设计的闭合/展开、分组和连接变化。画面透明，沿用本次尺寸引线与全部单元编号。"));
            }else{
                if(!calculatedEffect)pictures.Add(StoredPng("overall-presentation-reference.png","用户指定的整体效果表达参考：只借鉴大尺寸闭合实物图与完全打开实物图并列、少量尺寸箭头和局部说明；禁止复制原图的织物拉链包、240支、Hilo名称、旧尺寸与配件。当前纸盒必须逐项服从本方案已通过草图和计算账本。"));
                using(var source=Resource("ohuhu-official-board.jpg"))pictures.Add(new Picture{Name="Ohuhu官网产品外观研究：使用当前笔型与真实笔长；旋转收纳不代表全部笔身可见。",Data=EncodePicture(source)});
                if(!calculatedEffect&&!version.Pictures.ContainsKey("整体效果")&&String.IsNullOrEmpty(version.ParentPicture))foreach(var id in ReferenceSearch.PictureIds(sources)){
                    if(pictures.Count>=14)break;
                    using(var source=Resource(id+".jpg"))pictures.Add(new Picture{Name="后台案例结构参考：仅借鉴动作，不复制旧尺寸、数量或主题。",Data=EncodePicture(source)});
                }
            }
            return pictures;
        }
        async Task Generate()
        {
            string request=C<TextBox>("Request").Text.Trim(),details=C<TextBox>("Details").Text.Trim();
            if(request.Length==0){Alert("先用一句话描述你想做的包装。");return;}
            if(project.Attachments.Count>0&&project.ReferenceMode=="creative"&&ReferenceDesign.RequestsRestoration(request)){project.ReferenceMode="restore";SelectReferenceMode();foreach(var p in project.Attachments.Where(p=>p.ReferenceType=="creative"))p.ReferenceType="structure";RenderAttachments();}
            var input=ReadPackaging();var constraints=input.Build();
            if(project.ReferenceMode!="creative"&&project.Attachments.Count==0){Alert("参考结构模式需要先添加结构参考图。");return;}
            settings.Validate(false);settings.Validate(true);
            if(project.Options.Count>0 && MessageBox.Show(window,"重新生成会替换当前方案。需要保留时，请先保存项目。继续生成？","重新生成",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            project.Options.Clear();project.Request=request;project.Details=details;project.DraftRequest=request;project.DraftDetails=details;project.DraftPackaging=input;project.Constraints=constraints;
            generationTask=GenerationTask.Create(CurrentInputKey(request,details,input),project);
            StoreTask();UpdateContinueButton();
            await ExecuteGeneration(request,details,input,constraints);
        }
        async Task ContinueGeneration()
        {
            if(busy)return;
            string request=C<TextBox>("Request").Text.Trim(),details=C<TextBox>("Details").Text.Trim();
            if(generationTask==null){
                if(!File.Exists(PlanningPath))return;
                if(request.Length==0){Alert("旧版只保存了规划检查点。请先打开旧项目或重新填写原需求和产品参数。");return;}
                var legacyInput=ReadPackaging();var legacyConstraints=legacyInput.Build();settings.Validate(false);settings.Validate(true);
                if(project.Options.Count>0&&!CanLeave())return;
                if(MessageBox.Show(window,"检测到 V1.0.0 的规划检查点。旧版没有保存完整表单，请确认当前需求、产品参数和参考图与中断时一致。后端会核对指纹；若不一致将重新规划。旧请求的服务端状态未知，重新调用可能计费。确定继续吗？","恢复旧版设计进度",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                var legacy=Json.Read<PlanningCheckpoint>(File.ReadAllText(PlanningPath,Encoding.UTF8));
                project.Options.Clear();project.Request=request;project.Details=details;project.DraftRequest=request;project.DraftDetails=details;project.DraftPackaging=legacyInput;project.Constraints=legacyConstraints;
                generationTask=GenerationTask.Create(CurrentInputKey(request,details,legacyInput),project);generationTask.Planning=legacy;StoreTask();
            }
            var input=ReadPackaging();var constraints=input.Build();
            settings.Validate(false);settings.Validate(true);
            if(generationTask.InputKey!=CurrentInputKey(request,details,input)){
                for(int i=1;i<=7;i++)if(generationTask.Steps[i-1].Status=="completed")SetAgentStep(i,"warning","需要重新执行");
                MessageBox.Show(window,"设计输入或资料已变化，旧结果不能直接复用。将从需求理解开始重新执行。","需要重新执行",MessageBoxButton.OK,MessageBoxImage.Information);
                await Generate();return;
            }
            bool localDelivery=TryDeliverSavedConcepts(request,details,constraints);
            if(!String.IsNullOrEmpty(generationTask.UnknownTool)){
                if(!localDelivery&&MessageBox.Show(window,"上次 "+generationTask.UnknownTool+" 请求的服务端状态未知。当前服务商没有已确认可用的任务状态查询或幂等恢复接口。重新发起失败步骤可能重复计费。确定重新发起吗？","确认重新请求",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                generationTask.UnknownTool=null;StoreTask();
            }
            await ExecuteGeneration(request,details,input,constraints);
        }
        bool TryDeliverSavedConcepts(string request,string details,PackagingConstraints constraints)
        {
            var checkpoint=generationTask.Planning;
            if(project.ReferenceMode!="creative"||project.Options.Count>0||checkpoint==null||checkpoint.Understanding==null||checkpoint.Round==0||checkpoint.Issues==null||
                !checkpoint.Issues.Any(i=>!i.Resolved&&(i.Category=="实际结构几何冲突"||i.Category=="产品尺寸或排列错误")))return false;
            try{
                var sources=TextReferences(generationTask.RetrievedSources??References(request+" "+details));
                var result=ReliableConcepts.Create(constraints,checkpoint.Understanding,sources);
                project.Brief=result.Brief;project.Missing=result.Missing;project.Sources=sources.Select(s=>s.Id).ToList();project.Selected=0;
                project.Options=result.Concepts.Select((c,i)=>{var schemeId=SchemeIdFor(i);return new DesignOption{SchemeId=schemeId,Versions=new List<DesignVersion>{new DesignVersion{SchemeId=schemeId,Concept=c,Requirements=request+"\n"+details,Constraints=constraints}}};}).ToList();
                checkpoint.Concepts=Json.Copy(result.Concepts);checkpoint.Complete=true;checkpoint.AwaitingReview=false;
                generationTask.Steps[4].Status="completed";generationTask.Steps[4].OutputSummary="已交付3套受控概念；工程待验证";generationTask.Steps[4].Error=null;
                ReferenceDiagnostics.Write("resume-controlled-concepts",new{generationTask.TaskId,CheckpointRound=checkpoint.Round,Concepts=result.Concepts.Select(c=>c.Name).ToList()});
                StoreTask();dirty=true;Render();return true;
            }catch(InvalidOperationException error){ReferenceDiagnostics.Write("resume-controlled-unavailable",new{generationTask.TaskId,Error=error.ToString()});return false;}
        }
        async Task ExecuteGeneration(string request,string details,PackagingInput input,PackagingConstraints constraints)
        {
            trackingGeneration=true;lastRemoteTool=null;if(generationTask!=null){generationTask.Status="running";generationTask.Error=null;}
            await Run(async token=>{
                var sources=generationTask.RetrievedSources;
                if(sources==null){sources=References(constraints.PenTypeName+" "+constraints.MarkerCount+"支 "+request+" "+details);generationTask.RetrievedSources=sources;StoreTask();}
                using(var service=Service())
                {
                    if(project.Options.Count==0){
                        Status(project.ReferenceMode=="restore"?"正在还原已确认的Reference结构…":"正在结合产品参数与Reference生成设计方向…");
                        var result=await service.Generate(request,details,TextReferences(sources),ContextPictures(sources,true),null,null,token,constraints,true,project.Attachments);
                        if(result==null){generationTask.Status="reference-pending";generationTask.Error=null;StoreTask();RenderReferenceConfirmation();Status("已识别Reference结构，请核对确认后继续生成草图。");return;}
                        result.Validate(project.ReferenceMode=="restore"?1:3);
                        project.Brief=result.Brief;project.Missing=result.Missing;
                        project.Sources=TextReferences(sources).Select(r=>r.Id).ToList();project.Selected=0;
                        project.Options=result.Concepts.Select((c,i)=>{var schemeId=SchemeIdFor(i);return new DesignOption{SchemeId=schemeId,Versions=new List<DesignVersion>{new DesignVersion{SchemeId=schemeId,Concept=c,Requirements=request+"\n"+details,Constraints=constraints}}};}).ToList();
                        StoreTask();dirty=true;view="设计草图";detailOpen=false;Render();
                    }
                    {
                        for(int i=0;i<project.Options.Count;i++)
                        {
                            if(!String.IsNullOrEmpty(project.Options[i].Current.CurrentSketchData()))continue;
                            token.ThrowIfCancellationRequested();
                            try{
                                await GenerateConceptImages(service,project.Options[i].Current,sources,token,i+1,false);
                            }catch(ImageQualityException error){
                                ReferenceDiagnostics.Write("image-quality-pending",new{TaskId=generationTask.TaskId,Concept=project.Options[i].Current.Concept.Name,Issue=error.Message});
                                Status("方向 "+(i+1)+" 的图片已返回，但比例或结构待复核；继续生成其他方向。");
                            }
                            StoreTask();dirty=true;Render();
                        }
                        if(project.Options.Any(o=>String.IsNullOrEmpty(o.Current.CurrentSketchData())))throw new ImageQualityException("部分结构草图未生成或未通过基础核对。已完成的方案和图片已保存；请继续处理失败方向。");
                        generationTask.Steps[5].Status="pending";generationTask.Steps[5].OutputSummary="等待用户选择草图后生成效果图";generationTask.Steps[5].Error=null;
                        generationTask.Steps[6].Status="pending";generationTask.Steps[6].OutputSummary="等待效果图生成";generationTask.Steps[6].Error=null;
                        generationTask.Status="concept-pending";generationTask.Error=null;generationTask.UnknownTool=null;StoreTask();
                        Status(project.ReferenceMode=="restore"?"还原结构草图已生成。请核对后生成效果图。":"3套结构草图已生成。请先查看并选择方向，再生成对应效果图。");
                    }
                }
            });
        }
        async Task GenerateConceptImages(DesignService service,DesignVersion version,List<SourceRecord> sources,CancellationToken token,int direction,bool includeEffect)
        {
            version.EnsureBinding(String.IsNullOrWhiteSpace(version.SchemeId)?SchemeIdFor(Math.Max(0,direction-1)):version.SchemeId);
            foreach(var targetView in (includeEffect?new[]{"设计草图","整体效果"}:new[]{"设计草图"})){
                if(targetView=="整体效果"?version.HasVerifiedEffect():!String.IsNullOrEmpty(version.CurrentSketchData()))continue;
                PendingImage sketchPending;
                if(targetView=="设计草图"&&version.PendingImages.TryGetValue(targetView,out sketchPending)&&sketchPending.VisualReferenceUsable)continue;
                token.ThrowIfCancellationRequested();
                Status("方向 "+direction+" / 3：正在生成并核对"+targetView+"…");
                string img;
                try{
                    if(targetView=="设计草图"&&(version.Concept.DesignOrigin??"").StartsWith("controlled-",StringComparison.Ordinal)){
                        img=CalculatedSketch.Create(version.Constraints,version.Concept);
                        ReferenceDiagnostics.Write("calculated-sketch",new{Concept=version.Concept.Name,Count=version.Constraints.MarkerCount,Width=version.Concept.PenLayout.BoxWidth,Depth=version.Concept.PenLayout.BoxDepth,Height=version.Concept.PenLayout.BoxHeight});
                    }else img=await service.GenerateImage(version,targetView,ImageReferences(version,sources,targetView),token,TextReferences(sources));
                }
                catch(ImageQualityException){
                    if(targetView=="设计草图"&&version.PendingImages.TryGetValue(targetView,out sketchPending)&&sketchPending.VisualReferenceUsable){
                        version.Concept.SketchReferenceId="sketch:"+version.CurrentSketchHash();
                        Status("方向 "+direction+" / 3：草图实体已返回，尺寸标注待修正；效果图等待草图确认。");
                        StoreTask();dirty=true;Render();continue;
                    }
                    throw;
                }
                version.Pictures[targetView]=NormalizePicture(img);
                if(targetView=="设计草图"){
                    version.SketchId=version.CurrentSketchId();
                    version.Concept.SketchReferenceId=version.SketchId;
                }
                StoreTask();dirty=true;Render();
            }
        }
        async Task Revise()
        {
            if(Current==null)return;string feedback=C<TextBox>("Feedback").Text.Trim();if(feedback.Length==0){Alert("请输入本次想修改的内容，以及需要保留的部分。");return;}
            var constraints=Current.Constraints??project.Constraints??ReadPackaging().Build();
            if(constraints.PenType==null){constraints=Json.Copy(constraints);constraints.PenType=ReadPackaging().PenType;constraints.Validate();}
            settings.Validate(false);settings.Validate(true);
            await Run(async token=>{
                var previous=Current;DesignVersion next;
                {
                    using(var service=Service())
                    {
                        Status("正在基于所选版本理解修改，并保留未改动的约束…");
                        var sources=References(constraints.PenTypeName+" "+constraints.MarkerCount+"支 "+project.Request+" "+project.Details+" "+feedback);
                        var response=await service.Generate(project.Request,project.Details,TextReferences(sources),ContextPictures(sources,true),previous,feedback,token,constraints,true,project.Attachments);
                        next=new DesignVersion{SchemeId=Selected.SchemeId,Concept=response.Concepts[0],Instruction=feedback,Constraints=constraints,Requirements=(previous.Requirements??(project.Request+"\n"+project.Details))+"\n修改要求："+feedback,Demo=false,Theme=previous.Theme,Accent=previous.Accent,
                            ParentPicture=previous.HasVerifiedEffect()?previous.Pictures["整体效果"]:previous.ParentPicture};
                        Selected.Versions.Add(next);Selected.Selected=Selected.Versions.Count-1;dirty=true;Render();
                        C<TextBox>("Feedback").Clear();Status("修改方案已保存，正在先生成新的结构草图…");
                        await GenerateConceptImages(service,next,sources,token,project.Selected+1,false);
                        dirty=true;view="设计草图";Render();Status("新结构草图已生成。请先核对草图，再从整体效果视图生成效果图。可在右侧切换比较。");return;
                    }
                }
            });
        }
        async Task RetryImage()
        {
            if(Current==null)return;
            if(Current.Constraints==null||Current.Constraints.PenType==null||Current.Concept.PenLayout==null)throw new InvalidOperationException("旧方案尚未记录笔类与摆放比例。请先选择笔类，通过“基于选中方案修改”补齐，或重新生成方案后再出图。");
            var target=Current;target.EnsureBinding(Selected.SchemeId);string targetView=view;PendingImage pending;bool resume=target.PendingImages!=null&&target.PendingImages.TryGetValue(targetView,out pending)&&!pending.NeedsCorrection;
            if(resume)settings.Validate(false);else settings.Validate(true);
            if(String.IsNullOrEmpty(target.Requirements))target.Requirements=project.Request+"\n"+project.Details;
            if(!resume&&target.Pictures.ContainsKey(targetView)&&MessageBox.Show(window,"重新生成会替换此版本的当前视图。继续？","重新生成视图",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            trackingGeneration=true;generationTask.Status="running";generationTask.Error=null;
            await Run(async token=>{
                Status("正在生成并核对"+targetView+"，保持所选方案的一致性…");
                var sources=References(target.Constraints.PenTypeName+" "+target.Constraints.MarkerCount+"支 "+project.Request+" "+project.Details+" "+target.Instruction);
                using(var service=Service()){
                    if(targetView=="整体效果"&&String.IsNullOrEmpty(target.CurrentSketchData())){
                        await GenerateConceptImages(service,target,sources,token,project.Selected+1,false);
                        generationTask.Steps[5].Status="pending";generationTask.Steps[5].OutputSummary="等待用户确认草图后生成效果图";generationTask.Steps[6].Status="pending";generationTask.Steps[6].OutputSummary="等待效果图生成";
                        generationTask.Status="concept-pending";StoreTask();
                        view="设计草图";Render();Status("已先生成结构草图。请核对草图后，再次选择整体效果生成效果图。");
                    }else {
                        string img=await service.GenerateImage(target,targetView,ImageReferences(target,sources,targetView),token,TextReferences(sources));
                        target.Pictures[targetView]=NormalizePicture(img);target.EnsureBinding(Selected.SchemeId);target.Concept.SketchReferenceId=target.CurrentSketchId();target.SketchId=target.CurrentSketchId();
                        if(generationTask!=null){generationTask.Steps[5].Status="completed";generationTask.Steps[5].OutputSummary="已按选定草图生成效果图";generationTask.Steps[6].Status="completed";generationTask.Steps[6].OutputSummary="效果图已返回，仍需人工核对";generationTask.Status="concept-pending";}
                        StoreTask();dirty=true;Render();Status("效果图已按当前草图生成，仍需人工核对结构一致性。");
                    }
                }
            });
        }
        void SetImageZoom(double value)
        {
            imageZoom=Math.Max(0.6,Math.Min(2.4,value));
            C<Image>("ResultImage").LayoutTransform=new ScaleTransform(imageZoom,imageZoom);
            C<Button>("ZoomReset").Content=((int)Math.Round(imageZoom*100))+"%";
            Status("画布缩放："+((int)Math.Round(imageZoom*100))+"%。按住图片拖动可查看细节。");
        }
        void ShowOverview()
        {
            detailOpen=false;Render();C<ScrollViewer>("Results").ScrollToTop();
        }
        void FitImage()
        {
            var viewport=C<ScrollViewer>("ImageViewport");
            C<Image>("ResultImage").Width=Math.Max(1,viewport.ActualWidth-24);
            C<Image>("ResultImage").Height=Math.Max(1,viewport.ActualHeight-24);
        }
        void UpdateWorkspaceLayout()
        {
            bool show=detailOpen&&Current!=null;
            var column=(ColumnDefinition)window.FindName("InspectorColumn");
            column.Width=new GridLength(show?280:0);
            C<Border>("InspectorPanel").Visibility=show?Visibility.Visible:Visibility.Collapsed;
            C<Grid>("DetailView").Visibility=show?Visibility.Visible:Visibility.Collapsed;
            C<Border>("ChatBar").Visibility=show?Visibility.Visible:Visibility.Collapsed;
            C<ScrollViewer>("Results").Visibility=Current!=null&&!show?Visibility.Visible:Visibility.Collapsed;
            // Keep images large on laptop windows; the gallery scrolls as a single column.
            C<UniformGrid>("ConceptCards").Columns=project.Options.Count==1||window.ActualWidth>0&&window.ActualWidth<1280?1:3;
            C<WrapPanel>("ToolItems").MaxWidth=Math.Max(240,C<Grid>("CanvasArea").ActualWidth-24);
        }
        void Render()
        {
            syncing=true;bool has=Current!=null;
            UpdateWorkspaceLayout();
            C<Border>("Empty").Visibility=has?Visibility.Collapsed:Visibility.Visible;
            C<Button>("Revise").IsEnabled=has&&!busy;
            if(!has){C<TextBlock>("ResultTitle").Text="创作";C<TextBlock>("ResultHint").Text="";C<TextBlock>("RevisionTarget").Text="选择方案后修改";C<StackPanel>("Conversation").Children.Clear();syncing=false;return;}
            C<TextBlock>("ResultTitle").Text=detailOpen?"设计预览":"创意方向";
            C<TextBlock>("ResultHint").Text=Current.Demo?"旧版演示存档":project.ReferenceMode=="creative"&&ConceptDiversity.HasRepeatedMechanism(project.Options.Select(o=>o.Current.Concept).ToList())?"当前方向结构重复，请重新生成":project.Options.Any(o=>o.Current.Concept.EngineeringIssues!=null&&o.Current.Concept.EngineeringIssues.Count>0)?"概念方案已生成，工程待验证":detailOpen?"":project.Options.Count==1?"参考结构还原 · 选择后细化":"3 个方向 · 选择后细化";
            C<TextBlock>("Brief").Text=project.Brief;C<TextBlock>("Missing").Text=project.Missing;
            var cards=C<UniformGrid>("ConceptCards");cards.Children.Clear();
            for(int i=0;i<project.Options.Count;i++)
            {
                int selected=i;var v=project.Options[i].Current;var c=v.Concept;
                var content=new StackPanel();
                var picture=new Border{Height=336,Background=B("#FFFFFF"),CornerRadius=new CornerRadius(12)};
                string galleryImage=v.HasVerifiedEffect()?v.Pictures["整体效果"]:v.CurrentSketchData();
                if(!String.IsNullOrEmpty(galleryImage)){
                    picture.Child=new Image{Source=DecodePicture(galleryImage),Stretch=Stretch.Uniform,Margin=new Thickness(8)};
                    if(!v.HasVerifiedEffect())picture.ToolTip="先核对草图，再生成与草图一致的效果图。";
                }
                else picture.Child=new TextBlock{Text=busy?"草图生成中…":"草图待生成 · 旧效果图未经核验",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=B("#8A8A8A"),FontSize=12};
                content.Children.Add(picture);
                var caption=new DockPanel{Margin=new Thickness(0,16,0,0)};
                var number=new TextBlock{Text=(i+1).ToString("00"),Foreground=B("#8A8A8A"),FontSize=12,Margin=new Thickness(0,2,12,0)};DockPanel.SetDock(number,Dock.Left);caption.Children.Add(number);
                caption.Children.Add(new TextBlock{Text=c.Name,FontSize=14,FontWeight=FontWeights.Medium,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap});content.Children.Add(caption);
                if(!v.HasVerifiedEffect())content.Children.Add(new TextBlock{Text=String.IsNullOrEmpty(galleryImage)?"待生成草图":"草图阶段 · 效果图待核对",FontSize=11,Foreground=B("#8A8A8A"),Margin=new Thickness(28,5,0,0)});
                var button=new Button{Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,8,24),ToolTip="选择方案并查看详情"};
                System.Windows.Automation.AutomationProperties.SetName(button,"选择方案 "+(i+1)+"："+c.Name);
                button.Click+=delegate{HideAgentProgress();project.Selected=selected;detailOpen=true;view=v.HasVerifiedEffect()?"整体效果":"设计草图";imageZoom=1;C<Button>("ZoomReset").Content="100%";C<Image>("ResultImage").LayoutTransform=Transform.Identity;dirty=true;Render();};cards.Children.Add(button);
            }
            C<ComboBox>("Versions").ItemsSource=Selected.Versions.Select((v,i)=>"V"+(i+1)+" · "+v.Created).ToList();C<ComboBox>("Versions").SelectedIndex=Selected.Selected;
            C<TextBlock>("RevisionTarget").Text="正在修改：方向 "+(char)('A'+project.Selected)+" / V"+(Selected.Selected+1);
            C<TextBlock>("FeedbackHint").Text=Current.Constraints==null||Current.Constraints.PenType==null?"旧版方案需先填写产品参数。":"修改沿用当前版本参数；更改产品参数后请重新生成。";
            C<StackPanel>("Conversation").Children.Clear();
            foreach(var v in Selected.Versions.Skip(1).Reverse().Take(4))C<StackPanel>("Conversation").Children.Add(new TextBlock{Text=v.Created+"\n"+v.Instruction,FontSize=12,Margin=new Thickness(0,0,0,16),TextWrapping=TextWrapping.Wrap});
            RenderDetail();syncing=false;
        }
        void RenderDetail()
        {
            if(Current==null)return;var c=Current.Concept;
            C<Popup>("ViewPopup").IsOpen=false;C<Popup>("MorePopup").IsOpen=false;
            C<Button>("ViewMenu").Content=view;FitImage();
            C<TextBlock>("SelectedTitle").Text=c.Name;
            if(C<TextBlock>("InspectorTitle")!=null)C<TextBlock>("InspectorTitle").Text=c.Name;
            bool has=Current.Pictures.ContainsKey(view);
            PendingImage preview=null;bool hasPreview=Current.PendingImages!=null&&Current.PendingImages.TryGetValue(view,out preview)&&(!has||view=="设计草图"&&preview.VisualReferenceUsable);
            if(hasPreview&&view=="设计草图")has=false;
            C<Image>("ResultImage").Source=has?DecodePicture(Current.Pictures[view]):hasPreview?DecodePicture(preview.Data):null;C<Image>("ResultImage").Visibility=has||hasPreview?Visibility.Visible:Visibility.Collapsed;
            C<StackPanel>("ImagePlaceholder").Visibility=has||hasPreview?Visibility.Collapsed:Visibility.Visible;
            C<TextBlock>("ImageLabel").Text=view+" · 等待生成";
            C<TextBlock>("ImageHint").Text="在底部“···”菜单选择“生成此视图”。\n新视图会参考当前已有的整体效果图。";
            C<Button>("RetryImage").Content=view=="设计草图"?(has?"重新生成草图":"生成草图"):(has?"重新生成此视图":"生成此视图");
            if(view=="设计草图")C<TextBlock>("ImageHint").Text="生成所选方案的设计草图：闭合、展开与内部布局，配合结构动作箭头。";
            C<TextBlock>("ImageCaption").Text=has?(view=="设计草图"?"透明PNG设计草图 · 浅色背景仅用于预览，导出保留透明通道。尺寸与装配仍需打样核对。":"AI概念效果图 · 已提供笔长比例参考；若笔身明显变短，请不要采用该图并重新生成。结构与比例仍需核对，不作为可生产图纸。"):"没有生成图片。需求、结构描述和版本仍可保存。";
            if(view=="整体效果"&&has&&!Current.HasVerifiedEffect())C<TextBlock>("ImageCaption").Text="历史效果图缺少同版本草图校验，不能作为当前结构依据。请先生成草图，再重新生成效果图。";
            PendingImage pending;if(Current.PendingImages!=null&&Current.PendingImages.TryGetValue(view,out pending)){
                C<TextBlock>("ImageLabel").Text=pending.VisualReferenceUsable?view+" · 尺寸标注待修正":pending.NeedsCorrection?view+" · 需要修正":view+" · 等待复核";
                C<TextBlock>("ImageHint").Text=pending.Issue;
                C<Button>("RetryImage").Content=pending.VisualReferenceUsable?"修正草图标注":pending.NeedsCorrection?"按检查意见重新生成":"继续复核已有图片";
                C<TextBlock>("ImageCaption").Text=has?"保留上次已通过的图片。新图片尚未通过，保存项目可保留待处理进度。":pending.VisualReferenceUsable?"草图实体可作概念效果图参考；尺寸标注仍有误，不可作为生产图纸。":"待复核概念图：图片已经返回，但笔数、比例或结构尚未通过核对，不能作为生产依据。";
            }
            if(has&&(Current.Constraints==null||Current.Constraints.PenType==null||Current.Concept.PenLayout==null))C<TextBlock>("ImageCaption").Text="旧版图片 · 尚未使用笔类与比例参考，比例和装载关系待核验。";
            C<TextBlock>("CreativeDetails").Text=c.Idea;C<TextBlock>("StructureDetails").Text=c.Structure;C<TextBlock>("OpeningDetails").Text=c.Opening;C<TextBlock>("LayoutDetails").Text=c.Layout;C<TextBlock>("InnovationDetails").Text=c.Innovation;
            C<TextBlock>("CalculationDetails").Text=AssemblyGeometry.Describe(c,Current.Constraints);
            C<TextBlock>("RequirementDetails").Text=c.RequirementChecks==null?"旧方案没有逐条记录。":String.Join("\n\n",c.RequirementChecks.Select(r=>r.RequirementId+" · "+r.Requirement+"\n"+r.Implementation));
            C<TextBlock>("ManufacturingDetails").Text=DesignMethod.ManufacturingSummary(c.Manufacturing);
            C<TextBlock>("EvidenceDetails").Text=c.Understanding==null?"旧方案未记录资料推导。":String.Join("\n\n",c.Understanding.Evidence.Select(e=>e.SourceId+"\n原文："+e.Quote+"\n应用："+e.Application));
            if(c.Understanding!=null){C<Expander>("UnderstandingSettings").Visibility=Visibility.Visible;C<TextBlock>("UnderstandingPreview").Text=DesignMethod.Summary(c.Understanding);}
            var layout=c.PenLayout;var dimensions=c.PackageDimensions;
            string box=layout!=null?layout.BoxWidth+" × "+layout.BoxDepth+" × "+layout.BoxHeight+" mm（宽 × 深 × 高）":dimensions==null?"旧版方案未记录尺寸":dimensions.Longest+" × "+dimensions.Middle+" × "+dimensions.Shortest+" mm（长边 × 中边 × 短边）";
            C<TextBlock>("EngineeringDetails").Text="方案包装尺寸\n"+box+"\n\n材料与工艺\n"+c.Materials+"\n\n生产难度\n"+c.Difficulty+"\n\n成本判断\n"+c.CostNote+"\n\n风险与验证\n"+c.Risks+"\n"+c.Checks+"\n\n概念阶段，装载与生产仍需打样验证。";
            foreach(var pair in new[]{new[]{"ViewOverall","整体效果"},new[]{"ViewOpen","打开状态"},new[]{"ViewLayout","内部布局"},new[]{"ViewSketch","设计草图"}})
            {C<Button>(pair[0]).Background=B(view==pair[1]?"#F1F1EF":"#FFFFFF");}
        }
        static BitmapImage DecodePicture(string data)
        {
            return DecodePictureBytes(Convert.FromBase64String(data),1600);
        }
        static BitmapImage DecodePictureBytes(byte[] bytes,int maxEdge)
        {
            int width,height;
            using(var input=new MemoryStream(bytes))using(var original=System.Drawing.Image.FromStream(input,false,false))
            {width=original.Width;height=original.Height;}
            if(width<=0||height<=0||(long)width*height>40000000)throw new InvalidOperationException("图片尺寸超出客户端可处理范围（最多4000万像素）。");
            double scale=Math.Min(1,maxEdge/(double)Math.Max(width,height));
            using(var input=new MemoryStream(bytes))
            {
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=input;
                if(scale<1){image.DecodePixelWidth=Math.Max(1,(int)Math.Round(width*scale));image.DecodePixelHeight=Math.Max(1,(int)Math.Round(height*scale));}
                image.EndInit();image.Freeze();return image;
            }
        }
        static string NormalizePicture(string data){ValidatePicture(data);return data;}
        static void ValidatePicture(string data){var b=DecodePicture(data);if(b.PixelWidth<1||b.PixelHeight<1)throw new InvalidOperationException("图片无法读取。");}
        static string EncodePicture(Stream stream)
        {
            BitmapImage img;using(var input=new MemoryStream()){stream.CopyTo(input);img=DecodePictureBytes(input.ToArray(),1400);}
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(img));using(var output=new MemoryStream()){encoder.Save(output);return Convert.ToBase64String(output.ToArray());}
        }
#if QA
        static void CheckPictureDecodeBounds()
        {
            foreach(var size in new[]{new[]{100,1000},new[]{100,4000},new[]{20,30},new[]{4000,100}})
            using(var original=new System.Drawing.Bitmap(size[0],size[1]))using(var png=new MemoryStream())
            {
                original.Save(png,System.Drawing.Imaging.ImageFormat.Png);string data=Convert.ToBase64String(png.ToArray());
                var preview=DecodePicture(data);
                if(preview.PixelWidth>size[0]||preview.PixelHeight>size[1]||preview.PixelWidth>1600||preview.PixelHeight>1600)throw new Exception("Preview decoding must never upscale or exceed either 1600px edge");
                png.Position=0;string reference=EncodePicture(png);
                using(var encoded=new MemoryStream(Convert.FromBase64String(reference)))using(var image=System.Drawing.Image.FromStream(encoded))
                    if(image.Width>size[0]||image.Height>size[1]||image.Width>1400||image.Height>1400)throw new Exception("Reference encoding must never upscale or exceed either 1400px edge");
            }
        }
#endif
        void AddPictures()
        {
            var dialog=new OpenFileDialog{Filter="产品或参考图片|*.jpg;*.jpeg;*.png;*.bmp",Multiselect=true};if(dialog.ShowDialog(window)!=true)return;
            if(project.Attachments.Count+dialog.FileNames.Length>6)throw new InvalidOperationException("最多添加6张产品或参考图片。");
            var added=new List<Picture>();
            foreach(var f in dialog.FileNames){if(new FileInfo(f).Length>15000000)throw new InvalidOperationException("单张图片请小于15MB。");using(var s=File.OpenRead(f))added.Add(new Picture{Name=System.IO.Path.GetFileName(f),Data=EncodePicture(s),Id="ref:"+Guid.NewGuid().ToString("N"),ReferenceType=project.ReferenceMode=="restore"?"structure":"creative"});}
            ReferenceDesign.EnsurePictures(added);
            foreach(var p in added)ReferenceDiagnostics.Write("reference-upload",new{p.Id,p.Name,p.ContentHash,p.ReferenceType,State="saved"});
            project.Attachments.AddRange(added);dirty=true;RenderAttachments();Status("图片已加入当前项目。生成时会发送给你配置的模型服务。");
        }
        void RenderAttachments()
        {
            var panel=C<WrapPanel>("AttachmentList");panel.Children.Clear();C<TextBlock>("PictureCount").Text=project.Attachments.Count+" / 6";
            foreach(var p in project.Attachments)
            {
                var tile=new StackPanel{Width=86,Margin=new Thickness(0,0,8,8)};tile.Children.Add(new Image{Source=DecodePicture(p.Data),Height=64,Stretch=Stretch.Uniform});
                var kind=new ComboBox{Height=30,Margin=new Thickness(0,4,0,4),ToolTip="Reference 用途"};
                foreach(var pair in new[]{new[]{"structure","结构"},new[]{"layout","布局"},new[]{"appearance","外观"},new[]{"creative","创意"}})kind.Items.Add(new ComboBoxItem{Content=pair[1],Tag=pair[0]});
                kind.SelectedItem=kind.Items.Cast<ComboBoxItem>().First(i=>i.Tag.ToString()==(p.ReferenceType??"creative"));
                kind.SelectionChanged+=delegate{if(!syncing){p.ReferenceType=((ComboBoxItem)kind.SelectedItem).Tag.ToString();dirty=true;}};tile.Children.Add(kind);
                var remove=new Button{Content="移除",Padding=new Thickness(5,3,5,3),FontSize=11,ToolTip=p.Name};remove.Click+=delegate{project.Attachments.Remove(p);dirty=true;RenderAttachments();};tile.Children.Add(remove);panel.Children.Add(tile);
            }
        }
        void SelectReferenceMode()
        {
            C<ComboBox>("ReferenceMode").SelectedItem=C<ComboBox>("ReferenceMode").Items.Cast<ComboBoxItem>().First(i=>i.Tag.ToString()==(project.ReferenceMode??"creative"));
        }
        bool CanLeave()
        {
            if(!dirty)return true;var answer=MessageBox.Show(window,"当前设计尚未保存，是否先保存？","保存项目",MessageBoxButton.YesNoCancel);
            if(answer==MessageBoxResult.Cancel)return false;return answer==MessageBoxResult.No||Save();
        }
        bool Save()
        {
            var dialog=new SaveFileDialog{Filter="纸盒包装创意项目|*.paperstudio",FileName="包装创意项目.paperstudio"};if(dialog.ShowDialog(window)!=true)return false;
            try{CaptureDraft();ProjectFile.Save(dialog.FileName,project);dirty=false;Status("项目已保存：包括需求、参考图片、生成图片和全部版本。");return true;}catch(Exception e){Alert("保存失败："+e.Message);return false;}
        }
        void CaptureDraft(){project.DraftRequest=C<TextBox>("Request").Text;project.DraftDetails=C<TextBox>("Details").Text;project.DraftPackaging=ReadPackaging();}
        void Open()
        {
            if(!CanLeave())return;var dialog=new OpenFileDialog{Filter="纸盒包装创意项目|*.paperstudio"};if(dialog.ShowDialog(window)!=true)return;
            var next=ProjectFile.Open(dialog.FileName);
            foreach(var p in next.Attachments)ValidatePicture(p.Data);foreach(var option in next.Options)foreach(var version in option.Versions){foreach(var picture in version.Pictures.Values)ValidatePicture(picture);foreach(var candidate in version.PendingImages.Values)ValidatePicture(candidate.Data);if(!String.IsNullOrEmpty(version.ParentPicture))ValidatePicture(version.ParentPicture);}
            project=next;detailOpen=false;syncing=true;C<TextBox>("Request").Text=project.DraftRequest??project.Request;C<TextBox>("Details").Text=project.DraftDetails??project.Details;RestorePackaging(project.DraftPackaging);SelectReferenceMode();syncing=false;
            C<TextBox>("Feedback").Clear();C<TextBlock>("SizeHint").Text=ReferenceSearch.SizeHint(knowledge,C<TextBox>("Request").Text);view="整体效果";RenderAttachments();Render();dirty=false;Status("已恢复项目与版本；继续生成需要配置API。");
        }
        void Settings()
        {
            CreateSettingsWindow().ShowDialog();
        }
        Window CreateSettingsWindow()
        {
            var dialog=new Window{Title="模型与图片 API 设置",Owner=window,Width=660,Height=720,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#FAFAF9"),Foreground=B("#1C1C1C"),FontFamily=window.FontFamily,FontSize=13};
            dialog.Height=Math.Min(dialog.Height,SystemParameters.WorkArea.Height-30);
            var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,Background=dialog.Background,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            stack.Children.Add(new TextBlock{Text="连接真实创意与图像模型",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
            stack.Children.Add(new TextBlock{Text="支持 OpenAI 兼容接口：Chat Completions，以及 Images generations / edits。服务商需支持图片输入、JSON方案输出和base64图片返回。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
            Func<string,string,TextBox> field=(label,value)=>{stack.Children.Add(new TextBlock{Text=label});var box=new TextBox{Text=value,Padding=new Thickness(8),Margin=new Thickness(0,5,0,12)};stack.Children.Add(box);return box;};
            Func<string,string,ComboBox> modelField=(label,value)=>{stack.Children.Add(new TextBlock{Text=label});var box=new ComboBox{IsEditable=true,Text=value,Padding=new Thickness(8),Margin=new Thickness(0,5,0,12),MaxDropDownHeight=220};stack.Children.Add(box);return box;};
            stack.Children.Add(new TextBlock{Text="基础地址请复制接入文档中的 Base URL，不是浏览器里的 /dashboard 或 /keys。模型ID请复制模型列表中的标识，不是密钥备注名或账户名称。",TextWrapping=TextWrapping.Wrap,Foreground=B("#80633B"),Margin=new Thickness(0,0,0,16)});
            var tb=field("文字API基础地址（按服务商文档填写）",settings.TextBase);var tm=modelField("文字模型ID（读取列表后选择，需支持视觉输入）",settings.TextModel);
            stack.Children.Add(new TextBlock{Text="文字API密钥（当前 Windows 用户加密保存）"});var tk=new PasswordBox{Password=settings.TextKey,Padding=new Thickness(8),Margin=new Thickness(0,5,0,16)};stack.Children.Add(tk);
            var textModels=new Button{Content="读取可用文字模型",Margin=new Thickness(0,0,0,16),Padding=new Thickness(10)};stack.Children.Add(textModels);
            var streamText=new CheckBox{Content="流式接收文字（建议开启；服务商不支持时可关闭）",IsChecked=settings.StreamText,Margin=new Thickness(0,0,0,16)};stack.Children.Add(streamText);
            var ib=field("图片API基础地址（按服务商文档填写）",settings.ImageBase);var im=modelField("图片模型（已预置 GPT Image 2，也可手动填写）",settings.ImageModel);
            im.ItemsSource=ImageModelChoices.Display(new[]{settings.ImageModel});im.Text=settings.ImageModel;
            stack.Children.Add(new TextBlock{Text="gpt-image-2 是图片模型预设，账号权限需实际测试。即使服务商模型列表未显示它，也可直接测试；6 astra、codex-auto-review 不能用于此处。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),Foreground=B("#6A745F")});
            stack.Children.Add(new TextBlock{Text="图片API密钥（当前 Windows 用户加密保存）"});var ik=new PasswordBox{Password=settings.ImageKey,Padding=new Thickness(8),Margin=new Thickness(0,5,0,16)};stack.Children.Add(ik);
            var imageModels=new Button{Content="补充服务商图片模型候选",Margin=new Thickness(0,0,0,12),Padding=new Thickness(10)};stack.Children.Add(imageModels);
            var testImage=new Button{Content="测试图片接入（生成 1 张测试图）",Margin=new Thickness(0,0,0,8),Padding=new Thickness(10)};stack.Children.Add(testImage);
            var cancelTest=new Button{Content="取消图片测试",Visibility=Visibility.Collapsed,Padding=new Thickness(8),Margin=new Thickness(0,0,0,8)};stack.Children.Add(cancelTest);
            stack.Children.Add(new TextBlock{Text="测试只生成一张纯白纸盒图片，不发送项目资料；会调用图片API并可能计费。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),Foreground=B("#6A745F")});
            var preview=new Image{Height=160,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,12)};stack.Children.Add(preview);
            var lookupStatus=new TextBlock{Text="图片列表只展示按名称筛选的候选，不再混入文字模型。读取列表不会出图，点击测试才会验证实际图片权限。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16),Foreground=B("#6A745F")};stack.Children.Add(lookupStatus);
            stack.Children.Add(new TextBlock{Text="生成会将需求、相关资料及图片发送给此处配置的服务商，并可能计费。密钥以 Windows 当前用户身份加密保存，不写入项目文件、配置文件或 EXE。",TextWrapping=TextWrapping.Wrap,Foreground=B("#6A745F"),Margin=new Thickness(0,0,0,16)});
            var save=new Button{Content="保存接口设置",Padding=new Thickness(16,10,16,10)};stack.Children.Add(save);
            CancellationTokenSource imageTestCancellation=null;
            cancelTest.Click+=delegate{if(imageTestCancellation!=null)imageTestCancellation.Cancel();};
            dialog.Closed+=delegate{if(imageTestCancellation!=null)imageTestCancellation.Cancel();};
            Action<bool> setRequestBusy=value=>{foreach(var control in new Control[]{textModels,imageModels,testImage,save,tb,tm,tk,ib,im,ik,streamText})control.IsEnabled=!value;};
            Action<Button,TextBox,PasswordBox,ComboBox,bool> bindModels=(button,address,key,choices,isImage)=>{
                button.Click+=async delegate{
                    setRequestBusy(true);
                    lookupStatus.Text="正在读取服务商模型列表…";
                    try{
                        var config=new ApiSettings{TextBase=address.Text.Trim(),ImageBase=address.Text.Trim(),TextKey=key.Password.Trim(),ImageKey=key.Password.Trim()};
                        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25)))using(var service=new DesignService(config)){
                            var ids=await service.GetModelIds(isImage,timeout.Token);
                            if(!dialog.IsVisible)return;string current=choices.Text;choices.ItemsSource=isImage?ImageModelChoices.Display(ids):ids;choices.Text=current;
                            lookupStatus.Text=isImage?(ids.Count==0?"服务商列表未提供可识别的图片模型。已保留 gpt-image-2 预设，请用图片测试确认权限；没有换成文字模型。":"服务商列表中发现 "+ids.Count+" 个图片候选。实际权限请用图片测试确认。"):("已读取 "+ids.Count+" 个文字模型，请展开对应下拉框选择。");
                        }
                    }catch(OperationCanceledException){if(dialog.IsVisible)lookupStatus.Text="读取模型列表超时，请检查网络后重试。";}
                    catch(Exception e){if(dialog.IsVisible)lookupStatus.Text=e.Message;}
                    finally{setRequestBusy(false);}
                };
            };
            bindModels(textModels,tb,tk,tm,false);bindModels(imageModels,ib,ik,im,true);
            testImage.Click+=async delegate{
                setRequestBusy(true);preview.Visibility=Visibility.Collapsed;lookupStatus.Text="正在测试图片模型："+im.Text+"。只请求一张测试图…";cancelTest.Visibility=Visibility.Visible;
                imageTestCancellation=new CancellationTokenSource();
                try{
                    var config=new ApiSettings{ImageBase=ib.Text.Trim(),ImageModel=im.Text.Trim(),ImageKey=ik.Password.Trim()};
                    using(var service=new DesignService(config)){
                        string result=await service.TestImage(imageTestCancellation.Token);
                        if(!dialog.IsVisible)return;preview.Source=DecodePicture(result);preview.Visibility=Visibility.Visible;
                        lookupStatus.Text="图片测试成功："+config.ImageModel+" 已返回可读取的图片。点击保存接口设置，然后打开已有项目生成方案效果图。";
                    }
                }catch(OperationCanceledException){if(dialog.IsVisible)lookupStatus.Text=imageTestCancellation.IsCancellationRequested?"已取消图片测试。":"图片测试超时，尚未确认成功。";}
                catch(Exception e){if(dialog.IsVisible)lookupStatus.Text="图片测试未通过："+e.Message;}
                finally{imageTestCancellation.Dispose();imageTestCancellation=null;cancelTest.Visibility=Visibility.Collapsed;setRequestBusy(false);}
            };
            save.Click+=delegate{
                try{var next=new ApiSettings{TextBase=tb.Text.Trim(),TextModel=tm.Text.Trim(),ImageBase=ib.Text.Trim(),ImageModel=im.Text.Trim(),TextKey=tk.Password.Trim(),ImageKey=ik.Password.Trim(),StreamText=streamText.IsChecked==true};
                    if(next.TextBase!="")ApiSettings.Endpoint(next.TextBase,"chat/completions");if(next.ImageBase!="")ApiSettings.Endpoint(next.ImageBase,"images/generations");
                    ImageModelChoices.Validate(next.ImageModel);
                    ApiSecretStore.Save(next);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConfigPath));File.WriteAllText(ConfigPath,Json.Write(next),Encoding.UTF8);settings=next;dialog.Close();CheckStartup(null);
                }catch(Exception e){MessageBox.Show(dialog,e.Message,"设置提示");}};
            return dialog;
        }
        string DimensionDetails()
        {
            if(Current==null||Current.Constraints==null)return "";
            var c=Current.Constraints;var d=Current.Concept.PackageDimensions;
            var layout=Current.Concept.PenLayout;
            string placement=layout==null?"":("\n摆放："+(layout.Orientation=="vertical"?"竖直":"横向")+"；实际宽 × 深 × 高："+layout.BoxWidth+" × "+layout.BoxDepth+" × "+layout.BoxHeight+" mm\n完整笔长占对应盒边 "+(c.MarkerLength/layout.PenAxisLength()).ToString("P1")+"；出图时附等比参考。");
            string estimate=c.MarkerDiameter>0?"\n逐组计算："+AssemblyGeometry.Describe(Current.Concept,c):"\n程序估算装载起点：未填写笔直径，截面尺寸待确认。";
            return "产品与包装尺寸\n"+c.PenTypeName+" "+c.MarkerCount+"支，完整笔长 "+c.MarkerLength+" mm，单支直径 "+(c.MarkerDiameter>0?c.MarkerDiameter.ToString():"未填写")+" mm\n"+c.Summary()+estimate+"\n本方案产品包装外径："+d.Longest+" × "+d.Middle+" × "+d.Shortest+" mm（最长/次长/最短）"+placement+"\n数值在所选上限内；内托、笔径与实际容纳能力仍需打样验证。\n\n";
        }
        string Explanation()
        {
            if(Current==null)return "设计需求："+C<TextBox>("Request").Text+"\n补充："+C<TextBox>("Details").Text;
            var c=Current.Concept;
            return "纸盒包装创意方案\n"+(Current.Demo?"本地演示方案，非AI生成":"AI概念方案，未做工程或实物验证")+"\n\n"+DimensionDetails()+"用户需求\n"+project.Request+"\n\n需求理解\n"+project.Brief+"\n\n待确认条件\n"+project.Missing+"\n\n"+c.Name+"\n设计理念\n"+c.Idea+"\n结构\n"+c.Structure+"\n开合方式\n"+c.Opening+"\n内部布局\n"+c.Layout+"\n材料工艺\n"+c.Materials+"\n创新点\n"+c.Innovation+"\n生产难度\n"+c.Difficulty+"\n成本\n"+c.CostNote+"\n风险\n"+c.Risks+"\n验证事项\n"+c.Checks+"\n\n制造推导\n"+DesignMethod.ManufacturingSummary(c.Manufacturing)+"\n\n当前版本 V"+(Selected.Selected+1)+"\n修改要求："+Current.Instruction;
        }
        void Export()
        {
            var menu=new ContextMenu();
            Action<string,Action> add=(title,action)=>{var item=new MenuItem{Header=title};item.Click+=delegate{try{action();}catch(Exception e){Alert(e.Message);}};menu.Items.Add(item);};
            add("导出当前方案说明 TXT",delegate{SaveText("方案说明.txt","文本文件|*.txt",Explanation());});
            add("导出结构化项目 JSON",delegate{CaptureDraft();SaveText("包装项目.json","JSON文件|*.json",Json.Write(project));});
            add("Packbox · 模板展开图",delegate{PackboxExport.CreateDialog(window,Current==null?null:Current.Concept).ShowDialog();});
            add("导出打样沟通资料包 ZIP",delegate{
                if(Current==null||Current.Concept.Assembly==null){Alert("请先生成包含逐组计算数据的方案。");return;}
                var d=new SaveFileDialog{Filter="打样沟通资料包|*.zip",FileName="包装打样沟通资料.zip"};if(d.ShowDialog(window)!=true)return;
                ExportSamplePackage(d.FileName);Status("已导出打样资料包：尺寸、材料来源、方案图片和尺寸参考图。");
            });
            add("导出当前视图 PNG",delegate{
                if(Current==null||!Current.Pictures.ContainsKey(view)){Alert("当前视图尚未生成图片。");return;}
                var d=new SaveFileDialog{Filter="PNG图片|*.png",FileName="包装概念_"+view+".png"};if(d.ShowDialog(window)!=true)return;
                ExportPicture(d.FileName,Current.Pictures[view]);Status("已导出当前概念图（原始分辨率）。");
            });
            add("打印方案 / 保存为 PDF",Print);
            menu.PlacementTarget=C<Button>("Export");menu.IsOpen=true;
        }
        void SaveText(string name,string filter,string text){var d=new SaveFileDialog{FileName=name,Filter=filter};if(d.ShowDialog(window)==true){File.WriteAllText(d.FileName,text,Encoding.UTF8);Status("已导出："+System.IO.Path.GetFileName(d.FileName));}}
        static void ExportPicture(string path,string data)
        {
            byte[] bytes=Convert.FromBase64String(data);
            using(var input=new MemoryStream(bytes))using(var original=System.Drawing.Image.FromStream(input)){
                if(original.RawFormat.Guid==System.Drawing.Imaging.ImageFormat.Png.Guid)File.WriteAllBytes(path,bytes);
                else original.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        void ExportSamplePackage(string path)
        {
            var snapshot=Json.Copy(Current);
            SamplePackage.Write(path,snapshot,Explanation(),AssemblyGuide.CreatePages(snapshot.Constraints,snapshot.Concept));
        }
        void Print()
        {
            var print=new PrintDialog();if(print.ShowDialog()!=true)return;
            var doc=new FlowDocument{FontFamily=window.FontFamily,FontSize=11,ColumnWidth=1000,PagePadding=new Thickness(35),PageWidth=print.PrintableAreaWidth,PageHeight=print.PrintableAreaHeight};
            foreach(var section in Explanation().Split('\n'))doc.Blocks.Add(new Paragraph(new Run(section)){Margin=new Thickness(0,0,0,6)});
            if(Current!=null)foreach(var p in Current.Pictures)doc.Blocks.Add(new BlockUIContainer(new Image{Source=DecodePicture(p.Value),MaxHeight=380,Stretch=Stretch.Uniform}));
            print.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,"包装创意方案");Status("已提交打印。选择 Microsoft Print to PDF 可保存PDF。");
        }
#if QA
        async Task RenderCheck(string output)
        {
            Directory.CreateDirectory(output);
            GenerationTaskStore.PathOverride=System.IO.Path.Combine(output,"qa-active-generation.json");
            var restoredSources=Json.Copy(knowledge.Take(3).ToList());
            if(TextReferences(restoredSources).Count!=knowledge.Count)throw new Exception("Restarted source snapshots duplicated stable records");
            restoredSources[0].Text+="changed";
            bool sourceMismatch=false;try{TextReferences(restoredSources);}catch(InvalidOperationException){sourceMismatch=true;}
            if(!sourceMismatch)throw new Exception("Changed source text was silently deduplicated");
            CheckPictureDecodeBounds();
            SketchQualityChecks.Run(System.IO.Path.Combine(output,"sketch-geometry"));
            settings=new ApiSettings{TextBase="https://qa.invalid/v1",ImageBase="https://qa.invalid/v1",TextModel="qa-vision",ImageModel="gpt-image-2"};
            C<TextBox>("Request").Text="QA no API configuration";
            bool rejected=false;try{await Generate();}catch(InvalidOperationException){rejected=true;}
            if(!rejected||project.Options.Count!=0||DesktopQaHandler.Calls!=0)throw new Exception("Required marker input gate failed");
            C<TextBox>("MarkerCount").Text="48";C<TextBox>("MarkerLength").Text="150.5";C<TextBox>("MarkerDiameter").Text="12.7";
            if(C<TextBlock>("PackagingRecommendationHint")!=null)throw new Exception("Packaging recommendation preview should be removed");
            if(C<ComboBox>("SizeTier").Items.Count!=2||C<ComboBox>("SizeTier").Items.Cast<PackagingTier>().Any(t=>t.Id!="small-standard"&&t.Id!="large-standard"))throw new Exception("Only standard product size tiers should be shown");
            foreach(var id in new[]{"alcohol","pencil","acrylic","fineliner"}){C<ComboBox>("PenType").SelectedValue=id;if(ReadPackaging().PenType!=id)throw new Exception("Pen type selection failed");}
            C<TextBox>("TrayHeight").Text="30";if(ReadPackaging().TrayHeight!="30")throw new Exception("Optional tray height input was not read");C<TextBox>("TrayHeight").Clear();
            if(!C<TextBlock>("PenPlacementHint").Text.Contains("实物确认"))throw new Exception("Fine pen placement must remain unverified");
            C<ComboBox>("PenType").SelectedValue="acrylic";
            if(!C<TextBlock>("PenPlacementHint").Text.Contains("只能竖直"))throw new Exception("Acrylic placement hint missing");
            rejected=false;try{await Generate();}catch(InvalidOperationException){rejected=true;}
            if(!rejected||DesktopQaHandler.Calls!=0)throw new Exception("API gate failed");
            foreach(var pair in new[]{new[]{"0","427.2"},new[]{"1","417.2"},new[]{"2","407.2"}}){C<ComboBox>("AllowanceCm").SelectedIndex=Int32.Parse(pair[0]);if(!C<TextBlock>("PackagingLimitHint").Text.Contains(pair[1]))throw new Exception("Allowance live preview failed");}
            C<ComboBox>("SizeTier").SelectedValue="small-standard";
            rejected=false;try{await Generate();}catch(InvalidOperationException e){rejected=e.Message.Contains("预留");}
            if(!rejected||DesktopQaHandler.Calls!=0)throw new Exception("Impossible tier not blocked before API");
            C<ComboBox>("SizeTier").SelectedValue="large-standard";
            var guideConcept=DesktopQaHandler.ConceptFixture(0);WorkflowFixtures.Shell(guideConcept);AssemblyGeometry.Calculate(guideConcept,ReadPackaging().Build());var guide=ProportionGuide.Create(ReadPackaging().Build(),guideConcept);
            File.WriteAllBytes(System.IO.Path.Combine(output,"assembly-guide.png"),Convert.FromBase64String(AssemblyGuide.Create(ReadPackaging().Build(),guideConcept).Data));
            DesktopQaHandler.ExpectedGuide=guide.Data;File.WriteAllBytes(System.IO.Path.Combine(output,"proportion-guide.png"),Convert.FromBase64String(guide.Data));
            settings.TextKey="QA-NOT-A-REAL-KEY";settings.ImageKey="QA-NOT-A-REAL-KEY";
            window.Width=1380;window.Height=920;window.Show();
            Action<string> snapshot=name=>{window.UpdateLayout();var surface=(FrameworkElement)window.Content;var bmp=new RenderTargetBitmap((int)surface.ActualWidth,(int)surface.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(System.IO.Path.Combine(output,name+".png")))encoder.Save(f);};
            C<TextBox>("Request").Clear();snapshot("empty");C<TextBox>("Request").Text="设计48支丙烯笔纸盒，万圣节主题，打开后能变成桌面笔架，尽量少用塑料。";
            runner.CheckGame();SetBusy(true,"QA 加载状态，不调用真实 API");window.UpdateLayout();runner.PreviewAttack(9);snapshot("loading-game");runner.PreviewAttack(8);snapshot("loading-surfboard");runner.PreviewShield();snapshot("loading-shield");SetBusy(false,"就绪");
            await Generate();
            if(project.Options.Count!=3||!Current.Concept.Name.Contains("QA")||busy)throw new Exception("API generation state failed");
            if(Enumerable.Range(1,5).Any(i=>!C<TextBlock>("AgentStep"+i).Text.StartsWith("✓"))||generationTask.Steps[5].Status!="pending"||generationTask.Steps[6].Status!="pending")throw new Exception("Initial generation must stop after the structure-sketch stage");
            if(!C<TextBlock>("EngineeringDetails").Text.Contains("资料来源"))throw new Exception("Material source provenance missing from rendered details");
            if(C<Border>("InspectorPanel").Visibility!=Visibility.Collapsed||C<Grid>("DetailView").Visibility!=Visibility.Collapsed||C<UniformGrid>("ConceptCards").Children.Count!=3)throw new Exception("Gallery must precede detail selection");
            if(Current.Pictures.Count!=1||!Current.Pictures.ContainsKey("设计草图")||DesktopQaHandler.SketchCalls!=3||DesktopQaHandler.AuditCalls!=3||DesktopQaHandler.UnderstandingCalls!=1||DesktopQaHandler.ReviewCalls!=1)throw new Exception("Initial generation must produce audited sketches only");
            for(int i=0;i<3;i++){
                project.Selected=i;detailOpen=true;view="整体效果";Render();await RetryImage();
                if(!project.Options[i].Current.HasVerifiedEffect())throw new Exception("Effect image was not bound to the selected sketch");
            }
            project.Selected=0;detailOpen=false;view="设计草图";Render();
            if(DesktopQaHandler.AuditCalls!=6)throw new Exception("Effect images were not audited after sketch confirmation");
            snapshot("halloween");
            ((Button)C<UniformGrid>("ConceptCards").Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            if(C<Border>("AgentProgress").Visibility!=Visibility.Collapsed)throw new Exception("Selected design should reclaim progress-panel space");
            if(project.Selected!=1||C<Border>("InspectorPanel").Visibility!=Visibility.Visible||C<Grid>("DetailView").Visibility!=Visibility.Visible)throw new Exception("Selecting a card did not open its details");
            C<Button>("ZoomIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(imageZoom<=1)throw new Exception("Zoom button failed");
            C<Button>("ZoomReset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(imageZoom!=1)throw new Exception("Zoom reset failed");
            C<Button>("Compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(C<Border>("InspectorPanel").Visibility!=Visibility.Collapsed||C<ScrollViewer>("Results").Visibility!=Visibility.Visible)throw new Exception("Compare did not restore gallery");
            ((Button)C<UniformGrid>("ConceptCards").Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));snapshot("selected");
            C<Button>("ViewMenu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!C<Popup>("ViewPopup").IsOpen)throw new Exception("View menu failed");
            C<Button>("ViewOpen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(view!="打开状态"||C<Popup>("ViewPopup").IsOpen)throw new Exception("View menu selection failed");snapshot("missing-view");
            C<Button>("More").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!C<Popup>("MorePopup").IsOpen)throw new Exception("More menu failed");
            C<Button>("ViewOverall").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            C<TextBox>("MarkerCount").Text="96";C<ComboBox>("AllowanceCm").SelectedIndex=0;C<ComboBox>("PenType").SelectedValue="pencil";
            C<TextBox>("Feedback").Text="保留结构，只改成绿色";await Revise();
            if(Selected.Versions.Count!=2 || Selected.Selected!=1 || busy||Current.Constraints.MarkerCount!=48||Current.Constraints.AllowanceMm!=50||Current.Constraints.PenType!="acrylic")throw new Exception("Revision must retain selected version constraints, not edited draft fields");
            C<Button>("ViewSketch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(view!="设计草图"||(string)C<Button>("RetryImage").Content!="重新生成草图")throw new Exception("Sketch view selection failed");
            if(!Current.Pictures.ContainsKey("设计草图")||DesktopQaHandler.SketchCalls!=4)throw new Exception("Revised design did not regenerate its sketch first");SketchOutput.RequireTransparent(Current.Pictures["设计草图"]);snapshot("sketch");
            string samplePath=System.IO.Path.Combine(output,"sample-package.zip");
            string beforeExport=Json.Write(Current);ExportSamplePackage(samplePath);
            if(Json.Write(Current)!=beforeExport)throw new Exception("Sample export mutated current version");
            string exportedImage=System.IO.Path.Combine(output,"exported-sketch.png");ExportPicture(exportedImage,Current.Pictures["设计草图"]);
            if(Convert.ToBase64String(File.ReadAllBytes(exportedImage))!=Current.Pictures["设计草图"])throw new Exception("PNG export changed original resolution or alpha bytes");
            using(var sampleStream=File.OpenRead(samplePath))using(var sample=new System.IO.Compression.ZipArchive(sampleStream,System.IO.Compression.ZipArchiveMode.Read)){
                if(sample.GetEntry("units.csv")==null||sample.GetEntry("parts.csv")==null||sample.GetEntry("requirements.csv")==null||sample.GetEntry("guides/guide-02.png")==null)throw new Exception("Sample export omitted dimensions, requirements or per-component guide");
                using(var imageStream=sample.GetEntry("images/view-04.png").Open())using(var imageBytes=new MemoryStream()){imageStream.CopyTo(imageBytes);if(Convert.ToBase64String(imageBytes.ToArray())!=Current.Pictures["设计草图"])throw new Exception("Sample export changed original transparent pixels");}
            }
            snapshot("revision");C<ComboBox>("Versions").SelectedIndex=0;
            if(Selected.Selected!=0)throw new Exception("Version selection failed");
            C<ComboBox>("Versions").SelectedIndex=1;CaptureDraft();
            string testProject=System.IO.Path.Combine(output,"qa.paperstudio");ProjectFile.Save(testProject,project);project=ProjectFile.Open(testProject);Render();
            syncing=true;RestorePackaging(project.DraftPackaging);syncing=false;
            if(C<TextBox>("MarkerCount").Text!="96"||ReadPackaging().Diameter!="12.7"||ReadPackaging().AllowanceCm!="3"||ReadPackaging().PenType!="pencil"||Current.Constraints.PenType!="acrylic"||Current.Constraints.MarkerCount!=48||Current.Concept.PackageDimensions.Longest!=184.5m||!Current.Pictures.ContainsKey("设计草图"))throw new Exception("Draft input and generated dimensions did not round trip independently");
            if(Selected.Versions.Count!=2||project.Sources.Count==0)throw new Exception("Project restoration failed");
            var pencilSources=References("300个彩铅的纸盒，仙人掌，尺寸不能超规格");
            var backgroundPictures=ImageReferences(Current,pencilSources);
            if(backgroundPictures.Count>15 || !backgroundPictures.Any(p=>p.Name.Contains("程序按毫米")) || !backgroundPictures.Any(p=>p.Name.StartsWith("计算结构总览")))throw new Exception("Stored style/scale references missing from image pipeline");
            if(!backgroundPictures.Any(p=>p.Name.Contains("整体效果表达参考")))throw new Exception("Requested overall presentation example was not attached");
            var finePen=Json.Copy(Current);finePen.Constraints.PenType="fineliner";
            if(!ImageReferences(finePen,pencilSources).Any(p=>p.Name.Contains("Wai Momi")))throw new Exception("Official fine pen appearance reference missing");
            var alcoholPen=Json.Copy(Current);alcoholPen.Constraints.PenType="alcohol";
            if(!ImageReferences(alcoholPen,pencilSources).Any(p=>p.Name.Contains("Honolulu 320")))throw new Exception("Official alcohol marker cap reference missing");
            foreach(var p in backgroundPictures)ValidatePicture(p.Data);
            var savedAttachments=project.Attachments;
            try{
                project.Attachments=Enumerable.Range(1,6).Select(i=>new Picture{Name="QA reference "+i,Data=Current.Pictures["设计草图"]}).ToList();
                var complex=Json.Copy(Current);complex.Concept.Assembly.Parts=Enumerable.Range(1,40).Select(i=>new AssemblyPart{Id="QA部件"+i,Role="尺寸检查",Width=20+i,Depth=40+i,Height=1}).ToList();WorkflowFixtures.Shell(complex.Concept);
                foreach(string requestedView in new[]{"整体效果","设计草图"}){
                    var refs=ImageReferences(complex,pencilSources,requestedView);
                    if(refs.Count+1>16||project.Attachments.Any(p=>!refs.Contains(p)))throw new Exception("Image request must preserve all six user references within the sixteen-image budget");
                }
            }finally{project.Attachments=savedAttachments;}
            File.WriteAllText(System.IO.Path.Combine(output,"reference-check.txt"),"Stored records: "+knowledge.Count+"; matched cases: "+pencilSources.Count(ReferenceSearch.IsCase)+"; decoded reference images: "+backgroundPictures.Count+"\n"+String.Join("\n",pencilSources.Select(r=>r.Source)),Encoding.UTF8);
            window.Width=1080;window.Height=720;window.UpdateLayout();snapshot("compact");
            AssertWorkspaceBounds();
            C<Button>("Compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();snapshot("compact-gallery");
            if(C<UniformGrid>("ConceptCards").Columns!=1)throw new Exception("Compact gallery should keep large images in one column");
            ((Button)C<UniformGrid>("ConceptCards").Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Width=1366;window.Height=768;window.UpdateLayout();AssertWorkspaceBounds();snapshot("laptop");
            window.UpdateLayout();await window.Dispatcher.InvokeAsync(delegate{},DispatcherPriority.ContextIdle);snapshot("parameters");
            var penBox=C<ComboBox>("PenType");penBox.IsDropDownOpen=true;window.UpdateLayout();if(!penBox.IsDropDownOpen)throw new Exception("Custom combo could not open");penBox.IsDropDownOpen=false;

            window.Width=1536;window.Height=864;window.UpdateLayout();AssertWorkspaceBounds();snapshot("details");
            var oldDimensions=Current.Concept.PackageDimensions;var oldLayout=Current.Concept.PenLayout;
            Current.Concept.PackageDimensions=null;Current.Concept.PenLayout=null;RenderDetail();
            if(!C<TextBlock>("EngineeringDetails").Text.Contains("旧版方案未记录尺寸"))throw new Exception("Legacy project details failed");
            Current.Concept.PackageDimensions=oldDimensions;Current.Concept.PenLayout=oldLayout;RenderDetail();
            var options=CreateSettingsWindow();options.Show();options.UpdateLayout();
            await options.Dispatcher.InvokeAsync(delegate{},DispatcherPriority.ContextIdle);
            var modelBox=((StackPanel)((ScrollViewer)options.Content).Content).Children.OfType<ComboBox>().First();
            var editor=(TextBox)modelBox.Template.FindName("PART_EditableTextBox",modelBox);editor.Text="qa-custom-model";
            if(modelBox.Text!="qa-custom-model")throw new Exception("Editable API model combo no longer accepts custom IDs");
            var settingsSurface=(FrameworkElement)options.Content;var settingsBitmap=new RenderTargetBitmap((int)settingsSurface.ActualWidth,(int)settingsSurface.ActualHeight,96,96,PixelFormats.Pbgra32);settingsBitmap.Render(settingsSurface);
            var settingsEncoder=new PngBitmapEncoder();settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));using(var f=File.Create(System.IO.Path.Combine(output,"settings.png")))settingsEncoder.Save(f);options.Close();
            if(C<StackPanel>("ProductSettings").Visibility!=Visibility.Visible||C<Expander>("ReferenceSettings").Header.ToString()!="reference")throw new Exception("Required parameters or reference label failed");
            project=new DesignProject();detailOpen=false;syncing=true;
            C<TextBox>("MarkerCount").Text="48";C<TextBox>("MarkerLength").Text="150.5";C<TextBox>("MarkerDiameter").Text="12.7";
            C<ComboBox>("PenType").SelectedValue="acrylic";C<ComboBox>("SizeTier").SelectedValue="large-standard";C<ComboBox>("AllowanceCm").SelectedIndex=2;syncing=false;Render();
            int priorUnderstanding=DesktopQaHandler.UnderstandingCalls,priorImages=DesktopQaHandler.ImageCalls;
            DesktopQaHandler.FailDraftOnce=true;bool draftInterrupted=false;
            try{await Generate();}catch(ApiTimeoutException){draftInterrupted=true;}
            if(!draftInterrupted||generationTask.Steps.Take(4).Any(s=>s.Status!="completed")||generationTask.Steps[4].Status!="failed"||project.Options.Count!=0||generationTask.Planning==null||generationTask.Planning.Understanding==null)throw new Exception("Draft timeout did not preserve completed client stages");
            generationTask.UnknownTool=null;StoreTask();
            DesktopQaHandler.FailImageAt=DesktopQaHandler.ImageCalls+2;bool imageInterrupted=false;
            try{await ContinueGeneration();}catch(ApiTimeoutException){imageInterrupted=true;}
            if(!imageInterrupted||project.Options.Count!=3||!project.Options[0].Current.Pictures.ContainsKey("设计草图")||project.Options[1].Current.Pictures.ContainsKey("设计草图")||generationTask.Steps[5].Status!="failed"||generationTask.Steps[6].Status!="pending"||DesktopQaHandler.UnderstandingCalls!=priorUnderstanding+1)throw new Exception("Client resumed draft but did not preserve first sketch on second-sketch failure");
            var savedTask=GenerationTaskStore.Load();
            if(savedTask.TaskId!=generationTask.TaskId||!savedTask.Project.Options[0].Current.Pictures.ContainsKey("设计草图"))throw new Exception("Interrupted sketch was not persisted for restart");
            File.Copy(GenerationTaskStore.PathOverride,System.IO.Path.Combine(output,"restart-task.json"),true);
            project=savedTask.Project;generationTask=savedTask;generationTask.UnknownTool=null;StoreTask();DesktopQaHandler.FailImageAt=0;
            await ContinueGeneration();
            if(generationTask.Status!="concept-pending"||project.Options.Any(o=>!o.Current.Pictures.ContainsKey("设计草图")||o.Current.Pictures.ContainsKey("整体效果"))||DesktopQaHandler.ImageCalls!=priorImages+4||DesktopQaHandler.UnderstandingCalls!=priorUnderstanding+1)throw new Exception("Client restart resume repeated finished work or generated effects before sketch selection");
            project.Options[0].Current.Concept.EngineeringIssues.Add("QA工艺依据待验证");generationTask.Status="concept-pending";Render();
            if(!C<TextBlock>("ResultHint").Text.Contains("工程待验证"))throw new Exception("Concept-only completion status is missing from the desktop canvas");
            snapshot("resumed-gallery");
            var previewVersion=project.Options[0].Current;
            project.Selected=0;detailOpen=true;view="整体效果";Render();await RetryImage();
            string acceptedImage=previewVersion.Pictures["整体效果"];
            previewVersion.Pictures.Remove("整体效果");previewVersion.PendingImages["整体效果"]=new PendingImage{Data=acceptedImage,Issue="QA数量或比例待复核",NeedsCorrection=true,Attempts=1};
            detailOpen=true;project.Selected=0;Render();
            if(C<Image>("ResultImage").Source==null||!C<TextBlock>("ImageCaption").Text.Contains("待复核概念图"))throw new Exception("Unreviewed returned image is invisible or mislabeled");
            snapshot("pending-preview");
            previewVersion.PendingImages.Remove("整体效果");previewVersion.Pictures["整体效果"]=acceptedImage;detailOpen=false;Render();
            int callsBeforeLocalRecovery=DesktopQaHandler.Calls;
            var recoveryLimits=new PackagingInput{Count="72",Length="150",Diameter="15",PenType="alcohol",Tier="large-standard",AllowanceCm="5"}.Build();
            var recoveryRequest="儿童使用，便于携带，打开后全部笔可见";
            project=new DesignProject{Request=recoveryRequest,Constraints=recoveryLimits};
            generationTask=GenerationTask.Create("qa-recovery",project);
            generationTask.RetrievedSources=Json.Copy(knowledge);
            generationTask.Planning=new PlanningCheckpoint{Round=2,Understanding=WorkflowFixtures.Understanding(AssemblyGeometry.Requirements(recoveryRequest,"Ohuhu风格",null),knowledge),Issues=new List<PlanningIssue>{new PlanningIssue{Category="实际结构几何冲突",Resolved=false}}};
            if(!TryDeliverSavedConcepts(recoveryRequest,"Ohuhu风格",recoveryLimits)||project.Options.Count!=3||DesktopQaHandler.Calls!=callsBeforeLocalRecovery||generationTask.Steps[4].Status!="completed")throw new Exception("Saved structural failure did not resume into three local concepts without another API call");
            var controlledReferences=ImageReferences(project.Options[0].Current,generationTask.RetrievedSources,"整体效果");
            if(controlledReferences.Any(p=>p.Name.StartsWith("计算结构总览")||p.Name.StartsWith("程序按毫米等比")||p.Name.StartsWith("用户指定的整体效果表达参考")||p.Name.Contains("风格参考总览")||p.Name.Contains("官网产品外观研究")))throw new Exception("Selected sketch effect still contains competing structure or presentation boards");
            File.WriteAllText(System.IO.Path.Combine(output,"passed.txt"),"PASS: calculated assembly geometry; 276 stored source analyses with focused original records in planning; cached brand/sketch descriptions and original image references during rendering; streamed responses; vision acceptance gate; runner jump/collision/restart/timer cleanup; permanent product inputs; split collapsible details; hidden inspector until card selection; overview/detail/compare navigation; view and more menus; zoom/reset; compact gallery; 1080x720, 1366x768, 1380x920 and 1536x864 window bounds; custom combo; API-only inputs; sketch generation; saved versions and project round trip. Images are existing reference fixtures, not generated output. No live API call.");
            dirty=false;window.Close();Application.Current.Shutdown();
        }
        void AssertWorkspaceBounds()
        {
            var surface=(FrameworkElement)window.Content;
            foreach(var name in new[]{"Generate","Request","Feedback","Revise","FloatingTools","InspectorPanel","SelectedTitle","Versions"})
            {
                var control=C<FrameworkElement>(name);var point=control.TranslatePoint(new Point(0,0),surface);
                if(point.X<0||point.Y<0||point.X+control.ActualWidth>surface.ActualWidth+1||point.Y+control.ActualHeight>surface.ActualHeight+1)throw new Exception("Control outside compact window: "+name);
            }
        }
#endif
    }
}
