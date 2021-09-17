﻿using System;
using System.IO;
using System.Collections.Generic;

namespace UnityFS.Editor
{
    using UnityEngine;
    using UnityEditor;

    public partial class BundleBuilderData
    {
        public class PackedObject
        {
            public PackagePlatform platform;
            public string assetPath;
        }

        [Serializable]
        public class BundleSplit
        {
            public bool encrypted;
            public string name = string.Empty; // 分包名
            public int sliceObjects;
            public List<BundleSplitRule> rules = new List<BundleSplitRule>();

            // scan 过程收集将要打入此 split 的所有资源的列表
            private HashSet<string> _assetPathHashSet = new HashSet<string>();
            private List<PackedObject> _assets = new List<PackedObject>();

            public List<BundleSlice> slices = new List<BundleSlice>();

            public void GetTotalSize(out long totalRawSize, out long totalBuildSize)
            {
                totalRawSize = 0L;
                totalBuildSize = 0L;
                for (int i = 0, count = slices.Count; i < count; i++)
                {
                    var slice = slices[i];
                    totalRawSize += slice.totalRawSize;
                    totalBuildSize += slice.lastBuildSize;
                }
            }

            public bool AddObject(string assetPath, PackagePlatform platform)
            {
                _assetPathHashSet.Add(assetPath);
                _assets.Add(new PackedObject()
                {
                    assetPath = assetPath,
                    platform = platform,
                });
                return true;
            }

            public bool ContainsAssetPath(string assetPath)
            {
                return _assetPathHashSet.Contains(assetPath);
            }

            public void Reset()
            {
                slices.Clear();
                Cleanup();
            }

            public void Cleanup()
            {
                _assetPathHashSet.Clear();
                _assets.Clear();
                foreach (var slice in slices)
                {
                    slice.Cleanup();
                }
            }

            public void ForEachAssetPath(Action<BundleSlice, string> visitor)
            {
                for (int i = 0, size = slices.Count; i < size; i++)
                {
                    var slice = slices[i];
                    slice.ForEachAsset(assetPath => visitor(slice, assetPath));
                }
            }

            // 查找指定资源所在 slice, 不存在时返回 null
            public BundleSlice LookupAssetPath(string assetPath)
            {
                for (var i = 0; i < slices.Count; i++)
                {
                    var slice = slices[i];
                    if (slice.LookupAssetPath(assetPath))
                    {
                        return slice;
                    }
                }

                return null;
            }

            public bool Slice(BundleBuilderData data, BundleBuilderData.BundleInfo bundleInfo, string bundleName)
            {
                var dirty = false;
                foreach (var asset in _assets)
                {
                    if (AdjustBundleSlice(data, bundleInfo, bundleName, asset))
                    {
                        dirty = true;
                    }
                }

                return dirty;
            }

            private const string encodingBase = "23456789abcdefghijklmnopqrstuvwxyzABCDEFGHJMNPQRTVWY";

            private static string encode(long rawValue)
            {
                var result = "";
                do
                {
                    var part = rawValue % encodingBase.Length;
                    result += encodingBase[(int)part];
                    rawValue = (rawValue - part) / encodingBase.Length;
                } while (rawValue > 0);

                return result;
            }

            // 将 slice 切分命名插入 split 命名与文件后缀名之间
            public string GetBundleSliceName(string bundleName)
            {
                string part1;
                string part2;
                var dotIndex = bundleName.LastIndexOf(".");
                if (dotIndex >= 0)
                {
                    part1 = bundleName.Substring(0, dotIndex);
                    part2 = bundleName.Substring(dotIndex);
                }
                else
                {
                    part1 = bundleName;
                    part2 = "";
                }

                if (!string.IsNullOrEmpty(name))
                {
                    if (part1.Length > 0)
                    {
                        part1 += "_" + name;
                    }
                    else
                    {
                        part1 = name;
                    }
                }

                if (part1.Length > 0)
                {
                    part1 += "_";
                }

                var p1 = encode(DateTime.Now.Ticks);
                var p2 = encode(Random.Range(0, int.MaxValue));
                return part1 + p1 + "_" + p2 + part2;
            }

            // 返回最后一个符合 StreamingAssets 性质的 slice 包
            private BundleSlice GetLastSlice(bool streamingAssets, PackagePlatform platform)
            {
                var count = this.slices.Count;
                for (var i = count - 1; i >= 0; i--)
                {
                    var slice = slices[i];
                    if (slice.platform == platform && slice.streamingAssets == streamingAssets)
                    {
                        return slice;
                    }

                    // 如果 slice 为空, 那么 StreamingAssets 可调整
                    if (slice.GetAssetCount() == 0 && slice.assetPathHistroy.Count == 0)
                    {
                        slice.streamingAssets = streamingAssets;
                        slice.platform = platform;
                        return slice;
                    }
                }

                return null;
            }

            // 将指定资源放入合适的分包中, 产生变化时返回 true
            // buildPlatform: 当前正在打包的平台
            private bool AdjustBundleSlice(BundleBuilderData data, BundleBuilderData.BundleInfo bundleInfo,
                string bundleName, PackedObject packedObject)
            {
                var assetPath = packedObject.assetPath;
                var streamingAssets = data.IsStreamingAssets(assetPath, bundleInfo);
                var slicePlatform = packedObject.platform;
                for (var i = 0; i < this.slices.Count; i++)
                {
                    var oldSlice = this.slices[i];
                    if (oldSlice.AddHistory(assetPath, streamingAssets, slicePlatform))
                    {
                        return false;
                    }
                }

                var lastSlice = GetLastSlice(streamingAssets, slicePlatform);
                if (lastSlice == null || !lastSlice.AddNewAssetPath(assetPath))
                {
                    var sliceName = GetBundleSliceName(bundleName).ToLower();
                    var newSlice = new BundleSlice(sliceName, sliceObjects, streamingAssets, slicePlatform);
                    this.slices.Add(newSlice);
                    newSlice.AddNewAssetPath(assetPath);
                }

                return true;
            }
        }
    }
}