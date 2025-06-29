using System;
using System.Collections.Generic;

namespace ATree
{
    public static class ListExtensions
    {
        /// <summary>
        /// Performs a binary search on the specified read-only list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The sorted read-only list to search.</param>
        /// <param name="value">The value to search for.</param>
        /// <returns>The zero-based index of <paramref name="value"/> in the sorted <paramref name="list"/>, if <paramref name="value"/> is found;
        /// otherwise, a negative number that is the bitwise complement of the index of the next element that is larger than <paramref name="value"/> or,
        /// if there is no larger element, the bitwise complement of <see cref="IReadOnlyList{T}.Count"/>.</returns>
        public static int BinarySearch<T>(this IReadOnlyList<T> list, T value) where T : IComparable<T>
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            
            int low = 0;
            int high = list.Count - 1;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                int comparison = list[mid].CompareTo(value);

                if (comparison == 0)
                    return mid;
                if (comparison < 0)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return ~low;
        }
    }
}
