using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PaperStudio;

internal static class SamplePackageChecks
{
    const string Pixel="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jp1kAAAAASUVORK5CYII=";
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static string Read(ZipArchive zip,string path)
    { var entry=zip.GetEntry(path);Check(entry!=null,"Missing sample package entry: "+path);using(var reader=new StreamReader(entry.Open(),Encoding.UTF8))return reader.ReadToEnd(); }
    static DesignVersion Fixture()
    {
        var constraints=new PackagingInput{Count="12",Length="150.5",Diameter="15",PenType="alcohol",Tier="large-standard",AllowanceCm="5"}.Build();
        var c=new Concept{Name="白色,抽屉\"方案",Assembly=new AssemblyPlan{LengthClearance=24,Assumptions="纸板厚度为设计假设",Units=new List<DisplayUnit>{new DisplayUnit{Id="A",Count=12,Columns=12,Motion="抽出后全部展示",Closed=new Placement(),Open=new Placement()}},Parts=new List<AssemblyPart>{new AssemblyPart{Id="外盖",Role="闭合保护",Width=188,Depth=23,Height=1},new AssemblyPart{Id="连接片",Role="展开连接",Width=20,Depth=10,Height=1}}},
            Manufacturing=new ManufacturingPlan{Materials=new List<MaterialChoice>{new MaterialChoice{Part="内托",Role="unit-wall",Quantity=1,MaterialName="白卡纸",SourceId="材料汇总/5",ThicknessMm=1,ThicknessBasis="待供应商白样确认",ProcessSourceIds=new List<string>{"工艺/模切","工艺/压痕"},Purpose=" \t@SUM(1,2)"}}},
            Understanding=new DesignUnderstanding{Requirements=new List<InterpretedRequirement>{
                new InterpretedRequirement{Id="R1",Original="白色",Meaning="旧颜色",Kind="preference",Status="superseded",SupersededBy="R2"},
                new InterpretedRequirement{Id="R2",Original="=SUM(1,2)\n不要磁铁",Meaning="保留原文\"不要磁铁\"",Kind="avoid",Status="active"}}},
            RequirementChecks=new List<RequirementCheck>{new RequirementCheck{RequirementId="R2",Implementation="纸插舌闭合，无磁铁"}}};
        return new DesignVersion{Concept=c,Constraints=constraints,Requirements="用户原始全文",Pictures=new Dictionary<string,string>{{"整体效果",Pixel}},PendingImages=new Dictionary<string,PendingImage>{{"设计草图",new PendingImage{Data="PENDING_SECRET_SENTINEL",Issue="not approved"}}},ParentPicture="PARENT_IMAGE_SENTINEL"};
    }
    public static void Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"paperstudio-sample-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string target=Path.Combine(folder,"sample.zip");var culture=CultureInfo.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            var version=Fixture();var guides=new List<Picture>{new Picture{Name="../Guide\"1,\n尺寸",Data=Pixel}};
            SamplePackage.Write(target,version,"当前方案\n完整解释",guides);
            Check(version.Concept.Assembly.Units[0].Width==0,"Export must calculate a snapshot without mutating the open project");
            using(var stream=File.OpenRead(target))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
            {
                var names=zip.Entries.Select(e=>e.FullName).ToList();
                Check(names.Contains("README.txt")&&names.Contains("current-plan.txt")&&names.Contains("units.csv")&&names.Contains("parts.csv")&&names.Contains("materials.csv")&&names.Contains("requirements.csv"),"Complete engineering handoff files");
                Check(names.Count(n=>n.StartsWith("images/"))==1&&names.Count(n=>n.StartsWith("guides/"))==1,"Only accepted views and explicitly provided dimensional guides are exported");
                Check(names.All(n=>!n.Contains("..")&&!n.Contains("\\")),"Reference names cannot become ZIP paths");
                foreach(var png in zip.Entries.Where(e=>e.FullName.EndsWith(".png")))using(var input=png.Open())using(var output=new MemoryStream()){input.CopyTo(output);Check(output.ToArray().SequenceEqual(Convert.FromBase64String(Pixel)),"Image bytes and transparency must be preserved without re-encoding");}
                string units=Read(zip,"units.csv"),materials=Read(zip,"materials.csv"),requirements=Read(zip,"requirements.csv");
                Check(units.Contains("\"184\"")&&units.Contains("\"186\"")&&units.Contains("\"150.5\"")&&!units.Contains("150,5"),"CSV holds recomputed exact net/outer sizes and invariant full pen length");
                Check(materials.Contains("材料汇总/5")&&materials.Contains("工艺/压痕")&&materials.Contains("待供应商白样确认"),"BOM retains traceable source, processes and thickness assumption");
                Check(materials.Contains("\"' \t@SUM(1,2)\""),"Leading whitespace cannot bypass CSV formula protection");
                Check(Read(zip,"parts.csv").Contains("外盖")&&Read(zip,"parts.csv").Contains("连接片"),"Every separately dimensioned part is included");
                Check(Read(zip,"envelope.csv").Contains("旧方案外壳预算未记录"),"Legacy export must not pretend it has independently budgeted assembly space");
                Check(requirements.Contains("superseded")&&requirements.Contains("active")&&requirements.Contains("\"'=SUM(1,2)\n不要磁铁\"")&&requirements.Contains("\"保留原文\"\"不要磁铁\"\"\"")&&requirements.Contains("纸插舌闭合，无磁铁"),"CSV preserves requirement history, exact quoting, multiline text and neutralizes formulas");
                Check(Read(zip,"README.txt").Contains("不是生产刀模")&&Read(zip,"current-plan.txt").Contains("完整解释"),"Sample package clearly limits engineering claims and retains explanation");
                foreach(var csv in zip.Entries.Where(e=>e.FullName.EndsWith(".csv")))using(var input=csv.Open())Check(input.ReadByte()==0xEF&&input.ReadByte()==0xBB&&input.ReadByte()==0xBF,"CSV has Excel-friendly UTF-8 BOM");
                foreach(var text in zip.Entries.Where(e=>e.FullName.EndsWith(".txt")||e.FullName.EndsWith(".csv")))Check(!Read(zip,text.FullName).Contains("PENDING_SECRET_SENTINEL")&&!Read(zip,text.FullName).Contains("PARENT_IMAGE_SENTINEL"),"No pending pixels or parent images leak through serialized model data");
            }
            byte[] before=File.ReadAllBytes(target);bool rejected=false;
            try { SamplePackage.Write(target,version,"replacement",new List<Picture>{new Picture{Name="broken",Data="not-base64"}}); } catch(InvalidOperationException) { rejected=true; }
            Check(rejected&&before.SequenceEqual(File.ReadAllBytes(target)),"Failed export preserves the previous complete ZIP byte for byte");
            Check(Directory.GetFiles(folder).Length==1,"Failed export removes temporary output");
            SamplePackage.Write(target,version,"替换成功",guides);
            using(var stream=File.OpenRead(target))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))Check(Read(zip,"current-plan.txt").Contains("替换成功"),"Completed ZIP atomically replaces existing export");
            var budgeted=Fixture();budgeted.Concept.Assembly.Shell=new ShellPlan{WallThickness=1.5m,AssemblyClearance=2,InternalReserve=new Size3{Width=2,Depth=3,Height=4},ExternalReserve=new Size3{Width=1,Depth=2,Height=3},Basis="折页底板占用分别计入；每侧净间隙2mm"};
            budgeted.Concept.Assembly.OpenReserve=new Size3{Width=6,Depth=40,Height=2};budgeted.Concept.Assembly.OpenReserveBasis="底座后伸40mm，侧盖共6mm，底部2mm";
            foreach(var part in budgeted.Concept.Assembly.Parts){part.SizeBasis="flat";part.PlacementScope=part.Id=="外盖"?"shell":"internal";part.ClosedSize=new Size3{Width=part.Width,Depth=part.Depth,Height=part.Height};part.AssemblyNotes="接A侧边；沿长边压痕，两端各5mm胶接区";}
            budgeted.Concept.Assembly.Parts[1].ClosedSize=new Size3{Width=20,Depth=2,Height=176.4m};
            SamplePackage.Write(target,budgeted,"外壳预算版本",guides);
            Check(budgeted.Concept.Assembly.Shell.InnerSize==null,"Export must not overwrite a live project's declared or derived shell data");
            using(var stream=File.OpenRead(target))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
            {
                string envelope=Read(zip,"envelope.csv"),parts=Read(zip,"parts.csv"),ledger=Read(zip,"current-plan.txt");
                Func<string,decimal[]> dimensions=label=>envelope.Split('\n').Single(line=>line.StartsWith("\""+label+"\",",StringComparison.Ordinal)).Split(',').Skip(1).Take(3).Select(value=>Decimal.Parse(value.Trim('"'),CultureInfo.InvariantCulture)).ToArray();
                Check(dimensions("壳内空").SequenceEqual(new[]{192m,28m,184.5m})&&dimensions("最终包装外径").SequenceEqual(new[]{196m,33m,190.5m}),"Export distinguishes exact cavity and final package including protrusions");
                Check(envelope.Contains("笔组展开包络")&&envelope.Contains("全结构展开预算范围")&&envelope.Contains("底座后伸40mm"),"Whole-open budget and its basis propagate without replacing pen-group geometry");
                Check(parts.Contains("平片 W×D×厚")&&parts.Contains("闭合安装高_mm")&&parts.Contains("\"20\",\"2\",\"176.4\"")&&parts.Contains("两端各5mm胶接区"),"Flat-blank, installed size, scope and mechanical notes remain explicit in parts.csv");
                Check(ledger.Contains("外壳预算")&&ledger.Contains("安装包络"),"The human-readable handoff includes shell and part installation semantics");
            }
            Console.WriteLine("PASS: sample package exact geometry, traceability, CSV safety, accepted-only images and atomic export");
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture=culture;Directory.Delete(folder,true); }
    }
}
