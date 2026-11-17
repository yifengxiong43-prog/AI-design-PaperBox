using System;
using System.Collections.Generic;

namespace PaperStudio
{
    internal static class AssemblyResponseChecks
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static Dictionary<string,object> Read(string json){return (Dictionary<string,object>)Json.Serializer().DeserializeObject(json);}
        static Dictionary<string,object> Fixture(string closed,string open="{\"X\":0,\"Y\":1,\"Z\":2}")
        {return Read("{\"Name\":\"定位测试\",\"Assembly\":{\"Units\":[{\"Id\":\"left\",\"Closed\":"+closed+",\"Open\":"+open+"}]}}");}
        static void Reject(Dictionary<string,object> raw,string path,string unit="left")
        {
            string before=Json.Write(raw);bool rejected=false;
            try{AssemblyResponse.Normalize(raw);}
            catch(InvalidOperationException ex){rejected=ex.Message.Contains("定位测试")&&ex.Message.Contains(unit)&&ex.Message.Contains(path);}
            Check(rejected,"Assembly placement must reject with context: "+path);
            Check(before==Json.Write(raw),"Invalid coordinates must not partially mutate the response");
        }
        public static void Run()
        {
            var raw=Fixture("{\"x\":-12,\"Y\":\"+2.5 mm\",\"z\":\"-0.25 毫米\",\"Note\":\"keep\"}","[-20,3.5,0]");
            AssemblyResponse.Normalize(raw);
            var parsed=Json.Read<Concept>(Json.Write(raw));
            Check(parsed.Assembly.Units[0].Closed.X==-12m&&parsed.Assembly.Units[0].Closed.Y==2.5m&&parsed.Assembly.Units[0].Closed.Z==-.25m,"Signed coordinate objects must deserialize exactly");
            Check(parsed.Assembly.Units[0].Open.X==-20m&&parsed.Assembly.Units[0].Open.Y==3.5m,"Coordinate arrays must deserialize exactly");
            Check(Json.Write(raw).Contains("\"Note\":\"keep\""),"Unrelated coordinate fields must survive normalization");
            string normalized=Json.Write(raw);AssemblyResponse.Normalize(raw);
            Check(Json.Write(raw)==normalized,"Normalization must be idempotent");

            foreach(string closedKey in new[]{"closed","closedPosition","closed_position"})
            foreach(string openKey in new[]{"open","openPosition","open_position"})
            {
                raw=Read("{\"Name\":\"定位测试\",\"Layout\":\"原始描述\",\"Extra\":{\"x\":\"7 cm\"},\"assembly\":{\"Assumptions\":\"keep\",\"units\":[{\"Id\":\"left\",\"Count\":2,\"Motion\":\"rotate\",\""+closedKey+"\":[\"-2 mm\",0,1],\""+openKey+"\":{\"x\":\"-1.5\",\"y\":0,\"z\":2}}]}}");
                string extra=Json.Write(raw["Extra"]);AssemblyResponse.Normalize(raw);parsed=Json.Read<Concept>(Json.Write(raw));
                Check(parsed.Assembly.Units[0].Closed.X==-2m&&parsed.Assembly.Units[0].Open.X==-1.5m,"Equivalent aliases must deserialize into Placement");
                Check(parsed.Assembly.Units[0].Count==2&&parsed.Assembly.Units[0].Motion=="rotate"&&parsed.Assembly.Assumptions=="keep"&&parsed.Layout=="原始描述"&&Json.Write(raw["Extra"])==extra,"Noncoordinate data must remain unchanged");
            }
            foreach(string value in new[]{"null","true","{}","[]","\"1 cm\"","\"1-2 mm\"","\"NaN\"","\"1 px\"","\"约 2 mm\"","\"79228162514264337593543950336\""})
                Reject(Fixture("{\"X\":"+value+",\"Y\":0,\"Z\":0}"),"Assembly.Units[0].Closed.X");
            Reject(Fixture("{\"X\":1,\"Y\":2}"),"Assembly.Units[0].Closed.Z");
            Reject(Fixture("{\"X\":1,\"x\":2,\"Y\":0,\"Z\":0}"),"Assembly.Units[0].Closed.X");
            foreach(string value in new[]{"null","4","\"1,2,3\"","[1,2]","[1,2,3,4]"})
                Reject(Fixture(value),"Assembly.Units[0].Closed");
            Reject(Fixture("[1,2,3]","{\"X\":0,\"Y\":0}"),"Assembly.Units[0].Open.Z");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"Units\":[{\"Id\":\"left\",\"Open\":[0,0,0]}]}}"),"Assembly.Units[0].Closed");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"Units\":[{\"Id\":\"left\",\"Closed\":[0,0,0],\"closedPosition\":[0,0,0],\"Open\":[0,0,0]}]}}"),"Assembly.Units[0].Closed");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"Units\":[{\"Id\":\"left\",\"Closed\":[0,0,0],\"Open\":[0,0,0]}, {\"Id\":\"right\",\"Closed\":[1,0,0]}]}}"),"Assembly.Units[1].Open","right");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":null}"),"Assembly","");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"Units\":{}}}"),"Assembly.Units","");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"Units\":[],\"units\":[]}}"),"Assembly.Units","");
            raw=Read("{\"Name\":\"legacy\",\"PackageDimensions\":{\"Longest\":42}}");string legacy=Json.Write(raw);
            AssemblyResponse.Normalize(raw);Check(Json.Write(raw)==legacy,"Responses without Assembly must remain unchanged");
            raw=Read("{\"Name\":\"定位测试\",\"Assembly\":{\"openlayout\":\"side-by-side\",\"Units\":[{\"Id\":\"left\",\"Closed\":[0,0,0]}]}}");
            AssemblyResponse.Normalize(raw);parsed=Json.Read<Concept>(Json.Write(raw));
            Check(parsed.Assembly.OpenLayout=="side-by-side"&&parsed.Assembly.Units[0].Open!=null,"Only explicit computed layout may omit open coordinates");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"OpenLayout\":\"side-by-side\",\"Units\":[{\"Id\":\"left\"}]}}"),"Assembly.Units[0].Closed");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"OpenLayout\":\"manual\",\"Units\":[{\"Id\":\"left\",\"Closed\":[0,0,0]}]}}"),"Assembly.Units[0].Open");
            Reject(Read("{\"Name\":\"定位测试\",\"Assembly\":{\"OpenLayout\":\"magic\",\"Units\":[]}}"),"Assembly.OpenLayout","");
            raw=Fixture("[0,0,0]");var assembly=(Dictionary<string,object>)raw["Assembly"];
            assembly["Shell"]=Read("{\"WallThickness\":1,\"AssemblyClearance\":2,\"InternalReserve\":{\"Width\":0,\"Depth\":4,\"Height\":2},\"ExternalReserve\":{\"Width\":0,\"Depth\":0,\"Height\":0}}");
            assembly["OpenReserve"]=Read("{\"Width\":0,\"Depth\":20,\"Height\":0}");
            assembly["Parts"]=new object[]{Read("{\"Id\":\"底板\",\"ClosedSize\":{\"Width\":100,\"Depth\":20,\"Height\":1}}")};
            var validBudget=Json.Write(raw);AssemblyResponse.Normalize(raw);
            Check(Json.Read<Concept>(Json.Write(raw)).Assembly.Shell.InternalReserve.Width==0,"Explicit zero reserves are accepted");
            raw=Read(validBudget);assembly=(Dictionary<string,object>)raw["Assembly"];((Dictionary<string,object>)((Dictionary<string,object>)assembly["Shell"])["InternalReserve"]).Remove("Width");
            Reject(raw,"InternalReserve.Width","");
            raw=Read(validBudget);assembly=(Dictionary<string,object>)raw["Assembly"];((Dictionary<string,object>)assembly["Shell"]).Remove("AssemblyClearance");
            Reject(raw,"Shell.AssemblyClearance","");
            Console.WriteLine("Assembly response coordinate checks passed.");
        }
    }
}
