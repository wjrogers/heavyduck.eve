using System;
using System.Collections.Generic;
using System.Text;

namespace HeavyDuck.Eve
{
    /// <summary>
    /// Contains information on a file in the local cache.
    /// </summary>
    public class CacheResult
    {
        private string m_path;
        private bool m_updated;
        private CacheState m_state;
        private Exception m_ex;
        private DateTime m_cachedUntil;

        /// <summary>
        /// Creates a new instance of CachedResult.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        /// <param name="updated">If the file was updated as a result of this request, true; otherwise, false.</param>
        /// <param name="state">The state of the cache for this file.</param>
        /// <param name="cachedUntil">The time the cache expires.</param>
        public CacheResult(string path, bool updated, CacheState state, DateTime cachedUntil) : this(path, updated, state, cachedUntil, null) { }

        /// <summary>
        /// Creates a new instance of CachedResult.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        /// <param name="updated">If the file was updated as a result of this request, true; otherwise, false.</param>
        /// <param name="state">The state of the cache for this file.</param>
        /// <param name="cachedUntil">The time the cache expires.</param>
        /// <param name="ex">The exception that prevented the file from being updated, if any.</param>
        public CacheResult(string path, bool updated, CacheState state, DateTime cachedUntil, Exception ex)
        {
            m_path = path;
            m_updated = updated;
            m_state = state;
            m_cachedUntil = cachedUntil;
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

        /// <summary>
        /// Gets the time when the cache expires.
        /// </summary>
        public DateTime CachedUntil
        {
            get { return m_cachedUntil; }
        }

        /// <summary>
        /// Creates a new CachedResult from an existing one, substituting a new exception.
        /// </summary>
        /// <param name="existing">The existing cachedResult.</param>
        /// <param name="ex">The new exception that prevented it from being updated.</param>
        public static CacheResult FromExisting(CacheResult existing, Exception ex)
        {
            return new CacheResult(existing.Path, false, existing.State, existing.CachedUntil, ex);
        }

        /// <summary>
        /// Gets a default CachedResult instance with the Uncached state.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        public static CacheResult Uncached(string path)
        {
            return Uncached(path, null);
        }

        /// <summary>
        /// Gets a default CachedResult instance with the Uncached state.
        /// </summary>
        /// <param name="ex">The exception that prevented the file from being updated, if any.</param>
        public static CacheResult Uncached(Exception ex)
        {
            return Uncached(null, ex);
        }

        /// <summary>
        /// Gets a default CachedResult instance with the Uncached state.
        /// </summary>
        /// <param name="path">The path to the cached file.</param>
        /// <param name="ex">The exception that prevented the file from being updated, if any.</param>
        public static CacheResult Uncached(string path, Exception ex)
        {
            return new CacheResult(path, false, CacheState.Uncached, DateTime.MinValue, ex);
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
