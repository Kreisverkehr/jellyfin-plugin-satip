namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;

public static class StringExtensions
{
    extension(string str)
    {
        public Guid ToMd5Guid()
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(str));
            return new Guid(hash);
        }
    }
}