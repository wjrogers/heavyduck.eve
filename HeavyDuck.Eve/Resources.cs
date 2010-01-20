using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HeavyDuck.Eve
{
    internal delegate void PostDownloadAction(string tempPath);

    internal enum HttpMethod
    {
        Get,
        Post
    }

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
        /// <param name="ttl">The amount of time before the cache expires.</param>
        public static CacheResult CacheFile(string url, string cachePath, TimeSpan ttl)
        {
            return CacheFile(url, cachePath, ttl, null);
        }

        /// <summary>
        /// Caches a file from the web using the GET method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="ttl">The amount of time before the cache expires.</param>
        /// <param name="action">A validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/>.</param>
        public static CacheResult CacheFile(string url, string cachePath, TimeSpan ttl, PostDownloadAction action)
        {
            return CacheFileInternal(HttpMethod.Get, url, cachePath, new TtlCacheStrategy(ttl), null, action);
        }

        /// <summary>
        /// Caches a file from the web using the GET method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="cacheStrategy">decides file cache state and duration</param>
        /// <param name="action">A validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/>.</param>
        public static CacheResult CacheFile(string url, string cachePath, ICacheStrategy cacheStrategy, PostDownloadAction action)
        {
            return CacheFileInternal(HttpMethod.Get, url, cachePath, cacheStrategy, null, action);
        }

        /// <summary>
        /// Caches a file from the web using the POST method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="ttl">The amount of time before the cache expires.</param>
        /// <param name="parameters">The POST parameters.</param>
        public static CacheResult CacheFilePost(string url, string cachePath, TimeSpan ttl, IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return CacheFilePost(url, cachePath, ttl, parameters, null);
        }

        /// <summary>
        /// Caches a file from the web using the POST method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="ttl">The amount of time before the cache expires.</param>
        /// <param name="parameters">The POST parameters.</param>
        /// <param name="action">A validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/>.</param>
        public static CacheResult CacheFilePost(string url, string cachePath, TimeSpan ttl, IEnumerable<KeyValuePair<string, string>> parameters, PostDownloadAction action)
        {
            return CacheFileInternal(HttpMethod.Post, url, cachePath, new TtlCacheStrategy(ttl), parameters, action);
        }

        /// <summary>
        /// Caches a file from the web using the POST method.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="cachePath">The path where the cached file will be saved.</param>
        /// <param name="cacheStrategy">decides file cache state and duration</param>
        /// <param name="parameters">The POST parameters.</param>
        /// <param name="action">A validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/>.</param>
        public static CacheResult CacheFilePost(string url, string cachePath, ICacheStrategy cacheStrategy, IEnumerable<KeyValuePair<string, string>> parameters, PostDownloadAction action)
        {
            return CacheFileInternal(HttpMethod.Post, url, cachePath, cacheStrategy, parameters, action);
        }

        /// <summary>
        /// Caches a file from the internets.
        /// </summary>
        /// <param name="method">specifies GET or POST</param>
        /// <param name="url">the url to request</param>
        /// <param name="cachePath">the path where the cached file will be saved</param>
        /// <param name="cacheStrategy">decides file cache state and duration</param>
        /// <param name="parameters">parameters for POST requests</param>
        /// <param name="action">a validation or processing action to be run after downloading the file but before copying it to <paramref name="cachePath"/></param>
        private static CacheResult CacheFileInternal(HttpMethod method, string url, string cachePath, ICacheStrategy cacheStrategy, IEnumerable<KeyValuePair<string, string>> parameters, PostDownloadAction action)
        {
            CacheResult currentResult;
            string tempPath = null;

            // validate parameters
            if (method == HttpMethod.Get && parameters != null)
                throw new ArgumentException("GET method and parameters don't mix");

            // check whether the file is cached
            currentResult = IsFileCached(cachePath, cacheStrategy);
            if (currentResult.State == CacheState.Cached)
                return currentResult;

            try
            {
                // download to a temp file
                switch (method)
                {
                    case HttpMethod.Get:
                        tempPath = DownloadUrlGet(url);
                        break;
                    case HttpMethod.Post:
                        tempPath = DownloadUrlPost(url, parameters);
                        break;
                    default:
                        throw new ArgumentException("Unknown HTTP method " + method.ToString());
                }

                // call the PostDownloadAction if there is one
                if (action != null)
                    action(tempPath);

                // we assume the file is fine if the PostDownloadAction didn't throw an exception
                File.Copy(tempPath, cachePath, true);

                // return success
                return new CacheResult(cachePath, true, CacheState.Cached, cacheStrategy.GetCachedUntil(cachePath));
            }
            catch (Exception ex)
            {
                // if we currently have a valid local copy of the file, even if out of date, return that info
                if (currentResult.State != CacheState.Uncached)
                    return CacheResult.FromExisting(currentResult, ex);
                else
                    return CacheResult.Uncached(ex);
            }
            finally
            {
                // get rid of the temp file, don't care if it doesn't work
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }
            }
        }

        /// <summary>
        /// Downloads a file from the internets using the GET method and writes it to a temporary file.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <returns>The path to the temporary file.</returns>
        public static string DownloadUrlGet(string url)
        {
            // create request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            // set request properties
            request.KeepAlive = false;
            request.Method = "GET";
            request.UserAgent = USER_AGENT;

            // prep for response
            WebResponse response = null;

            try
            {
                // do the actual net stuff
                response = request.GetResponse();

                // read and write to a temp file
                using (Stream input = response.GetResponseStream())
                    return DownloadStream(input);
            }
            finally
            {
                // clean up
                if (response != null) response.Close();
            }
        }

        /// <summary>
        /// Downloads a file from the internets using the POST method and writes it to a temporary file.
        /// </summary>
        /// <param name="url">The url to request.</param>
        /// <param name="parameters">The form parameters.</param>
        /// <returns>The path to the temporary file.</returns>
        public static string DownloadUrlPost(string url, IEnumerable<KeyValuePair<string, string>> parameters)
        {
            byte[] buffer;

            // create our request crap
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            // set the standard request properties
            request.ContentType = "application/x-www-form-urlencoded";
            request.KeepAlive = false;
            request.Method = "POST";
            request.UserAgent = Resources.USER_AGENT;
            request.ServicePoint.Expect100Continue = false; // fixes problems with POSTing to EVE-Central

            // prep to handle response
            WebResponse response = null;

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
                    return Resources.DownloadStream(input);
            }
            finally
            {
                // close the response
                if (response != null) response.Close();
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
        /// <param name="ttl">The amount of time before the file is considered out of date.</param>
        public static CacheResult IsFileCached(string path, TimeSpan ttl)
        {
            return IsFileCached(path, new TtlCacheStrategy(ttl));
        }

        /// <summary>
        /// Checks whether a file exists and is fresh.
        /// </summary>
        /// <param name="path">the path to the cached file</param>
        /// <param name="cacheStrategy">decides cache state and duration</param>
        /// <returns></returns>
        public static CacheResult IsFileCached(string path, ICacheStrategy cacheStrategy)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                DateTime cachedUntil;

                if (info.Exists)
                {
                    cachedUntil = cacheStrategy.GetCachedUntil(path);

                    if (DateTime.Now < cachedUntil)
                        return new CacheResult(path, false, CacheState.Cached, cachedUntil);
                    else
                        return new CacheResult(path, false, CacheState.CachedOutOfDate, cachedUntil);
                }
                else
                {
                    return CacheResult.Uncached(path);
                }
            }
            catch (Exception ex)
            {
                return CacheResult.Uncached(path, ex);
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
