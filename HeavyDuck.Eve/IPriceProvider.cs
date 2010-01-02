using System;
using System.Collections.Generic;

namespace HeavyDuck.Eve
{
    /// <summary>
    /// Interface to a pricing data provider.
    /// </summary>
    public interface IPriceProvider
    {
        /// <summary>
        /// Loads the provider's cache from disk.
        /// </summary>
        void LoadCache();

        /// <summary>
        /// Saves the provider's cache to disk.
        /// </summary>
        void SaveCache();

        /// <summary>
        /// Gets the average price of an item in all regions.
        /// </summary>
        /// <param name="typeID">The type ID of the item.</param>
        /// <param name="stat">Which statistic to use for the price.</param>
        decimal GetPrice(int typeID, PriceStat stat);

        /// <summary>
        /// Gets the average price of an item in all connected high-sec systems.
        /// </summary>
        /// <param name="typeID">The type ID of the item.</param>
        /// <param name="stat">Which statistic to use for the price.</param>
        decimal GetPriceHighSec(int typeID, PriceStat stat);

        /// <summary>
        /// Gets the price of an item in a particular region.
        /// </summary>
        /// <param name="typeID">The type ID of the item.</param>
        /// <param name="regionID">The ID of the region.</param>
        /// <param name="stat">Which statistic to use for the price.</param>
        decimal GetPriceByRegion(int typeID, int regionID, PriceStat stat);

        /// <summary>
        /// Gets the average price of a list of items in all regions.
        /// </summary>
        /// <param name="typeIDs">The list of typeIDs.</param>
        /// <param name="stat">Which statistic to use for the price.</param>
        /// <returns>A dictionary mapping typeIDs to prices.</returns>
        Dictionary<int, decimal> GetPrices(IEnumerable<int> typeIDs, PriceStat stat);

        /// <summary>
        /// Gets the average price of a list of items in all connected high-sec systems.
        /// </summary>
        /// <param name="typeIDs">The list of typeIDs.</param>
        /// <param name="stat">Which statistic to use for the price.</param>
        /// <returns>A dictionary mapping typeIDs to prices.</returns>
        Dictionary<int, decimal> GetPricesHighSec(IEnumerable<int> typeIDs, PriceStat stat);

        /// <summary>
        /// Gets the average price of a list of items in a particular region.
        /// </summary>
        /// <param name="typeIDs">The list of typeIDs.</param>
        /// <param name="regionID">The ID of the region.</param>
        /// <param name="stat">Which statistic to use for the price.</param>
        /// <returns>A dictionary mapping typeIDs to prices.</returns>
        Dictionary<int, decimal> GetPricesByRegion(IEnumerable<int> typeIDs, int regionID, PriceStat stat);
    }

    public enum PriceStat
    {
        Mean,
        Median
    }

    /// <summary>
    /// The exception that is thrown when a method of IPriceProvider is not completed successfully.
    /// </summary>
    public class PriceProviderException : ApplicationException
    {
        private readonly PriceProviderFailureReason m_reason;

        public PriceProviderException(PriceProviderFailureReason reason, string message)
            : base(message)
        {
            m_reason = reason;
        }

        public PriceProviderException(PriceProviderFailureReason reason, string message, Exception innerException)
            : base(message, innerException)
        {
            m_reason = reason;
        }

        public PriceProviderFailureReason FailureReason
        {
            get { return m_reason; }
        }
    }

    public enum PriceProviderFailureReason
    {
        PriceMissing,
        CacheEmpty,
        UnexpectedError
    }
}