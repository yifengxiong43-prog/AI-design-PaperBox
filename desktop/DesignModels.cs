using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace PaperStudio
{
    public static class Json
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 100000000, RecursionLimit = 100 }; }
        public static string Write(object value) { return Serializer().Serialize(value); }
        public static T Read<T>(string text) { return Serializer().Deserialize<T>(text); }
        public static T Copy<T>(T value) { return Read<T>(Write(value)); }
    }
    public class SourceBrief { public string Name, Size, Quantity, Cost, Market, Style, Needs; }
    public class ReferenceImageConstraint
    {
        public string ImageName, ImageId, ContentHash, ReferenceType;
        public string BoxType, Opening, Layout, VisualStyle, Constraints;
        public string TrayHierarchy, Linkages, PivotPoints, ClosedOpenMapping, Unknowns;
    }
    public class ReferenceDesignSpecification
    {
        public string BodyShape, TrayCount, TrayHierarchy, OpeningMechanism, Linkages, PivotPoints, ProductLayout, ClosedOpenMapping;
        public List<string> LockedFeatures=new List<string>();
        public List<string> Unknowns=new List<string>();
        public List<string> ImageIds=new List<string>();
    }
    public class SourceRecord
    {
        public string Id, StableId, ChunkId, DocumentId, SourceContentHash, IndexVersion, Title, Group, Source, Text;
        public SourceBrief Brief;
        public List<string> Images = new List<string>();
    }
    public class Picture
    {
        public string Name, Data, Id, ContentHash, ReferenceType="creative";
    }
    public class Concept
    {
        public string DesignOrigin;
        public string Name, Idea, Structure, Opening, Layout, Materials, Innovation, Difficulty, CostNote, Risks, Checks, ImagePrompt;
        // Reusable strategy metadata. These describe the role and mechanism selected for this project;
        // they are not product-specific templates or fixed box names.
        public string ConceptType, StructuralFamily, OpeningMechanism, InternalLayout, DesktopUse, Portability, CreativityLevel, FeasibilityStatus, SketchReferenceId;
        public List<string> ReferenceSources = new List<string>();
        public PackageDimensions PackageDimensions;
        public PenLayout PenLayout;
        public AssemblyPlan Assembly;
        public DesignUnderstanding Understanding;
        public ManufacturingPlan Manufacturing;
        public ConceptVerdict PlanReview;
        public List<RequirementCheck> RequirementChecks;
        public List<string> EngineeringIssues = new List<string>();
        public void Validate()
        {
            foreach (var s in new[] { Name, Idea, Structure, Opening, Layout, Materials, Innovation, Difficulty, CostNote, Risks, Checks, ImagePrompt })
                if (String.IsNullOrWhiteSpace(s) || s.Length > 12000) throw new InvalidOperationException("模型返回的方案字段缺失或过长，请重试。");
        }
    }
    public class DesignResponse
    {
        public string Brief, Missing;
        public List<Concept> Concepts;
        public void Validate(int count)
        {
            if (String.IsNullOrWhiteSpace(Brief) || String.IsNullOrWhiteSpace(Missing) || Concepts == null || Concepts.Count != count)
                throw new InvalidOperationException("模型返回的数据不完整，需要" + count + "套完整方案。");
            foreach (var c in Concepts) { if (c == null) throw new InvalidOperationException("方案为空。"); c.Validate(); }
            if (Concepts.Select(c=>c.Name.Trim()).Distinct().Count()!=count) throw new InvalidOperationException("方案名称重复，请重新生成差异化方向。");
        }
    }
    public class DesignVersion
    {
        public string Created = DateTime.Now.ToString("MM-dd HH:mm"), Instruction = "初始方案";
        public string SchemeId, SketchId, StructureDataVersion;
        public Concept Concept;
        public bool Demo;
        public string Theme = "自然", Accent = "#477C65";
        public Dictionary<string,string> Pictures = new Dictionary<string,string>();
        public Dictionary<string,PendingImage> PendingImages = new Dictionary<string,PendingImage>();
        public string ParentPicture;
        public string EffectSketchHash;
        public string EffectSchemeId, EffectSketchId, EffectStructureDataVersion;
        public string Requirements;
        public PackagingConstraints Constraints;
        public string CurrentSketchData()
        {
            PendingImage pending;
            if(PendingImages!=null&&PendingImages.TryGetValue("设计草图",out pending)&&pending.VisualReferenceUsable)return pending.Data;
            string data=null;
            if(Pictures!=null)Pictures.TryGetValue("设计草图",out data);
            return data;
        }
        public string CurrentSketchHash()
        {
            string data=CurrentSketchData();
            if(String.IsNullOrEmpty(data))return null;
            using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(Convert.FromBase64String(data)));
        }
        public string CurrentSketchId()
        {
            var hash=CurrentSketchHash();return String.IsNullOrEmpty(hash)?null:"sketch:"+hash;
        }
        public string CurrentStructureDataVersion()
        {
            var payload=Json.Write(new {Assembly=Concept==null?null:Concept.Assembly,Constraints=Constraints,StructuralFamily=Concept==null?null:Concept.StructuralFamily,OpeningMechanism=Concept==null?null:Concept.OpeningMechanism,InternalLayout=Concept==null?null:Concept.InternalLayout});
            using(var hash=SHA256.Create())return "structure:"+Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
        public void EnsureBinding(string fallbackSchemeId=null)
        {
            if(String.IsNullOrWhiteSpace(SchemeId)){
                if(!String.IsNullOrWhiteSpace(fallbackSchemeId))SchemeId=fallbackSchemeId;
                else {
                    var seed=Concept==null?"empty":Concept.Name??"concept";using(var hash=SHA256.Create()){var bytes=hash.ComputeHash(Encoding.UTF8.GetBytes(seed));SchemeId="scheme:legacy:"+BitConverter.ToString(bytes).Replace("-","").Substring(0,12);}
                }
            }
            StructureDataVersion=CurrentStructureDataVersion();
            var sketch=CurrentSketchId();if(!String.IsNullOrEmpty(sketch))SketchId=sketch;
        }
        public bool HasVerifiedEffect()
        {
            if(Pictures==null||!Pictures.ContainsKey("整体效果")||String.IsNullOrEmpty(EffectSketchHash)||EffectSketchHash!=CurrentSketchHash())return false;
            if(!String.IsNullOrEmpty(EffectSchemeId)&&EffectSchemeId!=SchemeId)return false;
            if(!String.IsNullOrEmpty(EffectSketchId)&&EffectSketchId!=CurrentSketchId())return false;
            if(!String.IsNullOrEmpty(EffectStructureDataVersion)&&EffectStructureDataVersion!=CurrentStructureDataVersion())return false;
            return true;
        }
    }
    public class PendingImage
    {
        public string Data,Issue,SketchHash,ReferenceSignature,SchemeId,SketchId,StructureDataVersion;
        public List<string> ReferenceIds;
        public int Attempts;
        public bool NeedsCorrection;
        public bool VisualReferenceUsable;
    }
    public class DesignOption
    {
        public string SchemeId;
        public List<DesignVersion> Versions = new List<DesignVersion>();
        public int Selected;
        [ScriptIgnore] public DesignVersion Current { get { return Versions[Selected]; } }
    }
    public class DesignProject
    {
        public int Format = 2;
        public string Request = "", Details = "", Brief = "", Missing = "";
        public string DraftRequest, DraftDetails;
        public PackagingInput DraftPackaging;
        public PackagingConstraints Constraints;
        public string ReferenceMode="creative";
        public List<Picture> Attachments = new List<Picture>();
        public List<string> Sources = new List<string>();
        public List<DesignOption> Options = new List<DesignOption>();
        public int Selected;
        public void Validate()
        {
            if (Format != 2) throw new InvalidOperationException("请打开新版 .paperstudio 项目文件。");
            if(Constraints!=null)Constraints.Validate();
            if (Request == null || Details == null || Options == null || Attachments == null || Sources == null || Options.Count > 3 || Attachments.Count > 6)
                throw new InvalidOperationException("项目结构无效。");
            if (Options.Count > 0 && (Selected < 0 || Selected >= Options.Count)) throw new InvalidOperationException("项目方案索引无效。");
            foreach(var picture in Attachments)
                if(picture==null || String.IsNullOrWhiteSpace(picture.Data))throw new InvalidOperationException("项目附件无效。");
            if(!new[]{"creative","restore","variants"}.Contains(ReferenceMode??"creative"))throw new InvalidOperationException("Reference 使用模式无效。");
            ReferenceDesign.EnsurePictures(Attachments);
            var usedSchemeIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int optionIndex=0;optionIndex<Options.Count;optionIndex++)
            {
                var o=Options[optionIndex];
                if (o == null || o.Versions == null || o.Versions.Count == 0 || o.Selected < 0 || o.Selected >= o.Versions.Count)
                    throw new InvalidOperationException("版本数据无效。");
                if(String.IsNullOrWhiteSpace(o.SchemeId))o.SchemeId="scheme-"+(char)('A'+optionIndex);
                if(usedSchemeIds.Contains(o.SchemeId)){
                    int duplicate=2;string baseId=o.SchemeId;while(usedSchemeIds.Contains(baseId+"-"+duplicate))duplicate++;o.SchemeId=baseId+"-"+duplicate;
                }
                usedSchemeIds.Add(o.SchemeId);
                foreach (var v in o.Versions)
                {
                    if (v == null || v.Concept == null || v.Pictures == null || !Regex.IsMatch(v.Accent ?? "", "^#[0-9a-fA-F]{6}$")) throw new InvalidOperationException("版本内容无效。");
                    v.Concept.Validate();
                    if(v.PendingImages==null)v.PendingImages=new Dictionary<string,PendingImage>();
                    if(v.Constraints!=null){v.Constraints.Validate();AssemblyGeometry.Calculate(v.Concept,v.Constraints);v.Constraints.ValidateLayout(v.Concept.PenLayout,v.Concept.PackageDimensions,v.Concept.Assembly!=null);}
                    v.SchemeId=o.SchemeId;v.EnsureBinding(o.SchemeId);
                    foreach(var pending in v.PendingImages.Values){
                        if(String.IsNullOrWhiteSpace(pending.SchemeId)||pending.SchemeId.StartsWith("scheme:legacy:",StringComparison.Ordinal))pending.SchemeId=o.SchemeId;
                        if(String.IsNullOrWhiteSpace(pending.StructureDataVersion))pending.StructureDataVersion=v.CurrentStructureDataVersion();
                        if(String.IsNullOrWhiteSpace(pending.SketchId)&&pending.Data!=null&&v.CurrentSketchId()!=null&&pending.SchemeId==o.SchemeId)pending.SketchId=v.CurrentSketchId();
                    }
                    if(String.IsNullOrWhiteSpace(v.EffectSchemeId)||v.EffectSchemeId.StartsWith("scheme:legacy:",StringComparison.Ordinal))v.EffectSchemeId=o.SchemeId;
                    foreach(var pair in v.Pictures) if(!new[]{"整体效果","打开状态","内部布局","设计草图"}.Contains(pair.Key))throw new InvalidOperationException("图片视图无效。");
                    foreach(var pair in v.PendingImages)if(!new[]{"整体效果","打开状态","内部布局","设计草图"}.Contains(pair.Key)||pair.Value==null||String.IsNullOrEmpty(pair.Value.Data)||pair.Value.Data.Length>40000000)throw new InvalidOperationException("待复核图片内容无效。");
                }
            }
        }
    }
    public static class ProjectFile
    {
        public static void Save(string path,DesignProject project)
        {
            project.Validate();string content=Json.Write(project);
            if(Encoding.UTF8.GetByteCount(content)>90000000)throw new InvalidOperationException("项目超过90MB，请减少附件或另存较少版本。");
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,content,Encoding.UTF8);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public static DesignProject Open(string path)
        {
            if(new FileInfo(path).Length>90000000)throw new InvalidOperationException("项目超过90MB，无法打开。");
            var project=Json.Read<DesignProject>(File.ReadAllText(path));
            if(project==null)throw new InvalidOperationException("项目内容为空。");project.Validate();return project;
        }
    }
    public static class ImageModelChoices
    {
        public const string Default="gpt-image-2";
        public static bool IsImageCandidate(string id)
        {string value=(id??"").ToLowerInvariant();return value.Contains("image")||value.StartsWith("dall-e")||value.StartsWith("flux")||value.Contains("stable-diffusion");}
        public static void Validate(string id)
        {
            if(!IsImageCandidate(id)&&Regex.IsMatch(id??"",@"^(codex[-_]|gpt-[0-9]|o[134](-|$))",RegexOptions.IgnoreCase))
                throw new InvalidOperationException("当前图片栏填的是文字/代码模型，不能用于图片接口。请使用 gpt-image-2 或服务商指定的图片模型ID；文字模型不需要更改。");
        }
        public static List<string> Display(IEnumerable<string> reported)
        {return new[]{Default}.Concat(reported.Where(IsImageCandidate)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
    }
    public class ApiSettings
    {
        public string TextBase = "", TextModel = "", ImageBase = "", ImageModel = ImageModelChoices.Default;
        public bool StreamText = true;
        [ScriptIgnore] public string TextKey = "";
        [ScriptIgnore] public string ImageKey = "";
        public static Uri Endpoint(string root, string path)
        {
            Uri uri;
            if (!Uri.TryCreate((root??"").Trim().TrimEnd('/'), UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("请填写 HTTPS API 基础地址，例如 https://服务商域名/v1。");
            if(uri.AbsolutePath.Split('/').Any(segment=>new[]{"dashboard","keys","login","console"}.Contains(segment.ToLowerInvariant())))
                throw new InvalidOperationException("这里填入的是账户管理或登录页面。请改填服务商接入文档中的 API Base URL，不能填 /dashboard 或 /keys 页面地址。");
            if(uri.AbsolutePath.EndsWith("/chat/completions")||uri.AbsolutePath.EndsWith("/images/generations")||uri.AbsolutePath.EndsWith("/images/edits"))
                throw new InvalidOperationException("请填写 API 基础地址，不要包含 /chat/completions 或 /images/generations 等完整接口路径。");
            return new Uri(uri.AbsoluteUri.TrimEnd('/')+"/"+path);
        }
        public void Validate(bool image)
        {
            Endpoint(image?ImageBase:TextBase, image?"images/generations":"chat/completions");
            if (String.IsNullOrWhiteSpace(image?ImageModel:TextModel) || String.IsNullOrWhiteSpace(image?ImageKey:TextKey))
                throw new InvalidOperationException(image?"请先填写图片模型与图片API密钥。":"请先填写文字模型与文字API密钥。");
            if(image)ImageModelChoices.Validate(ImageModel);
        }
    }
    public static class ReferenceSearch
    {
        public static List<SourceRecord> Find(List<SourceRecord> records, string request)
        {
            var words = new[] { "丙烯", "酒精", "马克笔", "彩铅", "铅笔", "画笔", "儿童", "三角", "后压", "棉芯", "圣诞", "万圣", "礼盒", "纸盒", "彩盒", "抽屉", "翻盖", "展示", "收纳", "提手", "纸托", "瓦楞", "环保", "成本", "跌落", "磁吸", "折叠", "笔架" };
            var qty=Regex.Match(request, @"(\d+)\s*(支|个|色)");
            bool penRequest=new[]{"笔","彩铅"}.Any(request.Contains);
            var ranked=records.Where(r=>r.Group!="测试与项目")
                .Select(r=>new { Row=r, Score=words.Where(w=>request.Contains(w)).Sum(w=>(r.Title.Contains(w)?6:0)+(r.Text.Contains(w)?1:0))
                    +(penRequest&&IsCase(r)&&(r.Title+r.Text+r.Source).Contains("笔")?3:0)
                    +(qty.Success&&IsCase(r)&&Regex.IsMatch(r.Text,@"(?<!\d)"+qty.Groups[1].Value+@"\s*(支|个|色)")?4:0) })
                .Where(x=>x.Score>0).OrderByDescending(x=>x.Score).Select(x=>x.Row).ToList();
            return ranked.Where(r=>r.Group!="材料与工艺" && r.Group!="历史问题").Take(6)
                .Concat(ranked.Where(r=>r.Group=="材料与工艺").Concat(records.Where(r=>r.Group=="材料与工艺"&&(r.Text.Contains("白卡")||r.Text.Contains("瓦楞")))).Distinct().Take(3))
                .Concat(ranked.Where(r=>r.Group=="历史问题").Concat(records.Where(r=>r.Group=="历史问题"&&(r.Title.Contains("纸箱")||r.Title.Contains("防震")))).Distinct().Take(2))
                .Concat(records.Where(r=>r.Title.Contains("合规与测试"))).Distinct().ToList();
        }
        public static bool IsCase(SourceRecord record)
        {return record.Group=="产品案例"||record.Group=="市场参考"||record.Group=="历史包装"||record.Group=="案例分析"||record.Group=="网络案例";}
        public static List<string> PictureIds(List<SourceRecord> sources)
        {
            var cases=sources.Where(r=>IsCase(r)&&r.Images.Count>0).ToList();
            return cases.Select(r=>r.Images[0]).Concat(cases.SelectMany(r=>r.Images)).Distinct().Take(2).ToList();
        }
        public static string ContextNote(string request,List<SourceRecord> sources)
        {
            if((request.Contains("彩铅")||request.Contains("铅笔"))&&!sources.Any(r=>IsCase(r)&&(r.Title+r.Text).Contains("铅")))
                return "未检索到明确的同款彩铅/铅笔文字记录；笔具历史案例仅借鉴包装结构、使用方式和材料，不得把马克笔尺寸、数量、价格当作本次彩铅的已确认参数。";
            return "历史案例仅为参考，匹配不代表同款。以用户当前产品、主题和约束为准。";
        }
        public static string SizeHint(List<SourceRecord> records, string request)
        {
            var products=Find(records,request).Where(r=>r.Brief!=null && !String.IsNullOrWhiteSpace(r.Brief.Size)).Take(2).ToList();
            if(products.Count==0)return "已有资料暂未匹配到具体产品尺寸。可先做概念设计，尺寸留待确认。";
            return "已有资料中的尺寸参考（待确认是否同款）：\n" + String.Join("\n",products.Select(r=>r.Title+"："+r.Brief.Size+" mm"));
        }
    }
}
