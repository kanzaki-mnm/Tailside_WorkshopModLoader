using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TailsideWorkshopModLoader
{
    public sealed class WorkshopFolder
    {
        public ulong Id;
        public string Path;
    }

    public static class SteamSubscriptions
    {
        public static bool IsReady(uint state) => (state & 5) == 5 && (state & (8 | 16 | 32)) == 0;
        // Uses the native SDK shipped with the game; no second Facepunch instance or callback thread.
        [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
        private static extern IntPtr LoadLibraryW(string path);
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)]
        private static extern int SteamInternal_SteamAPI_Init(byte[] interfaceVersions, [Out] byte[] error);
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern void SteamAPI_Shutdown();
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern int SteamAPI_GetHSteamUser();
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamUGC_v020();
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamUtils_v010();
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern uint SteamAPI_ISteamUtils_GetAppID(IntPtr self);
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern uint SteamAPI_ISteamUGC_GetNumSubscribedItems(IntPtr self);
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern uint SteamAPI_ISteamUGC_GetSubscribedItems(IntPtr self, [Out] ulong[] ids, uint size);
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)] private static extern uint SteamAPI_ISteamUGC_GetItemState(IntPtr self, ulong id);
        [DllImport("steam_api64", CallingConvention=CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamUGC_GetItemInstallInfo(IntPtr self, ulong id, out ulong bytes, [Out] byte[] folder, uint size, out uint timestamp);

        // Proton translates GetItemInstallInfo paths into its Windows namespace. Never guess a
        // drive letter for a raw Unix path or scan Steam folders when that translation is unavailable.
        public static string DecodeInstallFolder(byte[] buffer)
        {
            if (buffer == null) throw new InvalidDataException("Missing Steam install-folder buffer.");
            var end = Array.IndexOf(buffer, (byte)0);
            if (end <= 0) throw new InvalidDataException("Steam install-folder buffer is empty or unterminated.");
            string folder;
            try { folder = new UTF8Encoding(false, true).GetString(buffer, 0, end); }
            catch (DecoderFallbackException e) { throw new InvalidDataException("Steam install folder is not valid UTF-8.", e); }
            var drive = folder.Length >= 3 && ((folder[0] >= 'A' && folder[0] <= 'Z') || (folder[0] >= 'a' && folder[0] <= 'z')) &&
                folder[1] == ':' && (folder[2] == '\\' || folder[2] == '/');
            var uncParts = folder.StartsWith(@"\\", StringComparison.Ordinal) ? folder.Substring(2).Split('\\') : new string[0];
            var unc = uncParts.Length >= 2 && uncParts[0].Length > 0 && uncParts[1].Length > 0 &&
                uncParts[0] != "." && uncParts[0] != "?";
            if (!drive && !unc) throw new InvalidDataException("Steam did not return an absolute Windows-mapped install folder; no path fallback is used.");
            return Path.GetFullPath(folder);
        }

        public static List<WorkshopFolder> Read(string gameRoot, Action<string> log)
        {
            var result = new List<WorkshopFolder>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Windows x64 game ABI required (including Proton). Native Linux/macOS is not supported.");
            var native = Path.Combine(Path.GetFullPath(gameRoot), "Tailside_Data", "Plugins", "x86_64", "steam_api64.dll");
            if (!File.Exists(native)) throw new FileNotFoundException("Game-supplied Steam API not found; no library fallback is used.", native);
            if (LoadLibraryW(native) == IntPtr.Zero) throw new InvalidOperationException("Cannot load game Steam API; Win32 error=" + Marshal.GetLastWin32Error());
            var previousApp = Environment.GetEnvironmentVariable("SteamAppId");
            bool ownsSession = false;
            try
            {
                if (SteamAPI_GetHSteamUser() == 0)
                {
                    if (previousApp != null && previousApp != Catalog.AppId.ToString())
                        throw new InvalidOperationException("Different SteamAppId in process environment.");
                    Environment.SetEnvironmentVariable("SteamAppId", Catalog.AppId.ToString());
                    var error = new byte[1024];
                    var status = SteamInternal_SteamAPI_Init(Encoding.UTF8.GetBytes("STEAMUGC_INTERFACE_VERSION020\0SteamUtils010\0\0"), error);
                    if (status != 0) throw new InvalidOperationException("Steam API unavailable (" + status + "): " + Encoding.UTF8.GetString(error).TrimEnd('\0'));
                    ownsSession = true;
                }
                var utils = SteamAPI_SteamUtils_v010();
                if (utils == IntPtr.Zero || SteamAPI_ISteamUtils_GetAppID(utils) != Catalog.AppId)
                    throw new InvalidOperationException("Steam API belongs to a different app.");
                var ugc = SteamAPI_SteamUGC_v020();
                if (ugc == IntPtr.Zero) throw new InvalidOperationException("Steam UGC v020 unavailable.");
                var count = SteamAPI_ISteamUGC_GetNumSubscribedItems(ugc);
                if (count > 10000) throw new InvalidOperationException("Unexpected subscription count.");
                var ids = new ulong[count];
                var read = count == 0 ? 0 : SteamAPI_ISteamUGC_GetSubscribedItems(ugc, ids, count);
                if (read > count) throw new InvalidOperationException("Subscription count changed; retry next launch.");
                log("Steam subscriptions: " + read);
                for (int i = 0; i < read; i++)
                {
                    var state = SteamAPI_ISteamUGC_GetItemState(ugc, ids[i]);
                    // Subscribed + installed; skip incomplete or updating files. Do not download while loading.
                    if (!IsReady(state))
                    { log("Workshop item not ready; skipped " + ids[i] + " state=" + state); continue; }
                    var buffer = new byte[32768];
                    if (!SteamAPI_ISteamUGC_GetItemInstallInfo(ugc, ids[i], out _, buffer, (uint)buffer.Length, out _))
                    { log("No install folder for " + ids[i]); continue; }
                    try
                    {
                        var folder = DecodeInstallFolder(buffer);
                        if (!Directory.Exists(folder)) { log("Steam install folder is not accessible; skipped " + ids[i]); continue; }
                        result.Add(new WorkshopFolder { Id=ids[i], Path=folder });
                    }
                    catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is NotSupportedException || e is IOException)
                    { log("Invalid Steam install folder; skipped " + ids[i] + ": " + e.Message); }
                }
                return result;
            }
            finally
            {
                if (ownsSession) SteamAPI_Shutdown();
                Environment.SetEnvironmentVariable("SteamAppId", previousApp);
            }
        }
    }
}
