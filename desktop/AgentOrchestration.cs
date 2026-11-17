using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace PaperStudio
{
    public sealed class AgentToolCall
    {
        public string RunId, Tool, Stage, Status, Error, InputSummary, OutputSummary;
        public int Attempt;
        public DateTime StartedUtc, FinishedUtc;
    }

    public sealed class AgentRun
    {
        readonly List<AgentToolCall> calls=new List<AgentToolCall>();
        public string RunId {get;private set;}
        public IReadOnlyList<AgentToolCall> Calls {get{return calls.AsReadOnly();}}
        public Action<AgentToolCall> Changed;
        public AgentRun(string runId=null){RunId=String.IsNullOrWhiteSpace(runId)?Guid.NewGuid().ToString("N"):runId;}
        void Publish(AgentToolCall call){AgentDiagnostics.Write(call);if(Changed!=null)Changed(call);}
        public void Warn(string tool,string stage,string reason)
        {
            var call=new AgentToolCall{RunId=RunId,Tool=tool,Stage=stage,Status="warning",Error=Trim(reason),StartedUtc=DateTime.UtcNow,FinishedUtc=DateTime.UtcNow};
            calls.Add(call);Publish(call);
        }
        public static string InputHash(string value)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value??""))).Replace("-","").ToLowerInvariant().Substring(0,16);}

        public T Invoke<T>(string tool,string stage,int attempt,string inputSummary,Func<T> action,Func<T,string> outputSummary=null)
        {
            var call=new AgentToolCall{RunId=RunId,Tool=tool,Stage=stage,Attempt=attempt,Status="running",InputSummary=Trim(inputSummary),StartedUtc=DateTime.UtcNow};
            calls.Add(call);Publish(call);
            try{
                T result=action();call.Status="succeeded";call.OutputSummary=Trim(outputSummary==null?null:outputSummary(result));return result;
            }catch(OperationCanceledException){call.Status="cancelled";throw;}
            catch(Exception error){call.Status="failed";call.Error=error.GetType().Name+": "+error.Message;throw;}
            finally{call.FinishedUtc=DateTime.UtcNow;Publish(call);}
        }
        public async Task<T> InvokeAsync<T>(string tool,string stage,int attempt,string inputSummary,Func<Task<T>> action,Func<T,string> outputSummary=null)
        {
            var call=new AgentToolCall{RunId=RunId,Tool=tool,Stage=stage,Attempt=attempt,Status="running",InputSummary=Trim(inputSummary),StartedUtc=DateTime.UtcNow};
            calls.Add(call);Publish(call);
            try{
                T result=await action();call.Status="succeeded";call.OutputSummary=Trim(outputSummary==null?null:outputSummary(result));return result;
            }catch(OperationCanceledException){call.Status="cancelled";throw;}
            catch(Exception error){call.Status="failed";call.Error=error.GetType().Name+": "+error.Message;throw;}
            finally{call.FinishedUtc=DateTime.UtcNow;Publish(call);}
        }
        public async Task InvokeAsync(string tool,string stage,int attempt,string inputSummary,Func<Task> action)
        {
            var call=new AgentToolCall{RunId=RunId,Tool=tool,Stage=stage,Attempt=attempt,Status="running",InputSummary=Trim(inputSummary),StartedUtc=DateTime.UtcNow};
            calls.Add(call);Publish(call);
            try{await action();call.Status="succeeded";}
            catch(OperationCanceledException){call.Status="cancelled";throw;}
            catch(Exception error){call.Status="failed";call.Error=error.GetType().Name+": "+error.Message;throw;}
            finally{call.FinishedUtc=DateTime.UtcNow;Publish(call);}
        }
        static string Trim(string value){if(String.IsNullOrWhiteSpace(value))return null;return value.Length<=600?value:value.Substring(0,600)+"…";}
    }

    public static class AgentToolCatalog
    {
        public static string Prompt()
        {
            return "主 Agent 工具协议（工具由客户端程序执行，模型不能伪造执行结果）："+
                " retrieve_references=检索本轮资料并建立短引用目录；"+
                " understand_request=从用户文字和reference图片提取约束；"+
                " calculate_geometry=用确定性结构账本计算尺寸；"+
                " validate_manufacturing=检查材料/工艺/风险/测试的真实引用；"+
                " review_design=独立复核方案；"+
                " generate_image=调用图片API；"+
                " inspect_image=复核图片质量；"+
                " revise_design=按具体失败原因有限回修。"+
                " 模型不得声称未被程序调用的工具已运行，不得用自然语言替代工具输出。";
        }
    }

    public static class AgentDiagnostics
    {
        static readonly object Gate=new object();
        public static string LogPath {get{
#if QA || TEST
            return Path.Combine(Path.GetTempPath(),"PaperStudio-QA","agent-runs.jsonl");
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","agent-runs.jsonl");
#endif
        }}
        public static void Write(AgentToolCall call)
        {
            try{lock(Gate){Directory.CreateDirectory(Path.GetDirectoryName(LogPath));File.AppendAllText(LogPath,Json.Write(new{TimeUtc=DateTime.UtcNow.ToString("o"),Call=call})+Environment.NewLine,System.Text.Encoding.UTF8);}}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }

    public static class PackagingAgentTools
    {
        public static void ValidateReferenceImages(DesignUnderstanding understanding,List<Picture> references)
        {
            var supplied=(references??new List<Picture>()).Where(p=>p!=null&&!String.IsNullOrWhiteSpace(p.Name)).ToList();
            if(supplied.Count==0)return;
            var observations=understanding==null?null:understanding.ReferenceImages;
            if(observations==null||observations.Any(o=>o==null||String.IsNullOrWhiteSpace(o.ImageName)||String.IsNullOrWhiteSpace(o.BoxType)||String.IsNullOrWhiteSpace(o.Opening)||String.IsNullOrWhiteSpace(o.Layout)||String.IsNullOrWhiteSpace(o.VisualStyle)||String.IsNullOrWhiteSpace(o.Constraints)))
                throw new DesignValidationException("reference图片缺少逐张盒型、开合、布局或风格观察。");
            ReferenceDesign.BindObservations(understanding,supplied);
        }
        public static List<SourceRecord> RetrieveReferences(List<SourceRecord> references,string query,KnowledgeCache cache)
        {
            if(references==null)throw new InvalidOperationException("资料库为空，无法检索。");
            return cache==null?DesignService.UnderstandingSources(references,query):cache.Select(references,query);
        }
        public static void CalculateGeometry(Concept concept,PackagingConstraints constraints)
        {if(concept==null||concept.Assembly==null)throw new InvalidOperationException("方案缺少Assembly结构。");AssemblyGeometry.Calculate(concept,constraints);PythonGeometryTool.Verify(concept,constraints);}
        public static string SummarizeSources(IEnumerable<SourceRecord> sources)
        {return String.Join(",",(sources??Enumerable.Empty<SourceRecord>()).Select(s=>s.StableId).Where(s=>!String.IsNullOrWhiteSpace(s)).Take(8));}
    }
}
