using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PaperStudio
{
    internal static class RequirementQualityChecks
    {
        static void Check(bool condition,string message){if(!condition)throw new Exception("Requirement quality: "+message);}
        static void Reject(Action action,string message)
        {bool rejected=false;try{action();}catch(DesignValidationException){rejected=true;}Check(rejected,message);}
        public static void Run()
        {
            var records=Json.Read<List<SourceRecord>>(File.ReadAllText("desktop/resources/knowledge.json",Encoding.UTF8));
            var clauses=new List<string>{"使用磁铁固定盒盖","改成纸插舌，不要磁铁","改成纸腰封固定，取消插舌；保留盒型和尺寸"};
            var chain=WorkflowFixtures.Understanding(clauses,records);
            chain.Requirements[0].Status="superseded";chain.Requirements[0].SupersededBy="R2";
            chain.Requirements[1].Status="superseded";chain.Requirements[1].SupersededBy="R3";
            Check(DesignMethod.ReconcileRequirements(chain,clauses)==0,"valid successive edits need no ambiguous fallback");
            Check(chain.Requirements[0].Status=="superseded"&&chain.Requirements[0].SupersededBy=="R3"&&chain.Requirements[1].SupersededBy=="R3","successive edits resolve to the final current requirement, never reactivate old magnets");
            DesignMethod.ValidateUnderstanding(chain,clauses,records);

            var repeated=new List<string>{"保留盒型和尺寸","只改配色","保留盒型和尺寸"};
            var duplicate=WorkflowFixtures.Understanding(repeated,records);
            duplicate.Requirements[0].SupersededBy="R2";
            Check(DesignMethod.ReconcileRequirements(duplicate,repeated)==0,"identical original clauses with unique IDs remain correctly aligned");
            Check(duplicate.Requirements.All(r=>r.Status=="active")&&duplicate.Requirements[0].Kind=="keep"&&duplicate.Requirements[2].Kind=="keep","repeated keep constraints are not weakened into generic fallbacks");
            Check(String.IsNullOrEmpty(duplicate.Requirements[0].SupersededBy),"active keep constraints cannot carry a misleading superseded pointer");

            var cycle=WorkflowFixtures.Understanding(clauses,records);
            cycle.Requirements[0].Status="superseded";cycle.Requirements[0].SupersededBy="R2";
            cycle.Requirements[1].Status="superseded";cycle.Requirements[1].SupersededBy="R1";
            DesignMethod.ReconcileRequirements(cycle,clauses);
            Check(cycle.Requirements[0].Status=="active"&&cycle.Requirements[1].Status=="active","cyclic or backward supersession cannot delete user requirements");

            var current=WorkflowFixtures.Understanding(new List<string>{"保留盒型，只把红色改成蓝色；不要改变笔数"},records);
            var concepts=new List<Concept>{new Concept{Name="纸腰封展示盒"}};
            var review=WorkflowFixtures.Review(concepts,current);
            review.Concepts[0].Requirements.Add(new RequirementVerdict{Id="R1",Met=false,Evidence="改动了盒型"});
            Reject(()=>DesignMethod.ValidateReview(review,concepts,current),"a contradictory duplicate cannot hide behind another passing verdict");
            review=WorkflowFixtures.Review(concepts,current);review.Concepts[0].Requirements.Add(new RequirementVerdict{Id="R99",Met=true,Evidence="invented requirement"});
            Reject(()=>DesignMethod.ValidateReview(review,concepts,current),"unknown review IDs cannot impersonate real requirements");

            var material=WorkflowFixtures.Manufacturing(records,1);
            var inconsistent=Json.Copy(material.Materials[0]);inconsistent.Part="右侧内托";inconsistent.ThicknessMm=2;material.Materials.Add(inconsistent);
            Reject(()=>DesignMethod.ValidateManufacturing(new Concept{Manufacturing=material,Assembly=new AssemblyPlan{WallThickness=1}},records),"all calculated inner tray walls must use the same thickness as geometry");
            material.Materials.Last().ThicknessMm=1;
            DesignMethod.ValidateManufacturing(new Concept{Manufacturing=material,Assembly=new AssemblyPlan{WallThickness=1}},records);
            Console.WriteLine("PASS: chained edits, repeated keep constraints, cyclic supersession, unambiguous review verdicts and all-wall material consistency.");
        }
    }
}
