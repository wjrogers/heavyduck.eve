using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

        public static CachedResult CacheFile(string url, string cachePath, int cacheHours)
        {
            CacheState currentState = IsFileCached(cachePath, cacheHours);

            // check whether the file is cached
            if (currentState == CacheState.Cached) return new CachedResult(cachePath, false, CacheState.Cached);

            // create request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            // set request properties
            request.KeepAlive = false;
            request.Method = "GET";
            request.UserAgent = USER_AGENT;

            // prep for response
            WebResponse response = null;
            string tempPath = null;

            try
            {
                // do the actual net stuff
                response = request.GetResponse();

                // read and write to a temp file
                using (Stream input = response.GetResponseStream())
                {
                    tempPath = DownloadStream(input);
                }

                // we assume the file is fine because there is really no easy way to test it
                File.Copy(tempPath, cachePath, true);

                // return success
                return new CachedResult(cachePath, true, CacheState.Cached);
            }
            catch (Exception ex)
            {
                // if we currently have a valid local copy of the file, even if out of date, return that info
                if (currentState != CacheState.Uncached)
                    return new CachedResult(cachePath, false, currentState, ex);
                else
                    return new CachedResult(null, false, CacheState.Uncached, ex);
            }
            finally
            {
                // clean up
                if (response != null) response.Close();

                // get rid of the temp file if we can
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }
            }
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

        /// <summary>
        /// Checks whether a file exists and is less than a certain number of hours old.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        /// <param name="cacheHours">The number of hours before the file is considered out of date.</param>
        public static CacheState IsFileCached(string path, int cacheHours)
        {
            try
            {
                FileInfo info = new FileInfo(path);

                if (info.Exists && DateTime.Now.Subtract(info.LastWriteTime).TotalHours < cacheHours)
                    return CacheState.Cached;
                else if (info.Exists)
                    return CacheState.CachedOutOfDate;
                else
                    return CacheState.Uncached;
            }
            catch
            {
                return CacheState.Uncached;
            }
        }
    }
}
