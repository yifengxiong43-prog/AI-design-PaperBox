using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperStudio
{
    public static class DesignStrategy
    {
        public const string Mature = "成熟结构优化型";
        public const string Functional = "功能创新型";
        public const string Exploratory = "创意探索型";

        static readonly string[] Required = { Mature, Functional, Exploratory };

        public static string Prompt()
        {
            return "\n全局三方向设计策略（每个新项目重新选结构，不固定盒型）："+
                "方案A的ConceptType必须是‘"+Mature+"’：优先使用本轮检索到的成熟盒型、开合和工艺，适度优化便利性、成本和风险。"+
                "方案B的ConceptType必须是‘"+Functional+"’：核心承载、开合或功能转换必须与A有实质差异，探索桌面展示、携带、收纳或互动功能，但仍以可打样工艺为基础。"+
                "方案C的ConceptType必须是‘"+Exploratory+"’：允许变形、折叠、模块化或场景转换，必须解释纸结构原理和连接关系；标记工程待验证，不能声称已量产。"+
                "不能按A/B/C固定套用某个盒型，先根据产品参数、数量、用户场景和真实资料动态选择StructuralFamily。"+
                "每个方案必须返回ConceptType、StructuralFamily、OpeningMechanism、InternalLayout、DesktopUse、Portability、CreativityLevel、FeasibilityStatus、ReferenceSources、SketchReferenceId。"+
                "ConceptType只描述策略角色；StructuralFamily描述实际承载结构；OpeningMechanism描述实际动作；InternalLayout描述真实排列。只改颜色、图案、提手位置、分组数量或名称不算结构差异。正式输出前必须比较主体盒型、开合机制、内部装载、桌面使用和携带方式；每对方案至少有两个核心维度不同，且至少一个差异来自主体盒型或开合机制。若语义归一化后仍属于同一抽屉/托盘/取盖结构，必须重构重复方向。所有方向都必须说明纸板模切、压痕、插舌/粘合/卡扣、支撑和装配路径。";
        }

        public static void Validate(List<Concept> concepts, bool requireSketchReference=false)
        {
            if (concepts == null || concepts.Count != 3)
                throw new DesignValidationException("必须返回A、B、C三套设计方向。");
            foreach (var concept in concepts)
            {
                if (concept == null || Required.Any(s => String.Equals(s, concept.ConceptType, StringComparison.Ordinal)) == false)
                    throw new DesignValidationException("每套方案必须明确成熟结构优化型、功能创新型或创意探索型定位。");
                if (String.IsNullOrWhiteSpace(concept.StructuralFamily) || String.IsNullOrWhiteSpace(concept.OpeningMechanism) ||
                    String.IsNullOrWhiteSpace(concept.InternalLayout) || String.IsNullOrWhiteSpace(concept.DesktopUse) ||
                    String.IsNullOrWhiteSpace(concept.Portability) || String.IsNullOrWhiteSpace(concept.CreativityLevel) ||
                    String.IsNullOrWhiteSpace(concept.FeasibilityStatus))
                    throw new DesignValidationException("方案“"+concept.Name+"”缺少结构策略字段，不能判断三套方案是否真正不同。");
                if (concept.ReferenceSources == null) concept.ReferenceSources = new List<string>();
                if (requireSketchReference && String.IsNullOrWhiteSpace(concept.SketchReferenceId))
                    throw new DesignValidationException("方案“"+concept.Name+"”缺少草图引用标识，不能进入效果图阶段。");
            }
            if (Required.Any(type => concepts.Count(c => c.ConceptType == type) != 1))
                throw new DesignValidationException("三套方案必须各自承担一个不同的策略角色：成熟、功能创新、创意探索。");
            for (int i=0;i<concepts.Count;i++)
                for (int j=i+1;j<concepts.Count;j++)
                    if (DifferenceCount(concepts[i], concepts[j]) < 2 || (Same(FamilyKey(concepts[i]),FamilyKey(concepts[j])) && Same(OpeningKey(concepts[i]),OpeningKey(concepts[j]))))
                        throw new DesignValidationException("方案“"+concepts[i].Name+"”与“"+concepts[j].Name+"”的核心结构、开合或使用方式过于相似；需要重新选择结构方向。");
            ConceptDiversity.EnsureDistinct(concepts);
        }

        static int DifferenceCount(Concept a, Concept b)
        {
            int different=0;
            if (!Same(FamilyKey(a),FamilyKey(b))) different++;
            if (!Same(OpeningKey(a),OpeningKey(b))) different++;
            if (!Same(LayoutKey(a),LayoutKey(b))) different++;
            if (!Same(UseKey(a),UseKey(b))) different++;
            if (!Same(PortabilityKey(a),PortabilityKey(b))) different++;
            return different;
        }

        static string FamilyKey(Concept c){return FamilyKey(c==null?null:c.StructuralFamily+" "+(c.Structure??""));}
        static string FamilyKey(string value){var t=(value??"").ToLowerInvariant();if(t.Contains("抽屉")||t.Contains("drawer")||t.Contains("滑轨"))return "drawer";if(t.Contains("连体")||t.Contains("折页")||t.Contains("翼")||t.Contains("铰链")||t.Contains("hinge"))return "connected-fold";if(t.Contains("套")||t.Contains("sleeve"))return "sleeve";if(t.Contains("分盒")||t.Contains("模块")||t.Contains("可取")||t.Contains("removable")||t.Contains("提箱"))return "removable-case";if(t.Contains("托盘")||t.Contains("内托")||t.Contains("tray"))return "tray";return t.Trim();}
        static string OpeningKey(Concept c){var t=(c==null?null:c.OpeningMechanism+" "+(c.Opening??"")).ToLowerInvariant();if(t.Contains("抽")||t.Contains("滑")||t.Contains("drawer")||t.Contains("slide"))return "slide-out";if(t.Contains("折页")||t.Contains("翻转")||t.Contains("联动")||t.Contains("hinge"))return "fold-turn";if(t.Contains("取盖")||t.Contains("抬盖")||t.Contains("掀盖")||t.Contains("翻盖")||t.Contains("lift-off"))return "lift-lid";if(t.Contains("套")||t.Contains("sleeve"))return "sleeve-open";return t.Trim();}
        static string LayoutKey(Concept c){var t=(c==null?null:c.InternalLayout+" "+(c.Layout??"")).ToLowerInvariant();if(t.Contains("抽屉")||t.Contains("drawer"))return "drawer-layers";if(t.Contains("翼")||t.Contains("扇")||t.Contains("连体"))return "connected-wings";if(t.Contains("分盒")||t.Contains("模块")||t.Contains("独立"))return "removable-modules";if(t.Contains("网格")||t.Contains("矩阵")||t.Contains("grid"))return "grid";return t.Trim();}
        static string UseKey(Concept c){var t=(c==null?null:c.DesktopUse+" "+(c.Idea??"")).ToLowerInvariant();if(t.Contains("抽屉")||t.Contains("滑出"))return "drawer-display";if(t.Contains("翼")||t.Contains("画廊")||t.Contains("支撑"))return "folio-display";if(t.Contains("分盒")||t.Contains("模块"))return "module-display";return t.Trim();}
        static string PortabilityKey(Concept c){var t=(c==null?null:c.Portability??"").ToLowerInvariant();if(t.Contains("提")||t.Contains("carry"))return "carry-case";if(t.Contains("折叠")||t.Contains("folio"))return "fold-flat";if(t.Contains("柜")||t.Contains("cabinet"))return "cabinet";return t.Trim();}

        static bool Same(string a,string b)
        {return String.Equals((a??"").Trim(),(b??"").Trim(),StringComparison.OrdinalIgnoreCase);}

        // Used only by the deterministic recovery path when a provider cannot return a valid draft.
        // The normal path still selects the families from the current request and retrieved evidence.
        public static void ApplyRecoveryRole(Concept concept,int index,List<SourceRecord> sources)
        {
            if (concept == null) throw new ArgumentNullException("concept");
            concept.ConceptType = index == 0 ? Mature : index == 1 ? Functional : Exploratory;
            concept.CreativityLevel = index == 0 ? "适度" : index == 1 ? "中等" : "高";
            concept.FeasibilityStatus = index == 2 ? "创意探索，需进一步工程验证" : "概念可打样，工程仍需验证";
            concept.StructuralFamily = concept.Structure;
            concept.OpeningMechanism = concept.Opening;
            concept.InternalLayout = concept.Layout;
            concept.DesktopUse = concept.Idea;
            concept.Portability = concept.Opening;
            concept.ReferenceSources = (sources??new List<SourceRecord>()).Where(s=>s!=null&&!String.IsNullOrWhiteSpace(s.StableId)).Select(s=>s.StableId).Distinct().Take(8).ToList();
            concept.SketchReferenceId = "calculated-sketch:"+(concept.Name??"concept");
        }
    }
}
