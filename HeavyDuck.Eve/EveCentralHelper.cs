using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public class EveCentralHelper : IPriceProvider
    {
        private const string EVECENTRAL_MARKETSTAT_URL = @"http://api.eve-central.com/api/marketstat";
        private const string EVECENTRAL_MINERAL_URL = @"http://api.eve-central.com/api/evemon";
        private const int MAX_TYPES_PER_QUERY = 100;
        private const int CACHE_VERSION = 2;

        private static readonly Dictionary<int, Dictionary<int, MarketStat>> m_cache = new Dictionary<int, Dictionary<int, MarketStat>>();
        private static readonly string m_cachePath = Path.Combine(Resources.CacheRoot, "eve-central");
        private static readonly string m_cacheFilePath = Path.Combine(m_cachePath, "cache");
        private static readonly string m_cacheVersionPath = Path.Combine(m_cachePath, "version");
        private static readonly TimeSpan m_cacheDuration = TimeSpan.FromHours(23);
        private static readonly TimeSpan m_rateLimit = TimeSpan.FromMilliseconds(100);
        private static readonly object m_rateLock = new object();
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false);

        private static DateTime m_lastQuery = DateTime.MinValue;
        private static bool m_cacheDirty = false;

        public static readonly EveCentralHelper Instance = new EveCentralHelper();

        private EveCentralHelper()
        {
            // pass
        }

        private static bool TryGetCachedMarketStat(int typeID, int regionID, out MarketStat value)
        {
            Dictionary<int, MarketStat> regionCache;

            // check cache
            lock (m_cache)
            {
                if (m_cache.TryGetValue(regionID, out regionCache)
                    && regionCache.TryGetValue(typeID, out value)
                    && DateTime.Now.Subtract(value.TimeStamp) < m_cacheDuration)
                    return true;
            }

            // default
            value = new MarketStat();
            return false;
        }

        private static void CacheMarketStat(int typeID, int regionID, MarketStat value)
        {
            Dictionary<int, MarketStat> regionCache;

            lock (m_cache)
            {
                // fetch or create the region cache
                if (!m_cache.TryGetValue(regionID, out regionCache))
                {
                    regionCache = new Dictionary<int, MarketStat>();
                    m_cache[regionID] = regionCache;
                }

                // store the value
                regionCache[typeID] = value;
                m_cacheDirty = true;
            }
        }

        private void OnUpdateProgress(int progress, int max)
        {
            EventHandler<ProgressEventArgs> handler = this.UpdateProgress;

            if (handler != null)
                handler(this, new ProgressEventArgs(progress, max));
        }

        private Dictionary<int, decimal> GetPriceHelper(IEnumerable<int> typeIDs, int regionID, PriceStat stat)
        {
            Dictionary<int, MarketStat> parsed;
            Dictionary<int, MarketStat> cached = new Dictionary<int, MarketStat>();
            Dictionary<int, decimal> answer;
            List<int> uncachedTypeIDs = new List<int>();
            List<KeyValuePair<string, string>> parameters;
            string resultPath = null;

            // check the cache, add cache misses to the query parameters
            foreach (int typeID in typeIDs)
            {
                MarketStat value;

                if (TryGetCachedMarketStat(typeID, regionID, out value))
                    cached[typeID] = value;
                else
                    uncachedTypeIDs.Add(typeID);
            }

            try
            {
                // EVE Central allows us only 100 types per request
                for (int i = 0; i * MAX_TYPES_PER_QUERY < uncachedTypeIDs.Count; ++i)
                {
                    // progress feedback
                    OnUpdateProgress(i * MAX_TYPES_PER_QUERY, uncachedTypeIDs.Count);

                    // rate limit queries
                    lock (m_rateLock)
                    {
                        TimeSpan elapsed = DateTime.Now.Subtract(m_lastQuery);

                        if (elapsed < m_rateLimit)
                            System.Threading.Thread.Sleep(m_rateLimit.Subtract(elapsed));

                        m_lastQuery = DateTime.Now;
                    }

                    // generate typeid parameters for this iteration
                    parameters = new List<KeyValuePair<string, string>>();
                    for (int j = i * MAX_TYPES_PER_QUERY; j < uncachedTypeIDs.Count && j < (i + 1) * MAX_TYPES_PER_QUERY; ++j)
                        parameters.Add(new KeyValuePair<string, string>("typeid", uncachedTypeIDs[j].ToString()));

                    // if regionID has a value, fill in our single region limit
                    if (regionID > 0)
                        parameters.Add(new KeyValuePair<string, string>("regionlimit", regionID.ToString()));

                    try
                    {
                        // fetch the data we want
                        resultPath = Resources.DownloadUrlPost(EVECENTRAL_MARKETSTAT_URL, parameters);

                        // parse it
                        parsed = ParseMarketStat(resultPath);
                    }
                    finally
                    {
                        // clean up temp file
                        if (resultPath != null)
                        {
                            try { File.Delete(resultPath); }
                            catch { /* pass */ }
                        }
                    }

                    // cache the new stuff and copy to the cached dictionary for output below
                    foreach (KeyValuePair<int, MarketStat> entry in parsed)
                    {
                        CacheMarketStat(entry.Key, regionID, entry.Value);
                        cached[entry.Key] = entry.Value;
                    }
                }

                // progress update the last
                OnUpdateProgress(uncachedTypeIDs.Count, uncachedTypeIDs.Count);

                // convert output to the form we want
                answer = new Dictionary<int, decimal>(cached.Count);
                foreach (KeyValuePair<int, MarketStat> entry in cached)
                {
                    decimal price;

                    // read the requested value from the struct
                    switch (stat)
                    {
                        case PriceStat.Mean:
                            price = entry.Value.All.Avg;
                            break;
                        case PriceStat.Median:
                            price = entry.Value.All.Median;
                            break;
                        case PriceStat.High:
                            price = entry.Value.All.Max;
                            break;
                        case PriceStat.Low:
                            price = entry.Value.All.Min;
                            break;
                        default:
                            throw new ArgumentException("Don't know how to process PriceStat " + stat);
                    }

                    // add the converted value
                    answer[entry.Key] = price;
                }

                // return the answer
                return answer;
            }
            catch (Exception ex)
            {
                throw new PriceProviderException(PriceProviderFailureReason.UnexpectedError, "Unexpected error while querying EVE-Central prices", ex);
            }
        }

        private static Dictionary<int, MarketStat> ParseMarketStat(string path)
        {
            Dictionary<int, MarketStat> results = new Dictionary<int, MarketStat>();
            DateTime now = DateTime.Now;

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
                        stat.TimeStamp = now;
                        stat.All = ParseMarketStatEntry(typeNodes.Current.SelectSingleNode("all"));
                        stat.Buy = ParseMarketStatEntry(typeNodes.Current.SelectSingleNode("buy"));
                        stat.Sell = ParseMarketStatEntry(typeNodes.Current.SelectSingleNode("sell"));

                        results[typeID] = stat;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex.ToString());
                    }
                }
            }

            return results;
        }

        private static MarketStatEntry ParseMarketStatEntry(XPathNavigator root)
        {
            MarketStatEntry entry;

            entry = new MarketStatEntry();
            entry.Volume = Convert.ToDecimal(root.SelectSingleNode("volume").Value);
            entry.Avg = Convert.ToDecimal(root.SelectSingleNode("avg").Value);
            entry.Max = Convert.ToDecimal(root.SelectSingleNode("max").Value);
            entry.Min = Convert.ToDecimal(root.SelectSingleNode("min").Value);
            entry.StdDev = Convert.ToDecimal(root.SelectSingleNode("stddev").Value);
            entry.Median = Convert.ToDecimal(root.SelectSingleNode("median").Value);

            return entry;
        }

        /// <summary>
        /// Contains the data from an EVE-Central marketstat query.
        /// </summary>
        [Serializable]
        private class MarketStat
        {
            public DateTime TimeStamp;
            public MarketStatEntry All;
            public MarketStatEntry Buy;
            public MarketStatEntry Sell;
        }

        /// <summary>
        /// Contains one set of statistics from an EVE-Central marketstat query.
        /// </summary>
        [Serializable]
        private class MarketStatEntry
        {
            public decimal Volume;
            public decimal Avg;
            public decimal Max;
            public decimal Min;
            public decimal StdDev;
            public decimal Median;
        }

        #region IPriceProvider Members

        public event EventHandler<ProgressEventArgs> UpdateProgress;

        public void LoadCache()
        {
            BinaryFormatter formatter = new BinaryFormatter();
            int version;

            try
            {
                // read the cache version
                using (StreamReader r = new StreamReader(File.OpenRead(m_cacheVersionPath)))
                    version = int.Parse(r.ReadToEnd());

                // if it doesn't match the current version, abort
                if (version != CACHE_VERSION)
                    throw new ApplicationException("Invalid EveCentralHelper cache version");
            }
            catch (Exception ex)
            {
                // an error means the cache is invalid; try to delete it
                System.Diagnostics.Debug.WriteLine(ex.ToString());
                try { Directory.Delete(m_cachePath, true); }
                catch { /* pass */ }
                return;
            }

            lock (m_cache)
            {
                Dictionary<int, Dictionary<int, MarketStat>> cachedObject;

                try
                {
                    // deserialize prices
                    using (FileStream fs = File.OpenRead(m_cacheFilePath))
                        cachedObject = (Dictionary<int, Dictionary<int, MarketStat>>)formatter.Deserialize(fs);

                    // merge them with our existing readonly cache
                    foreach (KeyValuePair<int, Dictionary<int, MarketStat>> entry in cachedObject)
                    {
                        Dictionary<int, MarketStat> regionCache;

                        // create or fetch region dictionary
                        if (!m_cache.TryGetValue(entry.Key, out regionCache))
                        {
                            regionCache = new Dictionary<int, MarketStat>();
                            m_cache[entry.Key] = regionCache;
                        }

                        foreach (KeyValuePair<int, MarketStat> regionEntry in entry.Value)
                            regionCache[regionEntry.Key] = regionEntry.Value;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                }
            }
        }

        public void SaveCache()
        {
            BinaryFormatter formatter = new BinaryFormatter();

            // sanity check
            lock (m_cache) if (!m_cacheDirty) return;

            try
            {
                // make sure cache path exists
                Directory.CreateDirectory(m_cachePath);

                // mark the cache version
                using (StreamWriter w = new StreamWriter(File.OpenWrite(m_cacheVersionPath), m_encoding))
                    w.Write(CACHE_VERSION);

                lock (m_cache)
                {
                    // serialize prices
                    using (FileStream fs = File.OpenWrite(m_cacheFilePath))
                        formatter.Serialize(fs, m_cache);

                    // not dirty!
                    m_cacheDirty = false;
                }
            }
            catch
            {
                // if something bad happens, delete the version file so the possibly-invalid cache won't get read in the future
                try { File.Delete(m_cacheVersionPath); }
                catch { /* pass */ }
                throw;
            }
        }

        public decimal GetPrice(int typeID, PriceStat stat)
        {
            return GetPriceByRegion(typeID, PriceRegion.ALL, stat);
        }

        public decimal GetPriceHighSec(int typeID, PriceStat stat)
        {
            throw new NotImplementedException();
        }

        public decimal GetPriceByRegion(int typeID, int regionID, PriceStat stat)
        {
            Dictionary<int, decimal> answer = GetPriceHelper(new int[] { typeID }, regionID, stat);
            decimal price;

            if (answer.TryGetValue(typeID, out price))
                return price;
            else
                throw new PriceProviderException(PriceProviderFailureReason.PriceMissing, "Answer did not contain the requested price");
        }

        public Dictionary<int, decimal> GetPrices(IEnumerable<int> typeIDs, PriceStat stat)
        {
            return GetPricesByRegion(typeIDs, PriceRegion.ALL, stat);
        }

        public Dictionary<int, decimal> GetPricesHighSec(IEnumerable<int> typeIDs, PriceStat stat)
        {
            throw new NotImplementedException();
        }

        public Dictionary<int, decimal> GetPricesByRegion(IEnumerable<int> typeIDs, int regionID, PriceStat stat)
        {
            Dictionary<int, decimal> result;

            // do the query
            result = GetPriceHelper(typeIDs, regionID, stat);

            // save the cache sooner rather than later, just in case!
            SaveCache();

            // return it
            return result;
        }

        #endregion
    }
}
