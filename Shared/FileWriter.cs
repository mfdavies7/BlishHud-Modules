using System;
using System.IO;
using System.Threading.Tasks;
using Blish_HUD;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Saves a file off the update thread, as Blish HUD's module guide asks of work in Update and input handlers: the text
    /// is made by the caller, the write happens in the background, one at a time, and only the newest text is written if
    /// several saves come in during one write. <see cref="Flush"/> writes what's waiting right away, for Unload.
    /// </summary>
    internal sealed class FileWriter
    {
        private static readonly Logger Logger = Logger.GetLogger(typeof(FileWriter));

        private readonly string _path;
        private readonly object _lock = new object();
        // Held while taking the waiting text and writing it, so the background write and Flush never write at once.
        private readonly object _fileLock = new object();
        private string _pending;
        private bool _writing;

        public FileWriter(string path) => _path = path;

        /// <summary>Saves the text in the background.</summary>
        public void Write(string text)
        {
            lock (_lock)
            {
                _pending = text;
                if (_writing) return; // the running write picks it up next
                _writing = true;
            }
            Task.Run(WriteWaiting);
        }

        /// <summary>
        /// Writes the text waiting to be saved now, on this thread, after any write already running, so the file is
        /// complete when it returns.
        /// </summary>
        public void Flush()
        {
            lock (_fileLock)
            {
                string text = Take();
                if (text != null) WriteNow(text);
            }
        }

        private void WriteWaiting()
        {
            while (true)
            {
                // Taken inside the file lock, so a Flush either waits for this write or takes the text itself.
                lock (_fileLock)
                {
                    string text = Take();
                    if (text == null)
                    {
                        lock (_lock)
                        {
                            if (_pending != null) continue; // came in just now
                            _writing = false;
                        }
                        return;
                    }
                    WriteNow(text);
                }
            }
        }

        private string Take()
        {
            lock (_lock)
            {
                string text = _pending;
                _pending = null;
                return text;
            }
        }

        private void WriteNow(string text)
        {
            try
            {
                File.WriteAllText(_path, text);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Couldn't save {Path.GetFileName(_path)}.");
                Blocked.Check(ex, _path);
            }
        }
    }
}
