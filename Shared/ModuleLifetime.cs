using System.Threading;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Whether the module is loaded, so work it started (a key sequence, a download) stops when Blish HUD unloads it, as
    /// Blish HUD's module guide asks ("reverse as much of your setup work as possible when unloading"). Every module
    /// compiles its own copy of Shared, so this is the module's own: <see cref="Begin"/> in LoadAsync, <see cref="End"/>
    /// in Unload. <see cref="ChatInput.EnsureSafe"/> checks it before every simulated key or click, and
    /// <see cref="Notify"/> shows nothing once it has ended.
    /// </summary>
    internal static class ModuleLifetime
    {
        private static CancellationTokenSource _source;
        // Kept apart from the source: a disposed source's Token throws, a token read before disposal doesn't.
        private static CancellationToken _token = new CancellationToken(canceled: true);
        private static volatile bool _ended;

        /// <summary>Cancelled when the module unloads.</summary>
        public static CancellationToken Token => _token;

        /// <summary>The module has been unloaded (and not loaded again since).</summary>
        public static bool Ended => _ended;

        public static void Begin()
        {
            End();
            _source = new CancellationTokenSource();
            _token = _source.Token;
            _ended = false;
        }

        public static void End()
        {
            var source = _source;
            _source = null;
            _ended = true;
            if (source == null) return;
            source.Cancel();
            source.Dispose();
        }

        /// <summary>Stops a sequence once the module has unloaded. The message is never shown: Notify is silent by then.</summary>
        public static void ThrowIfEnded()
        {
            if (_ended) throw new AbortException("Stopped because the module was turned off.");
        }
    }
}
