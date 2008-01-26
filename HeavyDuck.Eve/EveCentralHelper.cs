using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public static class EveCentralHelper
    {
        private const string EVECENTRAL_MARKETSTAT_URL = @"http://eve-central.com/home/marketstat_xml.html?regionlimit=10000002&typeid=";
        private const string EVECENTRAL_MINERAL_URL = @"http://eve-central.com/home/marketstat_xml.html?evemon=1";
        private const int CACHE_HOURS = 24;

        private static readonly string m_cachePath = Path.Combine(Resources.CacheRoot, "eve-central");

        static EveCentralHelper()
        {
            if (!Directory.Exists(m_cachePath)) Directory.CreateDirectory(m_cachePath);
        }

        public static Dictionary<string, double> GetMineralPrices()
        {
            Dictionary<string, double> results;
            string filePath = Path.Combine(m_cachePath, "minerals.xml");

            // check whether the file is already there
            if (IsFileCached(filePath)) return ParseMineralFile(filePath);

            // create request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(EVECENTRAL_MINERAL_URL);

            // set request properties
            request.KeepAlive = false;
            request.Method = "GET";
            request.UserAgent = Resources.USER_AGENT;

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
                    tempPath = Resources.DownloadStream(input);
                }

                // we will try to parse the file now
                results = ParseMineralFile(tempPath);

                // if that worked, we assume everything is dandy and copy the file to the cache
                File.Copy(tempPath, filePath, true);
            }
            finally
            {
                // clean up
                if (response != null) response.Close();

                // get rid of the temp file if we can
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }
            }

            // return the results
            return results;
        }

        public static double GetItemAveragePrice(int typeID)
        {
            string filePath = GetMarketFileName(typeID);
            double price;

            // check whether the file is cached
            if (IsFileCached(filePath)) return ParseAveragePrice(filePath);

            // create request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(EVECENTRAL_MARKETSTAT_URL + typeID.ToString());

            // set request properties
            request.KeepAlive = false;
            request.Method = "GET";
            request.UserAgent = Resources.USER_AGENT;

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
                    tempPath = Resources.DownloadStream(input);
                }

                // we will try to parse the file now
                price = ParseAveragePrice(tempPath);

                // if that worked, we assume everything is dandy and copy the file to the cache
                File.Copy(tempPath, filePath, true);
            }
            finally
            {
                // clean up
                if (response != null) response.Close();

                // get rid of the temp file if we can
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }
            }

            // return the results
            return price;
        }

        private static Dictionary<string, double> ParseMineralFile(string path)
        {
            Dictionary<string, double> prices = new Dictionary<string,double>();

            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                XPathDocument doc = new XPathDocument(fs);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNodeIterator iter = nav.Select("/minerals/mineral");

                while (iter.MoveNext())
                {
                    string mineral = iter.Current.SelectSingleNode("name").Value;
                    double price = iter.Current.SelectSingleNode("price").ValueAsDouble;

                    prices[mineral] = price;
                }
            }

            return prices;
        }

        private static double ParseAveragePrice(string path)
        {
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                XPathDocument doc = new XPathDocument(fs);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNavigator node = nav.SelectSingleNode("/market_stat/avg_price");

                if (node == null)
                    return -1;
                else
                    return node.ValueAsDouble;
            }
        }

        private static bool IsFileCached(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                TimeSpan age = DateTime.Now.Subtract(info.LastWriteTime);

                return age.TotalHours < CACHE_HOURS;
            }
            catch
            {
                return false;
            }
        }

        private static string GetMarketFileName(int typeID)
        {
            return Path.Combine(m_cachePath, string.Format("marketstat_{0}.xml", typeID));
        }
    }
}
