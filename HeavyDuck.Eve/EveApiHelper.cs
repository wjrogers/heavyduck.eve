using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public static class EveApiHelper
    {
        private static readonly Uri m_apiRoot = new Uri(@"http://api.eve-online.com/");
        private static readonly Regex m_regexAspx = new Regex(@"\.aspx$");
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false);

        public static CachedResult GetCharacters(int userID, string apiKey)
        {
            return QueryAccountApi(@"/account/Characters.xml.aspx", userID, apiKey);
        }

        public static CachedResult GetCharacterSheet(int userID, string apiKey, int characterID)
        {
            return QueryCharacterApi(@"/char/CharacterSheet.xml.aspx", userID, apiKey, characterID);
        }

        public static CachedResult GetCharacterAssetList(int userID, string apiKey, int characterID)
        {
            return QueryCharacterApi(@"/char/AssetList.xml.aspx", userID, apiKey, characterID);
        }

        public static CachedResult GetCorporationAssetList(int userID, string apiKey, int characterID, int corporationID)
        {
            return QueryCorporationApi(@"/corp/AssetList.xml.aspx", userID, apiKey, characterID, corporationID);
        }

        private static CachedResult QueryAccountApi(string apiPath, int userID, string apiKey)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["version"] = "2";

            // call the basic query method
            return QueryApi(apiPath, parameters);
        }

        private static CachedResult QueryCharacterApi(string apiPath, int userID, string apiKey, int characterID)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["characterID"] = characterID.ToString();
            parameters["version"] = "2";

            // call the basic query method
            return QueryApi(apiPath, parameters);
        }

        private static CachedResult QueryCorporationApi(string apiPath, int userID, string apiKey, int characterID, int corporationID)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["characterID"] = characterID.ToString();
            parameters["corporationID"] = corporationID.ToString();
            parameters["version"] = "2";

            // call the basic query method
            return QueryApi(apiPath, parameters);
        }

        public static CachedResult QueryApi(string apiPath, IDictionary<string, string> parameters)
        {
            byte[] buffer;
            string cachePath = GetCachePath(apiPath, parameters);
            CacheState currentState;

            // check parameters
            if (string.IsNullOrEmpty(apiPath)) throw new ArgumentNullException("apiPath");

            // check whether the thing is already cached anyway
            currentState = IsFileCached(cachePath);
            if (currentState == CacheState.Cached) return new CachedResult(cachePath, false, currentState, null);

            // create our request crap
            Uri uri = new Uri(m_apiRoot, apiPath);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);

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
                    buffer = m_encoding.GetBytes(GetEncodedParameters(parameters));
                    s.Write(buffer, 0, buffer.Length);
                }

                // here we actually send the request and get a response (we hope)
                response = request.GetResponse();

                // read the response and write it to the temp file
                using (Stream input = response.GetResponseStream())
                {
                    tempPath = Resources.DownloadStream(input);
                }

                // inspect the resulting file for errors
                using (FileStream tempStream = File.Open(tempPath, FileMode.Open, FileAccess.Read))
                {
                    XPathDocument doc = new XPathDocument(tempStream);
                    XPathNavigator nav = doc.CreateNavigator();
                    XPathNavigator errorNode = nav.SelectSingleNode("/eveapi/error");

                    // check if there was an error node
                    if (errorNode != null)
                        throw new EveApiException(errorNode.SelectSingleNode("@code").ValueAsInt, errorNode.Value);

                    // now check if there appears to at least be an eveapi node
                    if (nav.SelectSingleNode("/eveapi") == null)
                        throw new EveApiException(0, "No valid eveapi XML found in response.");
                }

                // we now assume the file is valid and copy it to the cache path
                File.Copy(tempPath, cachePath, true);

                // return success
                return new CachedResult(cachePath, true, CacheState.Cached, null);
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
        /// Checks the state of the cache for a particular file.
        /// </summary>
        /// <param name="apiPath">The EVE API path used to retrieve the cached file.</param>
        /// <param name="parameters">The EVE API parameters used to retrieve the cached file.</param>
        private static CacheState IsFileCached(string apiPath, IDictionary<string, string> parameters)
        {
            return IsFileCached(GetCachePath(apiPath, parameters));
        }

        /// <summary>
        /// Checks the state of the cache for a particular file.
        /// </summary>
        /// <param name="filePath">The filesystem path to the cached file.</param>
        private static CacheState IsFileCached(string filePath)
        {
            // check whether it even exists
            if (!File.Exists(filePath)) return CacheState.Uncached;

            // open and look for the cachedUntil element
            try
            {
                using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read))
                {
                    XPathDocument doc = new XPathDocument(fs);
                    XPathNavigator nav = doc.CreateNavigator();
                    XPathNavigator cacheNode;
                    DateTime cacheTime;

                    // parse the cached-until date from the XML
                    cacheNode = nav.SelectSingleNode("/eveapi/cachedUntil");
                    cacheTime = DateTime.Parse(cacheNode.Value);

                    // now we can compare to the current time
                    return TimeZone.CurrentTimeZone.ToUniversalTime(DateTime.Now) < cacheTime ? CacheState.Cached : CacheState.CachedOutOfDate;
                }
            }
            catch
            {
                return CacheState.Uncached;
            }
        }

        private static string GetCachePath(string apiPath, IDictionary<string, string> parameters)
        {
            string mungedApiPath;
            string paramHash;
            string cachePath;
            string dirPath;

            // munge up the api path to be a file system path
            mungedApiPath = apiPath.StartsWith("/") ? apiPath.Substring(1) : apiPath;
            mungedApiPath = m_regexAspx.Replace(mungedApiPath, "");
            mungedApiPath = mungedApiPath.Replace('/', '.');

            // get the parameters and hash them, then stick the hash in the filename
            paramHash = BitConverter.ToString(Resources.MD5.ComputeHash(m_encoding.GetBytes(GetEncodedParameters(parameters)))).Replace("-", "");
            mungedApiPath = mungedApiPath.Insert(mungedApiPath.LastIndexOf('.'), "." + paramHash);

            // make sure the directory exists before returning this path
            cachePath = Path.Combine(Resources.CacheRoot, mungedApiPath);
            dirPath = Path.GetDirectoryName(cachePath);
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            return cachePath;
        }

        private static string GetEncodedParameters(IDictionary<string, string> parameters)
        {
            StringBuilder list;
            string[] keys;

            // check the, uh, parameter
            if (parameters == null) return "";

            // copy the list of keys and sort them
            keys = new string[parameters.Count];
            parameters.Keys.CopyTo(keys, 0);
            Array.Sort(keys);

            // build the list
            list = new StringBuilder();
            foreach (string key in keys)
            {
                list.Append(System.Web.HttpUtility.UrlEncode(key));
                list.Append("=");
                list.Append(System.Web.HttpUtility.UrlEncode(parameters[key]));
                list.Append("&");
            }
            if (list.Length > 0) list.Remove(list.Length - 1, 1);

            // done
            return list.ToString();
        }
    }
}
