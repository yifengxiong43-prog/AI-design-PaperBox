using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

class PackboxDesktopChecks
{
    [STAThread] static int Main(string[] args)
    {
        try{
            var app=new Application();var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var studio=Activator.CreateInstance(assembly.GetType("PaperStudio.CreativeStudio"));
            var owner=(Window)studio.GetType().GetField("window",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(studio);
            var bridge=assembly.GetType("PaperStudio.PackboxExport");
            var dialog=(Window)bridge.GetMethod("CreateDialog").Invoke(null,new object[]{null,null});
            dialog.ShowInTaskbar=false;dialog.WindowStartupLocation=WindowStartupLocation.Manual;
            dialog.Left=-10000;dialog.Top=-10000;dialog.Show();dialog.UpdateLayout();
            var scroll=(ScrollViewer)dialog.Content;var body=(StackPanel)scroll.Content;
            var choices=body.Children.OfType<ComboBox>().Single();
            if(choices.Items.Count!=10||String.IsNullOrEmpty(((ComboBoxItem)choices.Items[0]).Content as string))throw new Exception("Template names missing");
            var button=body.Children.OfType<Button>().Single();
            if(button.ActualHeight<32||button.ActualWidth<300)throw new Exception("Export action layout broken");
            string folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
            var bitmap=new RenderTargetBitmap(528,552,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(folder,"packbox-dialog.png")))png.Save(f);
            string target=Path.Combine(folder,"template-export.zip");
            string request="{\"style\":\"tuck-end\",\"length\":196,\"width\":80,\"height\":185.4,\"title\":\"36支模板参考\"}";
            bridge.GetMethod("Write").Invoke(null,new object[]{target,request});
            using(var stream=File.OpenRead(target))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
                foreach(string name in new[]{"template.svg","template.pbox","request.json","PACKBOX-LICENSE.txt","UPSTREAM.json","说明.txt"})
                    if(zip.GetEntry(name)==null||zip.GetEntry(name).Length==0)throw new Exception("Missing export entry: "+name);
                using(var reader=new StreamReader(zip.GetEntry("template.svg").Open())){
                    string svg=reader.ReadToEnd();if(!svg.Contains("<svg")||!svg.Contains("mm"))throw new Exception("SVG is not a millimetre document");
                }
            }
            byte[] before=File.ReadAllBytes(target);bool rejected=false;
            try{bridge.GetMethod("Write").Invoke(null,new object[]{target,"{\"style\":\"unknown\",\"length\":196,\"width\":80,\"height\":185.4}"});}
            catch(TargetInvocationException){rejected=true;}
            if(!rejected||!before.SequenceEqual(File.ReadAllBytes(target)))throw new Exception("Failed export damaged existing ZIP");
            bridge.GetMethod("Write").Invoke(null,new object[]{target,request});
            dialog.Close();owner.Close();
            string result="PASS: native 10-template dialog, portable Node subprocess, SVG/pbox ZIP with sources and license, overwrite, failed-export preservation. No API call.";
            File.WriteAllText(Path.Combine(folder,"packbox-passed.txt"),result);Console.WriteLine(result);return 0;
        }catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
