using System;
using System.Collections.Generic;
using System.Text;

namespace HeavyDuck.Eve
{
    /// <summary>
    /// Contains information on a file in the local cache.
    /// </summary>
    public class CachedResult
    {
        private string m_path;
        private bool m_updated;
        private CacheState m_state;
        private Exception m_ex;

        /// <summary>
        /// Creates a new instance of CachedResult.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        /// <param name="updated">If the file was updated as a result of this request, true; otherwise, false.</param>
        /// <param name="state">The state of the cache for this file.</param>
        public CachedResult(string path, bool updated, CacheState state) : this(path, updated, state, null) { }

        /// <summary>
        /// Creates a new instance of CachedResult.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        /// <param name="updated">If the file was updated as a result of this request, true; otherwise, false.</param>
        /// <param name="state">The state of the cache for this file.</param>
        /// <param name="ex">The exception that prevented the file from being updated, if any.</param>
        public CachedResult(string path, bool updated, CacheState state, Exception ex)
        {
            m_path = path;
            m_updated = updated;
            m_state = state;
            m_ex = ex;
        }

        /// <summary>
        /// Gets the path to the cached file.
        /// </summary>
        public string Path
        {
            get { return m_path; }
        }

        /// <summary>
        /// Gets a value that indicates whether the file was updated as a result of this request.
        /// </summary>
        public bool IsUpdated
        {
            get { return m_updated; }
        }

        /// <summary>
        /// Gets the state of the cache for this file.
        /// </summary>
        public CacheState State
        {
            get { return m_state; }
        }

        /// <summary>
        /// Gets the exception that prevented the file from being updated, if any.
        /// </summary>
        public Exception Exception
        {
            get { return m_ex; }
        }
    }

    /// <summary>
    /// Represents the state of the cache for particular file.
    /// </summary>
    public enum CacheState
    {
        Cached,
        CachedOutOfDate,
        Uncached
    }
}
