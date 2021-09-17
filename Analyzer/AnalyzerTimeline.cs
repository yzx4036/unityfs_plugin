using System;
using System.IO;
using System.Collections.Generic;
using ICSharpCode.SharpZipLib.Zip;

namespace UnityFS.Analyzer
{
    using UnityEngine;

    public class AnalyzerState
    {
        public int frameIndex;
        public bool open;
        public int refCount;
    }

    public class AnalyzerAsset
    {
        public string assetPath;
        public float firstOpenTime;
        public List<AnalyzerState> states = new List<AnalyzerState>();

        public AnalyzerState GetState(int frameIndex)
        {
            var count = states.Count;
            if (count > 0)
            {
                var lastState = states[count - 1];
                if (lastState.frameIndex == frameIndex)
                {
                    return lastState;
                }
            }
            var newState = new AnalyzerState()
            {
                frameIndex = frameIndex,
            };
            states.Add(newState);
            return newState;
        }
    }

    public class AnalyzerFrame
    {
        public int frameIndex;
        public float time;

        private List<AnalyzerAsset> _assets;

        public int assetCount { get { return _assets == null ? 0 : _assets.Count; } }

        public AnalyzerAsset GetAsset(int index)
        {
            return _assets[index];
        }

        public void AddAsset(AnalyzerAsset asset)
        {
            if (_assets == null)
            {
                _assets = new List<AnalyzerAsset>();
            }
            _assets.Add(asset);
        }
    }

    public class AnalyzerTimeline
    {
        private int _frameIndex;
        private float _frameStartTime;
        private float _frameTime;
        private AnalyzerFrame _currentFrame;
        private AssetListData _listData;
        public List<AnalyzerFrame> frames = new List<AnalyzerFrame>();
        public Dictionary<string, AnalyzerAsset> assets = new Dictionary<string, AnalyzerAsset>();

        public int frameIndex { get { return _frameIndex; } }

        public float frameTime { get { return _frameTime; } }

        // 返回 true 表示第一次访问此资源
        private bool GetAsset(string assetPath, out AnalyzerAsset asset)
        {
            if (!assets.TryGetValue(assetPath, out asset))
            {
                asset = assets[assetPath] = new AnalyzerAsset()
                {
                    assetPath = assetPath,
                    firstOpenTime = _frameTime,
                };
                return true;
            }
            return false;
        }

        public void Start()
        {
            _frameStartTime = Time.realtimeSinceStartup;
            _frameTime = 0f;
        }

        public void Stop()
        {
        }

        public void Update()
        {
            _currentFrame = null;
            _frameIndex++;
            _frameTime = Time.realtimeSinceStartup - _frameStartTime;
        }

        public AnalyzerFrame GetFrame(int frameIndex)
        {
            if (frameIndex < _frameIndex)
            {
                for (int i = 0, size = frames.Count; i < size; i++)
                {
                    var frame = frames[i];
                    if (frame.frameIndex == frameIndex)
                    {
                        return frame;
                    }
                }
            }
            return null;
        }

        private AnalyzerFrame GetCurrentFrame()
        {
            if (_currentFrame == null)
            {
                _currentFrame = new AnalyzerFrame()
                {
                    frameIndex = _frameIndex,
                    time = _frameTime,
                };
                frames.Add(_currentFrame);
            }
            return _currentFrame;
        }

        public bool OpenAsset(string assetPath)
        {
            AnalyzerAsset asset;
            var ret = GetAsset(assetPath, out asset);
            var state = asset.GetState(_frameIndex);
            state.open = true;
            state.refCount++;
            GetCurrentFrame().AddAsset(asset);
            return ret;
        }

        public bool AccessAsset(string assetPath)
        {
            AnalyzerAsset asset;
            var ret = GetAsset(assetPath, out asset);
            var state = asset.GetState(_frameIndex);
            state.refCount++;
            GetCurrentFrame().AddAsset(asset);
            return ret;
        }

        public bool CloseAsset(string assetPath)
        {
            AnalyzerAsset asset;
            var ret = GetAsset(assetPath, out asset);
            var state = asset.GetState(_frameIndex);
            state.open = false;
            GetCurrentFrame().AddAsset(asset);
            return ret;
        }
    }
}
