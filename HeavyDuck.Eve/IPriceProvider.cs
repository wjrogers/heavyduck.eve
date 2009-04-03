using System.Collections.Generic;

namespace HeavyDuck.Eve
{
    /// <summary>
    /// Interface to a pricing data provider.
    /// </summary>
    public interface IPriceProvider
    {
        /// <summary>
        /// Gets the price of an item.
        /// </summary>
        /// <param name="typeID">The type ID of the item.</param>
        float GetItemPrice(int typeID);

        /// <summary>
        /// Gets prices for multiple items.
        /// </summary>
        /// <param name="itemIDs">The list of typeIDs.</param>
        /// <returns>A dictionary mapping typeIDs to prices.</returns>
        Dictionary<int, float> GetItemPrices(IEnumerable<int> typeIDs);
    }
}