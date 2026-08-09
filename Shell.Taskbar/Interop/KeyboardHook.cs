using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shell.Taskbar.Interop
{
    // =====================================================================
    //  KEYBOARD HOOK (Win-key tap detector)
    // =====================================================================
    //
    // Purpose:
    // - Installs a low-level keyboard hook (WH_KEYBOARD_LL) and raises WinKeyTapped
    //   when the user presses and releases the Win key *without* using it in a combo.
    // - Used to toggle the Start menu in WinPE where the normal shell isn’t present.
    //
    // How ShellTaskbarForm uses it:
    // - ShellTaskbarForm constructor creates KeyboardHook and subscribes:
    //     _kbHook.WinKeyTapped => ToggleStartMenu()
    // - ShellTaskbarForm.Dispose() disposes the hook to unregister it cleanly.
    //
    // Behavior rules:
    // - Win key down sets _winDown = true and clears “combo used” flag.
    // - Any other key pressed while Win is held marks _winUsedWithOtherKey = true.
    // - On Win key up:
    //     - If Win was down AND not used with another key => treat as a “tap” and fire event.
    //     - Otherwise do nothing (Win+X, Win+R, etc. should not trigger Start toggle).
    //
    // Notes / Debug tips:
    // - WH_KEYBOARD_LL does not require DLL injection; it still requires an HMODULE.
    //   This uses GetModuleHandle(current module) which is standard for LL hooks.
    // - If WinKeyTapped never fires in WinPE, verify SetWindowsHookEx succeeded and that
    //   the process stays alive with a message loop (WinForms UI thread).
    // =====================================================================

    internal sealed class KeyboardHook : IDisposable
    {
        // Low-level keyboard hook
        private const int WH_KEYBOARD_LL = 13;

        // Messages
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        // Virtual keys
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private IntPtr _hook = IntPtr.Zero;
        private HookProc _proc;

        private bool _winDown;
        private bool _winUsedWithOtherKey;

        public event Action? WinKeyTapped;

        public KeyboardHook()
        {
            _proc = HookCallback;

            // WH_KEYBOARD_LL doesn't require DLL injection; SetWindowsHookEx needs an HMODULE though.
            // Passing GetModuleHandle of current module is standard for LL hooks.
            using (var cur = Process.GetCurrentProcess())
            using (var mod = cur.MainModule)
            {
                IntPtr hMod = GetModuleHandle(mod.ModuleName);
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
            }

            if (_hook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool down = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool up = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)kbd.vkCode;

                bool isWin = (vk == VK_LWIN || vk == VK_RWIN);

                if (down)
                {
                    if (isWin)
                    {
                        _winDown = true;
                        _winUsedWithOtherKey = false;
                    }
                    else
                    {
                        // If any other key is pressed while Win is held, treat it as a combo
                        if (_winDown)
                            _winUsedWithOtherKey = true;
                    }
                }
                else if (up)
                {
                    if (isWin)
                    {
                        // Win key released: if it wasn't part of a combo, treat as a "tap"
                        if (_winDown && !_winUsedWithOtherKey)
                            WinKeyTapped?.Invoke();

                        _winDown = false;
                        _winUsedWithOtherKey = false;
                    }
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // -------- P/Invoke --------

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
