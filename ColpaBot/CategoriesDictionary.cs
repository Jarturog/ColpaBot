using System.Collections.Generic;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections;
using System.Linq;

namespace ColpaBot
{
    /// <summary>
    /// Implementation of a tree set. The collection stores trees of depth 1, where every non-root node is connected to its root. The smallest possible tree has 2 nodes.
    /// Every tree is supposed to be made of nodes of the same category, the root node is chosen arbitrarily.
    /// </summary>
    /// <typeparam name="T">Type that implements a hash code</typeparam>
    public class CategoriesDictionary<T> : IDictionary<T, HashSet<T>> where T : IEquatable<T>
    {
        /// <summary>
        /// The underlying dictionary storing the tree structure
        /// </summary>
        private readonly Dictionary<T, HashSet<T>> _backingDictionary = [];

        /// <summary>
        /// Indexer to get or set a category (tree) by key
        /// </summary>
        /// <param name="key">The key to access or modify</param>
        /// <returns>A HashSet representing the category (tree)</returns>
        public HashSet<T> this[T key]
        {
            get
            {
                T mainKey = GetMainKey(new(_backingDictionary[key]) { key });
                return new HashSet<T>(_backingDictionary[mainKey])
                {
                    mainKey
                };
            }
            set
            {
                if (value == null || value.Count < 1)
                {
                    throw new InvalidOperationException($"Error with {nameof(CategoriesDictionary<T>)}, there should not be empty HashSets");
                }
                HashSet<T> val = new(value) { key };
                T mainKey = GetMainKey(val);
                // start of error control

                // Store the state before modifications for potential rollback
                HashSet<T> categoryBeforeError = _backingDictionary.ContainsKey(mainKey)
                    ? _backingDictionary[mainKey]
                    : null;
                Dictionary<T, HashSet<T>> affectedDictionaryBeforeError = [];
                // end of error control

                if (!_backingDictionary.ContainsKey(mainKey))
                {
                    _backingDictionary[mainKey] = val;
                }
                else
                {
                    _backingDictionary[mainKey].UnionWith(val);
                }
                _backingDictionary[mainKey].Remove(mainKey);
                try
                {
                    foreach (T item in val)
                    {
                        if (item.Equals(mainKey))
                        {
                            continue;
                        }
                        T nodeForException;
                        if (_backingDictionary.ContainsKey(item) && !mainKey.Equals(nodeForException = _backingDictionary[item].First()))
                        {
                            throw new InvalidOperationException($"Error with {nameof(CategoriesDictionary<T>)}, there should not be more than one root in the tree, {mainKey} is the root and {item} tried to connect to it even though it is already connected to {nodeForException}");
                        }
                        // start of error control

                        // Store the state of the item before modification
                        affectedDictionaryBeforeError[item] = _backingDictionary.ContainsKey(item)
                            ? _backingDictionary[item]
                            : null;
                        // end of error control

                        _backingDictionary[item] = [mainKey];
                    }
                }
                catch (InvalidOperationException)
                {
                    // Rollback changes if an error occurs
                    if (categoryBeforeError == null)
                    {
                        _backingDictionary.Remove(mainKey);
                    }
                    else
                    {
                        _backingDictionary[mainKey] = categoryBeforeError;
                    }
                    foreach ((T keyToRestore, HashSet<T> valueToRestore) in affectedDictionaryBeforeError)
                    {
                        if (valueToRestore == null) // It did not have a key
                        {
                            _backingDictionary.Remove(keyToRestore);
                        }
                        else
                        {
                            _backingDictionary[keyToRestore] = valueToRestore;
                        }
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// Gets the collection of keys in the dictionary
        /// </summary>
        public ICollection<T> Keys => _backingDictionary.Keys;

        /// <summary>
        /// Gets the collection of values in the dictionary
        /// </summary>
        public ICollection<HashSet<T>> Values => _backingDictionary.Values;

        /// <summary>
        /// Gets the number of elements in the dictionary
        /// </summary>
        public int Count => _backingDictionary.Count;

        /// <summary>
        /// Gets a value indicating whether the dictionary is read-only
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Adds a new key-value pair to the dictionary
        /// </summary>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        public void Add(T key, HashSet<T> value) => this[key] = value;

        /// <summary>
        /// Adds a new key-value pair to the dictionary
        /// </summary>
        /// <param name="item">The key-value pair to add</param>
        public void Add(KeyValuePair<T, HashSet<T>> item) => this[item.Key] = item.Value;

        /// <summary>
        /// Removes all items from the dictionary
        /// </summary>
        public void Clear() => _backingDictionary.Clear();

        /// <summary>
        /// Determines whether the dictionary contains a specific key-value pair
        /// </summary>
        /// <param name="item">The key-value pair to locate</param>
        /// <returns>True if the item is found; otherwise, false</returns>
        public bool Contains(KeyValuePair<T, HashSet<T>> item)
        {
            if (!_backingDictionary.ContainsKey(item.Key))
            {
                return false;
            }
            foreach (T val in item.Value)
            {
                if (!_backingDictionary.ContainsKey(val))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Gets the main key or root of a tree set, which is a category. If there is no root, it chooses one arbitrarily.
        /// </summary>
        /// <param name="nodes">Nodes pertaining to the tree set</param>
        /// <returns>Root of the tree</returns>
        /// <exception cref="InvalidOperationException">Thrown when the rules of this data structure were not followed when inserting nodes</exception>
        private T GetMainKey(HashSet<T> nodes)
        {
            T root = default;
            foreach (T n in nodes)
            {
                if (_backingDictionary.ContainsKey(n))
                {
                    HashSet<T> nodesOfN = _backingDictionary[n];
                    if (nodesOfN.Count < 1)
                    {
                        throw new InvalidOperationException($"Error with {nameof(CategoriesDictionary<T>)}, there should not be empty HashSets");
                    }
                    if (nodesOfN.Count > 1)
                    {
                        if (root != null && !root.Equals(default))
                        {
                            throw new InvalidOperationException($"Error with {nameof(CategoriesDictionary<T>)}, there should not be more than one root in the tree, {root} and {n} are incompatible in the same set");
                        }
                        root = n; // Found root with more than one node connected to it
                        continue;
                    }
                }
            }
            if (root != null && !root.Equals(default)) // If a root was found, check if all nodes are connected to it
            {
                foreach (T n in nodes)
                {
                    if (root.Equals(n))
                    {
                        continue;
                    }
                    if (_backingDictionary.ContainsKey(n)
                        && (_backingDictionary[n].Count != 1
                        || (root != null && !root.Equals(_backingDictionary[n].First()))))
                    {
                        throw new InvalidOperationException($"Error with {nameof(CategoriesDictionary<T>)}, all nodes should be connected to the root {root}, the node {n} was not");
                    }
                }
            }
            if (root == null || root.Equals(default)) // If no root was found it means there are no current connections, choose one arbitrarily
            {
                root = nodes.First();
            }
            return root;
        }

        /// <summary>
        /// Determines whether the dictionary contains a specific key
        /// </summary>
        /// <param name="key">The key to locate</param>
        /// <returns>True if the key is found; otherwise, false</returns>
        public bool ContainsKey(T key)
        {
            return _backingDictionary.ContainsKey(key);
        }

        /// <summary>
        /// Copies the elements of the dictionary to an array, starting at a particular array index
        /// </summary>
        /// <param name="array">The one-dimensional array that is the destination of the elements copied from the dictionary</param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins</param>
        /// <exception cref="ArgumentNullException">Thrown when the array is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the arrayIndex is less than 0 or greater than or equal to the length of array</exception>
        /// <exception cref="ArgumentException">Thrown when the number of elements in the source dictionary is greater than the available space from arrayIndex to the end of the destination array</exception>
        public void CopyTo(KeyValuePair<T, HashSet<T>>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (arrayIndex < 0 || arrayIndex >= array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }
            if (array.Length - arrayIndex < _backingDictionary.Count)
            {
                throw new ArgumentException("Insufficient space in target array.");
            }

            foreach (var kvp in _backingDictionary)
            {
                array[arrayIndex++] = kvp;
            }
        }

        // The following methods are not implemented and throw NotImplementedException

        public IEnumerator<KeyValuePair<T, HashSet<T>>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public bool Remove(T key)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<T, HashSet<T>> item)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(T key, [MaybeNullWhen(false)] out HashSet<T> value)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}