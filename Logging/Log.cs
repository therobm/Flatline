using System;
using System.IO;

namespace Flatline.Logging
{
    public static class Log
    {
        private const string LogDirectory = "logs";
        private const string LogFilePrefix = "flatline-";
        private const string LogFileSuffix = ".log";

        private static object WriteLock = new object();
        private static StreamWriter CurrentLogWriter = null;
        private static DateTime CurrentLogDate = DateTime.MinValue;
        private static TextWriter OriginalConsoleOut = Console.Out;
        private static bool ProcessExitHandlerInstalled = false;

        public static void Info(string message)
        {
            WriteEntry(eLogLevel.Info, message);
        }

        public static void Warning(string message)
        {
            WriteEntry(eLogLevel.Warning, message);
        }

        public static void Error(string message)
        {
            WriteEntry(eLogLevel.Error, message);
        }

        public static void Exception(Exception exception)
        {
            string body = "(no exception)";
            if (exception != null)
            {
                body = exception.ToString();
            }
            WriteEntry(eLogLevel.Exception, body);
        }

        public static void Exception(Exception exception, string contextMessage)
        {
            string body = contextMessage;
            if (exception != null)
            {
                body = contextMessage + System.Environment.NewLine + exception.ToString();
            }
            WriteEntry(eLogLevel.Exception, body);
        }

        private static void WriteEntry(eLogLevel level, string message)
        {
            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelTag = GetLevelTag(level);
            string formatted = timestamp + " " + levelTag + " " + message;

            lock (WriteLock)
            {
                OriginalConsoleOut.WriteLine(formatted);
                EnsureLogWriterForDate(now);
                CurrentLogWriter.WriteLine(formatted);
                /* Flush only when the line matters enough that losing it on a
                 * crash would be a problem. Per-request Info lines are the hot
                 * path; they stay in StreamWriter's buffer and get flushed
                 * either on the next Warning+, on log-file rotation, or on
                 * process exit (see InstallProcessExitHandler). */
                if (level != eLogLevel.Info)
                {
                    CurrentLogWriter.Flush();
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

        private static void EnsureLogWriterForDate(DateTime now)
        {
            DateTime today = now.Date;
            if (CurrentLogWriter != null && CurrentLogDate == today)
            {
                return;
            }

            if (CurrentLogWriter != null)
            {
                /* StreamWriter.Dispose flushes its buffer before closing, so
                 * day-rollover does not lose the previous day's buffered Info
                 * lines. */
                CurrentLogWriter.Dispose();
                CurrentLogWriter = null;
            }

            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            string fileName = LogFilePrefix + today.ToString("yyyy-MM-dd") + LogFileSuffix;
            string filePath = Path.Combine(LogDirectory, fileName);
            FileStream fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            CurrentLogWriter = new StreamWriter(fileStream);
            CurrentLogDate = today;

            if (!ProcessExitHandlerInstalled)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                ProcessExitHandlerInstalled = true;
            }
        }

        private static void OnProcessExit(object sender, EventArgs eventArgs)
        {
            lock (WriteLock)
            {
                if (CurrentLogWriter != null)
                {
                    CurrentLogWriter.Dispose();
                    CurrentLogWriter = null;
                }
            }
        }
    }
}
