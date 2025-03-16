using System.Collections.Generic;

namespace KSPCommunityFixes.Library.Collections
{
    /// <summary>
    /// A dictionary optimized for faster lookup with <see cref="UnityEngine.Object"/> keys.
    /// </summary>
    public class UnityObjectDictionary<TKey, TValue> : Dictionary<TKey, TValue> where TKey : UnityEngine.Object
    {
        private class UnityObjectEqualityComparer : IEqualityComparer<UnityEngine.Object>
        {
            public static readonly UnityObjectEqualityComparer defaultComparer = new UnityObjectEqualityComparer();

            public bool Equals(UnityEngine.Object x, UnityEngine.Object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(UnityEngine.Object obj)
            {
                return obj.GetInstanceIDFast();
            }
        }

        public UnityObjectDictionary() : base(4, UnityObjectEqualityComparer.defaultComparer) { }
    }
}
