using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HeavyDuck.Eve
{
    internal interface ICacheStrategy
    {
        DateTime GetCachedUntil(string path);
    }

    internal class TtlCacheStrategy : ICacheStrategy
    {
        private TimeSpan m_ttl;

        public TtlCacheStrategy(TimeSpan ttl)
        {
            m_ttl = ttl;
        }

        #region ICacheStrategy Members

        public DateTime GetCachedUntil(string path)
        {
            return File.GetLastWriteTime(path).Add(m_ttl);
        }

        #endregion
    }

}
