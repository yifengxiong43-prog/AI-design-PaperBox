using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PaperStudio
{
    internal static class ImageRecoveryChecks
    {
        const string View="打开状态";
        sealed class Handler:HttpMessageHandler
        {
            public Func<HttpRequestMessage,string,HttpResponseMessage> Reply;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {token.ThrowIfCancellationRequested();return Reply(request,request.Content==null?"":await request.Content.ReadAsStringAsync());}
        }
        static void Check(bool condition,string message){if(!condition)throw new Exception("Image recovery: "+message);}
        static HttpResponseMessage Response(object content)
        {return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Json.Write(content))};}
        static HttpResponseMessage Review(string content)
        {return Response(new{choices=new[]{new{message=new{content=content}}}});}
        static string Pixels(Color color)
        {using(var bitmap=new Bitmap(20,30))using(var graphics=Graphics.FromImage(bitmap))using(var stream=new MemoryStream()){graphics.Clear(color);bitmap.Save(stream,ImageFormat.Png);return Convert.ToBase64String(stream.ToArray());}}
        static string SketchPixels()
        {using(var bitmap=new Bitmap(100,100))using(var graphics=Graphics.FromImage(bitmap))using(var pen=new Pen(Color.Black,2))using(var stream=new MemoryStream()){
            graphics.Clear(Color.Transparent);for(int i=0;i<10;i++)graphics.DrawLine(pen,10,12+i*7,88,12+i*7);
            bitmap.Save(stream,ImageFormat.Png);return Convert.ToBase64String(stream.ToArray());
        }}
        static ApiSettings Settings()
        {return new ApiSettings{TextBase="https://text.example/v1",TextModel="vision",TextKey="TEST",ImageBase="https://image.example/v1",ImageModel="image",ImageKey="TEST"};}
        static string PassingReview(DesignVersion version)
        {return Json.Write(WorkflowFixtures.ImageReview(version.Concept));}
        static DesignVersion Version()
        {
            var constraints=new PackagingInput{Count="12",Length="150",Diameter="15",Tier="large-standard",AllowanceCm="4",PenType="acrylic"}.Build();
            var concept=new Concept{Name="可展开单排纸盒",Idea="一排展示",Structure="翻盖纸盒",Opening="翻开前盖",Layout="12支一排",Materials="白卡纸",Innovation="低位定位带",Difficulty="需白样",CostNote="待报价",Risks="确认稳定性",Checks="按实际重量测试",ImagePrompt="Paper package with twelve full-length pens",
                Assembly=new AssemblyPlan{LengthClearance=24,Assumptions="1mm壁厚与4mm整排余量为设计假设",Units=new List<DisplayUnit>{new DisplayUnit{Id="A",Count=12,Columns=12,Motion="翻开前盖",Closed=new Placement(),Open=new Placement()}},Parts=new List<AssemblyPart>{new AssemblyPart{Id="盖",Role="翻盖",Width=188,Depth=23,Height=1}}}};
            AssemblyGeometry.Calculate(concept,constraints);
            return new DesignVersion{Concept=concept,Constraints=constraints,Requirements="不要磁铁；所有笔身一次可见"};
        }
        static async Task<Exception> Failure(DesignService service,DesignVersion version,CancellationToken token)
        {
            try{await service.GenerateImage(version,View,new List<Picture>(),token);}
            catch(Exception error){return error;}
            throw new Exception("Image recovery: expected the incomplete image review to fail");
        }
        static DesignVersion RoundTrip(DesignVersion version)
        {
            string path=Path.Combine("tmp","image-recovery-"+Guid.NewGuid().ToString("N")+".paperstudio");
            try{ProjectFile.Save(path,new DesignProject{Request="十二支丙烯笔",Options=new List<DesignOption>{new DesignOption{Versions=new List<DesignVersion>{version}}}});return ProjectFile.Open(path).Options[0].Current;}
            finally{if(File.Exists(path))File.Delete(path);}
        }
        public static async Task Run()
        {
            string accepted=Pixels(Color.White),candidate=Pixels(Color.Blue),replacement=Pixels(Color.Green);
            var version=Version();version.Pictures[View]=accepted;
            int images=0,audits=0;
            using(var service=new DesignService(Settings(),new Handler{Reply=(request,body)=>{
                if(request.RequestUri.AbsolutePath.Contains("/images/")){images++;return Response(new{data=new[]{new{b64_json=candidate}}});}
                audits++;throw new HttpRequestException("simulated interrupted review");
            }}))Check(await Failure(service,version,CancellationToken.None) is ApiConnectionException,"audit connection failures retain their transport classification");
            Check(images==1&&audits==1&&version.PendingImages[View].Data==candidate&&!version.PendingImages[View].NeedsCorrection,"one returned image is retained after failed audit without scheduling another image call");
            Check(version.Pictures[View]==accepted,"failed replacement cannot overwrite the previously accepted picture");
            version=RoundTrip(version);
            Check(version.PendingImages[View].Data==candidate&&version.Pictures[View]==accepted&&!version.PendingImages[View].NeedsCorrection,"pending and accepted pictures survive project save/open independently");
            using(var service=new DesignService(Settings(),new Handler{Reply=(request,body)=>{
                Check(!request.RequestUri.AbsolutePath.Contains("/images/"),"resuming after reopen must never charge for a second image");audits++;
                Check(version.Pictures[View]==accepted&&version.PendingImages[View].Data==candidate,"candidate is not accepted before successful review");
                return Review(PassingReview(version));
            }})){
                string result=await service.GenerateImage(version,View,new List<Picture>(),CancellationToken.None);
                Check(result==candidate&&version.PendingImages.Count==0&&version.Pictures[View]==accepted,"successful review returns original pixels; controller alone commits acceptance");
                version.Pictures[View]=result;
            }
            Check(images==1&&audits==2&&version.Pictures[View]==candidate,"resume completes with exactly one paid image request across save/open");

            version=Version();version.Pictures[View]=accepted;images=0;audits=0;
            using(var service=new DesignService(Settings(),new Handler{Reply=(request,body)=>{
                if(request.RequestUri.AbsolutePath.Contains("/images/")){images++;if(images==2)Check(body.Contains("only six full-length pens are visible"),"manual redraw must include prior review's concrete issues");return Response(new{data=new[]{new{b64_json=images==1?candidate:replacement}}});}
                audits++;var review=WorkflowFixtures.ImageReview(version.Concept);
                if(audits==1){review.Pass=false;review.ObservedVisiblePens=6;review.RequirementsMet=false;review.Units[0].VisiblePens=6;review.Issues="only six full-length pens are visible";}
                return Review(Json.Write(review));
            }})){
                Check(await Failure(service,version,CancellationToken.None) is ImageQualityException,"complete negative review is a confirmed quality rejection");
                Check(images==1&&audits==1&&version.PendingImages[View].NeedsCorrection&&version.Pictures[View]==accepted,"quality failure never automatically retries or replaces accepted pixels");
                string result=await service.GenerateImage(version,View,new List<Picture>(),CancellationToken.None);
                Check(result==replacement&&images==2&&audits==2&&version.PendingImages.Count==0&&version.Pictures[View]==accepted,"explicit manual correction creates and reviews exactly one replacement");
            }
            foreach(string mode in new[]{"cancel","invalid-json","null-review","empty-review","partial-review","string-boolean","fractional-count"}){
                version=Version();version.Pictures[View]=accepted;images=0;audits=0;
                using(var cancel=new CancellationTokenSource())using(var service=new DesignService(Settings(),new Handler{Reply=(request,body)=>{
                    if(request.RequestUri.AbsolutePath.Contains("/images/")){images++;return Response(new{data=new[]{new{b64_json=candidate}}});}
                    audits++;
                    if(audits>1)return Review(PassingReview(version));
                    if(mode=="cancel"){cancel.Cancel();throw new OperationCanceledException(cancel.Token);}
                    if(mode=="string-boolean")return Review(PassingReview(version).Replace("\"Pass\":true","\"Pass\":\"true\""));
                    if(mode=="fractional-count")return Review(PassingReview(version).Replace("\"ObservedVisiblePens\":12","\"ObservedVisiblePens\":12.5"));
                    return Review(mode=="invalid-json"?"not-json":mode=="null-review"?"null":mode=="empty-review"?"{}":"{\"Pass\":false,\"Issues\":\"incomplete reply\"}");
                }})){
                    var error=await Failure(service,version,cancel.Token);
                    Check(mode=="cancel"?error is OperationCanceledException:error is InvalidOperationException,"incomplete review fails without returning accepted pixels: "+mode);
                    Check(version.PendingImages.ContainsKey(View)&&version.PendingImages[View].Data==candidate&&!version.PendingImages[View].NeedsCorrection&&version.Pictures[View]==accepted,"cancellation or invalid review retains candidate for audit only: "+mode);
                    Check(await service.GenerateImage(version,View,new List<Picture>(),CancellationToken.None)==candidate&&images==1&&audits==2,"retry after incomplete review must not generate another image: "+mode);
                }
            }
            version=Version();string usableSketch=SketchPixels();images=0;audits=0;
            using(var service=new DesignService(Settings(),new Handler{Reply=(request,body)=>{
                if(request.RequestUri.AbsolutePath.Contains("/images/")){
                    images++;
                    if(images==2){
                        var multipart=request.Content as MultipartFormDataContent;
                        Check(multipart!=null,"effect image must use the saved visual sketch as an edit reference");
                        bool found=false;foreach(var part in multipart)if(part.Headers.ContentType!=null&&part.Headers.ContentType.MediaType=="image/png"){
                            found=true;Check(Convert.ToBase64String(part.ReadAsByteArrayAsync().Result)==usableSketch,"the unapproved sketch must be the first visual input");break;
                        }
                        Check(found&&body.Contains("尺寸文字/编号可能错误")&&body.Contains("paperboard package only")&&body.Contains("standing upright"),"effect prompt must prioritize the sketch, paperboard package and standing pen posture");
                    }
                    return Response(new{data=new[]{new{b64_json=images==1?usableSketch:accepted}}});
                }
                audits++;var review=WorkflowFixtures.ImageReview(version.Concept);
                if(audits==1){review.Pass=false;review.DimensionsPresent=false;review.Units[0].DimensionsMatch=false;review.Issues="outer width label is wrong";}
                return Review(Json.Write(review));
            }})){
                Check(await FailureSketch(service,version) is ImageQualityException,"wrong sketch dimensions remain a failed strict review");
                Check(images==1&&version.PendingImages["设计草图"].VisualReferenceUsable&&version.PendingImages["设计草图"].NeedsCorrection,"usable physical sketch is retained with its dimension error");
                version=RoundTrip(version);
                Check(version.PendingImages["设计草图"].VisualReferenceUsable,"visual-reference status survives reopening the project");
                string effect=await service.GenerateImage(version,"整体效果",new List<Picture>(),CancellationToken.None);
                Check(effect==accepted&&images==2&&audits==2&&version.PendingImages.ContainsKey("设计草图"),"effect can follow the saved sketch without redrawing or accepting incorrect dimensions");
            }
            Console.WriteLine("PASS: image audit recovery across save/open, single billed image call, manual quality correction, incomplete-review/cancellation retention and accepted-picture preservation.");
        }
        static async Task<Exception> FailureSketch(DesignService service,DesignVersion version)
        {try{await service.GenerateImage(version,"设计草图",new List<Picture>(),CancellationToken.None);}catch(Exception error){return error;}throw new Exception("Image recovery: wrong sketch dimensions were accepted");}
    }
}
