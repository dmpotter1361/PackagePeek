using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AmazonTracker;

/// <summary>
/// Gives the app a stable AppUserModelID and a matching Start-Menu shortcut, which is
/// what lets an *unpackaged* desktop app show toast notifications branded with its real
/// name and icon (instead of a generic title). Best-effort — failures are swallowed.
/// </summary>
internal static class ShellBranding
{
    public const string AppId = "MichaelPotter.PackagePeek";
    private const string AppName = "Package Peek";

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);

    public static void SetProcessAppId()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
    }

    /// <summary>Create (or refresh) the Start-Menu shortcut carrying our AppUserModelID.</summary>
    public static void EnsureStartMenuShortcut()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            // If a shortcut already exists (e.g. the MSI installer created a branded one,
            // or a previous run did), don't make a duplicate.
            var userLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
            var commonLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk");
            if (File.Exists(userLink) || File.Exists(commonLink)) return;
            var linkPath = userLink;

            var link = (IShellLinkW)new CShellLink();
            link.SetPath(exe);
            link.SetArguments("");
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetDescription("Package Peek - Amazon delivery tracker");
            var icon = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(icon)) link.SetIconLocation(icon, 0);

            // Stamp the AppUserModelID onto the shortcut so Windows brands our toasts.
            // Build a VT_LPWSTR PROPVARIANT by hand (avoids relying on propsys exports).
            var store = (IPropertyStore)link;
            var pv = new PROPVARIANT { vt = VT_LPWSTR, p = Marshal.StringToCoTaskMemUni(AppId) };
            try
            {
                var key = PKEY_AppUserModel_ID;
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally { PropVariantClear(ref pv); } // frees the string for VT_LPWSTR

            ((IPersistFile)link).Save(linkPath, true);
        }
        catch (Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PackagePeek");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "branding.log"), $"{DateTime.Now:G}\n{ex}");
            }
            catch { }
        }
    }

    // --- Shell interop ---------------------------------------------------------

    private static PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };

    private const ushort VT_LPWSTR = 31;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    // Sized to match PROPVARIANT (16 bytes x86 / 24 bytes x64); we only use the LPWSTR pointer.
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p, p2; }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }
}
