using System.Collections.Generic;

namespace 币安量化机器人.Core.Extensions
{
    public static class CollectionExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default!)
        {
            if (dict == null)
            {
                return defaultValue;
            }
            return dict.TryGetValue(key, out var v) ? v : defaultValue;
        }
    }
}
