using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PaperStudio
{
    public sealed class GenerationStep
    {
        public string Name, Status="pending", InputKey, InputSummary, OutputSummary, Error;
        public int InputVersion=1;
        public DateTime StartedUtc, FinishedUtc;
        public bool CanResume=true;
    }

    public sealed class GenerationTask
    {
        public string TaskId, InputKey, Status="pending", Error, UnknownTool;
        public DateTime CreatedUtc, UpdatedUtc;
        public DesignProject Project;
        public PlanningCheckpoint Planning;
        public List<SourceRecord> RetrievedSources;
        public List<GenerationStep> Steps=new List<GenerationStep>();

        public static GenerationTask Create(string inputKey,DesignProject project)
        {
            return new GenerationTask{TaskId=Guid.NewGuid().ToString("N"),InputKey=inputKey,CreatedUtc=DateTime.UtcNow,UpdatedUtc=DateTime.UtcNow,Project=project,
                Steps=Enumerable.Range(1,7).Select(i=>new GenerationStep{Name=new[]{"理解设计需求","检索包装知识库","分析参考草图","计算包装尺寸和排列方式","构思创意方案","生成结构草图 / 效果图","检查生成结果"}[i-1],InputKey=inputKey}).ToList()};
        }
        public void Record(int index,AgentToolCall call)
        {
            if(index<1||index>Steps.Count)return;
            var step=Steps[index-1];step.InputKey=InputKey;step.InputSummary=call.InputSummary;
            if(call.Status=="running"){step.Status="running";step.StartedUtc=call.StartedUtc;step.Error=null;Status="running";}
            else{
                step.Status=call.Status=="succeeded"?"completed":call.Status=="warning"?"completed":call.Status;
                step.FinishedUtc=call.FinishedUtc;step.OutputSummary=call.OutputSummary;
                step.Error=call.Error;step.CanResume=call.Status!="running";
                if(call.Status=="failed"||call.Status=="cancelled"){Status=call.Status;Error=call.Error;}
            }
            UpdatedUtc=DateTime.UtcNow;
        }
        public void Interrupt(string error,bool unknown,string tool)
        {
            Status="failed";Error=error;UnknownTool=unknown?tool:null;UpdatedUtc=DateTime.UtcNow;
            var running=Steps.FirstOrDefault(s=>s.Status=="running");
            if(running!=null){running.Status="failed";running.Error=error;running.FinishedUtc=UpdatedUtc;}
        }
        public bool ImagesComplete {get{return Project!=null&&Project.Options!=null&&Project.Options.Count==3&&Project.Options.All(o=>o.Current!=null&&o.Current.Pictures.ContainsKey("整体效果"));}}
    }

    public static class GenerationTaskStore
    {
        internal static string PathOverride=null;
        static string lastSavedPath,lastSavedTaskId;
        static string PathName {get{return PathOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","active-generation.json");}}
        public static string Fingerprint(object input)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(input)))).Replace("-","").ToLowerInvariant();}
        public static GenerationTask Load()
        {
            if(!File.Exists(PathName))return null;
            var value=Json.Read<GenerationTask>(File.ReadAllText(PathName,Encoding.UTF8));
            if(value==null||String.IsNullOrWhiteSpace(value.TaskId)||String.IsNullOrWhiteSpace(value.InputKey)||value.Steps==null||value.Steps.Count!=7||value.Project==null)throw new InvalidOperationException("上次生成任务记录不完整，未自动覆盖；请检查任务文件。");
            value.Project.Validate();
            lastSavedPath=PathName;lastSavedTaskId=value.TaskId;
            if(value.Status=="running"){
                value.Status="failed";value.Error="上次运行意外中断。若当时已发送 API 请求，服务端状态未知。";
                if(value.Steps.Any(s=>s.Status=="running")){foreach(var s in value.Steps.Where(s=>s.Status=="running")){s.Status="failed";s.Error=value.Error;s.FinishedUtc=DateTime.UtcNow;}
                    if(String.IsNullOrEmpty(value.UnknownTool))value.UnknownTool=value.Steps[4].Status=="failed"?"draft_concepts":value.Steps[5].Status=="failed"?"generate_image":value.Steps[6].Status=="failed"?"inspect_image":null;}
                Save(value);
            }
            return value;
        }
        public static void Save(GenerationTask value)
        {
            if(value==null)return;
            string path=PathName;Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(File.Exists(path)&&(lastSavedPath!=path||lastSavedTaskId!=value.TaskId)){
                try{
                    var previous=Json.Read<GenerationTask>(File.ReadAllText(path,Encoding.UTF8));
                    Guid id;
                    if(previous!=null&&previous.TaskId!=value.TaskId&&Guid.TryParseExact(previous.TaskId,"N",out id)){
                        string history=Path.Combine(Path.GetDirectoryName(path),"generation-tasks");Directory.CreateDirectory(history);
                        File.Copy(path,Path.Combine(history,previous.TaskId+".json"),true);
                    }
                }catch(InvalidOperationException){}catch(ArgumentException){}
            }
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,Json.Write(value),Encoding.UTF8);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);lastSavedPath=path;lastSavedTaskId=value.TaskId;}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
