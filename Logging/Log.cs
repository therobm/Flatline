using System;
using System.IO;

namespace Flatline.Logging
{
    public static class Log
    {
        private const string LogDirectory = "logs";
        private const string LogFilePrefix = "flatline-";
        private const string LogFileSuffix = ".log";

        private static object s_writeLock = new object();
        private static StreamWriter s_currentLogStream = null;
		private static DateTime s_lastLogFileDate = DateTime.MinValue;

		private static bool s_processExitHandlerInstalled = false;

        public static void Info(string message)
        {
            WriteLog(eLogLevel.Info, message);
        }

        public static void Warning(string message)
        {
            WriteLog(eLogLevel.Warning, message);
        }

        public static void Error(string message)
        {
            WriteLog(eLogLevel.Error, message);
        }

        public static void Exception(Exception exception)
        {
            string body = "(no exception)";
            if (exception != null)
            {
                body = exception.ToString();
            }
            WriteLog(eLogLevel.Exception, body);
        }

        public static void Exception(Exception exception, string contextMessage)
        {
            string body = contextMessage;
            if (exception != null)
            {
                body = contextMessage + System.Environment.NewLine + exception.ToString();
            }
            WriteLog(eLogLevel.Exception, body);
        }

        private static void WriteLog(eLogLevel level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelTag = GetLevelTag(level);
            string formatted = timestamp + " " + levelTag + " " + message;
			Console.WriteLine(formatted);

            lock (s_writeLock)
            {
				ValidateLogFile();
                s_currentLogStream.WriteLine(formatted);
                /* Flush only when the line matters enough that losing it on a
                 * crash would be a problem. Per-request Info lines are the hot
                 * path; they stay in StreamWriter's buffer and get flushed
                 * either on the next Warning+, on log-file rotation, or on
                 * process exit (see InstallProcessExitHandler). */
                if (level != eLogLevel.Info)
                {
                    s_currentLogStream.Flush();
                }
            }
        }

        private static string GetLevelTag(eLogLevel level)
        {
            if (level == eLogLevel.Info)
            {
                return "INFO ";
            }
            if (level == eLogLevel.Warning)
            {
                return "WARN ";
            }
            if (level == eLogLevel.Error)
            {
                return "ERROR";
            }
            if (level == eLogLevel.Exception)
            {
                return "EXCN ";
            }
            return "?????";
        }

		private static void ValidateLogFile()
        {
            DateTime today = DateTime.Now;

			bool initialized = s_lastLogFileDate == today.Date && s_currentLogStream != null;
			if (initialized)
				return;

			s_lastLogFileDate = today.Date;
            if (s_currentLogStream != null)
            {
                /* StreamWriter.Dispose flushes its buffer before closing, so
                 * day-rollover does not lose the previous day's buffered Info
                 * lines. */
                s_currentLogStream.Dispose();
                s_currentLogStream = null;
            }

            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            string fileName = LogFilePrefix + today.ToString("yyyy-MM-dd") + LogFileSuffix;
            string filePath = Path.Combine(LogDirectory, fileName);
            FileStream fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            s_currentLogStream = new StreamWriter(fileStream);
    
            if (!s_processExitHandlerInstalled)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                s_processExitHandlerInstalled = true;
            }
        }

        private static void OnProcessExit(object sender, EventArgs eventArgs)
        {
            lock (s_writeLock)
            {
                if (s_currentLogStream != null)
                {
                    s_currentLogStream.Dispose();
                    s_currentLogStream = null;
                }
            }
        }
    }
}
