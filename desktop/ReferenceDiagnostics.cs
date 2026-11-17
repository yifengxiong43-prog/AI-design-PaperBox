using System;
using System.IO;
using System.Text;

namespace PaperStudio
{
    public static class ReferenceDiagnostics
    {
        static readonly object Gate=new object();
        public static string LogPath {get{
#if TEST
            return Path.Combine(Path.GetTempPath(),"PaperStudio-TEST","reference-errors.jsonl");
#elif QA
            return Path.Combine(Path.GetTempPath(),"PaperStudio-QA","reference-errors.jsonl");
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","reference-errors.jsonl");
#endif
        }}
        public static void Write(string stage,object detail)
        {
            try{
                lock(Gate){
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath,Json.Write(new{TimeUtc=DateTime.UtcNow.ToString("o"),Stage=stage,Detail=detail})+Environment.NewLine,Encoding.UTF8);
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
