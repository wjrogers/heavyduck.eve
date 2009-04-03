using System;
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
        private const string EVECENTRAL_MARKETSTAT_URL = @"http://api.eve-central.com/api/marketstat";
        private const string EVECENTRAL_MINERAL_URL = @"http://api.eve-central.com/api/evemon";
        private const int CACHE_HOURS = 24;

        private static readonly string m_cachePath = Path.Combine(Resources.CacheRoot, "eve-central");
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false);

        public static readonly EveCentralHelper Instance = new EveCentralHelper();

        private EveCentralHelper()
        {
            if (!Directory.Exists(m_cachePath)) Directory.CreateDirectory(m_cachePath);
        }

        public static Dictionary<string, float> GetMineralPrices()
        {
            Dictionary<string, float> results = null;
            string filePath = Path.Combine(m_cachePath, "minerals.xml");

            // download the file
            Resources.CacheFile(EVECENTRAL_MINERAL_URL, Path.Combine(m_cachePath, "minerals.xml"), CACHE_HOURS, delegate(string tempPath)
            {
                // we will try to parse the file now
                results = ParseMineralFile(tempPath);
            });

            return results;
        }

        public static CachedResult GetMarketStat(IEnumerable<int> typeIDs, IEnumerable<int> regionLimits)
        {
            List<KeyValuePair<string, string>> parameters;

            // build the list of parameters
            parameters = new List<KeyValuePair<string, string>>();
            foreach (int typeID in typeIDs)
                parameters.Add(new KeyValuePair<string, string>("typeid", typeID.ToString()));
            foreach (int regionID in regionLimits)
                parameters.Add(new KeyValuePair<string, string>("regionlimit", regionID.ToString()));

            // get the file
            return Resources.CacheFilePost(EVECENTRAL_MARKETSTAT_URL, GetMarketStatPath(parameters), CACHE_HOURS, parameters);
        }

        private static Dictionary<int, double> GetPriceHelper(IEnumerable<int> typeIDs, int? regionID, PriceStat stat)
        {
            CachedResult result;
            List<int> regionLimits;
            Dictionary<int, MarketStat> parsed;
            Dictionary<int, double> answer;

            try
            {
                // if regionID has a value, fill in our single region limit
                regionLimits = new List<int>(1);
                if (regionID.HasValue)
                    regionLimits.Add(regionID.Value);

                // fetch the data we want
                result = GetMarketStat(typeIDs, regionLimits);

                // check that we got something
                if (result.State == CacheState.Uncached)
                    throw new PriceProviderException(PriceProviderFailureReason.CacheEmpty, "Failed to retrieve EVE-Central data", result.Exception);

                // parse it
                parsed = ParseMarketStat(result.Path);

                // convert to the form we want
                answer = new Dictionary<int, double>(parsed.Count);
                foreach (KeyValuePair<int, MarketStat> rawPair in parsed)
                {
                    double price;

                    // read the requested value from the struct
                    switch (stat)
                    {
                        case PriceStat.Mean:
                            price = rawPair.Value.Avg;
                            break;
                        case PriceStat.Median:
                            price = rawPair.Value.Median;
                            break;
                        default:
                            throw new ArgumentException("Don't know how to process PriceStat " + stat);
                    }

                    // add the converted value
                    answer[rawPair.Key] = price;
                }

                // return the answer
                return answer;
            }
            catch (Exception ex)
            {
                throw new PriceProviderException(PriceProviderFailureReason.UnexpectedError, "Unexpected error while querying EVE-Central prices", ex);
            }
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

        private static Dictionary<int, MarketStat> ParseMarketStat(string path)
        {
            Dictionary<int, MarketStat> results = new Dictionary<int, MarketStat>();

            // parse the requested value from the eve-central XML
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                XPathDocument doc = new XPathDocument(fs);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNodeIterator typeNodes = nav.Select("//marketstat/type");
                MarketStat stat;
                int typeID;

                while (typeNodes.MoveNext())
                {
                    // read the data from this node
                    try
                    {
                        typeID = typeNodes.Current.SelectSingleNode("@id").ValueAsInt;

                        stat = new MarketStat();
                        stat.Volume = typeNodes.Current.SelectSingleNode("volume").ValueAsLong;
                        stat.Avg = typeNodes.Current.SelectSingleNode("avg").ValueAsDouble;
                        stat.Max = typeNodes.Current.SelectSingleNode("max").ValueAsDouble;
                        stat.Min = typeNodes.Current.SelectSingleNode("min").ValueAsDouble;
                        stat.StdDev = typeNodes.Current.SelectSingleNode("stddev").ValueAsDouble;
                        stat.Median = typeNodes.Current.SelectSingleNode("median").ValueAsDouble;

                        results[typeID] = stat;
                    }
                    catch { /* pass on to the next one */ }
                }
            }

            return results;
        }

        /// <summary>
        /// Contains the data from an EVE-Central typeID query.
        /// </summary>
        private struct MarketStat
        {
            public long Volume;
            public double Avg;
            public double Max;
            public double Min;
            public double StdDev;
            public double Median;
        }

        private static string GetMarketStatPath(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return Path.Combine(m_cachePath, string.Format("marketstat_{0}.xml", Resources.ComputeParameterHash(parameters)));
        }

        #region IPriceProvider Members

        public double GetPrice(int typeID, PriceStat stat)
        {
            Dictionary<int, double> answer = GetPriceHelper(new int[] { typeID }, null, stat);
            double price;

            if (answer.TryGetValue(typeID, out price))
                return price;
            else
                throw new PriceProviderException(PriceProviderFailureReason.PriceMissing, "Answer did not contain the requested price");
        }

        public double GetPriceHighSec(int typeID, PriceStat stat)
        {
            throw new NotImplementedException();
        }

        public double GetPriceByRegion(int typeID, int regionID, PriceStat stat)
        {
            Dictionary<int, double> answer = GetPriceHelper(new int[] { typeID }, regionID, stat);
            double price;

            if (answer.TryGetValue(typeID, out price))
                return price;
            else
                throw new PriceProviderException(PriceProviderFailureReason.PriceMissing, "Answer did not contain the requested price");
        }

        public Dictionary<int, double> GetPrices(IEnumerable<int> typeIDs, PriceStat stat)
        {
            return GetPriceHelper(typeIDs, null, stat);
        }

        public Dictionary<int, double> GetPricesHighSec(IEnumerable<int> typeIDs, PriceStat stat)
        {
            throw new NotImplementedException();
        }

        public Dictionary<int, double> GetPricesByRegion(IEnumerable<int> typeIDs, int regionID, PriceStat stat)
        {
            return GetPriceHelper(typeIDs, regionID, stat);
        }

        #endregion
    }
}
