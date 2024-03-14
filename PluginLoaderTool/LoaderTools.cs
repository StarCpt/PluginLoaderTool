using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace avaness.PluginLoaderTool
{
    public static class LoaderTools
    {
        public static string GetHash256(string file)
        {
            using (SHA256CryptoServiceProvider sha = new SHA256CryptoServiceProvider())
            {
                using (FileStream fileStream = new FileStream(file, FileMode.Open))
                {
                    using (BufferedStream bufferedStream = new BufferedStream(fileStream))
                    {
                        return GetHash(bufferedStream, sha);
                    }
                }
            }
        }

        public static string GetHash(Stream input, HashAlgorithm hash)
        {
            byte[] data = hash.ComputeHash(input);
            StringBuilder sb = new StringBuilder(2 * data.Length);
            foreach (byte b in data)
                sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }
    }
}
