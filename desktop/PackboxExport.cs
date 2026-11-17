using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PaperStudio
{
    public class PackboxTemplate { public string id, name; }
    public static class PackboxExport
    {
        static string Root {get{return Path.Combine(Path.GetDirectoryName(typeof(PackboxExport).Assembly.Location),"packbox");}}
        static string Number(decimal value){return value.ToString("0.##",CultureInfo.InvariantCulture);}
        public static Window CreateDialog(Window owner,Concept concept)
        {
            var dialog=new Window{Title="Packbox · 模板展开图",Width=528,Height=552,MinWidth=480,MinHeight=520,
                Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,
                FontFamily=new FontFamily("Inter, Microsoft YaHei UI, Segoe UI"),FontSize=13};
            var body=new StackPanel{Margin=new Thickness(24)};
            dialog.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            body.Children.Add(new TextBlock{Text="模板展开图",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
            body.Children.Add(new TextBlock{Text="选择标准盒型，导出 SVG 与 Packbox 项目。",Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,24)});
            body.Children.Add(new TextBlock{Text="盒型",Margin=new Thickness(0,0,0,8)});
            var templates=Json.Read<List<PackboxTemplate>>(File.ReadAllText(Path.Combine(Root,"templates.json"),Encoding.UTF8));
            var style=new ComboBox{Margin=new Thickness(0,0,0,24)};
            foreach(var template in templates)style.Items.Add(new ComboBoxItem{Content=template.name,Tag=template.id});
            style.SelectedIndex=0;body.Children.Add(style);
            var row=new Grid{Margin=new Thickness(0,0,0,16)};var fields=new TextBox[3];
            var dims=concept==null?null:concept.PenLayout;
            decimal[] values=dims==null?new[]{180m,65m,160m}:new[]{dims.BoxWidth,dims.BoxDepth,dims.BoxHeight};
            string[] labels={"长度 L · mm","深度 W · mm","高度 H · mm"};
            for(int i=0;i<3;i++){
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var cell=new StackPanel{Margin=new Thickness(0,0,i==2?0:16,0)};
                cell.Children.Add(new TextBlock{Text=labels[i],Margin=new Thickness(0,0,0,8)});
                fields[i]=new TextBox{Text=Number(values[i])};cell.Children.Add(fields[i]);Grid.SetColumn(cell,i);row.Children.Add(cell);
            }
            body.Children.Add(row);
            body.Children.Add(new TextBlock{Text=dims==null?"当前无方案尺寸，以上为可修改的示例值。":"已带入方案外径 W/D/H，可手动修改；这不是原方案的自动刀模转换。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,16)});
            body.Children.Add(new TextBlock{Text="模板按零板厚计算。内托、纸厚补偿和装配间隙需另行设计；自锁底模板未包含完整咬合槽。先做白样，再用于生产。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,24)});
            var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};body.Children.Add(status);
            var export=new Button{Content="导出模板资料包",HorizontalAlignment=HorizontalAlignment.Stretch,Style=(Style)Application.Current.FindResource("Primary")};body.Children.Add(export);
            export.Click+=async delegate{
                try{
                    decimal[] numbers=new decimal[3];
                    for(int i=0;i<3;i++)if(!Decimal.TryParse(fields[i].Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out numbers[i])||numbers[i]<1||numbers[i]>2000)
                        throw new InvalidOperationException("请填写 1–2000 mm 的尺寸，使用小数点表示小数。");
                    string title=concept==null?"标准盒型参考":concept.Name;
                    if(title!=null&&title.Length>200)title=title.Substring(0,200);
                    var request=new {style=(string)((ComboBoxItem)style.SelectedItem).Tag,length=numbers[0],width=numbers[1],height=numbers[2],title=title};
                    var save=new SaveFileDialog{Filter="Packbox 模板资料包|*.zip",FileName="Packbox模板.zip"};if(save.ShowDialog(dialog)!=true)return;
                    export.IsEnabled=false;status.Text="正在计算展开图…";
                    await Task.Run(()=>Write(save.FileName,Json.Write(request)));
                    status.Text="已导出 SVG、.pbox 项目和尺寸说明。";
                }catch(Exception error){status.Text=error.Message;}
                finally{export.IsEnabled=true;}
            };
            return dialog;
        }
        public static void Write(string path,string request)
        {
            string work=Path.Combine(Path.GetTempPath(),"PaperStudio-Packbox-"+Guid.NewGuid().ToString("N"));
            string target=Path.GetFullPath(path),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            Directory.CreateDirectory(work);
            try{
                string input=Path.Combine(work,"request.json"),output=Path.Combine(work,"template");
                File.WriteAllText(input,request,new UTF8Encoding(false));
                var start=new ProcessStartInfo{FileName=Path.Combine(Root,"runtime","node.exe"),
                    Arguments="\""+Path.Combine(Root,"packbox-cli.cjs")+"\" \""+input+"\" \""+output+"\"",
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
                    StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                using(var process=Process.Start(start)){
                    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(30000)){process.Kill();process.WaitForExit();throw new InvalidOperationException("模板计算超时，请检查尺寸后重试。");}
                    Task.WaitAll(stdout,stderr);
                    if(process.ExitCode!=0)throw new InvalidOperationException("模板计算失败："+stderr.Result);
                }
                if(!File.Exists(output+".svg")||!File.Exists(output+".pbox"))throw new InvalidOperationException("模板文件不完整，未覆盖原导出文件。");
                using(var stream=new FileStream(temporary,FileMode.CreateNew))
                using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,false,Encoding.UTF8)){
                    foreach(string name in new[]{"template.svg","template.pbox","request.json"})Add(zip,name,File.ReadAllBytes(Path.Combine(work,name)));
                    foreach(string name in new[]{"PACKBOX-LICENSE.txt","UPSTREAM.json"})Add(zip,name,File.ReadAllBytes(Path.Combine(Root,name)));
                    Add(zip,"说明.txt",Encoding.UTF8.GetBytes("Packbox 参数化模板参考\r\n红线为裁切，绿线为折线；SVG 使用毫米。template.pbox 可在 Packbox Studio 中继续编辑、查看折叠。\r\nrequest.json 保存本次实际尺寸。所选标准盒型独立于 AI 方案，不包含原方案的内托、连接结构或图案。\r\n模型为零板厚，输入值不是已补偿的成品内径或外径；需设计师按实际纸厚、装配与折叠调整并打样。自锁底模板没有完整咬合槽，天地盖须分别导出底和盖并自行预留配合间隙。\r\n此包不是生产验收结果。\r\n"));
                }
                if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
            }finally{
                if(File.Exists(temporary))File.Delete(temporary);
                // This unique directory is created by this export and never contains user-selected paths.
                if(Directory.Exists(work))Directory.Delete(work,true);
            }
        }
        static void Add(ZipArchive zip,string name,byte[] bytes)
        {using(var stream=zip.CreateEntry(name).Open())stream.Write(bytes,0,bytes.Length);}
    }
}
