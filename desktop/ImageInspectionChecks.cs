using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperStudio
{
    internal static class ImageInspectionChecks
    {
        static void Check(bool condition,string message){if(!condition)throw new Exception("Image inspection: "+message);}
        static void Quality(Action action,string message)
        {bool rejected=false;try{action();}catch(ImageQualityException){rejected=true;}Check(rejected,message);}
        static void Incomplete(Action action,string message)
        {bool rejected=false;try{action();}catch(ImageQualityException){throw new Exception("Image inspection: incomplete review must not schedule paid redraw: "+message);}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
        static DesignVersion Version()
        {
            var constraints=new PackagingInput{Count="12",Length="150",Diameter="15",Tier="large-standard",AllowanceCm="4",PenType="acrylic"}.Build();
            var concept=new Concept{Assembly=new AssemblyPlan{LengthClearance=24,Assumptions="1mm纸板、4mm整排余量均为设计假设",Units=new List<DisplayUnit>{
                new DisplayUnit{Id="A",Count=6,Columns=6,Motion="左侧展开",Closed=new Placement(),Open=new Placement()},
                new DisplayUnit{Id="B",Count=6,Columns=6,Motion="右侧展开",Closed=new Placement{Y=24},Open=new Placement{X=104}}
            },Parts=new List<AssemblyPart>{new AssemblyPart{Id="盖",Role="翻盖",Width=98,Depth=47,Height=1},new AssemblyPart{Id="连接片",Role="连接两组",Width=10,Depth=23,Height=1}}}};
            AssemblyGeometry.Calculate(concept,constraints);return new DesignVersion{Concept=concept,Constraints=constraints};
        }
        static ImageInspectionReport Report(DesignVersion version)
        {
            return new ImageInspectionReport{Pass=true,SameStructure=true,ProportionsMatch=true,DimensionsPresent=true,RequirementsMet=true,ObservedVisiblePens=12,Issues="",SketchStyleMatches=true,
                Units=version.Concept.Assembly.Units.Select(u=>new ImageUnitInspection{Id=u.Id,VisiblePens=u.Count,FullLengthVisible=true,DimensionsMatch=true,WidthMm=u.Width,DepthMm=u.Depth,HeightMm=u.Height,Evidence="展开主视图中单排完整笔身与外尺寸引线可辨识"}).ToList(),
                Parts=version.Concept.Assembly.Parts.Select(p=>new ImagePartInspection{Id=p.Id,DimensionsMatch=true,WidthMm=p.Width,DepthMm=p.Depth,HeightMm=p.Height,Evidence="对应部件的三边与板厚引线可辨识"}).ToList()};
        }
        public static void Run()
        {
            var version=Version();var report=Report(version);
            string normal=Json.Write(new{Pass=true,SameStructure=true,ProportionsMatch=true,DimensionsPresent=true,RequirementsMet=true,ObservedVisiblePens=12,Issues="",Units=report.Units.Select(u=>new{u.Id,u.VisiblePens,u.FullLengthVisible,u.Evidence}).ToArray()});
            ImageInspection.Validate(ImageInspection.Parse(normal,false),version,"打开状态");
            Check(!normal.Contains("WidthMm")&&!normal.Contains("Parts")&&!normal.Contains("SketchStyleMatches"),"normal photographs need no dimensional labels or sketch-only fields");
            string sketch=Json.Write(report);ImageInspection.Validate(ImageInspection.Parse(sketch,true),version,"设计草图");
            foreach(string invalid in new[]{"null","{}","not-json",normal.Replace("\"Pass\":true","\"Pass\":\"true\""),normal.Replace("\"VisiblePens\":6","\"VisiblePens\":6.5"),normal.Replace("\"Evidence\":","\"MissingEvidence\":"),normal.Replace("\"Units\":","\"MissingUnits\":")})
                Incomplete(()=>ImageInspection.Parse(invalid,false),"missing or mistyped required per-unit review fields");
            Incomplete(()=>ImageInspection.Parse(normal,true),"a normal review is incomplete for a sketch, rather than confirmation that the image is wrong");
            Incomplete(()=>ImageInspection.Parse(sketch.Replace("\"WidthMm\":96","\"WidthMm\":\"96 mm\""),true),"dimension strings cannot be silently converted to observations");
            Incomplete(()=>ImageInspection.Parse(sketch.Replace("\"HeightMm\":1","\"MissingHeightMm\":1"),true),"each part must explicitly report its observed thickness, including null if unreadable");

            var bad=Report(version);bad.Units[0].FullLengthVisible=false;
            Quality(()=>ImageInspection.Validate(bad,version,"整体效果"),"global pass cannot hide a unit whose full pen bodies are concealed");
            bad=Report(version);bad.Units[0].VisiblePens=5;
            Quality(()=>ImageInspection.Validate(bad,version,"打开状态"),"actual count in every group must match the planned group");
            bad=Report(version);bad.ObservedVisiblePens=24;
            Quality(()=>ImageInspection.Validate(bad,version,"整体效果"),"repeated mini views cannot be added to main expanded view's pen count");
            bad=Report(version);bad.Units[1].Id="A";
            Quality(()=>ImageInspection.Validate(bad,version,"整体效果"),"duplicate unit IDs cannot satisfy a missing group's evidence");
            bad=Report(version);bad.Units[1].Id="unplanned";
            Quality(()=>ImageInspection.Validate(bad,version,"整体效果"),"every observed group must map to this exact design");
            bad=Report(version);bad.Units[0].Evidence=" ";
            Quality(()=>ImageInspection.Validate(bad,version,"整体效果"),"passing booleans require actual per-unit observation evidence");
            bad=Report(version);bad.Units[0].WidthMm-=2;
            Quality(()=>ImageInspection.Validate(bad,version,"设计草图"),"net width cannot substitute for observed outer unit width");
            Check(ImageInspection.VisualReferenceUsable(bad,version),"incorrect dimension labels may leave the physical sketch usable as a visual reference");
            bad=Report(version);bad.DimensionsPresent=false;bad.Pass=false;bad.Parts[0].DimensionsMatch=false;
            Check(ImageInspection.VisualReferenceUsable(bad,version),"overall and part dimension failures do not invalidate observed physical geometry");
            bad=Report(version);bad.Units[0].VisiblePens=5;
            Check(!ImageInspection.VisualReferenceUsable(bad,version),"a missing pen must block rendering from this sketch");
            bad=Report(version);bad.SameStructure=false;
            Check(!ImageInspection.VisualReferenceUsable(bad,version),"a conflicting box structure must block rendering from this sketch");
            version.Concept.Understanding=new DesignUnderstanding{ReferenceDesign=new ReferenceDesignSpecification{BodyShape="原始盒体"}};
            bad=Report(version);bad.ReferenceStructureMatch=false;bad.ReferenceIssues="缺少连杆";
            Quality(()=>ImageInspection.Validate(bad,version,"设计草图"),"reference structure mismatch must block sketch acceptance");
            Check(!ImageInspection.VisualReferenceUsable(bad,version),"reference mismatch cannot become a usable effect source");
            version.Concept.Understanding=null;
            bad=Report(version);bad.Units[1].HeightMm=null;
            var unreadable=ImageInspection.Parse(Json.Write(bad),true);
            Quality(()=>ImageInspection.Validate(unreadable,version,"设计草图"),"explicit unreadable dimensions are a confirmed image defect, not malformed JSON");
            bad=Report(version);bad.Parts.RemoveAt(1);
            Quality(()=>ImageInspection.Validate(bad,version,"设计草图"),"every connector and cover needs independent dimensions");
            bad=Report(version);bad.Parts[1].Id=bad.Parts[0].Id;
            Quality(()=>ImageInspection.Validate(bad,version,"设计草图"),"duplicate cover dimensions cannot stand in for the connector");
            bad=Report(version);bad.Parts[1].HeightMm=2;
            Quality(()=>ImageInspection.Validate(bad,version,"设计草图"),"observed connector thickness must match its design value");
            bad=Report(version);bad.SketchStyleMatches=false;
            Quality(()=>ImageInspection.Validate(bad,version,"设计草图"),"photographic rendering cannot pass as the requested sketch style");
            var tolerance=Report(version);tolerance.Units[0].WidthMm+=0.01m;ImageInspection.Validate(tolerance,version,"设计草图");
            tolerance.Units[0].WidthMm+=0.001m;
            Quality(()=>ImageInspection.Validate(tolerance,version,"设计草图"),"only the documented 0.01mm rounding tolerance is accepted");
            Check(ImageInspection.Contract(true).Contains("0.01mm")&&ImageInspection.Contract(true).Contains("null")&&ImageInspection.Contract(false).Contains("不累加")&&ImageInspection.Contract(false).Contains("不可单独因此判图片失败"),"API contract separates visible defects from engineering questions");
            Console.WriteLine("PASS: strict per-unit visual observations, full-length/count checks, all sketch part dimensions, measured-value tolerance and incomplete-review distinction.");
        }
    }
}
