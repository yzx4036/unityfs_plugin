using System;
using System.IO;
using System.Collections.Generic;

namespace UnityFS
{
    using UnityEngine;

    // 资源包抽象 (AssetBundle | ZipArchive)
    public abstract class UBundle : IRefCounted
    {
        protected int _refCount;
        protected bool _disposed;
        protected Manifest.BundleInfo _info;

        protected List<UBundle> _denpendencies;

        protected bool _loaded;
        private List<Action<UBundle>> _callbacks = new List<Action<UBundle>>();

        // main thread only
        public event Action<UBundle> completed
        {
            add
            {
                if (IsLoaded() && _IsDependenciesLoaded())
                {
                    value(this);
                }
                else
                {
                    _callbacks.Add(value);
                }
            }

            remove
            {
                _callbacks.Remove(value);
            }
        }

        public Manifest.BundleInfo bundleInfo
        {
            get { return _info; }
        }

        public Manifest.BundleType type
        {
            get { return _info.type; }
        }

        public int size
        {
            get { return _info.size; }
        }

        public int priority
        {
            get { return _info.priority; }
        }

        public string name
        {
            get { return _info.name; }
        }

        public string checksum
        {
            get { return _info.checksum; }
        }

        public UBundle(Manifest.BundleInfo bundleInfo)
        {
            _info = bundleInfo;
        }

        // main thread only
        public void AddRef()
        {
            _refCount++;
        }

        // main thread only
        public void RemoveRef()
        {
            _refCount--;
            if (_refCount == 0)
            {
                OnRelease();
            }
        }

        public void Release()
        {
            OnRelease();
        }

        protected virtual void OnRelease()
        {
            _disposed = true;
            if (_denpendencies != null)
            {
                for (int i = 0, size = _denpendencies.Count; i < size; i++)
                {
                    _denpendencies[i].completed -= OnDependedBundleLoaded;
                    _denpendencies[i].RemoveRef();
                }
                _denpendencies = null;
            }
        }

        protected bool _IsDependenciesLoaded()
        {
            if (_denpendencies != null)
            {
                for (int i = 0, size = _denpendencies.Count; i < size; i++)
                {
                    if (!_denpendencies[i].IsLoaded())
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public virtual bool IsLoaded()
        {
            return _loaded;
        }

        // 添加依赖资源包
        public bool AddDependency(UBundle bundle)
        {
            if (bundle != this)
            {
                if (_denpendencies == null)
                {
                    _denpendencies = new List<UBundle>();
                }
                else
                {
                    if (_denpendencies.Contains(bundle))
                    {
                        return false;
                    }
                }
                bundle.AddRef();
                _denpendencies.Add(bundle);
                bundle.completed += OnDependedBundleLoaded;
                return true;
            }
            return false;
        }

        private void OnDependedBundleLoaded(UBundle bundle)
        {
            if (_disposed)
            {
                return;
            }

            if (IsLoaded() && _IsDependenciesLoaded())
            {
                OnLoaded();
            }
        }

        // 载入资源包内容 (生命周期转由 UAssetBundleBundle 管理)
        public abstract void Load(Stream stream);

        // 调用所有回调
        protected void OnLoaded()
        {
            if (_disposed)
            {
                return;
            }
            
            while (_callbacks.Count > 0)
            {
                var callback = _callbacks[0];
                _callbacks.RemoveAt(0);
                try
                {
                    callback(this);
                }
                catch (Exception exception)
                {
                    Debug.LogErrorFormat("UBundle Exception: {0}\n{1}", _info.name, exception);
                }
            }
        }

        public abstract UAsset CreateAsset(string assetPath, Type type, bool concrete, EAssetHints hints);
    }
}