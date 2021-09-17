using System;
using System.IO;
using System.Collections.Generic;

namespace UnityFS.Utils
{
    using UnityEngine;

    public class PrefabPools
    {
        private bool _disposed;
        private Transform _root;
        private Dictionary<string, PrefabPool> _prefabPools;

        public PrefabPools(Transform root)
        {
            _root = root;
        }

        public PrefabPools(GameObject root, bool dontDestroyOnLoad = true)
        {
            if (root != null)
            {
                _root = root.transform;
                if (dontDestroyOnLoad)
                {
                    Object.DontDestroyOnLoad(root);
                }
            }
        }

        public PrefabPools()
        {
            _root = null;
        }

        public PrefabPool.Handle Alloc(string assetPath)
        {
            return GetPrefabPool(assetPath)?.GetHandle();
        }

        public GameObject Instantiate(string assetPath)
        {
            return GetPrefabPool(assetPath)?.Instantiate();
        }

        public PrefabPool GetPrefabPool(string assetPath, int capacity = 0)
        {
            if (_disposed)
            {
                return null;
            }

            if (_prefabPools == null)
            {
                _prefabPools = new Dictionary<string, PrefabPool>();
            }
            PrefabPool pool;
            if (!_prefabPools.TryGetValue(assetPath, out pool))
            {
                pool = _prefabPools[assetPath] = new PrefabPool(_root, assetPath, capacity);
            }
            return pool;
        }

        public void Release()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_prefabPools == null)
                {
                    return;
                }
                foreach (var kv in _prefabPools)
                {
                    kv.Value.Release();
                }
                _prefabPools.Clear();
            }
        }

        public void Drain()
        {
            if (_disposed || _prefabPools == null)
            {
                return;
            }
            foreach (var kv in _prefabPools)
            {
                kv.Value.Drain();
            }
        }
    }
}
