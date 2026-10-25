using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using static Fnaf2Mod.Native;

namespace Fnaf2Mod;

internal static unsafe class Mod
{
    // Window / style constants
    const uint WS_CHILD = 0x40000000;
    const uint StripStyle = 0x00C00000 | 0x00040000 | 0x00020000 | 0x00010000 | 0x00080000 | 0x01000000; // caption|thickframe|minbox|maxbox|sysmenu|maximize
    const uint StripExStyle = 0x200 | 0x100 | 0x1; // clientedge|windowedge|dlgmodalframe
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
    const int GWL_STYLE = -16;
    const int VK_LBUTTON = 1, VK_RBUTTON = 2, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_LSHIFT = 0xA0, VK_LCONTROL = 0xA2;


    static nint selfModule, gameModule, gameWindow;
    static string exePath = "", selfDir = "";
    static bool activePatch, validated;
    static byte[] delta = [];
    static readonly object gate = new();

    // ---------------------------------------------------------------- startup

    [UnmanagedCallersOnly(EntryPoint = "ModInitialize", CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void Attach()
    {
        try
        {
            nint self;
            GetModuleHandleExW(0x4 | 0x2, (void*)(nint)(delegate* unmanaged[Stdcall]<nint, void*, uint, uint*, void*, int>)&ReadPatched, &self);
            selfModule = self;
            selfDir = Path.GetDirectoryName(ModulePath(selfModule)) ?? "";
            gameModule = GetModuleHandleW(null);
            exePath = ModulePath(gameModule);
            Log("DLL attached");
            HookModule(gameModule);
        }
        catch { }
    }

    static string ModulePath(nint module)
    {
        char* buffer = stackalloc char[1024];
        uint n = GetModuleFileNameW(module, buffer, 1024);
        return new string(buffer, 0, (int)n);
    }

    static void Log(string text)
    {
        try
        {
            using var f = new FileStream(Path.Combine(selfDir, "fnaf2-mod.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            f.Write(Encoding.UTF8.GetBytes(text + "\r\n"));
        }
        catch { }
    }

    // ------------------------------------------------------------- validation

    static void EnsureValidated()
    {
        if (validated) return;
        lock (gate)
        {
            if (validated) return;
            try
            {
                using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long size = fs.Length;
                string hash = Convert.ToHexString(SHA256.HashData(fs));
                if (size == 217749606 && hash == "84DB9972C70AE02BA9596E1BE624F2942545EE939611E7FCD593B75A67D41AC6")
                {
                    using var resource = typeof(Mod).Assembly.GetManifestResourceStream("night.patch")!;
                    using var memory = new MemoryStream(); resource.CopyTo(memory); delta = memory.ToArray();
                    activePatch = delta.Length == 18274;
                }
            }
            catch { Log("Validation open failed"); }
            Log(activePatch ? "Validated supported game" : "Game hash not recognized or hash failed");
            validated = true;
        }
    }

    static bool IsGameFile(nint file)
    {
        char* path = stackalloc char[1032];
        uint n = GetFinalPathNameByHandleW(file, path, 1032, 0 /* FILE_NAME_NORMALIZED */);
        if (n == 0 || n >= 1032) return false;
        var span = new ReadOnlySpan<char>(path, (int)n);
        if (span.StartsWith(@"\\?\")) span = span.Slice(4);
        return span.Equals(exePath, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------- runtime patches

    [UnmanagedCallersOnly(EntryPoint = "ModReadFile", CallConvs = new[] { typeof(CallConvStdcall) })]
    static int ReadPatched(nint file, void* buffer, uint count, uint* read, void* overlapped)
    {
        bool target = false; long position = 0;
        try
        {
            target = overlapped == null && IsGameFile(file);
            if (target) { EnsureValidated(); long p = 0; target = activePatch && SetFilePointerEx(file, 0, &p, 1) != 0; position = p; }
        }
        catch { target = false; }
        int result = ReadFile(file, buffer, count, read, overlapped);
        uint error = GetLastError();
        if (result != 0 && target && read != null && gameWindow != 0)
        {
            const long offset = 2554056;
            long from = Math.Max(position, offset), to = Math.Min(position + *read, offset + delta.Length);
            if (to > from) Log($"Night patch read: offset={position} size={*read}");
            for (long p = from; p < to; ++p) ((byte*)buffer)[p - position] ^= delta[p - offset];
        }
        SetLastError(error); return result;
    }

    static void Bounds(nint window, out int x, out int y, out int w, out int h)
    {
        MonitorInfo m = default;
        m.Size = (uint)sizeof(MonitorInfo);
        GetMonitorInfoW(MonitorFromWindow(window, 2 /* MONITOR_DEFAULTTONEAREST */), &m);
        int mw = m.Monitor.Right - m.Monitor.Left, mh = m.Monitor.Bottom - m.Monitor.Top;
        w = mw; h = mw * 9 / 16;
        if (h > mh) { h = mh; w = mh * 16 / 9; }
        x = m.Monitor.Left + (mw - w) / 2;
        y = m.Monitor.Top + (mh - h) / 2;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int PositionPatched(nint window, nint after, int x, int y, int w, int h, uint flags)
    {
        try
        {
            if (window == gameWindow && activePatch && IsIconic(window) == 0)
            {
                Bounds(window, out x, out y, out w, out h);
                flags &= ~(SWP_NOSIZE | SWP_NOMOVE);
            }
        }
        catch { }
        return SetWindowPos(window, after, x, y, w, h, flags);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint CreatePatched(uint ex, char* cls, char* title, uint style, int x, int y, int w, int h,
        nint parent, nint menu, nint instance, void* param)
    {
        bool main = false;
        try
        {
            Log("Create window reached");
            main = activePatch && parent == 0 && (style & WS_CHILD) == 0 && w >= 500 && h >= 300;
            if (main) { style &= ~StripStyle; ex &= ~StripExStyle; }
        }
        catch { }
        nint window = CreateWindowExW(ex, cls, title, style, x, y, w, h, parent, menu, instance, param);
        if (main) { gameWindow = window; Log("Game window found"); }
        return window;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int ShowPatched(nint window, int mode)
    {
        int result = ShowWindow(window, mode);
        try
        {
            // SW_HIDE = 0, SW_SHOWMINIMIZED = 2, SW_MINIMIZE = 6
            if (window == gameWindow && activePatch && mode != 0 && mode != 6 && mode != 2)
            {
                Bounds(window, out int x, out int y, out int w, out int h);
                SetWindowLongW(window, GWL_STYLE, GetWindowLongW(window, GWL_STYLE) & ~(int)StripStyle);
                SetWindowPos(window, 0, x, y, w, h, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
            }
        }
        catch { }
        return result;
    }

    // --------------------------------------------- follow modules the game loads

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint LoadPatchedW(char* path)
    {
        nint m = LoadLibraryW(path);
        if (m != 0) HookModule(m);
        return m;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint LoadPatchedA(byte* path)
    {
        nint m = LoadLibraryA(path);
        if (m != 0) HookModule(m);
        return m;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint LoadExPatchedW(char* path, nint file, uint flags)
    {
        nint m = LoadLibraryExW(path, file, flags);
        if (m != 0 && (flags & (0x2 | 0x20)) == 0) HookModule(m); // not DATAFILE / IMAGE_RESOURCE
        return m;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint LoadExPatchedA(byte* path, nint file, uint flags)
    {
        nint m = LoadLibraryExA(path, file, flags);
        if (m != 0 && (flags & (0x2 | 0x20)) == 0) HookModule(m);
        return m;
    }

    static void HookModule(nint module)
    {
        try
        {
            if (module == selfModule) return;
            if (module != gameModule && !ModulePath(module).Contains("mrt", StringComparison.Ordinal)) return;
            Log("Hooking game/runtime module: " + ModulePath(module));

            Hook(module, "ReadFile"u8, (nint)(delegate* unmanaged[Stdcall]<nint, void*, uint, uint*, void*, int>)&ReadPatched);
            Hook(module, "CreateWindowExW"u8, (nint)(delegate* unmanaged[Stdcall]<uint, char*, char*, uint, int, int, int, int, nint, nint, nint, void*, nint>)&CreatePatched);
            Hook(module, "ShowWindow"u8, (nint)(delegate* unmanaged[Stdcall]<nint, int, int>)&ShowPatched);
            Hook(module, "SetWindowPos"u8, (nint)(delegate* unmanaged[Stdcall]<nint, nint, int, int, int, int, uint, int>)&PositionPatched);
            Hook(module, "LoadLibraryW"u8, (nint)(delegate* unmanaged[Stdcall]<char*, nint>)&LoadPatchedW);
            Hook(module, "LoadLibraryA"u8, (nint)(delegate* unmanaged[Stdcall]<byte*, nint>)&LoadPatchedA);
            Hook(module, "LoadLibraryExW"u8, (nint)(delegate* unmanaged[Stdcall]<char*, nint, uint, nint>)&LoadExPatchedW);
            Hook(module, "LoadLibraryExA"u8, (nint)(delegate* unmanaged[Stdcall]<byte*, nint, uint, nint>)&LoadExPatchedA);
        }
        catch { }
    }

    /// <summary>Overwrites the first import-table entry named <paramref name="name"/> (PE32 / x86).</summary>
    static void Hook(nint module, ReadOnlySpan<byte> name, nint replacement)
    {
        byte* b = (byte*)module;
        byte* nt = b + *(int*)(b + 0x3c);
        uint rva = *(uint*)(nt + 128); // DataDirectory[IMPORT].VirtualAddress
        if (rva == 0) return;

        // IMAGE_IMPORT_DESCRIPTOR = 5 x uint: OriginalFirstThunk, TimeDateStamp, ForwarderChain, Name, FirstThunk
        for (uint* d = (uint*)(b + rva); d[3] != 0; d += 5)
        {
            if (d[0] == 0) continue;
            uint* names = (uint*)(b + d[0]);
            uint* table = (uint*)(b + d[4]);
            for (; *names != 0; names++, table++)
            {
                if ((*names & 0x80000000) != 0) continue; // imported by ordinal
                byte* entry = b + *names + 2;              // skip IMAGE_IMPORT_BY_NAME.Hint
                if (!MemoryMarshal.CreateReadOnlySpanFromNullTerminated(entry).SequenceEqual(name)) continue;

                uint old;
                if (VirtualProtect(table, 4, 0x04 /* PAGE_READWRITE */, &old) != 0)
                {
                    *table = (uint)replacement;
                    uint ignored;
                    VirtualProtect(table, 4, old, &ignored);
                }
                return;
            }
        }
    }
}
