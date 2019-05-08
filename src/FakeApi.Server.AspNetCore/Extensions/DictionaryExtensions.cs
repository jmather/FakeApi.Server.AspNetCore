using System.Collections.Generic;

namespace FakeApi.Server.AspNetCore.Extensions
{
    public static class DictionaryExtensions
    {
        public static int? CountMatches<TKey, TValue>(this Dictionary<TKey, TValue> source, Dictionary<TKey, TValue> required)
        {
            return CountMatches(source, required, Comparer<TValue>.Default);
        }
        
        public static int? CountMatches<TKey, TValue>(this Dictionary<TKey, TValue> source,
            Dictionary<TKey, TValue> required, 
            Comparer<TValue> valueComparer)
        {
            var matches = 0;

            foreach (var (requiredKey, requiredValue) in required)
            {
                if (source.ContainsKey(requiredKey) == false)
                {
                    return null;
                }

                if (source.TryGetValue(requiredKey, out var refValue) == false)
                {
                    return null;
                }

                if (valueComparer.Compare(refValue, requiredValue) != 0)
                {
                    return null;
                }

                matches++;
            }

            return matches;
        }
    }
}