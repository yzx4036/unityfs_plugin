﻿using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityFS.Utils;

namespace UnityFS
{
    using UnityEngine;

    public class DownloadWorker
    {
        public const string BundleContentType = "application/octet-stream";
        public const string PartExt = ".part";

        public class JobInfo : ITask
        {
            public int bytesPerSecond = 128 * 1024; // 下载限速 128KB/S
            public bool emergency; // 是否紧急 (创建此任务时)
            public string url; // (可选) 指定地址, 如果没有指定, 则使用 manager.urls + name 得到
            public string dataChecker; // (可选) crc16, md5. 不指定时使用 crc16

            public int retry; // 重试次数 (<=0 时无限重试)
            public int tried; // 已重试次数
            public int bytes; // 当前字节数
            public string error; // 错误
            public Action callback;

            private string _finalPath; // 最终存储路径
            private int _size;
            private int _priority;
            private string _name;
            private string _checksum;
            private string _comment;

            public bool isRunning { get; set; }
            public bool isDone { get; set; }
            public float progress => Mathf.Clamp01((float)bytes / size);
            public string path => _finalPath;
            public int size => _size;

            public int priority => _priority;
            public string name => _name;
            public string checksum => _checksum;
            public string comment => _comment;

            public JobInfo(string name, string checksum, string comment, int priority, int size, string finalPath)
            {
                _name = name;
                _checksum = checksum;
                _comment = comment;
                _priority = priority;
                _size = size;
                _finalPath = finalPath;
            }
        }

        private bool _destroy;
        private byte[] _buffer;
        private int _timeout = 2 * 1000; // http 请求超时时间 (毫秒)
        private LinkedList<JobInfo> _jobInfos = new LinkedList<JobInfo>();
        private Thread _thread;
        private AutoResetEvent _event = new AutoResetEvent(false);
        private FileStream _fileStream;
        private IList<string> _urls;

        // invoke in main thread
        private Action<JobInfo> _callback;

        public DownloadWorker(Action<JobInfo> callback, int bufferSize,
            IList<string> urls,
            System.Threading.ThreadPriority threadPriority)
        {
            _urls = urls;
            _callback = callback;
            _buffer = new byte[bufferSize];
            _thread = new Thread(_Run)
            {
                Name = "DownloadWorker",
                Priority = threadPriority,
                IsBackground = true
            };
            ResourceManager.AddWorker(this);
            _thread.Start();
        }

        public void Abort()
        {
            ResourceManager.RemoveWorker(this);
            _destroy = true;
            lock (_jobInfos)
            {
                _jobInfos.Clear();
            }

            if (_thread != null)
            {
                try
                {
                    _thread.Abort();
                    _thread = null;
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            if (_fileStream != null)
            {
                try
                {
                    _fileStream.Close();
                    _fileStream = null;
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        public bool AddJob(JobInfo jobInfo)
        {
            jobInfo.isRunning = true;
            lock (_jobInfos)
            {
                if (_destroy)
                {
                    return false;
                }

                _jobInfos.AddLast(jobInfo);
            }

            _event.Set();
            return true;
        }

        private string GetUrl(JobInfo jobInfo)
        {
            if (string.IsNullOrEmpty(jobInfo.url))
            {
                var url = _urls[jobInfo.tried % _urls.Count];

                if (url.EndsWith("/"))
                {
                    url += jobInfo.name;
                }
                else
                {
                    url += "/" + jobInfo.name;
                }

                url += "?checksum=" + (jobInfo.checksum ?? DateTime.Now.Ticks.ToString());
                if (jobInfo.tried > 0)
                {
                    url += "&retry=" + DateTime.Now.Ticks;
                }
                return url;
            }

            var c_url = jobInfo.url;
            if (jobInfo.tried > 0)
            {
                if (c_url.Contains("?"))
                {
                    c_url += "&retry=" + DateTime.Now.Ticks;
                }
                else
                {
                    c_url += "?retry=" + DateTime.Now.Ticks;
                }
            }

            return c_url;
        }

        private void _Run()
        {
            while (!_destroy)
            {
                try
                {
                    var job = GetJob();
                    if (job != null)
                    {
                        ProcessJob(job);
                    }
                }
                catch (ThreadAbortException)
                {
                }
                catch (Exception exception)
                {
                    ResourceManager.logger.OnWorkerError(this, exception);
                }
                finally
                {
                    if (_fileStream != null)
                    {
                        _fileStream.Close();
                        _fileStream = null;
                    }
                }
            }
        }

        private JobInfo GetJob()
        {
            if (_jobInfos.Count == 0)
            {
                _event.WaitOne();
            }

            lock (_jobInfos)
            {
                if (_jobInfos.Count != 0)
                {
                    var first = _jobInfos.First.Value;
                    _jobInfos.RemoveFirst();
                    return first;
                }

                return null;
            }
        }

        private IDataChecker GetDataChecker(JobInfo jobInfo)
        {
            if (jobInfo.dataChecker == "md5")
            {
                return new MD5Hash();
            }
            return new Crc16();
        }

        private void ProcessJob(JobInfo jobInfo)
        {
            // Debug.LogFormat("processing job: {0} ({1})", jobInfo.name, jobInfo.comment);
            var tempPath = jobInfo.path + PartExt;

            try
            {
                if (_fileStream != null)
                {
                    _fileStream.Close();
                    _fileStream = null;
                }
            }
            catch (Exception) { }

            var dataChecker = GetDataChecker(jobInfo);
            while (true)
            {
                string error = null;
                var partialSize = 0;
                var success = true;
                var wsize = jobInfo.size;
                var wchecksum = jobInfo.checksum;
                var url = GetUrl(jobInfo);
                dataChecker.Reset();
                if (_fileStream == null)
                {
                    try
                    {
                        var fileInfo = new FileInfo(tempPath);
                        if (fileInfo.Exists) // 处理续传
                        {
                            _fileStream = fileInfo.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);
                            partialSize = (int)fileInfo.Length;
                            if (partialSize > jobInfo.size) // 目标文件超过期望大小, 直接废弃
                            {
                                _fileStream.SetLength(0);
                                partialSize = 0;
                            }
                            else if (partialSize <= jobInfo.size) // 续传
                            {
                                dataChecker.Update(_fileStream);
                                // Debug.LogFormat("partial check {0} && {1} ({2})", partialSize, jobInfo.size,
                                //     dataChecker.hex);
                            }
                        }
                        else // 创建下载文件
                        {
                            if (!Directory.Exists(fileInfo.DirectoryName))
                            {
                                Directory.CreateDirectory(fileInfo.DirectoryName);
                            }

                            _fileStream = File.Open(tempPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                            _fileStream.SetLength(0);
                        }
                    }
                    catch (Exception exception)
                    {
                        // ResourceManager.logger.OnTaskError(jobInfo, exception);
                        error = string.Format("file exception: {0}", exception);
                        success = false;
                    }
                }
                else
                {
                    _fileStream.SetLength(0L);
                }

                if (success && (jobInfo.size <= 0 || partialSize < jobInfo.size))
                {
                    try
                    {
                        var uri = new Uri(url);
                        var req = WebRequest.CreateHttp(uri);
                        req.Method = WebRequestMethods.Http.Get;
                        req.ContentType = BundleContentType;
                        // req.ReadWriteTimeout = 10000;
                        if (_timeout > 0)
                        {
                            req.Timeout = _timeout;
                        }

                        if (partialSize > 0)
                        {
                            req.AddRange(partialSize);
                        }

                        using (var rsp = req.GetResponse())
                        {
                            using (var webStream = rsp.GetResponseStream())
                            {
                                var recvAll = 0L;
                                var recvCalc = 0L;
                                var stopwatch = new Stopwatch();
                                stopwatch.Start();
                                _buffer = new byte[(int) rsp.ContentLength];
                                while (recvAll < rsp.ContentLength)
                                {
                                    var _bpms = Math.Max(1, jobInfo.bytesPerSecond / 10);
                                    var recv = webStream.Read(_buffer, 0, Math.Min(_bpms, _buffer.Length));
                                    if (recv > 0 && !_destroy)
                                    {
                                        recvCalc += recv;
                                        if (recvCalc >= _bpms)
                                        {
                                            var millisecs = stopwatch.ElapsedMilliseconds;
                                            var delay = (int)(100.0 * recvCalc / _bpms - millisecs);
                                            // Debug.LogFormat("net ++ {0} {1} sbps {2} recv {3}", delay, millisecs, _bpms, recvCalc);
                                            if (delay > 0)
                                            {
                                                Thread.Sleep(delay);
                                            }
                                            stopwatch.Restart();
                                            recvCalc -= _bpms;
                                        }
                                        recvAll += recv;
                                        _fileStream.Write(_buffer, 0, recv);
                                        dataChecker.Update(_buffer, 0, recv);
                                        jobInfo.bytes = (int)(recvAll + partialSize);
                                        // PrintDebug($"{recvAll + partialSize}, {_size}, {_progress}");
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        // ResourceManager.logger.OnTaskError(jobInfo, exception);
                        // Debug.LogErrorFormat("network exception: {0}\n{1}", url, exception);
                        error = string.Format("network exception: {0}", exception);
                        success = false;
                    }
                }

                if (success && _fileStream.Length != jobInfo.size)
                {
                    Debug.Log($">>>>{_fileStream.Length} ... { jobInfo.size}");
                    
                    if (jobInfo.size > 0)
                    {
                        error = string.Format("filesize exception: {0} != {1}", _fileStream.Length, jobInfo.size);
                        // Debug.LogError(error);
                        // ResourceManager.logger.OnTaskError(jobInfo, error);
                        success = false;
                    }
                    else
                    {
                        wsize = (int)_fileStream.Length;
                    }
                }

                if (success)
                {
                    // dirty code, md5 目前不能分段计算
                    if (dataChecker.hex == null)
                    {
                        _fileStream.Seek(0, SeekOrigin.Begin);
                        dataChecker.ComputeHashFull(_fileStream);
                    }

                    if (dataChecker.hex != jobInfo.checksum)
                    {
                        if (!string.IsNullOrEmpty(jobInfo.checksum))
                        {
                            error = string.Format("corrupted file: {0} != {1}", dataChecker.hex, jobInfo.checksum);
                            // Debug.LogError(error);
                            // ResourceManager.logger.OnTaskError(jobInfo, error);
                            success = false;
                        }
                        else
                        {
                            wchecksum = dataChecker.hex;
                        }
                    }
                }

                if (_destroy)
                {
                    success = false;
                }

                // close fileStream anyway
                try
                {
                    if (_fileStream != null)
                    {
                        _fileStream.Close();
                        _fileStream = null;
                    }
                }
                catch (Exception)
                {
                }

                if (success)
                {
                    try
                    {
                        // _WriteStream(buffer, fileStream, finalPath);
                        if (File.Exists(jobInfo.path))
                        {
                            File.Delete(jobInfo.path);
                        }

                        File.Move(tempPath, jobInfo.path);
                        // 写入额外的 meta
                        var meta = new Metadata()
                        {
                            checksum = wchecksum,
                            size = wsize,
                        };
                        var json = JsonUtility.ToJson(meta);
                        var metaPath = jobInfo.path + Metadata.Ext;
                        File.WriteAllText(metaPath, json);
                        Complete(jobInfo);
                        break;
                    }
                    catch (Exception exception)
                    {
                        error = string.Format("write exception: {0}", exception);
                        // Debug.LogError(error);
                        // ResourceManager.logger.OnTaskError(jobInfo, error);
                        success = false;
                    }
                }
                
                if (!success)
                {
                    try { File.Delete(tempPath); } catch (Exception) { }
                }

                jobInfo.tried++;
                if (jobInfo.retry > 0 && jobInfo.tried >= jobInfo.retry)
                {
                    jobInfo.error = error ?? "unknown error";
                    Complete(jobInfo);
                    break;
                }

                ResourceManager.logger.OnTaskError(jobInfo, error);
                Thread.Sleep(1000);
                // Debug.LogErrorFormat("[retry] download failed: {0}\n{1}", url, error);
            }
        }

        private void Complete(JobInfo jobInfo)
        {
            if (_callback != null)
            {
                JobScheduler.DispatchMain(() =>
                {
                    jobInfo.isDone = true;
                    jobInfo.isRunning = false;
                    _callback(jobInfo);
                });
            }
        }
    }
}