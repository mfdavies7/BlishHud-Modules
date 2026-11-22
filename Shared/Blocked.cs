using System;
using System.Net.Sockets;
using System.Threading;
using Blish_HUD.Debug;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Security software (anti-virus, a firewall, Controlled Folder Access) blocking a save or a download. Blish HUD's module
    /// guide asks modules to let Blish HUD tell the player, rather than work around it; once per kind, so a blocked
    /// download of thousands of items doesn't repeat it.
    /// </summary>
    internal static class Blocked
    {
        /// <summary>The spaced module name shown in the notification ("Tyria Search"); set when the module is constructed.</summary>
        public static string ModuleName = "this module";

        private static int _saveNotified, _downloadNotified;

        /// <param name="path">The file being saved, or null for a download.</param>
        public static void Check(Exception ex, string path = null)
        {
            if (path != null && ex is UnauthorizedAccessException)
            {
                if (Interlocked.Exchange(ref _saveNotified, 1) == 0)
                    Contingency.NotifyFileSaveAccessDenied(path, $"while saving {ModuleName} data", false);
                return;
            }

            // Being blocked shows as a socket the system won't let us use, unlike being offline or the server being down.
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is SocketException socket && socket.SocketErrorCode == SocketError.AccessDenied)
                {
                    if (Interlocked.Exchange(ref _downloadNotified, 1) == 0)
                        Contingency.NotifyHttpAccessDenied($"to download {ModuleName} data");
                    return;
                }
            }
        }
    }
}
