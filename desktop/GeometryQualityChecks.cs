using System;
using System.Collections.Generic;
using System.Linq;
using PaperStudio;

internal static class GeometryQualityChecks
{
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void Reject(Action action,string message)
    {
        bool rejected=false;
        try { action(); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,message);
    }
    static PackagingInput Input()
    { return new PackagingInput{Count="12",Length="150",Diameter="15",PenType="alcohol",Tier="large-standard",AllowanceCm="5"}; }
    static Concept Assembly()
    {
        return new Concept{Assembly=new AssemblyPlan{LengthClearance=24,Assumptions="4mm总余量、1mm壁厚为设计假设，待白样检验",Units=new List<DisplayUnit>{
            new DisplayUnit{Id="A",Count=6,Columns=6,Closed=new Placement(),Open=new Placement(),Motion="左侧展开"},
            new DisplayUnit{Id="B",Count=6,Columns=6,Closed=new Placement{Y=24},Open=new Placement{X=104},Motion="右侧展开"}
        },Parts=new List<AssemblyPart>{new AssemblyPart{Id="盖板",Role="连接外壳",Width=98,Depth=47,Height=1}}}};
    }
    static Concept BudgetedAssembly(PackagingConstraints constraints)
    {
        var c=Assembly();c.Assembly.LengthClearance=constraints.MinimumLengthClearanceMm;c.Assembly.OpenLayout="side-by-side";
        c.Assembly.Units=Enumerable.Range(0,3).Select(i=>new DisplayUnit{Id="ABC"[i].ToString(),Count=12,Columns=12,Motion="一体纸折页展开，底板支撑",Closed=new Placement{Y=i*23},Open=new Placement()}).ToList();
        var raw=(Dictionary<string,object>)Json.Serializer().DeserializeObject(Json.Write(c));
        var assembly=(Dictionary<string,object>)raw["Assembly"];
        assembly["Shell"]=Json.Serializer().DeserializeObject("{\"WallThickness\":1.5,\"AssemblyClearance\":2,\"InternalReserve\":{\"Width\":2,\"Depth\":4,\"Height\":3},\"ExternalReserve\":{\"Width\":1,\"Depth\":2,\"Height\":1},\"Basis\":\"每侧2mm装配间隙；内置折页及底板独立占用，外部插舌突出另计\"}");
        assembly["OpenReserve"]=Json.Serializer().DeserializeObject("{\"Width\":8,\"Depth\":40,\"Height\":2}");
        assembly["OpenReserveBasis"]="展开底座向后支撑40mm，两端盖板合计8mm，底板增高2mm";
        assembly["Parts"]=Json.Serializer().DeserializeObject("[{\"Id\":\"折页\",\"Role\":\"单元连接\",\"Width\":176.4,\"Depth\":20,\"Height\":1,\"SizeBasis\":\"flat\",\"PlacementScope\":\"internal\",\"ClosedSize\":{\"Width\":20,\"Depth\":2,\"Height\":176.4},\"AssemblyNotes\":\"176.4mm长边安装沿Z；两侧各5mm胶接区接A/B侧边；宽度中线压痕，折后厚度计入深度预算\"}]");
        return Json.Read<Concept>(Json.Write(raw));
    }
    static void ShellBudgetChecks()
    {
        var input=Input();input.Count="36";input.PenType="acrylic";var constraints=input.Build();
        Check(constraints.MaxLongest==407.2m&&constraints.MaxMiddle==305.6m&&constraints.MaxShortest==153.2m,"36-pen regression must use the actual large-standard closed-package limits after 5 cm allowance");
        var c=BudgetedAssembly(constraints);AssemblyGeometry.PrepareGeneratedLayout(c,constraints);AssemblyGeometry.Calculate(c,constraints);
        Check(c.PenLayout.BoxWidth==196&&c.PenLayout.BoxDepth==80&&c.PenLayout.BoxHeight==185.4m,"Shell reserves and independent shell walls must enlarge the package without changing pen units");
        Check(c.PenLayout.BoxHeight>constraints.MaxShortest&&c.PackageDimensions.Longest==196&&c.PackageDimensions.Middle==185.4m&&c.PackageDimensions.Shortest==80,"Upright acrylic pen height may exceed the shortest-edge limit when sorted closed dimensions fit");
        Check(c.Assembly.Units.All(u=>u.Width==186&&u.Depth==21&&u.Height==174.4m),"Shell clearance must never be allocated by enlarging the pen slots");
        Check(c.Assembly.Shell.InnerSize.Width==192&&c.Assembly.Shell.InnerSize.Depth==75&&c.Assembly.Shell.InnerSize.Height==181.4m,"Cavity includes each axis reserve once and each side clearance twice");
        Check(c.Assembly.Shell.OuterSize.Width==195&&c.Assembly.Shell.OuterSize.Depth==78&&c.Assembly.Shell.OuterSize.Height==184.4m,"Shell body excludes separately budgeted external protrusions");
        Check(c.Assembly.OpenWidth==566&&c.Assembly.OverallOpenWidth==574&&c.Assembly.OverallOpenDepth==61&&c.Assembly.OverallOpenHeight==176.4m,"Whole structure budget must remain distinct from visible pen-group extents");
        Check(c.Assembly.OverallOpenWidth>constraints.MaxLongest,"An unfolded footprint beyond the closed shipping limit must remain valid without a separate user open-size limit");
        Check(c.Assembly.Shell.AssemblyClearance==2&&c.Assembly.Shell.InternalReserve.Height==3&&c.Assembly.Shell.ExternalReserve.Width==1&&c.Assembly.OpenReserve.Depth==40,"Geometry must preserve the model's explicit assembly budgets");
        string accepted=Json.Write(c);var restored=Json.Read<Concept>(accepted);AssemblyGeometry.Calculate(restored,constraints);
        Check(Json.Write(restored)==accepted,"Saved shell dimensions and notes round trip without reinterpretation or repeated allowance");
        var oversized=Json.Copy(c);oversized.Assembly.Parts[0].ClosedSize.Height=181.41m;
        Reject(()=>AssemblyGeometry.Calculate(oversized,constraints),"An internal folded part above the cavity height must be rejected");
        foreach(string scope in new[]{"shell","external"}){
            oversized=Json.Copy(c);oversized.Assembly.Parts[0].PlacementScope=scope;oversized.Assembly.Parts[0].ClosedSize.Width=196.01m;
            Reject(()=>AssemblyGeometry.Calculate(oversized,constraints),"Shell and external parts cannot exceed the final package envelope");
        }
        var folded=Json.Copy(c);folded.Assembly.Parts[0].Width=400;AssemblyGeometry.Calculate(folded,constraints);
        Check(folded.PenLayout.BoxWidth==196,"A long flat blank is compared using its declared folded installation envelope, not guessed orientation");
        var invalid=Json.Copy(c);invalid.Assembly.Shell.InternalReserve.Width=-1;Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Negative reserves cannot shrink the computed enclosure");
        invalid=Json.Copy(c);invalid.Assembly.Shell.ExternalReserve=null;Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Missing external budget is not silently assumed zero");
        invalid=Json.Copy(c);invalid.Assembly.OpenReserve=null;Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Shell mode requires an explicit open-structure budget");
        invalid=Json.Copy(c);invalid.Assembly.OpenReserveBasis="";Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"A zero or nonzero open budget needs its basis");
        invalid=Json.Copy(c);invalid.Assembly.Parts[0].ClosedSize=null;Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Parts need installation dimensions in shell mode");
        invalid=Json.Copy(c);invalid.Assembly.Parts[0].SizeBasis="guess";Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Unknown part dimension states must not be guessed");
        invalid=Json.Copy(c);invalid.Assembly.Parts[0].PlacementScope="inside";Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Unknown part placements must not bypass the cavity check");
        invalid=Json.Copy(c);invalid.Assembly.Parts[0].AssemblyNotes="";Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Installation relationships cannot be omitted");
        invalid=Json.Copy(c);invalid.Assembly.Shell.ExternalReserve.Width=1000;Reject(()=>AssemblyGeometry.Calculate(invalid,constraints),"Final package including protrusions must still obey the shipping envelope");
        var zero=Json.Copy(c);zero.Assembly.Shell.AssemblyClearance=0;zero.Assembly.Shell.InternalReserve=new Size3();zero.Assembly.Shell.ExternalReserve=new Size3();zero.Assembly.OpenReserve=new Size3();zero.Assembly.Parts[0].ClosedSize.Height=100;
        AssemblyGeometry.Calculate(zero,constraints);Check(zero.PenLayout.BoxWidth==189&&zero.PenLayout.BoxHeight==177.4m&&zero.Assembly.OverallOpenWidth==566,"Explicit zero reserves are permitted without arbitrary compulsory enlargement");
        var legacy=Json.Copy(c);legacy.Assembly.Shell=null;legacy.Assembly.OpenReserve=null;AssemblyGeometry.Calculate(legacy,constraints);
        Check(legacy.PenLayout.BoxWidth==188&&legacy.PenLayout.BoxDepth==69&&legacy.PenLayout.BoxHeight==176.4m,"Legacy shell-null saved projects retain their original package dimensions");
        Check(AssemblyGeometry.Describe(c,constraints).Contains("闭合安装包络")&&AssemblyGeometry.Describe(c,constraints).Contains("笔组展开包络"),"The ledger must distinguish installation, cavity, and pen-group measurements");
    }
    public static void Run()
    {
        ShellBudgetChecks();
        var input=Input();
        input.Count="10000";
        Reject(()=>input.Build(),"Impossible minimum bounding space must fail before a paid design request");
        input=Input();input.Length="400";
        Reject(()=>input.Build(),"Pen length plus mandatory end clearance must fit some allowed axis");
        input=Input();input.Length="384.8";
        Reject(()=>input.Build(),"At the exact length-plus-clearance cap there is still no wall allowance");
        input=Input();input.Length="384.79";
        input.Build(); // A lower bound must not claim a definite impossibility while positive wall space remains.
        input=Input();input.Length="10";input.Diameter="15";
        Reject(()=>input.Build(),"A pen shorter than its supplied diameter signals invalid product dimensions");

        input=Input();input.Count="1";input.Tier="custom";input.OuterLongest="1000000";input.OuterMiddle="1000000";input.OuterShortest="1000000";
        var large=input.Build();
        // 55,552 x 55,552 slots overflow Int32; this valid input must remain acceptable.
        large.ValidateLayout(new PenLayout{BoxWidth=999950,BoxDepth=999950,BoxHeight=999950,Orientation="vertical",LengthAxis="Height"},new PackageDimensions{Longest=999950,Middle=999950,Shortest=999950});

        var limits=Input().Build();var concept=Assembly();
        concept.Assembly.Parts.Add(new AssemblyPart{Id="盖板",Role="另一个连接片",Width=20,Depth=10,Height=1});
        Reject(()=>AssemblyGeometry.Calculate(concept,limits),"Duplicate part IDs make dimensioned parts ambiguous");
        concept=Assembly();concept.Assembly.Units[1].Id=" A ";
        Reject(()=>AssemblyGeometry.Calculate(concept,limits),"Whitespace must not disguise duplicate unit labels");
        concept=Assembly();limits.MarkerLength=0;
        Reject(()=>AssemblyGeometry.Calculate(concept,limits),"Invalid input must fail before unit geometry is calculated");
        Check(concept.Assembly.Units[0].Width==0,"Rejected input must not overwrite a previously uncalculated unit");

        limits=Input().Build();concept=Assembly();AssemblyGeometry.Calculate(concept,limits);
        Check(concept.Assembly.Units.Sum(u=>u.Count)==12&&concept.Assembly.Units[0].Width==96,"Valid split-row geometry is preserved");
        Check(AssemblyGeometry.Span(12,15,4)==184,"Whole-row clearance remains 184 mm for 12 x 15 mm + 4 mm");
        var acrylicInput=Input();acrylicInput.Count="36";acrylicInput.PenType="acrylic";
        var acrylic=acrylicInput.Build();var expanded=Assembly();
        expanded.Assembly.LengthClearance=acrylic.MinimumLengthClearanceMm;
        expanded.Assembly.Units=Enumerable.Range(0,3).Select(i=>new DisplayUnit{Id="ABC"[i].ToString(),Count=12,Columns=12,Motion="取出独立单排笔座并排放置，盖板翻离展示面",Closed=new Placement{Y=i*21},Open=new Placement{Y=i*21}}).ToList();
        Reject(()=>AssemblyGeometry.Calculate(expanded,acrylic),"36 upright pens in three overlapping front projections must be rejected");
        expanded.Assembly.OpenLayout="side-by-side";
        AssemblyGeometry.PrepareGeneratedLayout(expanded,acrylic);AssemblyGeometry.Calculate(expanded,acrylic);
        Check(expanded.Assembly.Units.All(u=>u.Width==186&&u.Depth==21&&u.Height==174.4m),"Screenshot inputs must calculate actual unit dimensions");
        Check(expanded.Assembly.Units.Select(u=>u.Open.X).SequenceEqual(new[]{0m,190m,380m})&&expanded.Assembly.OpenWidth==566,"Explicit parallel layout computes all three visible positions");
        Check(expanded.Assembly.Units.Select(u=>u.Closed.Y).SequenceEqual(new[]{0m,21m,42m}),"Generated open placement never changes the closed mechanism");
        expanded.Assembly.Units[1].Open.X=0;
        Reject(()=>AssemblyGeometry.Calculate(expanded,acrylic),"Reopening a saved layout does not silently rearrange or conceal a real overlap");
        var untouched=Assembly();string manual=Json.Write(untouched);AssemblyGeometry.PrepareGeneratedLayout(untouched,limits);
        Check(Json.Write(untouched)==manual,"Manual layout remains exactly unchanged without explicit opt-in");
        foreach(string axis in new[]{"Width","Depth","Height"}){
            var unequal=Assembly();unequal.Assembly.OpenLayout="side-by-side";unequal.Assembly.LengthAxis=axis;unequal.Assembly.Orientation=axis=="Height"?"vertical":"horizontal";
            unequal.Assembly.Units[0].Count=unequal.Assembly.Units[0].Columns=5;unequal.Assembly.Units[1].Count=unequal.Assembly.Units[1].Columns=7;
            if(axis!="Height")unequal.Assembly.Units[1].Closed=new Placement{Z=24};
            AssemblyGeometry.PrepareGeneratedLayout(unequal,limits);AssemblyGeometry.Calculate(unequal,limits);
            Check(unequal.Assembly.Units[1].Open.X==unequal.Assembly.Units[0].Width+4,"All axes and unequal groups use actual extents, not a fixed 12-pen template");
        }
        var centered=Assembly();
        foreach(var unit in centered.Assembly.Units){unit.Closed.X-=48;unit.Closed.Y-=12;unit.Closed.Z-=88;unit.Open.X-=100;unit.Open.Y-=10;unit.Open.Z-=88;}
        AssemblyGeometry.Calculate(centered,limits);
        Check(centered.PenLayout.BoxWidth==concept.PenLayout.BoxWidth&&centered.PenLayout.BoxDepth==concept.PenLayout.BoxDepth&&centered.PenLayout.BoxHeight==concept.PenLayout.BoxHeight,"Changing coordinate origin must not invalidate or change a valid box");
        Check(centered.Assembly.Units[1].Open.X-centered.Assembly.Units[0].Open.X==104&&centered.Assembly.Units[1].Closed.Y-centered.Assembly.Units[0].Closed.Y==24,"Normalization must preserve all relative unit positions");
        string normalized=Json.Write(centered);AssemblyGeometry.Calculate(centered,limits);Check(Json.Write(centered)==normalized,"Coordinate normalization must be idempotent when reopening saved projects");
        var missing=Assembly();missing.Assembly.Units[1].Open=null;
        bool precise=false;try{AssemblyGeometry.Calculate(missing,limits);}catch(InvalidOperationException error){precise=error.Message.Contains("B")&&error.Message.Contains("Open");}Check(precise,"Missing positions identify the failing unit and state instead of treating them as negative coordinates");
        var centeredCollision=Assembly();centeredCollision.Assembly.Units[0].Closed.X=-48;centeredCollision.Assembly.Units[1].Closed.X=-48;centeredCollision.Assembly.Units[1].Closed.Y=1;
        Reject(()=>AssemblyGeometry.Calculate(centeredCollision,limits),"Origin normalization must not hide true intersections");
        input=Input();input.PenType="pencil";var pencil=input.Build();var pencilConcept=Assembly();AssemblyGeometry.Calculate(pencilConcept,pencil);
        Check(pencilConcept.Assembly.Units[0].Width==concept.Assembly.Units[0].Width&&pencil.ToPrompt().Contains("六角彩铅也按此包络宽度"),"Hexagonal pencils use the supplied maximum bounding width rather than a circular material-volume assumption");
        Check(!AssemblyGeometry.Contract(limits).Contains("LengthClearance:20,"),"Example length clearance must satisfy the current product diameter");
        var repeated=AssemblyGeometry.Requirements("白色；黑色；白色","","");
        Check(repeated.SequenceEqual(new[]{"白色","黑色","白色"}),"Repeated latest instructions must retain chronological precedence");
        foreach(string axis in new[]{"Width","Depth","Height"})
        {
            concept=Assembly();concept.Assembly.LengthAxis=axis;concept.Assembly.Orientation=axis=="Height"?"vertical":"horizontal";
            concept.Assembly.TotalClearance=4;concept.Assembly.BetweenGap=1;concept.Assembly.DividerThickness=0.5m;
            if(axis!="Height")concept.Assembly.Units[1].Closed=new Placement{Z=24};
            concept.Assembly.Units[1].Open=axis=="Width"?new Placement{Y=200}:new Placement{X=200};
            AssemblyGeometry.Calculate(concept,limits);
            foreach(bool open in new[]{false,true})foreach(var unit in concept.Assembly.Units)
            {
                var pens=AssemblyGeometry.PenBoxes(unit,concept.Assembly,limits,open).ToList();var pos=open?unit.Open:unit.Closed;
                Check(pens.Count==unit.Count&&pens.First().Index==1&&pens.Last().Index==unit.Count,"Every physical pen receives one stable numbered bounding box");
                Check(pens.All(p=>p.X>=pos.X+1&&p.Y>=pos.Y+1&&p.Z>=pos.Z+1&&p.X+p.Width<=pos.X+unit.Width-1&&p.Y+p.Depth<=pos.Y+unit.Depth-1&&p.Z+p.Height<=pos.Z+unit.Height-1),"Pen geometry must fit inside unit walls on all axes");
                Check(pens.All(p=>(axis=="Width"?p.Width:axis=="Depth"?p.Depth:p.Height)==150),"Drawn pens retain the complete 150 mm length in every orientation");
                decimal pitch=axis=="Width"?pens[1].Y-pens[0].Y:pens[1].X-pens[0].X;
                Check(pitch==16.5m,"Pen centres include diameter plus per-gap clearance plus divider thickness");
            }
        }
        Console.WriteLine("PASS: geometry quality checks (feasibility, capacities, labels and calculation contract)");
    }
}
