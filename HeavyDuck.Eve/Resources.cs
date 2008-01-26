using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HeavyDuck.Eve
{
    internal static class Resources
    {
        public const string USER_AGENT = "HeavyDuck.Eve";

        private static readonly string m_cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HeavyDuck.Eve");
        private static readonly MD5 m_md5 = MD5.Create();

        public static string CacheRoot
        {
            get { return m_cacheRoot; }
        }

        public static MD5 MD5
        {
            get { return m_md5; }
        }

        /// <summary>
        /// Reads all data from the stream and writes it to a temporary file.
        /// </summary>
        /// <param name="input">The stream to read.</param>
        /// <returns>The path to the temp file.</returns>
        public static string DownloadStream(Stream input)
        {
            string tempPath = null;
            byte[] buffer = new byte[32 * 1024];
            int bytesRead, offset = 0;

            try
            {
                // get the temp file name
                tempPath = Path.GetTempFileName();

                using (FileStream output = File.Open(tempPath, FileMode.Open, FileAccess.Write))
                {
                    while (0 < (bytesRead = input.Read(buffer, offset, buffer.Length)))
                        output.Write(buffer, 0, bytesRead);
                }
            }
            catch
            {
                // attempt to get rid fo the temp file in the event of an error
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }

                // pass along the exception
                throw;
            }

            return tempPath;
        }
    }
}
