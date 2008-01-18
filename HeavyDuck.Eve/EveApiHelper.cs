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
        private static readonly string m_cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HeavyDuck.Eve");
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false);
        private static readonly System.Security.Cryptography.MD5 m_md5 = System.Security.Cryptography.MD5.Create();

        public static string GetCharacters(int userID, string apiKey)
        {
            return QueryAccountApi(@"/account/Characters.xml.aspx", userID, apiKey);
        }

        public static string GetCharacterSheet(int userID, string apiKey, int characterID)
        {
            return QueryCharacterApi(@"/char/CharacterSheet.xml.aspx", userID, apiKey, characterID);
        }

        public static string GetCharacterAssetList(int userID, string apiKey, int characterID)
        {
            return QueryCharacterApi(@"/char/AssetList.xml.aspx", userID, apiKey, characterID);
        }

        private static string QueryAccountApi(string apiPath, int userID, string apiKey)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>(2);

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["version"] = "2";

            // call the basic query method
            return QueryApi(apiPath, parameters);
        }

        private static string QueryCharacterApi(string apiPath, int userID, string apiKey, int characterID)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>(2);

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["characterID"] = characterID.ToString();
            parameters["version"] = "2";

            // call the basic query method
            return QueryApi(apiPath, parameters);
        }

        public static string QueryApi(string apiPath, IDictionary<string, string> parameters)
        {
            byte[] buffer;
            string cachePath = GetCachePath(apiPath, parameters);

            // check parameters
            if (string.IsNullOrEmpty(apiPath)) throw new ArgumentNullException("apiPath");

            // check whether the thing is already cached anyway
            if (IsFileCached(cachePath)) return cachePath;

            // create our request crap
            Uri uri = new Uri(m_apiRoot, apiPath);
            WebRequest request = HttpWebRequest.Create(uri);

            // set the standard request properties
            request.ContentType = "application/x-www-form-urlencoded";
            request.Method = "POST";

            // write the request
            using (Stream s = request.GetRequestStream())
            {
                buffer = m_encoding.GetBytes(GetEncodedParameters(parameters));
                s.Write(buffer, 0, buffer.Length);
            }

            // prep to handle response
            WebResponse response = null;
            int offset, bytesRead;
            string tempPath = null;

            try
            {
                // here we actually send the request and get a response (we hope)
                response = request.GetResponse();

                // read the response and write it to the temp file
                using (Stream input = response.GetResponseStream())
                {
                    tempPath = Path.GetTempFileName();
                    buffer = new byte[32 * 1024];
                    offset = 0;

                    using (FileStream output = File.Open(tempPath, FileMode.Open, FileAccess.Write))
                    {
                        while (0 < (bytesRead = input.Read(buffer, offset, buffer.Length)))
                            output.Write(buffer, 0, bytesRead);
                    }
                }

                // inspect the resulting file for errors
                using (FileStream tempStream = File.Open(tempPath, FileMode.Open, FileAccess.Read))
                {
                    XPathDocument doc = new XPathDocument(tempStream);
                    XPathNavigator nav = doc.CreateNavigator();
                    XPathNavigator errorNode = nav.SelectSingleNode("/eveapi/error");

                    if (errorNode != null)
                        throw new EveApiException(errorNode.SelectSingleNode("@code").ValueAsInt, errorNode.Value);
                }

                // we now assume the file is valid and copy it to the cache path
                File.Copy(tempPath, cachePath, true);
            }
            finally
            {
                // close the response
                if (response != null) response.Close();

                // get rid of the temp file, don't care if it doesn't work
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }
            }

            // return the path to the file we downloaded
            return cachePath;
        }

        private static bool IsFileCached(string apiPath, IDictionary<string, string> parameters)
        {
            return IsFileCached(GetCachePath(apiPath, parameters));
        }

        private static bool IsFileCached(string filePath)
        {
            // check whether it even exists
            if (!File.Exists(filePath)) return false;

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
                    return TimeZone.CurrentTimeZone.ToUniversalTime(DateTime.Now) < cacheTime;
                }
            }
            catch
            {
                return false;
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
            paramHash = BitConverter.ToString(m_md5.ComputeHash(m_encoding.GetBytes(GetEncodedParameters(parameters)))).Replace("-", "");
            mungedApiPath = mungedApiPath.Insert(mungedApiPath.LastIndexOf('.'), "." + paramHash);

            // make sure the directory exists before returning this path
            cachePath = Path.Combine(m_cacheRoot, mungedApiPath);
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
