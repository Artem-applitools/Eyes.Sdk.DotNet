﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Applitools.Utils;

namespace Applitools
{
    /// <summary>
    /// Writes log messages to the standard output stream.
    /// </summary>
    [ComVisible(true)]
    public class FileLogHandler : ILogHandler
    {
        private Queue<string> queue_ = new Queue<string>(100);
        private AutoResetEvent waitHandle_ = new AutoResetEvent(false);
        private Thread fileWriterThread_;
        #region Constructors

        /// <summary>
        /// Creates a new <see cref="FileLogHandler"/> instance.
        /// </summary>
        /// <param name="filename">Where to write the log.</param>
        /// <param name="isVerbose">Whether to handle or ignore verbose log messages.</param>
        /// <param name="append">Whether to append to the file or create a new one.</param>
        public FileLogHandler(string filename, bool append, bool isVerbose)
        {
            IsVerbose = isVerbose;
            AppendToFile = append;
            FilePath = filename;
            Open();
        }

        /// <summary>
        /// Creates a new <see cref="FileLogHandler"/> instance.
        /// </summary>
        /// <param name="isVerbose">Whether to handle or ignore verbose log messages.</param>
        /// <param name="append">Whether to append to the file or create a new one.</param>
        public FileLogHandler(bool isVerbose, bool append = false)
            : this(Path.Combine(Environment.CurrentDirectory, "eyes.log"), append, isVerbose)
        {
        }

        /// <summary>
        /// Creates a new <see cref="FileLogHandler"/> that ignores verbose log messages.
        /// </summary>
        public FileLogHandler()
            : this(true)
        {
        }

        #endregion

        #region Properties

        /// <summary>
        /// Whether to handle or ignore verbose log messages.
        /// </summary>
        public bool IsVerbose { get; set; }

        /// <summary>
        /// Whether to append messages to the log file or to reset it on <see cref="Open"/>.
        /// </summary>
        public bool AppendToFile { get; set; }

        /// <summary>
        /// The path to the log file.
        /// </summary>
        public string FilePath { get; set; }

        #endregion

        #region Methods

        private void DumpLogToFile_()
        {
            while (IsOpen)
            {
                waitHandle_.WaitOne(2000);
                if (FilePath == null) continue;
                if (queue_.Count > 0)
                {
                    string[] logLines;
                    lock (queue_)
                    {
                        logLines = queue_.ToArray();
                        queue_.Clear();
                    }
                    lock (FilePath)
                    {
                        FileUtils.AppendToTextFile(FilePath, logLines);
                    }
                    Thread.Sleep(1000);
                }
            }
        }

        public void OnMessage(bool verbose, string message, params object[] args)
        {
            try
            {
                if (!verbose || IsVerbose)
                {
                    if (args != null && args.Length > 0)
                    {
                        message = string.Format(message, args);
                    }
                    lock (queue_)
                    {
                        queue_.Enqueue(DateTimeOffset.Now + " - Eyes: " + message);
                        waitHandle_.Set();
                    }
                }
            }
            catch
            {
                // We don't want a trace failure the fail the test
            }
        }
        public void OnMessage(bool verbose, Func<string> messageProvider)
        {
            try
            {
                if (!verbose || IsVerbose)
                {
                    lock (queue_)
                    {
                        queue_.Enqueue(messageProvider());
                        waitHandle_.Set();
                    }
                }
            }
            catch
            {
                // We don't want a trace failure the fail the test
            }
        }
        public bool IsOpen { get; private set; } = true;

        public void Open()
        {
            try
            {
                if (!AppendToFile)
                {
                    lock (FilePath)
                    {
                        FileUtils.WriteTextFile(FilePath, string.Empty, false);
                    }
                }
                if (fileWriterThread_ == null || !fileWriterThread_.IsAlive)
                {
                    fileWriterThread_ = new Thread(new ThreadStart(DumpLogToFile_));
                    fileWriterThread_.IsBackground = true;
                    IsOpen = true;
                }
                fileWriterThread_.Start();
            }
            catch
            {
                // We don't want a trace failure the fail the test
            }
        }

        public void Close()
        {
            waitHandle_.Set();
            IsOpen = false;
        }

        #endregion
    }
}
