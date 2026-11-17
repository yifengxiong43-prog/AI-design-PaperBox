using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PaperStudio
{
    public static class PythonGeometryTool
    {
        public static string RootOverride;
        static string Root {get{return RootOverride??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"python-tools");}}
        public static void Verify(Concept concept,PackagingConstraints constraints)
        {
            if(concept==null||concept.Assembly==null||constraints==null)throw new InvalidOperationException("Python核算缺少方案或产品参数。");
            var interpreter=Path.Combine(Root,"runtime","python.exe");var script=Path.Combine(Root,"geometry.py");
            if(!File.Exists(interpreter)||!File.Exists(script))throw new InvalidOperationException("随软件附带的Python几何工具缺失，请保留完整发布文件夹。");
            var input=new{constraints.MarkerCount,constraints.MarkerLength,constraints.MarkerDiameter,constraints.TrayHeightMm,
                ClosedLimits=new{Longest=constraints.MaxLongest,Middle=constraints.MaxMiddle,Shortest=constraints.MaxShortest},
                PackageDimensions=concept.PackageDimensions,Assembly=new{
                concept.Assembly.TotalClearance,concept.Assembly.BetweenGap,concept.Assembly.DividerThickness,
                concept.Assembly.WallThickness,concept.Assembly.LengthClearance,concept.Assembly.CombinedLengthClearance,concept.Assembly.LengthAxis,concept.Assembly.Orientation,
                Units=concept.Assembly.Units.Select(u=>new{u.Id,u.Count,u.Rows,u.Columns,u.Width,u.Depth,u.Height,u.Closed,u.Open}).ToList()}};
            var start=new ProcessStartInfo(interpreter,"\""+script+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Root};
            using(var process=Process.Start(start))
            {
                process.StandardInput.Write(Json.Write(input));process.StandardInput.Close();
                if(!process.WaitForExit(5000)){process.Kill();throw new InvalidOperationException("Python几何核算超时，未生成图片。");}
                string output=process.StandardOutput.ReadToEnd();
                if(output.Length>50000)throw new InvalidOperationException("Python几何工具返回过长结果。");
                Dictionary<string,object> result;
                try{result=Json.Read<Dictionary<string,object>>(output);}catch{throw new InvalidOperationException("Python几何工具未返回有效核算结果。");}
                if(process.ExitCode!=0||result==null||!result.ContainsKey("Ok")||!Convert.ToBoolean(result["Ok"]))
                    throw new InvalidOperationException("Python几何核算未通过："+(result!=null&&result.ContainsKey("Error")?result["Error"]:"工具运行失败"));
            }
        }
    }
}
