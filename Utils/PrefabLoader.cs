using System;
using System.IO;
using System.Collections.Generic;

namespace UnityFS.Utils
{
    using UnityEngine;

    // 创建一个占位用 GameObject, 异步加载指定prefab资源, 并实例化挂载与此节点
    public class PrefabLoader : MonoBehaviour
    {
        private UAsset _asset;

        protected bool _loaded;
        private GameObject _target;
        private List<Action<PrefabLoader>> _callbacks = new List<Action<PrefabLoader>>();

        public GameObject target
        {
            get { return _target; }
        }

        public event Action<PrefabLoader> completed
        {
            add
            {
                if (_loaded)
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

        public bool isLoaded
        {
            get { return _loaded; }
        }

        protected void OnLoaded()
        {
            while (_callbacks.Count > 0)
            {
                var callback = _callbacks[0];
                _callbacks.RemoveAt(0);
                callback(this);
            }
        }

        /// <summary>
        /// 异步加载
        /// </summary>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        public static PrefabLoader LoadAsync(string assetPath)
        {
            var gameObject = new GameObject("Prefab Loader");
            var loader = gameObject.AddComponent<PrefabLoader>();
            loader._LoadAsync(assetPath);
            return loader;
        }
        
        /// <summary>
        /// 同步加载
        /// </summary>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        public static PrefabLoader Load(string assetPath)
        {
            var gameObject = new GameObject("Prefab Loader");
            var loader = gameObject.AddComponent<PrefabLoader>();
            loader._Load(assetPath);
            Debug.Log($">LoadAsync>>loader.target {loader.target}"); 
            return loader;
        }

        /// <summary>
        /// 异步加载
        /// </summary>
        /// <param name="gameObject"></param>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        public static PrefabLoader LoadAsync(GameObject gameObject, string assetPath)
        {
            var loader = gameObject.AddComponent<PrefabLoader>();
            loader._LoadAsync(assetPath);
            return loader;
        }
        
        /// <summary>
        /// 同步加载
        /// </summary>
        /// <param name="gameObject"></param>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        public static PrefabLoader Load(GameObject gameObject, string assetPath)
        {
            var loader = gameObject.AddComponent<PrefabLoader>();
            loader._Load(assetPath);
            return loader;
        }


        public PrefabLoader DestroyAfter(float seconds)
        {
            StartCoroutine(Helpers.DestroyAfter(gameObject, seconds));
            return this;
        }

        private void _LoadAsync(string assetPath)
        {
            _asset = ResourceManager.LoadAssetAsync(assetPath, typeof(UnityEngine.Object), null);
            _asset.completed += OnCompleted;
        }
        
        private void _Load(string assetPath)
        {
            _asset = ResourceManager.TryLoadAssetSync(assetPath, typeof(UnityEngine.Object), null);
            if (_asset != null)
            {
                OnCompleted(_asset);
            }
        }


        private void OnCompleted(UAsset asset)
        {
            _target = Object.Instantiate(asset.GetObject() as GameObject, transform);
            _target.transform.localPosition = Vector3.zero;
            _target.transform.localRotation = Quaternion.identity;
            _target.transform.localScale = Vector3.one;
            if (!_loaded)
            {
                // Debug.Log($"asset loaded {_assetPath}");
                _loaded = true;
                OnLoaded();
            }
        }

        void OnDestroy()
        {
            _asset.completed -= OnCompleted;
            _asset = null;
        }
    }
}
