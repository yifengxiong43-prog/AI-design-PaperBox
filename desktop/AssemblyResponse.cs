using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaperStudio
{
    // Validate the raw API response before typed deserialization can default missing axes to zero.
    public static class AssemblyResponse
    {
        static InvalidOperationException Invalid(string context,string path)
        {return new InvalidOperationException(context+"："+path+" 必须明确提供唯一的毫米坐标；位置需包含 X、Y、Z，不能缺失、重复或使用范围及其他单位。");}
        static string Label(Dictionary<string,object> value,string key,string fallback)
        {
            var pair=value.FirstOrDefault(p=>String.Equals(p.Key,key,StringComparison.OrdinalIgnoreCase));
            return pair.Value is string&&!String.IsNullOrWhiteSpace((string)pair.Value)?(string)pair.Value:fallback;
        }
        static string Key(Dictionary<string,object> value,string name,string context,string path,params string[] aliases)
        {
            var matches=value.Keys.Where(k=>String.Equals(k,name,StringComparison.OrdinalIgnoreCase)||aliases.Any(a=>String.Equals(k,a,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(matches.Length>1)throw Invalid(context,path);
            return matches.Length==0?null:matches[0];
        }
        static decimal Number(object value,string context,string path)
        {
            var text=value as string;decimal number;
            if(text!=null)
            {
                var match=Regex.Match(text,@"^\s*([+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))\s*(?:mm|毫米)?\s*$",RegexOptions.IgnoreCase);
                if(match.Success&&Decimal.TryParse(match.Groups[1].Value,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number))return number;
            }
            else if(value is decimal||value is int||value is long||value is double||value is float||value is short||value is byte||value is uint||value is ulong||value is ushort||value is sbyte)
            {
                try{return Convert.ToDecimal(value,CultureInfo.InvariantCulture);}
                catch(OverflowException){throw Invalid(context,path);}
            }
            throw Invalid(context,path);
        }
        static Dictionary<string,object> Position(object value,string context,string path)
        {
            var source=value as Dictionary<string,object>;var array=value as object[];
            if(source==null&&(array==null||array.Length!=3))throw Invalid(context,path);
            var result=source==null?new Dictionary<string,object>():new Dictionary<string,object>(source);
            var axes=new[]{"X","Y","Z"};
            for(int i=0;i<axes.Length;i++)
            {
                string axis=axes[i],key=source==null?null:Key(source,axis,context,path+"."+axis);
                if(source!=null&&key==null)throw Invalid(context,path+"."+axis);
                decimal number=Number(source==null?array[i]:source[key],context,path+"."+axis);
                if(key!=null)result.Remove(key);result[axis]=number;
            }
            return result;
        }
        static Dictionary<string,object> Size(object value,string context,string path)
        {
            var source=value as Dictionary<string,object>;
            if(source==null)throw new InvalidOperationException(context+"："+path+"须明确提供Width、Depth、Height毫米数值，为0也必须填写。");
            var result=new Dictionary<string,object>(source);
            foreach(string field in new[]{"Width","Depth","Height"}){
                string key=Key(source,field,context,path+"."+field);
                if(key==null)throw new InvalidOperationException(context+"："+path+"."+field+"缺失，不能默认为0。");
                result.Remove(key);result[field]=Number(source[key],context,path+"."+field);
            }
            return result;
        }
        public static void Normalize(Dictionary<string,object> concept)
        {
            string context="方案“"+Label(concept,"Name","未命名")+"”";
            string assemblyKey=Key(concept,"Assembly",context,"Assembly");if(assemblyKey==null)return;
            var assembly=concept[assemblyKey] as Dictionary<string,object>;
            if(assembly==null)throw Invalid(context,"Assembly");
            string layoutKey=Key(assembly,"OpenLayout",context,"Assembly.OpenLayout");
            string layout=layoutKey==null?null:assembly[layoutKey] as string;
            if(layoutKey!=null&&layout!=null&&layout!=""&&layout!="manual"&&layout!="side-by-side")throw new InvalidOperationException(context+"：Assembly.OpenLayout仅支持manual或side-by-side。");
            if(layoutKey!=null&&assembly[layoutKey]!=null&&layout==null)throw new InvalidOperationException(context+"：Assembly.OpenLayout必须为字符串。");
            string unitsKey=Key(assembly,"Units",context,"Assembly.Units");
            var units=unitsKey==null?null:assembly[unitsKey] as object[];
            if(units==null)throw Invalid(context,"Assembly.Units");
            var normalizedUnits=new object[units.Length];
            for(int i=0;i<units.Length;i++)
            {
                string path="Assembly.Units["+i+"]";var unit=units[i] as Dictionary<string,object>;
                if(unit==null)throw Invalid(context,path);
                string unitContext=context+"，单元“"+Label(unit,"Id","#"+(i+1))+"”";
                var normalizedUnit=new Dictionary<string,object>(unit);
                foreach(string state in new[]{"Closed","Open"})
                {
                    string key=Key(unit,state,unitContext,path+"."+state,state+"Position",state+"_position");
                    if(key==null&&state=="Open"&&layout=="side-by-side"){
                        normalizedUnit[state]=new Dictionary<string,object>{{"X",0},{"Y",0},{"Z",0}};continue;
                    }
                    if(key==null)throw Invalid(unitContext,path+"."+state);
                    var position=Position(unit[key],unitContext,path+"."+state);
                    normalizedUnit.Remove(key);normalizedUnit[state]=position;
                }
                normalizedUnits[i]=normalizedUnit;
            }
            // Commit only after every unit validates, preserving unrelated response fields.
            var normalizedAssembly=new Dictionary<string,object>(assembly);
            string shellKey=Key(assembly,"Shell",context,"Assembly.Shell");
            if(shellKey!=null&&assembly[shellKey]!=null){
                var sourceShell=assembly[shellKey] as Dictionary<string,object>;
                if(sourceShell==null)throw new InvalidOperationException(context+"：Assembly.Shell须为对象。");
                var shell=new Dictionary<string,object>(sourceShell);
                foreach(string field in new[]{"WallThickness","AssemblyClearance","InternalReserve","ExternalReserve"}){
                    string key=Key(sourceShell,field,context,"Shell."+field);
                    if(key==null)throw new InvalidOperationException(context+"：Shell."+field+"缺失。");
                    shell.Remove(key);shell[field]=field.EndsWith("Reserve")?(object)Size(sourceShell[key],context,"Shell."+field):Number(sourceShell[key],context,"Shell."+field);
                }
                string openKey=Key(assembly,"OpenReserve",context,"Assembly.OpenReserve");
                if(openKey==null)throw new InvalidOperationException(context+"：OpenReserve缺失。");
                normalizedAssembly.Remove(openKey);normalizedAssembly["OpenReserve"]=Size(assembly[openKey],context,"OpenReserve");
                string partsKey=Key(assembly,"Parts",context,"Assembly.Parts");
                var parts=partsKey==null?null:assembly[partsKey] as object[];
                if(parts==null)throw new InvalidOperationException(context+"：Parts缺失。");
                var normalizedParts=new List<object>();
                foreach(var item in parts){
                    var part=item as Dictionary<string,object>;if(part==null)throw new InvalidOperationException(context+"：Parts存在空部件。");
                    var normalizedPart=new Dictionary<string,object>(part);string sizeKey=Key(part,"ClosedSize",context,"Parts.ClosedSize");
                    normalizedPart["ClosedSize"]=Size(sizeKey==null?null:part[sizeKey],context,"部件"+Label(part,"Id","未命名")+".ClosedSize");
                    if(sizeKey!=null&&sizeKey!="ClosedSize")normalizedPart.Remove(sizeKey);
                    normalizedParts.Add(normalizedPart);
                }
                normalizedAssembly.Remove(partsKey);normalizedAssembly["Parts"]=normalizedParts.ToArray();
                normalizedAssembly.Remove(shellKey);normalizedAssembly["Shell"]=shell;
            }
            if(layoutKey!=null){normalizedAssembly.Remove(layoutKey);normalizedAssembly["OpenLayout"]=layout;}
            normalizedAssembly.Remove(unitsKey);normalizedAssembly["Units"]=normalizedUnits;
            concept.Remove(assemblyKey);concept["Assembly"]=normalizedAssembly;
        }
    }
}
