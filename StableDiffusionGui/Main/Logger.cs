﻿using StableDiffusionGui.Extensions;
using StableDiffusionGui.Io;
using StableDiffusionGui.Ui;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DT = System.DateTime;

namespace StableDiffusionGui.Main
{
    internal class Logger
    {
        public static TextBox Textbox;
        private static string _file;
        public const string DefaultLogName = "sessionlog";
        public static long Id;

        private static Dictionary<string, string> sessionLogs = new Dictionary<string, string>();
        private static string _lastUi = "";
        public static string LastUiLine { get { return _lastUi; } }
        private static string _lastLog = "";
        public static string LastLogLine { get { return _lastLog; } }

        public struct LogEntry
        {
            public string logMessage;
            public bool hidden;
            public bool replaceLastLine;
            public string filename;

            public LogEntry(string logMessageArg, bool hiddenArg = false, bool replaceLastLineArg = false, string filenameArg = "")
            {
                logMessage = logMessageArg;
                hidden = hiddenArg;
                replaceLastLine = replaceLastLineArg;
                filename = filenameArg;
            }
        }

        private static ConcurrentQueue<LogEntry> logQueue = new ConcurrentQueue<LogEntry>();

        public static void Log(string msg, bool hidden = false, bool replaceLastLine = false, string filename = "")
        {
            logQueue.Enqueue(new LogEntry(msg, hidden, replaceLastLine, filename));
            ShowNext();
        }

        public static void ShowNext()
        {
            LogEntry entry;

            if (logQueue.TryDequeue(out entry))
                Show(entry);
        }

        public static void Show(LogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.logMessage))
                return;

            string msg = entry.logMessage;

            if (msg == LastUiLine)
                entry.hidden = true; // Never show the same line twice in UI, but log it to file

            _lastLog = msg;

            if (!entry.hidden)
                _lastUi = msg;
            Console.WriteLine(msg);

            try
            {
                if (!entry.hidden && entry.replaceLastLine)
                {
                    Textbox.Suspend();
                    string[] lines = Textbox.Text.SplitIntoLines();
                    Textbox.Text = string.Join(Environment.NewLine, lines.Take(lines.Count() - 1).ToArray());
                }
            }
            catch { }
            msg = msg.Replace("\n", Environment.NewLine);

            if (!entry.hidden && Textbox != null)
                Textbox.AppendText((Textbox.Text.Length > 1 ? Environment.NewLine : "") + msg);

            if (entry.replaceLastLine)
            {
                Textbox.Resume();
                msg = "[REPL] " + msg;
            }

            if (!entry.hidden)
                msg = "[UI] " + msg;

            LogToFile(msg, false, entry.filename);
        }

        public static void LogToFile(string logStr, bool noLineBreak, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                filename = DefaultLogName;

            if (Path.GetExtension(filename) != ".txt")
                filename = Path.ChangeExtension(filename, "txt");

            _file = Path.Combine(Paths.GetLogPath(), filename);
            logStr = logStr.Replace(Environment.NewLine, " ").TrimWhitespaces();
            string time = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss");

            try
            {
                string appendStr = noLineBreak ? $" {logStr}" : $"{Environment.NewLine}[{Id.ToString().PadLeft(8, '0')}] [{time}]: {logStr}";
                sessionLogs[filename] = (sessionLogs.ContainsKey(filename) ? sessionLogs[filename] : "") + appendStr;
                File.AppendAllText(_file, appendStr);
                Id++;
            }
            catch
            {
                // this if fine, i forgot why
            }
        }

        public static string GetSessionLog(string filename)
        {
            if (!filename.Contains(".txt"))
                filename = Path.ChangeExtension(filename, "txt");

            if (sessionLogs.ContainsKey(filename))
                return sessionLogs[filename];
            else
                return "";
        }

        public static List<string> GetSessionLogLastLines(string filename, int linesCount = 5)
        {
            string log = GetSessionLog(filename);
            string[] lines = log.SplitIntoLines();
            return lines.Reverse().Take(linesCount).Reverse().ToList();
        }

        public static void LogIfLastLineDoesNotContainMsg(string s, bool hidden = false, bool replaceLastLine = false, string filename = "")
        {
            if (!GetLastLine().Contains(s))
                Log(s, hidden, replaceLastLine, filename);
        }

        public static void WriteToFile(string content, bool append, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                filename = DefaultLogName;

            if (Path.GetExtension(filename) != ".txt")
                filename = Path.ChangeExtension(filename, "txt");

            _file = Path.Combine(Paths.GetLogPath(), filename);

            string time = DT.Now.Month + "-" + DT.Now.Day + "-" + DT.Now.Year + " " + DT.Now.Hour + ":" + DT.Now.Minute + ":" + DT.Now.Second;

            try
            {
                if (append)
                    File.AppendAllText(_file, Environment.NewLine + time + ":" + Environment.NewLine + content);
                else
                    File.WriteAllText(_file, Environment.NewLine + time + ":" + Environment.NewLine + content);
            }
            catch
            {

            }
        }

        public static void ClearLogBox()
        {
            Textbox.Text = "";
        }

        public static string GetLastLine(bool includeHidden = false)
        {
            return includeHidden ? _lastLog : _lastUi;
        }

        public static void RemoveLastLine()
        {
            Textbox.Text = Textbox.Text.Remove(Textbox.Text.LastIndexOf(Environment.NewLine));
        }
    }
}
