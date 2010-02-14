using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public class ZofuHelper : IPriceProvider
    {
        private static string m_cachePath = Path.Combine(Resources.CacheRoot, "zofu");
        private static readonly Uri m_root30d = new Uri("http://eve.no-ip.de/prices/30d/");
        private static readonly TimeSpan m_cacheTtl = TimeSpan.FromHours(4);
        private static readonly Dictionary<int, Dictionary<int, ZofuEntry>> m_cache = new Dictionary<int, Dictionary<int, ZofuEntry>>();
        private static readonly Regex m_cacheFileRegex = new Regex(@"^prices-(\d+|all|chs)\.xml$");

        public static readonly ZofuHelper Instance = new ZofuHelper();

        private ZofuHelper()
        {
            // pass
        }

        private static string GetRegionFileName(int regionID)
        {
            if (regionID == PriceRegion.ALL)
                return "prices-all.xml";
            else if (regionID == PriceRegion.CHS)
                return "prices-chs.xml";
            else
                return string.Format("prices-{0}.xml", regionID);
        }

        private static Uri GetRegionUri(int regionID)
        {
            return new Uri(m_root30d, GetRegionFileName(regionID));
        }

        private static int GetRegionID(string match)
        {
            if (match == "all")
                return PriceRegion.ALL;
            else if (match == "chs")
                return PriceRegion.CHS;
            else
                return int.Parse(match, CultureInfo.InvariantCulture);
        }

        private static Dictionary<int, ZofuEntry> ParseFile(string path)
        {
            Dictionary<int, ZofuEntry> result = new Dictionary<int, ZofuEntry>();

            using (FileStream fs = File.OpenRead(path))
            {
                XPathDocument doc = new XPathDocument(fs);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNodeIterator iter = nav.Select("/eveapi/result/rowset[@name = 'prices']/row");

                while (iter.MoveNext())
                {
                    int typeID = int.Parse(iter.Current.SelectSingleNode("@typeID").Value, CultureInfo.InvariantCulture);

                    result[typeID] = new ZofuEntry()
                    {
                        Avg = decimal.Parse(iter.Current.SelectSingleNode("@avg").Value, CultureInfo.InvariantCulture),
                        Median = decimal.Parse(iter.Current.SelectSingleNode("@median").Value, CultureInfo.InvariantCulture),
                        Volume = long.Parse(iter.Current.SelectSingleNode("@vol").Value, CultureInfo.InvariantCulture),
                        Low = decimal.Parse(iter.Current.SelectSingleNode("@lo").Value, CultureInfo.InvariantCulture),
                        High = decimal.Parse(iter.Current.SelectSingleNode("@hi").Value, CultureInfo.InvariantCulture),
                        First = DateTime.Parse(iter.Current.SelectSingleNode("@first").Value, CultureInfo.InvariantCulture),
                        Last = DateTime.Parse(iter.Current.SelectSingleNode("@last").Value, CultureInfo.InvariantCulture)
                    };
                }
            }

            return result;
        }

        private void OnUpdateProgress(int progress, int max)
        {
            EventHandler<ProgressEventArgs> handler = UpdateProgress;

            if (handler != null)
                handler(this, new ProgressEventArgs(progress, max));
        }

        private CacheResult DownloadRegionFile(int regionID)
        {
            string path = Path.Combine(m_cachePath, GetRegionFileName(regionID));
            string url = GetRegionUri(regionID).ToString();

            // create the cache path if it doesn't exist
            Directory.CreateDirectory(m_cachePath);

            // check first what the current state is so we can raise the progress event
            CacheResult current = Resources.IsFileCached(path, m_cacheTtl);
            if (current.State != CacheState.Cached)
                OnUpdateProgress(0, 1);

            // cache it
            return Resources.CacheFile(url, path, m_cacheTtl, delegate(string tempPath)
            {
                // parse it
                lock (m_cache)
                    m_cache[regionID] = ParseFile(tempPath);
            });
        }

        private decimal GetPriceInternal(int typeID, int regionID, PriceStat stat)
        {
            Dictionary<int, decimal> result;
            decimal value;

            result = GetPricesInternal(new int[] { typeID }, regionID, stat);
            if (!result.TryGetValue(typeID, out value))
                throw new PriceProviderException(PriceProviderFailureReason.PriceMissing, "No price for typeID " + typeID.ToString());
            else
                return value;
        }

        private Dictionary<int, decimal> GetPricesInternal(IEnumerable<int> typeIDs, int regionID, PriceStat stat)
        {
            Dictionary<int, ZofuEntry> regionCache;
            Dictionary<int, decimal> result = new Dictionary<int, decimal>();
            ZofuEntry entry;
            CacheResult cacheResult;

            // check and download the file if it's missing or out of date
            cacheResult = DownloadRegionFile(regionID);

            // all right, result, let's fill it
            lock (m_cache)
            {
                // see if we have prices for the region
                if (!m_cache.TryGetValue(regionID, out regionCache))
                    throw new PriceProviderException(PriceProviderFailureReason.CacheEmpty, "No prices available for region " + regionID.ToString(), cacheResult.Exception);

                // the easy part
                foreach (int typeID in typeIDs)
                {
                    if (regionCache.TryGetValue(typeID, out entry))
                    {
                        switch (stat)
                        {
                            case PriceStat.Mean:
                                result[typeID] = entry.Avg;
                                break;
                            case PriceStat.Median:
                                result[typeID] = entry.Median;
                                break;
                            case PriceStat.Low:
                                result[typeID] = entry.Low;
                                break;
                            case PriceStat.High:
                                result[typeID] = entry.High;
                                break;
                            default:
                                throw new ApplicationException("Unknown stat " + stat.ToString());
                        }
                    }
                }
            }

            // finally
            return result;
        }

        private class ZofuEntry
        {
            public decimal Avg;
            public decimal Median;
            public long Volume;
            public decimal Low;
            public decimal High;
            public DateTime First;
            public DateTime Last;
        }

        #region IPriceProvider Members

        public event EventHandler<ProgressEventArgs> UpdateProgress;

        public void LoadCache()
        {
            // sanity check
            if (!Directory.Exists(m_cachePath)) return;

            // scan for cached files
            lock (m_cache)
            {
                foreach (string file in Directory.GetFiles(m_cachePath))
                {
                    Match match;
                    int regionID;
                    
                    // check whether the filename matches our pattern
                    match = m_cacheFileRegex.Match(Path.GetFileName(file));
                    if (!match.Success)
                        continue;

                    // read the file into the in-memory cache
                    try
                    {
                        regionID = GetRegionID(match.Groups[1].Value);
                        m_cache[regionID] = ParseFile(file);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex.ToString());
                    }
                }
            }
        }

        public void SaveCache()
        {
            /* no-op, downloading the file saves the cache */
        }

        public decimal GetPrice(int typeID, PriceStat stat)
        {
            return GetPriceByRegion(typeID, PriceRegion.ALL, stat);
        }

        public decimal GetPriceHighSec(int typeID, PriceStat stat)
        {
            return GetPriceByRegion(typeID, PriceRegion.CHS, stat);
        }

        public decimal GetPriceByRegion(int typeID, int regionID, PriceStat stat)
        {
            return GetPriceInternal(typeID, regionID, stat);
        }

        public Dictionary<int, decimal> GetPrices(IEnumerable<int> typeIDs, PriceStat stat)
        {
            return GetPricesByRegion(typeIDs, PriceRegion.ALL, stat);
        }

        public Dictionary<int, decimal> GetPricesHighSec(IEnumerable<int> typeIDs, PriceStat stat)
        {
            return GetPricesByRegion(typeIDs, PriceRegion.CHS, stat);
        }

        public Dictionary<int, decimal> GetPricesByRegion(IEnumerable<int> typeIDs, int regionID, PriceStat stat)
        {
            return GetPricesInternal(typeIDs, regionID, stat);
        }

        #endregion
    }
}
