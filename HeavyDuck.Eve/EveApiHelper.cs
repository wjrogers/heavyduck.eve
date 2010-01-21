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
            return QueryApi(@"/account/Characters.xml.aspx", GetAccountParameters(userID, apiKey));
        }

        public static CacheResult GetCharacterSheet(int userID, string apiKey, int characterID)
        {
            return QueryApi(@"/char/CharacterSheet.xml.aspx", GetCharacterParameters(userID, apiKey, characterID));
        }

        public static CacheResult GetCharacterAssetList(int userID, string apiKey, int characterID)
        {
            return QueryApi(@"/char/AssetList.xml.aspx", GetCharacterParameters(userID, apiKey, characterID));
        }

        public static CacheResult GetCorporationAssetList(int userID, string apiKey, int characterID, int corporationID)
        {
            return QueryApi(@"/corp/AssetList.xml.aspx", GetCorporationParameters(userID, apiKey, characterID, corporationID));
        }

        private static Dictionary<string, string> GetAccountParameters(int userID, string apiKey)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["version"] = "2";

            // return them
            return parameters;
        }

        private static Dictionary<string, string> GetCharacterParameters(int userID, string apiKey, int characterID)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["characterID"] = characterID.ToString();
            parameters["version"] = "2";

            // return them
            return parameters;
        }

        private static Dictionary<string, string> GetCorporationParameters(int userID, string apiKey, int characterID, int corporationID)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            // add the standard parameters
            parameters["userID"] = userID.ToString();
            parameters["apiKey"] = apiKey;
            parameters["characterID"] = characterID.ToString();
            parameters["corporationID"] = corporationID.ToString();
            parameters["version"] = "2";

            // return them
            return parameters;
        }

        public static CacheResult QueryApi(string apiPath, IDictionary<string, string> parameters)
        {
            string cachePath;
            ICacheStrategy cacheStrategy;

            // check parameters
            if (string.IsNullOrEmpty(apiPath)) throw new ArgumentNullException("apiPath");

            // for the API we have special logic to check whether the file is cached
            cacheStrategy = new EveApiCacheStrategy();
            cachePath = GetCachePath(apiPath, parameters);

            // query the API
            return Resources.CacheFilePost(new Uri(m_apiRoot, apiPath).ToString(), cachePath, cacheStrategy, parameters, delegate(string tempPath)
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
        }

        /// <summary>
        /// Reads the cachedUntil element from an EVE API result.
        /// </summary>
        /// <param name="filePath">the path to the EVE API XML file</param>
        /// <returns>the cache expiration time in local time</returns>
        private static DateTime ReadCachedUntil(string filePath)
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

                // return the date in local time
                return cachedUntil;
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

        private class EveApiCacheStrategy : ICacheStrategy
        {
            #region ICacheStrategy Members

            public DateTime GetCachedUntil(string path)
            {
                return ReadCachedUntil(path);
            }

            #endregion
        }
    }
}
