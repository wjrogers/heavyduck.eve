using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HeavyDuck.Eve
{
    internal delegate void PostDownloadAction(string tempPath);

    internal static class Resources
    {
        public const string USER_AGENT = "HeavyDuck.Eve";

        private static readonly string m_cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HeavyDuck.Eve");
        private static readonly MD5 m_md5 = MD5.Create();
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false);

        public static string CacheRoot
        {
            get { return m_cacheRoot; }
        }

        public static MD5 MD5
        {
            get { return m_md5; }
        }

        public static UTF8Encoding UTF8
        {
            get { return m_encoding; }
        }

        /// <summary>
        /// Caches a file from the web using the GET method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="cacheHours">The number of hours before the cache expires.</param>
        public static CachedResult CacheFile(string url, string cachePath, int cacheHours)
        {
            return CacheFile(url, cachePath, cacheHours, null);
        }

        /// <summary>
        /// Caches a file from the web using the GET method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="cacheHours">The number of hours before the cache expires.</param>
        /// <param name="action">A validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/>.</param>
        public static CachedResult CacheFile(string url, string cachePath, int cacheHours, PostDownloadAction action)
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

                // call the PostDownloadAction if there is one
                if (action != null)
                    action(tempPath);

                // we assume the file is fine if the PostDownloadAction didn't throw an exception
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
        /// Caches a file from the web using the POST method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="cacheHours">The number of hours before the cache expires.</param>
        /// <param name="parameters">The POST parameters.</param>
        public static CachedResult CacheFilePost(string url, string cachePath, int cacheHours, IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return CacheFilePost(url, cachePath, cacheHours, parameters, null);
        }

        /// <summary>
        /// Caches a file from the web using the POST method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="cacheHours">The number of hours before the cache expires.</param>
        /// <param name="parameters">The POST parameters.</param>
        /// <param name="action">A validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/>.</param>
        public static CachedResult CacheFilePost(string url, string cachePath, int cacheHours, IEnumerable<KeyValuePair<string, string>> parameters, PostDownloadAction action)
        {
            byte[] buffer;
            CacheState currentState = IsFileCached(cachePath, cacheHours);

            // check whether the file is cached
            if (currentState == CacheState.Cached) return new CachedResult(cachePath, false, CacheState.Cached);

            // create our request crap
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            // set the standard request properties
            request.ContentType = "application/x-www-form-urlencoded";
            request.KeepAlive = false;
            request.Method = "POST";
            request.UserAgent = Resources.USER_AGENT;

            // prep to handle response
            WebResponse response = null;
            string tempPath = null;

            try
            {
                // write the request
                using (Stream s = request.GetRequestStream())
                {
                    buffer = UTF8.GetBytes(Resources.GetEncodedParameters(parameters));
                    s.Write(buffer, 0, buffer.Length);
                }

                // here we actually send the request and get a response (we hope)
                response = request.GetResponse();

                // read the response and write it to the temp file
                using (Stream input = response.GetResponseStream())
                {
                    tempPath = Resources.DownloadStream(input);
                }

                // call the PostDownloadAction if there is one
                if (action != null)
                    action(tempPath);

                // we assume the file is fine if the PostDownloadAction didn't throw an exception
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
                // close the response
                if (response != null) response.Close();

                // get rid of the temp file, don't care if it doesn't work
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

        public static string GetEncodedParameters(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            StringBuilder list;
            List<KeyValuePair<string, string>> sorted;

            // check the, uh, parameter
            if (parameters == null) return "";

            // copy the parameters and sort them
            sorted = new List<KeyValuePair<string, string>>(parameters);
            sorted.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b)
            {
                if (a.Key == b.Key)
                    return string.Compare(a.Value, b.Value);
                else
                    return string.Compare(a.Key, b.Key);
            });

            // build the list
            list = new StringBuilder();
            foreach (KeyValuePair<string, string> parameter in sorted)
            {
                list.Append(System.Web.HttpUtility.UrlEncode(parameter.Key));
                list.Append("=");
                list.Append(System.Web.HttpUtility.UrlEncode(parameter.Value));
                list.Append("&");
            }
            if (list.Length > 0) list.Remove(list.Length - 1, 1);

            // done
            return list.ToString();
        }

        public static string ComputeParameterHash(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return ComputeParameterHash(parameters, UTF8);
        }

        public static string ComputeParameterHash(IEnumerable<KeyValuePair<string, string>> parameters, Encoding encoding)
        {
            return BitConverter.ToString(MD5.ComputeHash(encoding.GetBytes(Resources.GetEncodedParameters(parameters)))).Replace("-", "");
        }
    }
}
