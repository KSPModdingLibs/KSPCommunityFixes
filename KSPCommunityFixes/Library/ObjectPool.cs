using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.Library
{
    internal class SimpleObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _stack = new Stack<T>();

        public T Get()
        {
            if (!_stack.TryPop(out T pooledObject))
                pooledObject = new T();

            return pooledObject;
        }

        public void Release(T pooledObject)
        {
            _stack.Push(pooledObject);
        }
    }

    internal class ListPool<T>
    {
        private readonly Stack<List<T>> _stack = new Stack<List<T>>();

        public List<T> Get()
        {
            if (!_stack.TryPop(out List<T> pooledList))
                pooledList = new List<T>();

            return pooledList;
        }

        public void Release(List<T> pooledList)
        {
            pooledList.Clear();
            _stack.Push(pooledList);
        }
    }

    internal class ObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _stack = new Stack<T>();
        private readonly Action<T> _onInstantiate;
        private readonly Action<T> _onRelease;

        public ObjectPool(Action<T> onInstantiate = null, Action<T> onRelease = null)
        {
            _onInstantiate = onInstantiate;
            _onRelease = onRelease;
        }

        public T Get()
        {
            if (!_stack.TryPop(out T pooledObject))
            {
                pooledObject = new T();
                _onInstantiate?.Invoke(pooledObject);
            }

            return pooledObject;
        }

        public void Release(T pooledObject)
        {
            _onRelease?.Invoke(pooledObject);
            _stack.Push(pooledObject);
        }
    }


}
