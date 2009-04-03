﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public class EveCentralHelper : IPriceProvider
    {
        private const string EVECENTRAL_MARKETSTAT_URL = @"http://eve-central.com/home/marketstat_xml.html?regionlimit=10000002&typeid=";
        private const string EVECENTRAL_MINERAL_URL = @"http://eve-central.com/home/marketstat_xml.html?evemon=1";
        private const int CACHE_HOURS = 24;

        private static readonly EveCentralHelper m_instance = new EveCentralHelper();
        private static readonly string m_cachePath = Path.Combine(Resources.CacheRoot, "eve-central");

        private EveCentralHelper()
        {
            if (!Directory.Exists(m_cachePath)) Directory.CreateDirectory(m_cachePath);
        }

        public Dictionary<string, float> GetMineralPrices()
        {
            Dictionary<string, float> results;
            string filePath = Path.Combine(m_cachePath, "minerals.xml");
            CacheState currentState = Resources.IsFileCached(filePath, CACHE_HOURS);

            // check whether the file is already there
            if (currentState == CacheState.Cached) return ParseMineralFile(filePath);

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
            catch
            {
                // parse the existing out of date file
                if (currentState != CacheState.Uncached)
                    return ParseMineralFile(filePath);
                else
                    throw;
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

        public float GetItemPrice(int typeID)
        {
            return GetItemMarketStat(typeID, MarketStat.AvgPrice);
        }

        public Dictionary<int, float> GetItemPrices(IEnumerable<int> typeIDs)
        {
            Dictionary<int, float> result = new Dictionary<int, float>();

            foreach (int typeID in typeIDs)
                result[typeID] = GetItemMarketStat(typeID, MarketStat.AvgPrice);

            return result;
        }

        public float GetItemMarketStat(int typeID, MarketStat stat)
        {
            string filePath = GetMarketFileName(typeID);
            float value;
            CacheState currentState = Resources.IsFileCached(filePath, CACHE_HOURS);

            // check whether the file is cached
            if (currentState == CacheState.Cached) return ParseMarketStat(filePath, stat);

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
                value = ParseMarketStat(tempPath, stat);

                // if that worked, we assume everything is dandy and copy the file to the cache
                File.Copy(tempPath, filePath, true);
            }
            catch
            {
                // parse the existing out of date file
                if (currentState != CacheState.Uncached)
                    return ParseMarketStat(filePath, stat);
                else
                    throw;
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
            return value;
        }

        private static Dictionary<string, float> ParseMineralFile(string path)
        {
            Dictionary<string, float> prices = new Dictionary<string, float>();

            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                XPathDocument doc = new XPathDocument(fs);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNodeIterator iter = nav.Select("/minerals/mineral");

                while (iter.MoveNext())
                {
                    string mineral = iter.Current.SelectSingleNode("name").Value;
                    float price = Convert.ToSingle(iter.Current.SelectSingleNode("price").Value);

                    prices[mineral] = price;
                }
            }

            return prices;
        }

        private static float ParseMarketStat(string path, MarketStat stat)
        {
            string localName;

            // convert the MarketStat enumeration to an XML element name
            switch (stat)
            {
                case MarketStat.AvgPrice:
                    localName = "avg_price";
                    break;
                case MarketStat.TotalVolume:
                    localName = "total_volume";
                    break;
                case MarketStat.AvgBuyPrice:
                    localName = "avg_buy_price";
                    break;
                case MarketStat.TotalBuyVolume:
                    localName = "total_buy_volume";
                    break;
                case MarketStat.StdDevBuyPrice:
                    localName = "stddev_buy_price";
                    break;
                case MarketStat.MaxBuyPrice:
                    localName = "max_buy_price";
                    break;
                case MarketStat.MinBuyPrice:
                    localName = "min_buy_price";
                    break;
                case MarketStat.AvgSellPrice:
                    localName = "avg_sell_price";
                    break;
                case MarketStat.TotalSellVolume:
                    localName = "total_sell_volume";
                    break;
                case MarketStat.StdDevSellPrice:
                    localName = "stddev_sell_price";
                    break;
                case MarketStat.MaxSellPrice:
                    localName = "max_sell_price";
                    break;
                case MarketStat.MinSellPrice:
                    localName = "min_sell_price";
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unrecognized MarketStat " + stat.ToString());
            }

            // parse the requested value from the eve-central XML
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                XPathDocument doc = new XPathDocument(fs);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNavigator node = nav.SelectSingleNode("/market_stat/" + localName);

                if (node == null)
                {
                    throw new ArgumentException(string.Format("File '{0}' does not contain a /market_stat/{1} node.", path, localName));
                }
                else
                {
                    // if the node is there, but the value is "None" or some such, return 0 to indicate "this file is OK, but there is no price"
                    try { return Convert.ToSingle(node.Value); }
                    catch (FormatException) { return 0; }
                }
            }
        }

        private static string GetMarketFileName(int typeID)
        {
            return Path.Combine(m_cachePath, string.Format("marketstat_{0}.xml", typeID));
        }
    }

    public enum MarketStat
    {
        AvgPrice,
        TotalVolume,

        AvgBuyPrice,
        TotalBuyVolume,
        StdDevBuyPrice,
        MaxBuyPrice,
        MinBuyPrice,

        AvgSellPrice,
        TotalSellVolume,
        StdDevSellPrice,
        MaxSellPrice,
        MinSellPrice,
    }
}
