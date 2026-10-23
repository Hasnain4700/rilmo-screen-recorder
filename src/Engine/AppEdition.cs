// SPDX-License-Identifier: GPL-2.0-or-later
namespace Rilmo.Engine
{
    internal static class AppEdition
    {
        // A build property, never an environment switch. Store-style tests also
        // exercise the shipped UI before package identity is available.
        internal static bool IsMicrosoftStore
        {
            get
            {
#if RILMO_STORE
                return true;
#else
                return WindowsPackageIdentity.IsPackaged;
#endif
            }
        }
        internal static bool OffersVirtualCamera => !IsMicrosoftStore;
    }
}
