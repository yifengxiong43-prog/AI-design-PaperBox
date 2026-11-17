using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PaperStudio
{
    public static class ApiSecretStore
    {
        sealed class Secrets { public string TextKey, ImageKey; }
        static readonly byte[] Entropy=Encoding.UTF8.GetBytes("PaperStudio API credentials v1");
        internal static string PathOverride=null;
        static string PathName { get { return PathOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperStudio","api-credentials.dat"); } }

        public static void Load(ApiSettings settings)
        {
            if(!File.Exists(PathName))return;
            try {
                var plain=ProtectedData.Unprotect(File.ReadAllBytes(PathName),Entropy,DataProtectionScope.CurrentUser);
                var secrets=Json.Read<Secrets>(Encoding.UTF8.GetString(plain));
                if(secrets!=null){settings.TextKey=secrets.TextKey??"";settings.ImageKey=secrets.ImageKey??"";}
            } catch(CryptographicException) { throw new InvalidOperationException("已保存的 API 密钥无法由当前 Windows 用户解密，请在设置中重新填写。"); }
        }
        public static void Save(ApiSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            var plain=Encoding.UTF8.GetBytes(Json.Write(new Secrets{TextKey=settings.TextKey??"",ImageKey=settings.ImageKey??""}));
            var encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);
            var temporary=PathName+".tmp";
            File.WriteAllBytes(temporary,encrypted);
            if(File.Exists(PathName))File.Replace(temporary,PathName,null);else File.Move(temporary,PathName);
        }
    }
}
