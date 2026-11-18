using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CleanFolder
{
    /// <summary>Современный диалог выбора папки (как в Проводнике), при сбое – классический.</summary>
    public static class FolderPicker
    {
        public static string Pick(IWin32Window owner, string title, string initial)
        {
            try { return PickModern(owner, title, initial); }
            catch (COMException) { }
            catch (InvalidCastException) { }

            using (var d = new FolderBrowserDialog { Description = title, SelectedPath = initial ?? "", ShowNewFolderButton = false })
                return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
        }

        static string PickModern(IWin32Window owner, string title, string initial)
        {
            var dlg = (IFileDialog)new FileOpenDialogRCW();
            try
            {
                dlg.GetOptions(out uint opts);
                dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dlg.SetTitle(title);
                dlg.SetOkButtonLabel("Выбрать папку");
                if (!string.IsNullOrEmpty(initial) && System.IO.Directory.Exists(initial))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out IShellItem item) == 0)
                        dlg.SetFolder(item);
                }
                int hr = dlg.Show(owner?.Handle ?? IntPtr.Zero);
                if (hr != 0) return null; // отмена
                dlg.GetResult(out IShellItem result);
                result.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally { Marshal.ReleaseComObject(dlg); }
        }

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint c, IntPtr filters);
            void SetFileTypeIndex(uint i);
            void GetFileTypeIndex(out uint i);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem psi);
            void GetCurrentSelection(out IShellItem psi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem psi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem psi);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attribs);
            void Compare(IShellItem psi, uint hint, out int order);
        }
    }
}
