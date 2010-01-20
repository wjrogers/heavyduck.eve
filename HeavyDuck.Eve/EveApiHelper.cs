using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly Regex m_regexAspx = new Regex(@"\.aspx$");
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false);

        public static readonly Uri DefaultApiRoot = new Uri(@"http://api.eve-online.com/");
        private static Uri m_apiRoot = DefaultApiRoot;

        public static Uri ApiRoot
        {
            get { return m_apiRoot; }
            set { m_apiRoot = value; }
        }

        public static CacheResult GetCharacters(int userID, string apiKey)
        {
            return QueryAccountApi(@"/account/Characters.xml.aspx", userID, apiKey);
        }

        public static CacheResult GetCharacterSheet(int userID, string apiKey, int characterID)
        {
            return QueryCharacterApi(@"/char/CharacterSheet.xml.aspx", userID, apiKey, characterID);
        }

        public static CacheResult GetCharacterAssetList(int userID, string apiKey, int characterID)
        {
            return QueryCharacterApi(@"/char/AssetList.xml.aspx", userID, apiKey, characterID);
        }

        public static CacheResult GetCorporationAssetList(int userID, string apiKey, int characterID, int corporationID)
        {
            return QueryCorporationApi(@"/corp/AssetList.xml.aspx", userID, apiKey, characterID, corporationID);
        }

        private static CacheResult QueryAccountApi(string apiPath, int userID, string apiKey)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["version"] = "2";

            // call the basic query method
            return QueryApi(apiPath, parameters);
        }

        private static CacheResult QueryCharacterApi(string apiPath, int userID, string apiKey, int characterID)
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

        private static CacheResult QueryCorporationApi(string apiPath, int userID, string apiKey, int characterID, int corporationID)
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

        public static CacheResult QueryApi(string apiPath, IDictionary<string, string> parameters)
        {
            string cachePath;
            CacheResult currentResult;

            // check parameters
            if (string.IsNullOrEmpty(apiPath)) throw new ArgumentNullException("apiPath");

            // for the API we have special logic to check whether the file is cached, so do this first
            cachePath = GetCachePath(apiPath, parameters);
            currentResult = IsFileCached(cachePath);
            if (currentResult.State == CacheState.Cached)
                return currentResult;

            // query the API
            currentResult = Resources.CacheFilePost(new Uri(m_apiRoot, apiPath).ToString(), cachePath, TimeSpan.Zero, parameters, delegate(string tempPath)
            {
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
            });

            // return the new state with correct cache expiration
            if (currentResult.State == CacheState.Uncached)
                return currentResult;
            else
                return IsFileCached(cachePath);
        }

        /// <summary>
        /// Checks the state of the cache for a particular file.
        /// </summary>
        /// <param name="apiPath">The EVE API path used to retrieve the cached file.</param>
        /// <param name="parameters">The EVE API parameters used to retrieve the cached file.</param>
        private static CacheResult IsFileCached(string apiPath, IDictionary<string, string> parameters)
        {
            return IsFileCached(GetCachePath(apiPath, parameters));
        }

        /// <summary>
        /// Checks the state of the cache for a particular file.
        /// </summary>
        /// <param name="filePath">The filesystem path to the cached file.</param>
        private static CacheResult IsFileCached(string filePath)
        {
            // check whether it even exists
            if (!File.Exists(filePath)) return CacheResult.Uncached(filePath);

            // open and look for the cachedUntil element
            try
            {
                using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read))
                {
                    XPathDocument doc = new XPathDocument(fs);
                    XPathNavigator nav = doc.CreateNavigator();
                    XPathNavigator cacheNode;
                    DateTime cachedUntil;

                    // parse the cached-until date from the XML
                    cacheNode = nav.SelectSingleNode("/eveapi/cachedUntil");
                    cachedUntil = DateTime.Parse(cacheNode.Value, CultureInfo.InvariantCulture);
                    cachedUntil = TimeZone.CurrentTimeZone.ToLocalTime(cachedUntil);

                    // now we can compare to the current time
                    if (DateTime.Now < cachedUntil)
                        return new CacheResult(filePath, false, CacheState.Cached, cachedUntil);
                    else
                        return new CacheResult(filePath, false, CacheState.CachedOutOfDate, cachedUntil);
                }
            }
            catch (Exception ex)
            {
                return CacheResult.Uncached(filePath, ex);
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
            paramHash = Resources.ComputeParameterHash(parameters);
            mungedApiPath = mungedApiPath.Insert(mungedApiPath.LastIndexOf('.'), "." + paramHash);

            // make sure the directory exists before returning this path
            cachePath = Path.Combine(Resources.CacheRoot, mungedApiPath);
            dirPath = Path.GetDirectoryName(cachePath);
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            return cachePath;
        }
    }
}
