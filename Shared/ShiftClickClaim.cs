using System;

namespace Bytex.ModuleShared
{
    /// <summary>
    /// Which module, if any, shift-clicks are meant for right now. Copy Item Name and Item Destruction claim them while
    /// they're turned on, so Wiki Search (always waiting for shift-clicks) leaves those clicks alone, and only one of them
    /// acts at a time. Every module compiles its own copy of this class, so the claim is kept in the AppDomain, which
    /// Blish HUD's modules share.
    /// </summary>
    internal static class ShiftClickClaim
    {
        private const string Key = "Bytex.ModuleShared.ShiftClickClaim";
        private static readonly object Lock = new object();

        /// <summary>The spaced name of the module that has claimed shift-clicks; null if none has.</summary>
        public static string Owner => AppDomain.CurrentDomain.GetData(Key) as string;

        /// <summary>Claims shift-clicks for the module, taking them from any other.</summary>
        public static void Claim(string moduleName)
        {
            lock (Lock) AppDomain.CurrentDomain.SetData(Key, moduleName);
        }

        /// <summary>Gives the claim up, if the module still has it.</summary>
        public static void Release(string moduleName)
        {
            lock (Lock)
            {
                if (Owner == moduleName) AppDomain.CurrentDomain.SetData(Key, null);
            }
        }

        public static bool IsOwner(string moduleName) => Owner == moduleName;

        /// <summary>Another module has claimed shift-clicks, so this one should leave them alone.</summary>
        public static bool TakenByOther(string moduleName)
        {
            string owner = Owner;
            return owner != null && owner != moduleName;
        }
    }
}
