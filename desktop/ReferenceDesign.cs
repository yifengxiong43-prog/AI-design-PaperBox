using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PaperStudio
{
    public static class ReferenceDesign
    {
        public static bool RequestsRestoration(string request)
        {
            if(String.IsNullOrEmpty(request)||request.Contains("不要还原")||request.Contains("不需还原"))return false;
            return request.Contains("还原")||request.Contains("复刻")||request.Contains("按照参考结构")||request.Contains("保留参考结构");
        }
        public static void EnsurePictures(List<Picture> pictures)
        {
            if(pictures==null)return;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            int ordinal=0;
            foreach(var picture in pictures)
            {
                ordinal++;
                if(picture==null||String.IsNullOrEmpty(picture.Data))throw new InvalidOperationException("Reference 图片内容缺失。");
                byte[] bytes;
                try{bytes=Convert.FromBase64String(picture.Data);}catch(FormatException){throw new InvalidOperationException("Reference 图片编码无效。");}
                string hash=Hash(bytes);
                if(!String.IsNullOrEmpty(picture.ContentHash)&&picture.ContentHash!=hash)throw new InvalidOperationException("Reference 图片内容与已保存哈希不一致："+picture.Name);
                picture.ContentHash=hash;
                if(String.IsNullOrEmpty(picture.Id))picture.Id="ref:"+hash.Substring(0,20)+":"+ordinal;
                if(!ids.Add(picture.Id))throw new InvalidOperationException("Reference 图片 ID 重复："+picture.Id);
                if(String.IsNullOrEmpty(picture.ReferenceType))picture.ReferenceType="creative";
                if(!new[]{"structure","layout","appearance","creative"}.Contains(picture.ReferenceType))throw new InvalidOperationException("Reference 类型无效："+picture.ReferenceType);
            }
        }
        public static string Hash(byte[] bytes)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static string Signature(ReferenceDesignSpecification spec)
        {return spec==null?null:Hash(Encoding.UTF8.GetBytes(Json.Write(spec)));}
        public static void BindObservations(DesignUnderstanding understanding,List<Picture> pictures)
        {
            EnsurePictures(pictures);
            if(pictures==null||pictures.Count==0)return;
            var observations=understanding==null?null:understanding.ReferenceImages;
            if(observations==null||observations.Count!=pictures.Count)throw new DesignValidationException("Reference 分析没有逐张覆盖本次上传图片。");
            foreach(var picture in pictures)
            {
                var matches=observations.Where(o=>o!=null&&(String.IsNullOrEmpty(o.ImageId)?o.ImageName==picture.Name:o.ImageId==picture.Id)).ToList();
                if(matches.Count!=1)throw new DesignValidationException("Reference 图片无法唯一绑定到分析结果："+picture.Name);
                var observation=matches[0];
                observation.ImageId=picture.Id;observation.ContentHash=picture.ContentHash;observation.ReferenceType=picture.ReferenceType;
            }
            if(observations.Select(o=>o.ImageId).Distinct().Count()!=pictures.Count)throw new DesignValidationException("Reference 图片分析存在重复或错配。");
        }
        public static void ValidateSpecification(ReferenceDesignSpecification spec,List<Picture> pictures)
        {
            if(spec==null||new[]{spec.BodyShape,spec.TrayCount,spec.TrayHierarchy,spec.OpeningMechanism,spec.Linkages,spec.PivotPoints,spec.ProductLayout,spec.ClosedOpenMapping}.Any(String.IsNullOrWhiteSpace)
                ||spec.LockedFeatures==null||spec.LockedFeatures.Count==0||spec.Unknowns==null)
                throw new DesignValidationException("Reference 结构解析不完整，尚不能确认或出图。");
            EnsurePictures(pictures);
            spec.ImageIds=pictures.Select(p=>p.Id).ToList();
        }
    }
}
