using System;
using System.Collections.Generic;
using System.Threading;

namespace ATree
{
    /// <summary>
    /// Provides a simple mechanism for interning strings in a thread-safe manner.
    /// This helps in reducing memory usage and allows for faster comparisons
    /// by using unique integer IDs for strings.
    /// </summary>
    public class StringTable
    {
        private readonly Dictionary<string, int> _stringToId = new Dictionary<string, int>();
        private readonly List<string> _idToString = new List<string>();
        private int _nextId = 0;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        public StringTable()
        {
        }

        /// <summary>
        /// Interns the given string and returns its unique ID.
        /// If the string is already interned, its existing ID is returned.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="s">The string to intern.</param>
        /// <returns>The unique integer ID for the string.</returns>
        public int Intern(string s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            _lock.EnterReadLock();
            try
            {
                if (_stringToId.TryGetValue(s, out int id))
                {
                    return id;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            _lock.EnterWriteLock();
            try
            {
                // Double-check in case another thread acquired the write lock and added the string first.
                if (_stringToId.TryGetValue(s, out int id))
                {
                    return id;
                }

                id = _nextId++;
                _idToString.Add(s);
                _stringToId.Add(s, id);
                return id;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Retrieves the string associated with the given ID.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="id">The ID of the string to retrieve.</param>
        /// <returns>The string corresponding to the ID.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the ID is invalid.</exception>
        public string GetString(int id)
        {
            _lock.EnterReadLock();
            try
            {
                if (id < 0 || id >= _idToString.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(id), "Invalid string ID.");
                }
                return _idToString[id];
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Tries to get the ID for a string if it's already interned.
        /// </summary>
        /// <param name="s">The string to look up.</param>
        /// <param name="id">When this method returns, contains the ID associated with the string, if found; otherwise, 0.</param>
        /// <returns>true if the string was found; otherwise, false.</returns>
        public bool TryGetId(string s, out int id)
        {
            if (s == null)
            {
                id = 0;
                return false;
            }

            _lock.EnterReadLock();
            try
            {
                return _stringToId.TryGetValue(s, out id);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets the total number of unique strings interned.
        /// </summary>
        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _idToString.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }
    }
}
