using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public class NonNinjaHelper
    {
        private const string MEDIANS_TXT_ALL_URL = @"http://nonninja.net/eve/prices/medians.txt.gz";
        private const string MEDIANS_TXT_URL = @"http://nonninja.net/eve/prices/{0}.txt.gz";
        private const int CACHE_HOURS = 24;

        private static readonly string m_cachePath = Path.Combine(Resources.CacheRoot, "nonninja");

        static NonNinjaHelper()
        {
            if (!Directory.Exists(m_cachePath)) Directory.CreateDirectory(m_cachePath);
        }

        /// <summary>
        /// Gets the median prices file for all regions.
        /// </summary>
        public static CachedResult GetMediansTxt()
        {
            return GetMediansTxt(null);
        }

        /// <summary>
        /// Gets the median prices file for the specified region.
        /// </summary>
        /// <param name="regionID">The region ID, or null for all regions.</param>
        public static CachedResult GetMediansTxt(int? regionID)
        {
            string url, cachePath;

            // set the url and filename based on regionID
            if (regionID.HasValue)
            {
                url = string.Format(MEDIANS_TXT_URL, regionID);
                cachePath = Path.Combine(m_cachePath, regionID.ToString() + ".txt.gz");
            }
            else
            {
                url = MEDIANS_TXT_ALL_URL;
                cachePath = Path.Combine(m_cachePath, "medians.txt.gz");
            }

            // use the generic downloader
            return Resources.CacheFile(url, cachePath, CACHE_HOURS);
        }

        public static Dictionary<int, NonNinjaMedians> ParseMediansTxt(string path)
        {
            Dictionary<int, NonNinjaMedians> medians = new Dictionary<int, NonNinjaMedians>();

            // parse the file, this should be fairly straightforward
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                using (GZipStream zip = new GZipStream(stream, CompressionMode.Decompress))
                {
                    using (StreamReader reader = new StreamReader(zip))
                    {
                        string[] fields;
                        string line;
                        int typeID;
                        float sellMedian, buyMedian;
                        int errors = 0;

                        // discard the column header line
                        reader.ReadLine();

                        // read stuff for real
                        while (null != (line = reader.ReadLine()))
                        {
                            fields = line.Split(',');

                            // check that there are there numbers there at least
                            if (fields.Length < 3) continue;

                            // parse numbers, add, etc.
                            try
                            {
                                typeID = Convert.ToInt32(fields[0]);
                                sellMedian = Convert.ToSingle(fields[1]);
                                buyMedian = Convert.ToSingle(fields[2]);

                                medians[typeID] = new NonNinjaMedians(typeID, buyMedian, sellMedian);
                            }
                            catch (Exception ex)
                            {
                                // tolerate 20 errors before we give it up
                                if (++errors > 20) throw new ApplicationException("Unable to parse nonninja medians.txt file.", ex);
                            }
                        }
                    }
                }
            }

            // return the numbahs
            return medians;
        }
    }

    public struct NonNinjaMedians
    {
        public readonly long TypeID;
        public readonly float BuyMedian;
        public readonly float SellMedian;

        public NonNinjaMedians(long typeID, float buyMedian, float sellMedian)
        {
            this.TypeID = typeID;
            this.BuyMedian = buyMedian;
            this.SellMedian = sellMedian;
        }
    }
}
