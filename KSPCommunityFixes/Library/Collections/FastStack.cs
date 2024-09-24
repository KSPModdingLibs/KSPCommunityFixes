using System;
using System.Runtime.CompilerServices;

namespace KSPCommunityFixes.Library.Collections
{
    internal class FastStack<T> where T : class
    {
        private T[] _array = Array.Empty<T>();
        private int _size;

        public void EnsureCapacity(int capacity)
        {
            if (_array.Length < capacity)
                _array = new T[capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(T item)
        {
            _array[_size++] = item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPop(out T result)
        {
            if (_size == 0)
            {
                result = null;
                return false;
            }

            result = _array[--_size];
            _array[_size] = null;
            return true;
        }

        public void Clear()
        {
            Array.Clear(_array, 0, _size);
            _size = 0;
        }
    }
}
