﻿using System;
using System.IO;
using System.Collections.Generic;

namespace UnityFS.Editor
{
    using UnityEngine;
    using UnityEditor;

    public partial class BundleBuilderData
    {
        [Serializable]
        public class BundleInfo
        {
            public int id;
            public int buildOrder = 1000;
            public string name; // bundle filename
            public string note;
            public string tag;
            public Manifest.BundleType type;
            public Manifest.BundleLoad load;
            public bool enabled = true;
            public bool streamingAssets = false; // 是否复制到 StreamingAssets 目录
            public int priority;
            public List<BundleAssetTarget> targets = new List<BundleAssetTarget>(); // 打包目标 (可包含文件夹)
            public List<BundleSplit> splits = new List<BundleSplit>();

            public BundleInfo()
            {
            }

            public void ForEachAssetPath(Action<BundleSplit, BundleSlice, string> visitor)
            {
                for (int i = 0, size = splits.Count; i < size; i++)
                {
                    var split = splits[i];
                    split.ForEachAssetPath((slice, assetPath) => visitor(split, slice, assetPath));
                }
            }

            public bool LookupAssetPath(string assetPath, out BundleSplit bundleSplit, out BundleSlice bundleSlice)
            {
                for (int i = 0, size = splits.Count; i < size; i++)
                {
                    var split = splits[i];
                    var slice = split.LookupAssetPath(assetPath);
                    if (slice != null)
                    {
                        bundleSplit = split;
                        bundleSlice = slice;
                        return true;
                    }
                }

                bundleSplit = null;
                bundleSlice = null;
                return false;
            }

            public bool Slice(BundleBuilderData data)
            {
                var dirty = false;
                foreach (var split in splits)
                {
                    if (split.Slice(data, this, name))
                    {
                        dirty = true;
                    }
                }

                return dirty;
            }

            public void Cleanup()
            {
                foreach (var split in splits)
                {
                    split.Cleanup();
                }
            }
        }
    }
}