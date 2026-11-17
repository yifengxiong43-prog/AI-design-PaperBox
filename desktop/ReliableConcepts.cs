using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperStudio
{
    public static class ConceptDiversity
    {
        static bool ContainsAny(string text,params string[] words)
        {return words.Any(word=>text.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);}
        static string Signature(Concept concept)
        {
            var plan=concept.Assembly;
            if(plan==null||plan.Units==null||plan.Parts==null)throw new DesignValidationException("方案缺少可比较的结构部件。");
            var roles=plan.Parts.Where(p=>p!=null&&!String.IsNullOrWhiteSpace(p.Role)).Select(p=>p.Role.Trim()).Distinct().OrderBy(s=>s);
            var motions=plan.Units.Where(u=>u!=null&&!String.IsNullOrWhiteSpace(u.Motion)).Select(u=>u.Motion.Trim()).Distinct().OrderBy(s=>s);
            string physical=String.Join(";",roles)+"|"+String.Join(";",motions);
            if(ContainsAny(physical,"抽屉","滑轨","drawer")&&ContainsAny(physical,"抽出","滑出","slide"))return "carrier:drawer";
            if(ContainsAny(physical,"连体折页","铰链","转轴","hinge")&&ContainsAny(physical,"翻转","旋转","展开翼","pivot"))return "carrier:connected-wing";
            if(ContainsAny(physical,"独立单排","可取出","removable")&&ContainsAny(physical,"取出","拿出","拆出","remove"))return "carrier:removable";
            string[] families={"纸套|抽套|滑套|sleeve","撑片|支撑片|kickstand","铰链|联动|连体|hinge","抽屉|滑轨|drawer","翻盖|掀盖|铰接盖","外盖|盖板|取盖|取下盖|可取盖","托盘|内托|展示盘|tray"};
            string mechanism=String.Join("",families.Select((terms,i)=>terms.Split('|').Any(term=>physical.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)?"1":"0"));
            if(mechanism=="0000000")mechanism=physical;
            return (plan.Orientation??"")+"|"+(plan.LengthAxis??"")+"|"+mechanism;
        }
        public static bool HasRepeatedMechanism(List<Concept> concepts)
        {
            if(concepts==null||concepts.Count<2||concepts.Any(c=>c==null||c.Assembly==null||c.Assembly.Units==null||c.Assembly.Parts==null))return false;
            return concepts.Select(Signature).Distinct(StringComparer.Ordinal).Count()!=concepts.Count;
        }
        public static void EnsureDistinct(List<Concept> concepts)
        {
            if(HasRepeatedMechanism(concepts))
                throw new DesignValidationException("多个方案只改变分组数、名称或外观，实际盒体部件与开合动作相同；请重新构思不同纸结构。");
        }
    }
    // The first two are conventional box mechanics; the connected-wing direction remains a concept until sampled.
    public static class ReliableConcepts
    {
        public static string EffectPrompt(Concept concept,PackagingConstraints limits)
        {
            if(concept==null||limits==null||concept.Assembly==null||concept.Assembly.Orientation!="vertical"||
                (concept.DesignOrigin!="controlled-drawer"&&concept.DesignOrigin!="controlled-case"))return concept==null?"":concept.ImagePrompt;
            int groups=concept.Assembly.Units.Count;
            string direction=concept.DesignOrigin=="controlled-drawer"
                ?"PAPERBOARD DRAWER CABINET: show the closed cabinet with separate drawer fronts and the same "+groups+" paper drawers completely slid out, standing upright side by side on the desk. Keep each drawer front, pull tab and visible paper support base; show the empty cabinet beside them. "
                :"PAPERBOARD CARRY CASE: show the closed case, its paper lid and the same "+groups+" removable paper modules standing upright side by side on the desk. Keep the empty case beside them. ";
            return direction+"All "+limits.MarkerCount+" full-length pens remain upright in every view, never laid horizontally in shallow trays. Match each module ID, count, complete pen length and package proportions to the selected sketch and calculated ledger. Base stability and opening force await a physical mockup.";
        }
        static SourceRecord Find(List<SourceRecord> sources,string id)
        {return sources.FirstOrDefault(s=>s.Id==id&&!String.IsNullOrWhiteSpace(s.StableId));}

        static RiskControl Control(SourceRecord source,string risk,string action,string test)
        {return new RiskControl{SourceId=source.Id,StableSourceId=source.StableId,SourceHash=CitationGuard.Hash(source),Risk=risk,Action=action,Test=test};}

        public static DesignResponse Create(PackagingConstraints limits,DesignUnderstanding understanding,List<SourceRecord> sources)
        {
            limits.Validate();
            if(understanding==null||sources==null)throw new InvalidOperationException("缺少已核验的需求或资料，不能交付概念方向。");
            if((understanding.Requirements??new List<InterpretedRequirement>()).Any(r=>r!=null&&r.Status=="active"&&(r.Kind=="must"||r.Kind=="keep")&&new[]{"不可拆","不能拆","不拆散","一体连接","连体","必须联动","必须折叠","必须抽屉"}.Any(word=>(r.Original??"").Contains(word))))
                throw new InvalidOperationException("用户明确要求连体或指定开合机构，不能改用独立托盘概念。");
            understanding=Json.Copy(understanding);
            foreach(var evidence in understanding.Evidence??new List<SourceEvidence>()){
                var bound=CitationGuard.Resolve(sources,evidence.SourceId,evidence.StableSourceId,evidence.SourceHash);
                evidence.SourceId=bound.Id;evidence.StableSourceId=bound.StableId;evidence.SourceHash=CitationGuard.Hash(bound);
            }
            var material=Find(sources,"纸质产品包装盒材料工艺配件汇总表/材料汇总/5");
            var process=Find(sources,"纸质产品包装盒材料工艺配件汇总表/工艺适配/17");
            var risk=Find(sources,"包装案例与问题收集/包装问题记录/4");
            var test=Find(sources,"合规与测试要求");
            var result=new DesignResponse{Brief=DesignMethod.Summary(understanding),Missing="概念方案已生成，工程待验证：实笔装载、儿童操作、提携承重和运输测试需要白样确认。",Concepts=new List<Concept>()};
            int[] requestedGroups={6,4,3};
            string[] names={"六格便携色谱箱","四屉桌面选色柜","三翼连体创意画廊"};
            string[] themes={"取下圆角箱盖，六个独立分盒按两列三行铺开；闭合后成为可提携纸箱。","四个带独立拉手的纸抽屉从正面滑出，完全抽离后按两列两行成为桌面选色盘。","三个叶形笔翼通过纸折页连在同一中央书脊，展开成桌面画廊；折页运动与支撑待白样验证。"};
            for(int index=0;index<requestedGroups.Length;index++)
            {
                int groups=requestedGroups[index];
                if(limits.MarkerCount<groups||limits.MarkerCount%groups!=0)throw new InvalidOperationException("当前支数不能均分为受控展示单元；保留原方案等待结构修正。");
                int perGroup=limits.MarkerCount/groups;
                bool vertical=limits.PenType=="acrylic";
                bool rotated=index==1&&!vertical;
                string deskAction=vertical?"立放桌面，完整笔身保持直立，底部支撑待白样验证":"平放桌面";
                var plan=new AssemblyPlan{
                    Orientation=vertical?"vertical":"horizontal",LengthAxis=vertical?"Height":rotated?"Depth":"Width",
                    TotalClearance=4m,BetweenGap=0m,DividerThickness=0m,WallThickness=1m,
                    LengthClearance=limits.MinimumLengthClearanceMm,
                    Assumptions="4mm为每排总余量；1mm单元壁厚和1mm外壳厚度是设计选值，须按实物、纸板和白样复核。"+(index==2?"连体折页的转动、支撑与儿童操作均为待验证概念。":index==1?"抽屉导向、止退和完全抽离动作须做白样。":"分盒取出后独立平放；提携强度须做白样。"),
                    Shell=new ShellPlan{WallThickness=1m,AssemblyClearance=2m,InternalReserve=new Size3(),ExternalReserve=new Size3(),Basis=index==2?"三个连体笔翼闭合层叠；折页在闭合态沿侧边包覆，另留每侧2mm装配余量。实际折线轨迹待白样。":index==1?"四个抽屉分层置于柜壳；外壳厚度和每侧2mm装配余量独立预算，抽屉开口及止退待白样。":"六个独立分盒层叠放入箱体；外壳厚度另计，每侧2mm装配余量。"},
                    OpenReserve=new Size3{Width=index==2?12m:limits.MarkerLength+limits.MinimumLengthClearanceMm+12m,Depth=index==2?20m:0m},
                    OpenReserveBasis=index==2?"中央书脊及折页展开侧向预留12mm，后部支撑预留20mm；实际占地与折页运动待白样。":"取下的外盖或抽屉柜壳摆在选色单元末端；实际桌面占地须白样复核。",
                    Units=new List<DisplayUnit>(),Parts=new List<AssemblyPart>()
                };
                decimal row=AssemblyGeometry.Span(perGroup,limits.MarkerDiameter,plan.TotalClearance)+2*plan.WallThickness;
                decimal thin=limits.MarkerDiameter+plan.TotalClearance+2*plan.WallThickness;
                decimal longSide=limits.MarkerLength+plan.LengthClearance+2*plan.WallThickness;
                decimal unitWidth=vertical||rotated?row:longSide,unitDepth=vertical?thin:rotated?longSide:row,unitHeight=vertical?longSide:thin;
                for(int i=0;i<groups;i++)
                {
                    var unit=new DisplayUnit{Id=((char)('A'+i)).ToString(),Count=perGroup,Rows=1,Columns=perGroup,
                        Motion=index==1?"沿独立纸滑轨将抽屉完全滑出，握住圆角拉手，抽出后"+deskAction+"。":index==2?"连体笔翼围绕中央书脊的纸折页翻转展开，三个笔翼始终连接，不可取出。":"取下外盖，手动取出独立单排分盒并"+deskAction+"；无铰链或滑轨。",
                        Closed=new Placement{Y=vertical?i*(unitDepth+1m):0m,Z=vertical?0m:i*(unitHeight+1m)},
                        Open=new Placement{X=vertical||index==2?i*(unitWidth+4m):(i%2)*(unitWidth+4m),Y=vertical||index==2?0m:(i/2)*(unitDepth+4m),Z=0m}};
                    plan.Units.Add(unit);
                    plan.Parts.Add(new AssemblyPart{Id=index==1?"抽屉"+unit.Id:index==2?"连体笔翼"+unit.Id:"分盒"+unit.Id,Role=index==1?"带正面拉手的单排纸抽屉与低挡边":index==2?"与书脊相连的单排叶形笔翼与低挡边":"独立单排分盒与低挡边",Width=unitWidth,Depth=unitDepth,Height=unitHeight,
                        SizeBasis="formed",ClosedSize=new Size3{Width=unitWidth,Depth=unitDepth,Height=unitHeight},PlacementScope="internal",
                        AssemblyNotes=index==2?"笔翼侧边与中央书脊折页连续连接；闭合时层叠，展开时保持连体。前挡边不得遮住完整笔身；转动间隙和抗倒待白样。":index==1?"抽屉在独立导向槽内滑出，可完整抽离放在桌面；拉手圆角、限位和儿童拉力待白样。":"分盒独立放入箱体，打开后逐盒取出；低挡边留取笔缺口，完整笔身须可见。"});
                    if(index==1)plan.Parts.Add(new AssemblyPart{Id="抽屉导向"+unit.Id,Role="纸抽屉滑轨与止退折片",Width=8m,Depth=unitDepth/2m,Height=1m,
                        SizeBasis="flat",ClosedSize=new Size3{Width=8m,Depth=unitDepth/2m,Height=1m},PlacementScope="internal",
                        AssemblyNotes="沿柜壳内侧与抽屉"+unit.Id+"对应层粘接，8mm导向折片是设计选值；抽出阻力、止退和脱轨须白样验证。"});
                    if(index==2&&i>0)plan.Parts.Add(new AssemblyPart{Id="连体折页"+unit.Id,Role="相邻笔翼之间的连体折页铰链",Width=vertical?unitDepth+4m:unitHeight+4m,Depth=vertical?1m:unitDepth,Height=1m,
                        SizeBasis="flat",ClosedSize=vertical?new Size3{Width=unitDepth+4m,Depth=1m,Height=unitHeight}:new Size3{Width=unitHeight+4m,Depth=unitDepth,Height=1m},PlacementScope="internal",
                        AssemblyNotes="折页连续连接笔翼"+((char)('A'+i-1))+"与"+unit.Id+"，跨越闭合层高并沿侧边交替压痕；折页宽度按单元厚度加4mm设计，实际弯折轨迹须白样核验。"});
                }
                var concept=new Concept{DesignOrigin=index==1?"controlled-drawer":index==2?"controlled-wing":"controlled-case",Name=names[index],Idea=vertical&&index==1?"四个纸抽屉从正面滑出并排直立展示，笔身始终直立，桌面支撑待白样验证。":vertical&&index==0?"取下圆角箱盖，六个分盒并排直立展示；闭合后成为可提携纸箱，底部支撑待白样验证。":themes[index],Structure=index==1?"四层正面开口纸抽屉柜，顶板内切平齐手指提孔；"+groups+"个带圆角拉手的独立抽屉，每屉"+perGroup+"支，抽屉槽与止退折片各自对应。":index==2?"中央纸书脊与"+groups+"个不可拆的连体叶形笔翼；每翼"+perGroup+"支，交替折页闭合成册。":"圆角可提携纸箱、可取外盖与"+groups+"个独立单排分盒；每盒"+perGroup+"支。",
                    Opening=index==1?"从正面依次抽出四个抽屉，完全抽离后"+(vertical?"并排直立放置，笔身保持竖直":"按两列两行平放")+"；收纳时按编号沿导向槽推回，再合上运输锁片。":index==2?"打开外盖，从中央书脊把三个连体笔翼逐片翻转展开；笔翼始终与折页相连，闭合时逆序折叠。":"提手箱盖抬起，取出六个独立分盒并"+(vertical?"并排直立放置，笔身保持竖直":"按两列三行平放")+"；逐盒放回并盖合。",
                    Materials=material==null?"材料依据缺失，暂不推荐具体材料。":"白卡纸 / 白板纸作为外壳与内托候选；来源："+material.Source+"。板厚、承重和模切适配须打样。",
                    Innovation=themes[index],Difficulty=index==1?"抽屉盒型可参考成熟纸抽屉结构；导向、止退、拉手和儿童开启力仍须白样验证。":index==2?"连体折页、转动轨迹和桌面支撑尚未验证；这是探索性概念，不声明可量产。":"可拆分盒与外盖工序相对直接；取笔缺口、提携和纸板挺度仍须白样验证。",
                    CostNote="未获得供应商报价；托盘数量、模切面积和印刷面积是主要成本因素。",
                    Risks=index==1?"抽屉过紧会卡住、过松会滑出；拉手和抽屉止退、儿童操作与运输跌落尚未测试。":index==2?"折页可能撕裂或使笔翼干涉，展开后可能倾倒；儿童操作和运输跌落尚未测试。":"分盒可能松动，提携开孔可能撕裂；儿童操作和运输跌落尚未测试。",
                    Checks="用实笔检查每单元"+perGroup+"支、完整笔长、低挡边遮挡、闭合与展开，并对白样做"+(index==1?"抽屉滑移与止退":index==2?"折页轨迹、抗倒与折线寿命":"提携承重")+"、跌落振动检查。",
                    ImagePrompt=(index==1?"DIRECTION 2, DRAWER CABINET: closed front elevation visibly has FOUR separate drawer fronts and rounded pull tabs, plus a flush die-cut finger carry aperture within the top paperboard panel; opened view has four drawers completely slid out of the cabinet, placed in a 2 by 2 grid. Show the cabinet body beside them. NO lift-off lid, NO sleeve, NO generic trays masquerading as drawers. ":index==2?"DIRECTION 3, CONNECTED WING FOLIO: closed view is a rounded leaf-shaped PAPERBOARD folio with a central spine and a flush die-cut finger carry aperture; opened view shows exactly THREE wide paper wings still physically connected to that spine by visible alternating scored-paper hinges. Show rear support details; no textile edging, zipper, stitched seams, detached trays or drawer cabinet. This is an engineering-unverified concept. ":"DIRECTION 1, MODULAR CARRY CASE: closed view is a rounded upright paper carry case with a single lift-off lid and a flush finger slot cut within the lid outline, no protruding handle; opened view shows SIX removable near-square shallow color modules in a 2 by 3 desk grid, with the empty case and lid beside them. NO drawers, NO connected wings. ")+"Exactly "+perGroup+" full-length pens in each of "+groups+" separate single-row units, all visible simultaneously. Preserve the same parts in closed and open states. No hidden rows or shortened pens. Engineering remains unverified.",
                    Assembly=plan,Understanding=Json.Copy(understanding),RequirementChecks=new List<RequirementCheck>(),
                    EngineeringIssues=new List<string>{index==2?"工程待验证：连体折页的运动轨迹、折线寿命、桌面抗倒与连接强度尚未白样验证；不得宣称可量产或合规。":"工程待验证：纸板实际厚度、儿童开启力、提携承重、运输保护和生产刀模均未通过白样或测试。"},
                    ConceptType=index==0?DesignStrategy.Mature:index==1?DesignStrategy.Functional:DesignStrategy.Exploratory,
                    StructuralFamily=index==0?"可取盖分盒":index==1?"抽屉柜体":"联动折叠翼",
                    OpeningMechanism=index==0?"掀盖取出":index==1?"抽屉滑出":"折页展开",
                    InternalLayout=index==0?"分盒平铺":index==1?"抽屉分层":"翼片展开",
                    DesktopUse=index==0?(vertical?"分盒直立展示":"分盒平放"):index==1?"抽屉陈列":"支撑展示",
                    Portability=index==0?"提箱携带":index==1?"柜体携带":"折叠携带",
                    CreativityLevel=index==0?"适度":index==1?"中等":"高",
                    FeasibilityStatus=index==2?"创意探索，需进一步工程验证":"概念可打样，工程仍需验证",
                    ReferenceSources=(sources??new List<SourceRecord>()).Where(s=>s!=null&&!String.IsNullOrWhiteSpace(s.StableId)).Select(s=>s.StableId).Distinct().Take(8).ToList(),
                    SketchReferenceId="calculated-sketch:"+names[index]};
                if(vertical&&index!=2)concept.ImagePrompt=EffectPrompt(concept,limits);
                foreach(var requirement in understanding.Requirements??new List<InterpretedRequirement>())
                    concept.RequirementChecks.Add(new RequirementCheck{RequirementId=requirement.Id,Requirement=requirement.Original,Implementation="概念处理："+requirement.Meaning+"；具体落实和与其他要求的兼容性需结合白样人工确认。"});
                AssemblyGeometry.Calculate(concept,limits);
                var box=concept.PenLayout;
                plan.Parts.Add(new AssemblyPart{Id="外壳",Role=index==1?"四层正面开口抽屉柜壳":index==2?"中央书脊与外部闭合围护":"底部与四侧围护箱体",Width=box.BoxWidth,Depth=box.BoxDepth,Height=box.BoxHeight,SizeBasis="formed",
                    ClosedSize=new Size3{Width=box.BoxWidth,Depth=box.BoxDepth,Height=box.BoxHeight},PlacementScope="shell",AssemblyNotes=index==1?"柜壳正面有四个独立抽屉口，顶板内切平齐手指提孔；每层导向折片、止退、孔边补强与儿童拉力须白样验证。":index==2?"书脊固定三片笔翼的折页端，顶板内切平齐提孔；闭合压紧、孔边强度与展开抗倒均须白样验证。":"独立围护分盒，壳底与侧壁相连；提携孔边和结构强度须白样验证。"});
                plan.Parts.Add(index==1?new AssemblyPart{Id="运输锁片",Role="闭合抽屉正面的可开启纸锁片",Width=box.BoxWidth,Depth=1m,Height=box.BoxHeight,SizeBasis="formed",
                    ClosedSize=new Size3{Width=box.BoxWidth,Depth=1m,Height=box.BoxHeight},PlacementScope="shell",AssemblyNotes="锁片沿柜壳侧边压痕连接，关闭时挡住四个抽屉正面；开启力与止退须白样验证。"}:new AssemblyPart{Id="外盖",Role=index==2?"包覆连体笔翼的可取盖板":"可取下的闭合盖与手提开孔",Width=box.BoxWidth,Depth=box.BoxDepth,Height=1m,SizeBasis="flat",
                    ClosedSize=new Size3{Width=box.BoxWidth,Depth=box.BoxDepth,Height=1m},PlacementScope="shell",AssemblyNotes=index==2?"盖板包覆折叠后的连体笔翼，打开时置于书脊旁；是否需要独立锁扣待白样。":"盖板盖合箱体顶面；开孔边缘需圆角和补强，承重及儿童拉力须测试。"});
                AssemblyGeometry.Calculate(concept,limits);
                if(material!=null&&process!=null&&risk!=null&&test!=null){
                    concept.Manufacturing=new ManufacturingPlan{
                        Materials=new List<MaterialChoice>{new MaterialChoice{Part=index==1?"纸抽屉":index==2?"连体笔翼":"独立分盒",Role="unit-wall",SourceId=material.Id,StableSourceId=material.StableId,SourceHash=CitationGuard.Hash(material),MaterialName="白卡纸 / 白板纸",Quantity=groups,ThicknessMm=1m,ThicknessBasis="设计假设；以实际纸板厚度和挺度复核",Purpose="单排定位与桌面陈列",ProcessSourceIds=new List<string>{process.Id},StableProcessIds=new List<string>{process.StableId}},new MaterialChoice{Part=index==1?"抽屉柜壳与运输锁片":index==2?"书脊与外盖":"箱体与外盖",Role="shell",SourceId=material.Id,StableSourceId=material.StableId,SourceHash=CitationGuard.Hash(material),MaterialName="白卡纸 / 白板纸",Quantity=2,ThicknessMm=1m,ThicknessBasis="设计假设；提携承重须打样",Purpose="闭合收纳与图文印刷",ProcessSourceIds=new List<string>{process.Id},StableProcessIds=new List<string>{process.StableId}}},
                        AssemblySteps=new List<string>{index==1?"模切成形正面开口柜壳及运输锁片。":index==2?"模切成形书脊和三片叶形笔翼。":"模切成形圆角箱体和可取外盖。",index==1?"每个抽屉与对应导向折片独立装配并编号。":index==2?"按相邻边压痕连接三片笔翼，检查闭合层叠轨迹。":"按单排尺寸成形并编号六个分盒。",index==1?"装入实笔，逐屉推进对应层并合上运输锁片。":index==2?"装入实笔，依次折叠笔翼并盖合；开合轨迹待白样。":"装入实笔，逐盒放入箱体并合盖。"},
                        Connections=index==1?"四个抽屉各有独立导向折片及正面拉手；柜壳侧边运输锁片防止滑出，阻力和止退待白样。":index==2?"三片笔翼通过交替压痕的连体折页接至中央书脊；转动轨迹、强度和支撑待白样。":"六个分盒独立放置，不设连体铰链；外盖扣合箱体，开孔承重待测。",
                        OpeningSequence=index==1?"掀开运输锁片，四屉各自沿导向槽滑出并"+(vertical?"并排直立展示，底部支撑待白样":"按两列两行平放")+"；逆序推回。":index==2?"取下外盖，逐片翻转连体笔翼，展开时保持与书脊连接；逆序折回。":"取下外盖，六盒"+(vertical?"并排直立展示，底部支撑待白样":"按两列三行独立平放")+"；逆序收纳。",
                        Stability=index==2?"连体笔翼与书脊落桌，后部支撑和抗倒须白样验证。":"每个分盒或抽屉独立落桌，低挡边与底面需白样检验防倒与取笔。",
                        Protection="闭合后托盘轴向定位和外壳抗压待跌落、振动测试。",
                        CostDrivers="托盘数量、模切面积和印刷面积影响成本，尚无报价。",
                        SampleChecks=new List<string>{"实笔装载与完整笔长",index==1?"抽屉抽出阻力与止退":index==2?"连体折页运动与压痕寿命":"盖合及提携承重","全部笔可见、取笔与儿童操作"},
                        Risks=new List<RiskControl>{Control(risk,"内部空隙导致运输碰撞","调整各笔位定位与外壳间隙","按原问题记录做运输白样验证")},
                        Tests=new List<RiskControl>{Control(test,"运输跌落与振动待确认","按实际毛重确定适用条件","按内置合规测试记录执行，不能预称通过")}
                    };
                    DesignMethod.ValidateManufacturing(concept,sources);
                }
                result.Concepts.Add(concept);
            }
            result.Validate(3);ConceptDiversity.EnsureDistinct(result.Concepts);
            return result;
        }
    }
}
