using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace HeavyDuck.Eve
{
    public class NonNinjaHelper
    {
        private const string MEDIANS_TXT_URL = @"http://nonninja.net/eve/prices/medians.txt";
        private const int CACHE_HOURS = 24;

        private static readonly string m_cachePath = Path.Combine(Resources.CacheRoot, "nonninja");

        static NonNinjaHelper()
        {
            if (!Directory.Exists(m_cachePath)) Directory.CreateDirectory(m_cachePath);
        }

        public static CachedResult GetMediansTxt()
        {
            string filePath = Path.Combine(m_cachePath, "medians.txt");
            CacheState currentState = IsFileCached(filePath);

            // check whether the file is cached
            if (currentState == CacheState.Cached) return new CachedResult(filePath, false, CacheState.Cached);

            // create request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(MEDIANS_TXT_URL);

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

                // we assume the file is fine because there is really no easy way to test it
                File.Copy(tempPath, filePath, true);

                // return success
                return new CachedResult(filePath, true, CacheState.Cached);
            }
            catch (Exception ex)
            {
                // if we currently have a valid local copy of the file, even if out of date, return that info
                if (currentState != CacheState.Uncached)
                    return new CachedResult(filePath, false, currentState, ex);
                else
                    return new CachedResult(null, false, CacheState.Uncached, ex);
            }
            finally
            {
                // clean up
                if (response != null) response.Close();

                // get rid of the temp file if we can
                try { if (!string.IsNullOrEmpty(tempPath)) File.Delete(tempPath); }
                catch { /* pass */ }
            }
        }

        public static Dictionary<long, NonNinjaMedians> ParseMediansTxt(string path)
        {
            Dictionary<long, NonNinjaMedians> medians = new Dictionary<long, NonNinjaMedians>();

            // parse the file, this should be fairly straightforward
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    string[] fields;
                    string line;
                    long typeID;
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
                            typeID = Convert.ToInt64(fields[0]);
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

            // return the numbahs
            return medians;
        }

        private static CacheState IsFileCached(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);

                if (info.Exists && DateTime.Now.Subtract(info.LastWriteTime).TotalHours < CACHE_HOURS)
                    return CacheState.Cached;
                else if (info.Exists)
                    return CacheState.CachedOutOfDate;
                else
                    return CacheState.Uncached;
            }
            catch
            {
                return CacheState.Uncached;
            }
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
