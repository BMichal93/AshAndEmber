using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace AshAndEmber
{
    /// <summary>
    /// A crash-proof error journal for the mod. Every otherwise-silent
    /// <c>catch</c> in the codebase funnels its exception here so that a failure
    /// leaves a trace on disk instead of vanishing. The log is written to
    /// <c>Documents\Mount and Blade II Bannerlord\AshAndEmber\errors.log</c>.
    ///
    /// Design constraints (this must be as unbreakable as the empty catches it
    /// replaces):
    ///  - It never throws. Any failure inside the logger is swallowed — losing a
    ///    log line must never crash a save.
    ///  - It references no TaleWorlds types, so it is safe to call from the pure
    ///    <c>*Math.cs</c> files and from any thread.
    ///  - It de-duplicates by failure site + exception, so a method that throws
    ///    every frame (e.g. inside a mission tick) is recorded once, not spammed
    ///    to death. A hard cap bounds both memory and file growth.
    ///  - It self-limits on disk: entries older than 30 days are pruned once at
    ///    session start, and the file is archived to <c>.old</c> past 5 MB, so we
    ///    never hoard more than a month of history.
    /// </summary>
    public static class ModLog
    {
        private static readonly object _gate = new object();
        private static readonly HashSet<string> _seen = new HashSet<string>();

        // Above this many distinct failure sites we stop recording, so a
        // pathological run can never grow the set or the file without bound.
        private const int MaxUniqueEntries = 5000;

        // If an existing log is larger than this at startup it is archived once,
        // so the file cannot grow forever across many sessions.
        private const long MaxFileBytes = 5 * 1024 * 1024;

        // Entries older than this are pruned once at startup, so the log never
        // hoards more than a month of history.
        private const int MaxRetentionDays = 30;

        private static string _path;
        private static bool _initTried;
        private static bool _disabled;

        /// <summary>
        /// Record an exception caught at a silent catch site. Caller identity is
        /// captured automatically — do not pass the optional arguments by hand.
        /// </summary>
        public static void Error(
            Exception ex,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            try
            {
                if (_disabled)
                    return;

                string where = Path.GetFileName(file) + ":" + line + " " + member;
                string key = where + "|" + (ex == null ? "null" : ex.GetType().Name + ":" + ex.Message);

                lock (_gate)
                {
                    if (_seen.Contains(key))
                        return;                       // already recorded this exact failure
                    if (_seen.Count >= MaxUniqueEntries)
                        return;                       // safety cap reached — stop recording
                    _seen.Add(key);

                    if (!EnsureInitialized())
                        return;

                    File.AppendAllText(_path, Format(where, ex), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never crash the game. If even this fails, give up
                // quietly for the rest of the session.
                _disabled = true;
            }
        }

        /// <summary>
        /// Record a failure with a short contextual note but no exception object
        /// (for the few catch sites that never had an exception variable and only
        /// want to mark that a branch failed).
        /// </summary>
        public static void Warn(
            string note,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            try
            {
                if (_disabled)
                    return;

                string where = Path.GetFileName(file) + ":" + line + " " + member;
                string key = where + "|" + note;

                lock (_gate)
                {
                    if (_seen.Contains(key))
                        return;
                    if (_seen.Count >= MaxUniqueEntries)
                        return;
                    _seen.Add(key);

                    if (!EnsureInitialized())
                        return;

                    File.AppendAllText(
                        _path,
                        Stamp() + "  [warn] " + where + " — " + note + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                _disabled = true;
            }
        }

        // Must be called under _gate.
        private static bool EnsureInitialized()
        {
            if (_path != null)
                return true;
            if (_initTried)
                return false;
            _initTried = true;

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord",
                    "AshAndEmber");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "errors.log");

                PruneOldEntries(path);
                ArchiveIfLarge(path);

                File.AppendAllText(
                    path,
                    Environment.NewLine +
                    "==== Ash and Ember session started " + Stamp() + " ====" + Environment.NewLine,
                    Encoding.UTF8);

                _path = path;
                return true;
            }
            catch
            {
                _disabled = true;
                return false;
            }
        }

        // Rewrites the log keeping only records stamped within the last
        // MaxRetentionDays. Every record begins with a "[yyyy-MM-dd HH:mm:ss]"
        // stamp (an entry, a warn line, or a session banner); indented follow-on
        // lines — stack traces, inner exceptions — inherit their record's fate.
        // Runs once per session, before the new banner is appended.
        private static void PruneOldEntries(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                DateTime cutoff = DateTime.Now.AddDays(-MaxRetentionDays);
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);

                var kept = new List<string>(lines.Length);
                bool keepCurrent = true;   // undated leading lines are kept
                bool prunedAny = false;

                foreach (string line in lines)
                {
                    DateTime stamp;
                    if (TryReadStamp(line, out stamp))
                        keepCurrent = stamp >= cutoff;   // start of a new record

                    if (keepCurrent)
                        kept.Add(line);
                    else
                        prunedAny = true;
                }

                if (prunedAny)
                    File.WriteAllLines(path, kept, Encoding.UTF8);
            }
            catch
            {
                // Non-fatal — a failed prune just leaves the old file in place.
            }
        }

        // A record-leading line carries its stamp in the first "[...]" span:
        // "[2026-07-05 14:03:22]  File.cs:1 Method" or the session banner
        // "==== ... started [2026-07-05 14:03:22] ====". Anything else (indented
        // detail, blanks) has no leading stamp and returns false.
        private static bool TryReadStamp(string line, out DateTime stamp)
        {
            stamp = default(DateTime);
            if (string.IsNullOrEmpty(line))
                return false;

            char first = line[0];
            if (first != '[' && first != '=')
                return false;   // indented continuation line — cheap reject

            int open = line.IndexOf('[');
            if (open < 0)
                return false;
            int close = line.IndexOf(']', open + 1);
            if (close < 0)
                return false;

            string raw = line.Substring(open + 1, close - open - 1);
            return DateTime.TryParseExact(
                raw, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out stamp);
        }

        private static void ArchiveIfLarge(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    string archive = path + ".old";
                    if (File.Exists(archive))
                        File.Delete(archive);
                    File.Move(path, archive);
                }
            }
            catch
            {
                // Non-fatal — keep appending to the existing file.
            }
        }

        private static string Format(string where, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append(Stamp()).Append("  ").Append(where).Append(Environment.NewLine);
            if (ex == null)
            {
                sb.Append("    (no exception object)").Append(Environment.NewLine);
            }
            else
            {
                sb.Append("    ").Append(ex.GetType().FullName)
                  .Append(": ").Append(ex.Message).Append(Environment.NewLine);
                if (!string.IsNullOrEmpty(ex.StackTrace))
                    sb.Append(Indent(ex.StackTrace)).Append(Environment.NewLine);
                var inner = ex.InnerException;
                while (inner != null)
                {
                    sb.Append("    --- inner: ").Append(inner.GetType().FullName)
                      .Append(": ").Append(inner.Message).Append(Environment.NewLine);
                    if (!string.IsNullOrEmpty(inner.StackTrace))
                        sb.Append(Indent(inner.StackTrace)).Append(Environment.NewLine);
                    inner = inner.InnerException;
                }
            }
            return sb.ToString();
        }

        private static string Indent(string text)
        {
            return "        " + text.Replace("\n", "\n        ");
        }

        private static string Stamp()
        {
            return "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]";
        }
    }
}
