using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using System.ComponentModel;
using System.Diagnostics;

namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // Start menu construction and runtime behavior.
        // Menus are rebuilt when layout/DPI changes so cached icon sizes and
        // ToolStrip spacing stay aligned with the current taskbar metrics.

        #region Start Menu (fields)

        // ---------------- Start menu state ----------------

        private ContextMenuStrip? _startMenu;

        private ContextMenuStrip? _startItemCtx;
        private bool _startCtxLoopActive;

        private ToolStripDropDownMenu? _activeSubMenu;

        private bool _suppressNextStartOpen;
        private bool _allowPowerDropOpen;
        private bool _subMenuScopeActive;

        private int _startMenuPreferredHeightPx = -1;

        #endregion

        #region Start Menu Build

        private ContextMenuStrip BuildStartMenu()
        {
            int rootPx = GetLargeIconPxFromLayout();
            int subPx = GetSmallIconPxFromLayout();

            ContextMenuStrip menu = CreateStartMenuStrip(showImageMargin: true);

            menu.Opening += (s, e) =>
            {
                SetStartButtonMenuOpenVisual(menuOpen: true);
            };
            
            menu.Closing += (s, e) =>
            {
                if (IsSeparatorClickCloseRequest(menu, e.CloseReason))
                {
                    ResetStartMenuAfterSeparatorClick();
                    e.Cancel = true;
                    return;
                }

                if (_startCtxLoopActive)
                {
                    if (IsMouseOverAnyStartSurface())
                    {
                        e.Cancel = true;
                        return;
                    }
                }

                if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked)
                {
                    var mouse = Control.MousePosition;
                    var btnRect = _startButton.RectangleToScreen(_startButton.ClientRectangle);
                    if (btnRect.Contains(mouse))
                    {
                        _suppressNextStartOpen = true;
                        _startButton.SuppressNextPressAnimation();
                    }
                }

                SetStartButtonMenuOpenVisual(menuOpen: false);
                this.ActiveControl = null;
            };

            string taskManagerPath = ResolveStartMenuExecutablePath("taskmgr.exe");

            menu.Items.Add(MakeExeMenuItem(
                "Task Manager",
                taskManagerPath,
                null,
                Icons.FromStartPath(taskManagerPath, rootPx)));

            if (_showBitLockerManagerStartMenu)
            {
                menu.Items.Add(MakeStartMenuSeparator());

                string bitLockerManagerPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "BitLocker.Manager.exe");

                string? bitLockerManagerArgs = ShellTheme.DarkMode ? "--dark" : null;

                var bitLockerManager = new ToolStripMenuItem("BitLocker Manager")
                {
                    Image = Icons.FromSystemDll(
                        ShellOwnedWindowIcons.IconDllName,
                        ShellOwnedWindowIcons.BitLockerManagerIconIndex,
                        rootPx)
                };

                WireStartMenuItemMouse(
                    bitLockerManager,
                    onLeftClick: () => OpenBitLockerManager(),
                    buildRightClickMenu: () => BuildStartItemContextMenu(
                        "BitLocker Manager",
                        () => OpenBitLockerManager(),
                        !_isWinPE
                            ? () => LaunchProcessElevated(bitLockerManagerPath, bitLockerManagerArgs, AppContext.BaseDirectory)
                            : null));

                menu.Items.Add(bitLockerManager);
            }

            menu.Items.Add(MakeStartMenuSeparator());

            string registryEditorPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "regedit.exe");

            menu.Items.Add(MakeExeMenuItem(
                "Registry Editor",
                registryEditorPath,
                null,
                Icons.FromStartPath(registryEditorPath, rootPx)));

            menu.Items.Add(MakeStartMenuSeparator());

            string notepadPath = ResolveStartMenuExecutablePath("notepad.exe");

            menu.Items.Add(MakeExeMenuItem(
                "Notepad",
                notepadPath,
                null,
                Icons.FromStartPath(notepadPath, rootPx)));

            menu.Items.Add(MakeStartMenuSeparator());

            string commandPromptPath = ResolveStartMenuExecutablePath("cmd.exe");

            menu.Items.Add(MakeExeMenuItem(
                "Command Prompt",
                commandPromptPath,
                "/k",
                Icons.FromStartPath(commandPromptPath, rootPx)));

            string? powerShellPath = TryGetPowerShellPath();

            if (!string.IsNullOrEmpty(powerShellPath))
            {
                menu.Items.Add(MakeStartMenuSeparator());

                menu.Items.Add(MakeExeMenuItem(
                    "PowerShell",
                    powerShellPath,
                    "-NoLogo -NoExit",
                    Icons.FromStartPath(powerShellPath, rootPx)));
            }

            menu.Items.Add(MakeStartMenuSeparator());

            var fileManager = new ToolStripMenuItem("File Manager")
            {
                Image = Icons.FromSystemDll("imageres.dll", index: 3, size: rootPx)
            };

            WireStartMenuItemMouse(
                fileManager,
                onLeftClick: () => OpenFileExplorer(),
                buildRightClickMenu: () => BuildStartItemContextMenu(
                    "File Manager",
                    () => OpenFileExplorer(),
                    null));

            menu.Items.Add(fileManager);

            menu.Items.Add(MakeStartMenuSeparator());

            var power = new InvalidateOnHoverMenuItem("Power Options")
            {
                Image = Icons.FromSystemDll("shell32.dll", index: 27, size: rootPx),
            };

            ToolStripDropDownMenu powerDrop = CreateStartMenuDropDown();

            void runShutdown()
            {
                RequestShutdown();
            }

            void runReboot()
            {
                RequestReboot();
            }

            var shutdown = new ToolStripMenuItem("Shutdown")
            {
                Image = Icons.FromSystemDll("shell32.dll", index: 27, size: subPx),
                ImageScaling = ToolStripItemImageScaling.None
            };

            WireStartMenuItemMouse(
                shutdown,
                onLeftClick: runShutdown,
                buildRightClickMenu: () => BuildStartItemContextMenu("Shutdown", runShutdown, null));

            var reboot = new ToolStripMenuItem("Reboot")
            {
                Image = Icons.FromSystemDll("shell32.dll", index: 238, size: subPx),
                ImageScaling = ToolStripItemImageScaling.None
            };

            WireStartMenuItemMouse(
                reboot,
                onLeftClick: runReboot,
                buildRightClickMenu: () => BuildStartItemContextMenu("Reboot", runReboot, null));

            powerDrop.Items.Add(shutdown);
            powerDrop.Items.Add(reboot);
            power.DropDown = powerDrop;

            WireStartSubMenuRoot(power, powerDrop, () =>
            {
                _allowPowerDropOpen = true;
            });

            WireStartSubMenuDropDown(
                powerDrop,
                canOpen: () => _allowPowerDropOpen,
                onOpenAccepted: () => _allowPowerDropOpen = false);

            menu.Items.Add(power);
            return menu;
        }

        private ContextMenuStrip BuildStartItemContextMenu(string label, Action runAction, Action? runAsAdminAction = null)
        {
            ContextMenuStrip menu = CreateStartMenuStrip(showImageMargin: false);

            var miRun = new ToolStripMenuItem($"Run {label}")
            {
                Tag = runAction
            };
            menu.Items.Add(miRun);

            if (runAsAdminAction != null)
            {
                menu.Items.Add(new ToolStripSeparator());

                var miAdmin = new ToolStripMenuItem("Run as Administrator")
                {
                    Tag = runAsAdminAction
                };
                menu.Items.Add(miAdmin);
            }

            return menu;
        }

        private ToolStripMenuItem MakeExeMenuItem(
            string text,
            string exe,
            string? args,
            Image? image,
            Func<ContextMenuStrip>? buildRightClickMenu = null)
        {
            string fullPath = ResolveStartMenuExecutablePath(exe);

            var item = new ToolStripMenuItem(text)
            {
                Image = image
            };

            WireStartMenuItemMouse(
                item,
                onLeftClick: () => LaunchProcess(fullPath, args),
                buildRightClickMenu: buildRightClickMenu ?? (() =>
                {
                    bool isExe = string.Equals(
                        Path.GetExtension(fullPath),
                        ".exe",
                        StringComparison.OrdinalIgnoreCase);

                    return BuildStartItemContextMenu(
                        text,
                        () => LaunchProcess(fullPath, args),
                        (!_isWinPE && isExe) ? () => LaunchProcessElevated(fullPath, args) : null);
                }));

            return item;
        }

        private static string ResolveStartMenuExecutablePath(string exe)
        {
            if (string.IsNullOrWhiteSpace(exe))
                return string.Empty;

            if (Path.IsPathRooted(exe))
                return exe;

            string systemPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                exe);

            return File.Exists(systemPath) ? systemPath : exe;
        }

        private static string? TryGetPowerShellPath()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);

            string[] candidates =
            {
                Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                Path.Combine(windows, "Sysnative", "WindowsPowerShell", "v1.0", "powershell.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private void LaunchProcess(string exe, string? args)
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            LaunchProcess(exe, args, sys);
        }

        private void LaunchProcess(string exe, string? args, string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args ?? "")
                {
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDir
                };

                Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                ShowProcessLaunchMessage(
                    "Elevation required",
                    "This action requires elevation.\n\nRight-click the item and choose “Run as Administrator”.",
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowProcessLaunchFailure(exe, ex);
            }
        }

        private void LaunchProcessElevated(string exe, string? args, string? workingDir = null)
        {
            if (_isWinPE)
            {
                LaunchProcess(exe, args, workingDir ?? Environment.GetFolderPath(Environment.SpecialFolder.System));
                return;
            }

            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);

                var psi = new ProcessStartInfo(exe, args ?? "")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? sys : workingDir
                };

                Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Debug.WriteLine(ex);
            }
            catch (Exception ex)
            {
                ShowProcessLaunchFailure(exe, ex);
            }

        }

        private void ShowProcessLaunchFailure(string exe, Exception ex)
        {
            Debug.WriteLine(ex);

            ShowProcessLaunchMessage(
                "Unable to start program",
                $"Unable to start:\n{exe}\n\n{ex.Message}",
                MessageBoxIcon.Error);
        }


        private void ShowStartMenuActionFailure(string commandText, Exception ex)
        {
            Debug.WriteLine(ex);

            ShowProcessLaunchMessage(
                "Unable to run command",
                $"Unable to run:\n{commandText}\n\n{ex.Message}",
                MessageBoxIcon.Error);
        }

        private void ShowProcessLaunchMessage(string title, string message, MessageBoxIcon icon)
        {
            try
            {
                MessageBox.Show(
                    this,
                    message,
                    title,
                    MessageBoxButtons.OK,
                    icon);
            }
            catch
            {
            }
        }

        private void ShowStartMenu()
        {
            if (_startMenu == null) return;

            ClearFocusedAppState();
            PrepareStartMenuForDisplay();

            if (_startMenuPreferredHeightPx <= 0)
            {
                _startMenu.PerformLayout();
                _startMenuPreferredHeightPx = _startMenu.GetPreferredSize(Size.Empty).Height;
            }

            int menuH = _startMenuPreferredHeightPx;

            var pt = _taskbar.PointToScreen(new Point(_startButton.Left, _startButton.Top));
            pt.Y -= menuH + 10;

            var work = Screen.FromControl(this).WorkingArea;
            if (pt.Y < work.Top)
            {
                pt = _taskbar.PointToScreen(new Point(_startButton.Left, _startButton.Bottom + 10));
            }

            _startMenu.Show(pt);
        }

        #endregion

        #region Start Menu Runtime

        private void ToggleStartMenu()
        {
            if (_startMenu == null) return;

            if (_startMenu.Visible)
                _startMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
            else
                ShowStartMenu();
        }

        private void WireStartMenuItemMouse(ToolStripMenuItem item, Action onLeftClick, Func<ContextMenuStrip>? buildRightClickMenu)
        {
            if (buildRightClickMenu != null)
                item.Tag = new StartItemCtxInfo { Factory = buildRightClickMenu };
            else
                item.Tag = null;

            item.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _startMenu == null || _startMenu.IsDisposed)
                    return;

                if (!_startCtxLoopActive)
                {
                    _startCtxLoopActive = true;
                }

                StartMenuRightClickVisualLock(item);

                BeginInvoke(new Action(() =>
                {
                    if (_startMenu == null || _startMenu.IsDisposed)
                        return;

                    var ctx = buildRightClickMenu?.Invoke();
                    if (ctx == null)
                        return;

                    ReplaceStartItemCtx(ctx);

                    if (_startItemCtx == null) return;
                    WireItemContextMenu(_startItemCtx);
                    PrepareStartItemContextMenuForDisplay(_startItemCtx);

                    if (!TryGetCtxOwnerAndPoint(out var owner, out var pt))
                        return;

                    _startItemCtx.Show(owner, pt);

                }));
            };

            item.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || _startMenu == null || _startMenu.IsDisposed)
                    return;

                if (_startCtxLoopActive)
                {
                    EndStartCtxLoop();

                    BeginInvoke(new Action(() =>
                    {
                        if (_activeSubMenu != null && !_activeSubMenu.IsDisposed)
                        {
                            AllowAllSubMenuDropsOnce();

                            try { _startMenu.Focus(); } catch { }

                            if (!_activeSubMenu.Visible)
                            {
                                PrepareStartSubMenuForDisplay(_activeSubMenu);
                                try { _activeSubMenu.Show(); } catch { }
                            }
                            else
                            {
                                try { _activeSubMenu.Focus(); } catch { }
                            }

                            ClearAllSubMenuDropAllowFlags();
                        }
                        else if (_startMenu != null && !_startMenu.IsDisposed)
                        {
                            PrepareStartMenuForDisplay();
                            try { _startMenu.Show(); } catch { }
                        }
                    }));

                    return;
                }

                try
                {
                    onLeftClick?.Invoke();
                }
                catch (Exception ex)
                {
                    ShowStartMenuActionFailure(item.Text, ex);
                }
            };
        }

        private void WireStartSubMenuRoot(ToolStripMenuItem item, ToolStripDropDownMenu dropDown, Action prepareToOpen)
        {
            item.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                    return;

                if (_startCtxLoopActive)
                {
                    EndStartCtxLoop();
                    PrepareStartMenuForDisplay();
                    _startMenu?.Show();
                    return;
                }

                if (dropDown.Visible)
                {
                    dropDown.Close(ToolStripDropDownCloseReason.CloseCalled);
                    return;
                }

                prepareToOpen?.Invoke();
                SetStartMenuVisualDisabled(true);
                PrepareStartSubMenuForDisplay(dropDown);
                item.ShowDropDown();
            };
        }

        private void WireStartSubMenuDropDown(
            ToolStripDropDownMenu dropDown,
            Func<bool> canOpen,
            Action onOpenAccepted)
        {
            dropDown.Opening += (s, e) =>
            {
                if (!canOpen())
                {
                    e.Cancel = true;
                    return;
                }

                onOpenAccepted?.Invoke();
                EnterSubMenuScope(dropDown);
            };

            dropDown.Closing += (s, e) =>
            {
                if (IsSeparatorClickCloseRequest(dropDown, e.CloseReason))
                {
                    ResetStartMenuAfterSeparatorClick();
                    e.Cancel = true;
                    return;
                }

                if (_startCtxLoopActive && IsMouseOverAnyStartSurface())
                {
                    e.Cancel = true;
                    return;
                }
            };

            dropDown.Closed += (s, e) =>
            {
                ExitSubMenuScope(dropDown);
                SetStartMenuVisualDisabled(false);
            };
        }

        private void AllowAllSubMenuDropsOnce()
        {
            _allowPowerDropOpen = true;
        }

        private void ClearAllSubMenuDropAllowFlags()
        {
            _allowPowerDropOpen = false;
        }

        private void EnterSubMenuScope(ToolStripDropDownMenu sub)
        {
            if (_startMenu == null || _startMenu.IsDisposed) return;
            if (sub == null || sub.IsDisposed) return;

            if (_subMenuScopeActive && ReferenceEquals(_activeSubMenu, sub))
                return;

            if (_subMenuScopeActive && _activeSubMenu != null && !_activeSubMenu.IsDisposed &&
                !ReferenceEquals(_activeSubMenu, sub))
            {
                try { _activeSubMenu.Close(ToolStripDropDownCloseReason.CloseCalled); } catch { }
            }

            _subMenuScopeActive = true;
            _activeSubMenu = sub;
        }

        private void ExitSubMenuScope(ToolStripDropDownMenu sub)
        {
            if (!_subMenuScopeActive)
                return;

            if (sub != null && !ReferenceEquals(_activeSubMenu, sub))
                return;

            _subMenuScopeActive = false;
            _activeSubMenu = null;
        }

        private void WireItemContextMenu(ContextMenuStrip ctx)
        {
            if (ctx == null || ctx.Tag is bool) return;
            ctx.Tag = true;
            var _ctxLastMouseButton = MouseButtons.None;

            ctx.MouseDown += (s, e) => _ctxLastMouseButton = e.Button;

            ctx.Closing += (s, e) =>
            {
                if (IsSeparatorClickCloseRequest(ctx, e.CloseReason))
                {
                    e.Cancel = true;
                    return;
                }
            };

            ctx.ItemClicked += (s, e) =>
            {
                if (e.ClickedItem is not ToolStripMenuItem mi)
                    return;

                if (_ctxLastMouseButton == MouseButtons.Right)
                    return;

                EndStartCtxLoop();

                if (mi.Tag is Action a)
                {
                    try
                    {
                        a();
                    }
                    catch (Exception ex)
                    {
                        ShowStartMenuActionFailure(mi.Text, ex);
                    }
                }
            };

            foreach (ToolStripItem tsi in ctx.Items)
            {
                if (tsi is not ToolStripMenuItem mi)
                    continue;

                mi.MouseDown += (s, e) => _ctxLastMouseButton = e.Button;

                mi.MouseUp += (s, e) =>
                {
                    if (e.Button != MouseButtons.Right)
                        return;

                    BeginInvoke(new Action(() =>
                    {
                        if (_startMenu == null || _startMenu.IsDisposed)
                            return;

                        var hoveredItem = GetStartSurfaceItemUnderMouse();
                        var factory = GetCtxFactoryFromItem(hoveredItem);
                        if (factory == null)
                            return;

                        StartMenuRightClickVisualLock(hoveredItem);

                        ReplaceStartItemCtx(factory.Invoke());
                        if (_startItemCtx == null) return;
                        WireItemContextMenu(_startItemCtx);
                        PrepareStartItemContextMenuForDisplay(_startItemCtx);

                        if (!TryGetCtxOwnerAndPoint(out var owner, out var pt))
                            return;

                        _startItemCtx.Show(owner, pt);
                    }));
                };
            }

            ctx.Closed += (s, e) =>
            {
                if (ReferenceEquals(_startItemCtx, ctx))
                    _startItemCtx = null;

                if (_startMenu == null || _startMenu.IsDisposed)
                {
                    EndStartCtxLoop();
                    return;
                }

                if (IsMouseOverAnyStartSurface() && _startCtxLoopActive)
                    return;

                EndStartCtxLoop();

                if (_startButton != null)
                {
                    var mouse = Control.MousePosition;
                    var btnRect = _startButton.RectangleToScreen(_startButton.ClientRectangle);
                    if (btnRect.Contains(mouse))
                        return;
                }

                BeginInvoke(new Action(() =>
                {
                    try { _activeSubMenu?.Close(ToolStripDropDownCloseReason.CloseCalled); } catch { }
                    try { _startMenu?.Close(ToolStripDropDownCloseReason.CloseCalled); } catch { }
                }));
            };
        }

        private sealed class StartItemCtxInfo
        {
            public Func<ContextMenuStrip>? Factory;
        }

        private Func<ContextMenuStrip>? GetCtxFactoryFromItem(ToolStripItem? item)
        {
            if (item == null) return null;

            if (item.Tag is StartItemCtxInfo info && info.Factory != null)
                return info.Factory;

            return null;
        }

        private void EndStartCtxLoop()
        {
            _startCtxLoopActive = false;
            ClearAllSubMenuDropAllowFlags();

            if (_startMenu != null && !_startMenu.IsDisposed)
                SetStartMenuVisualDisabled(false);
        }

        private void CloseStartSurfaces()
        {
            // Close / dispose the right-click context menu first
            ReplaceStartItemCtx(null);

            // Close dropdowns safely
            try { _activeSubMenu?.Close(ToolStripDropDownCloseReason.CloseCalled); } catch { }
            try { _startMenu?.Close(ToolStripDropDownCloseReason.CloseCalled); } catch { }

            // Reset loop/scope flags
            _startCtxLoopActive = false;
            ClearAllSubMenuDropAllowFlags();
            _subMenuScopeActive = false;
            _activeSubMenu = null;
            _suppressNextStartOpen = false;

            SetStartButtonMenuOpenVisual(menuOpen: false);
        }

        private void DisposeStartMenu()
        {
            CloseStartSurfaces();

            try
            {
                if (_startMenu != null && !_startMenu.IsDisposed)
                    DetachToolStripImages(_startMenu.Items);
            }
            catch { }

            try { _startMenu?.Dispose(); } catch { }
            _startMenu = null;
            _startMenuPreferredHeightPx = -1;
        }

        private void ReplaceStartItemCtx(ContextMenuStrip? next)
        {
            var old = _startItemCtx;
            _startItemCtx = next;

            if (old != null && !ReferenceEquals(old, next))
            {
                try { old.Close(); } catch { }
                try { old.Dispose(); } catch { }
            }
        }

        #endregion

        #region Start Menu Helpers (visuals + mouse)

        private static void SetItemForeColor(ToolStripItem it, Color c)
        {
            if (it is ToolStripMenuItem mi) mi.ForeColor = c;
        }

        private void SetStartButtonMenuOpenVisual(bool menuOpen)
        {
            if (_startButton == null || _startButton.IsDisposed)
                return;

            if (!menuOpen)
                _startButton.SuppressNextPressAnimation();

            _startButton.SetPressedVisualLocked(menuOpen);
        }


        private void SetStartMenuVisualDisabled(bool disabled)
        {
            if (_startMenu == null || _startMenu.IsDisposed)
                return;

            var color = disabled ? SystemColors.GrayText : SystemColors.ControlText;

            foreach (ToolStripItem it in _startMenu.Items)
                SetItemForeColor(it, color);

            if (_activeSubMenu != null && !_activeSubMenu.IsDisposed)
                foreach (ToolStripItem it in _activeSubMenu.Items)
                    SetItemForeColor(it, color);
        }

        private void StartMenuRightClickVisualLock(ToolStripItem? clickedItem)
        {
            SetStartMenuVisualDisabled(true);

            if (clickedItem != null)
                SetItemForeColor(clickedItem, SystemColors.ControlText);
        }

        private ToolStripSeparator MakeStartMenuSeparator()
        {
            var separator = new ToolStripSeparator();

            separator.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                    ResetStartMenuAfterSeparatorClick();
            };

            return separator;
        }

        private void ResetStartMenuAfterSeparatorClick()
        {
            bool rearmStartSurface = _startCtxLoopActive || _startItemCtx != null;

            ReplaceStartItemCtx(null);
            EndStartCtxLoop();
            ActiveControl = null;

            if (_startMenu != null && !_startMenu.IsDisposed)
                _startMenu.Invalidate();

            if (_activeSubMenu != null && !_activeSubMenu.IsDisposed)
                _activeSubMenu.Invalidate();

            if (rearmStartSurface)
                BeginInvoke(new Action(RearmStartMenuAfterSeparatorClick));
        }

        private void RearmStartMenuAfterSeparatorClick()
        {
            if (_startMenu == null || _startMenu.IsDisposed || !_startMenu.Visible)
                return;

            if (_activeSubMenu != null && !_activeSubMenu.IsDisposed && _activeSubMenu.Visible)
            {
                AllowAllSubMenuDropsOnce();

                try { _startMenu.Focus(); } catch { }

                PrepareStartSubMenuForDisplay(_activeSubMenu);
                try { _activeSubMenu.Show(); } catch { }
                try { _activeSubMenu.Focus(); } catch { }

                ClearAllSubMenuDropAllowFlags();
                return;
            }

            PrepareStartMenuForDisplay();
            try { _startMenu.Show(); } catch { }
            try { _startMenu.Focus(); } catch { }
        }

        private static bool IsSeparatorClickCloseRequest(ToolStripDropDown menu, ToolStripDropDownCloseReason closeReason)
        {
            if (closeReason != ToolStripDropDownCloseReason.ItemClicked &&
                closeReason != ToolStripDropDownCloseReason.AppClicked)
            {
                return false;
            }

            if (menu == null || menu.IsDisposed || !menu.Visible)
                return false;

            var p = Control.MousePosition;
            if (!menu.Bounds.Contains(p))
                return false;

            return menu.GetItemAt(menu.PointToClient(p)) is ToolStripSeparator;
        }

        private bool IsMouseOverAnyStartSurface()
        {
            var p = Control.MousePosition;

            if (_activeSubMenu != null && !_activeSubMenu.IsDisposed && _activeSubMenu.Visible && _activeSubMenu.Bounds.Contains(p))
                return true;

            if (_startMenu != null && !_startMenu.IsDisposed && _startMenu.Visible && _startMenu.Bounds.Contains(p))
                return true;

            return false;
        }

        private ToolStripItem? GetStartSurfaceItemUnderMouse()
        {
            var p = Control.MousePosition;

            if (_activeSubMenu != null && !_activeSubMenu.IsDisposed && _activeSubMenu.Visible)
            {
                var client = _activeSubMenu.PointToClient(p);
                var it = _activeSubMenu.GetItemAt(client);
                if (it != null) return it;
            }

            if (_startMenu != null && !_startMenu.IsDisposed && _startMenu.Visible)
            {
                var client = _startMenu.PointToClient(p);
                var it = _startMenu.GetItemAt(client);
                if (it != null) return it;
            }

            return null;
        }

        private bool TryGetCtxOwnerAndPoint([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ToolStrip? owner, out Point ownerClientPoint)
        {
            owner = null;
            ownerClientPoint = default;

            var p = Control.MousePosition;

            if (_activeSubMenu != null && !_activeSubMenu.IsDisposed && _activeSubMenu.Visible &&
                _activeSubMenu.Bounds.Contains(p))
            {
                owner = _activeSubMenu;
                ownerClientPoint = owner.PointToClient(p);
                return true;
            }

            if (_startMenu != null && !_startMenu.IsDisposed)
            {
                owner = _startMenu;
                ownerClientPoint = owner.PointToClient(p);
                return true;
            }

            return false;
        }

        #endregion

        #region ToolStrip Helpers (image detach + sizing)

        private static void DetachToolStripImages(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                if (it is ToolStripMenuItem mi)
                {
                    if (mi.DropDownItems.Count > 0)
                        DetachToolStripImages(mi.DropDownItems);

                    mi.Image = null;
                }
                else
                {
                    it.Image = null;
                }
            }
        }

        private ContextMenuStrip CreateStartMenuStrip(bool showImageMargin)
        {
            return new ContextMenuStrip
            {
                ShowImageMargin = showImageMargin,
                AutoSize = true
            };
        }

        private ToolStripDropDownMenu CreateStartMenuDropDown()
        {
            return new ToolStripDropDownMenu
            {
                ShowImageMargin = true,
                AutoSize = true
            };
        }


        private int GetStartMenuItemMinHeightPx(int iconPx, int verticalPadPx, Font font)
        {
            int textHeight = TextRenderer.MeasureText(
                "Ag",
                font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;

            int padY = Math.Max(0, verticalPadPx);
            return Math.Max(iconPx, textHeight) + (padY * 2);
        }

        private void ApplyStartMenuItemSizing(
            ToolStripItem item,
            int minHeightPx,
            bool forceImageScalingNone,
            Font font)
        {
            item.Font = font;

            if (forceImageScalingNone)
                item.ImageScaling = ToolStripItemImageScaling.None;

            if (item is ToolStripMenuItem mi)
            {
                mi.AutoSize = true;

                if (mi.Height != minHeightPx)
                    mi.Height = minHeightPx;
            }
        }

        private void PrepareStartMenuForDisplay()
        {
            if (_startMenu == null || _startMenu.IsDisposed)
                return;

            int rootPx = GetLargeIconPxFromLayout();

            EnsureToolStripHandle(_startMenu);
            _startMenu.SuspendLayout();
            try
            {
                _startMenu.Font = _startMenuFont;
                _startMenu.ImageScalingSize = new Size(rootPx, rootPx);
                ForceRootMenuSizing(_startMenu, rootPx);

                foreach (ToolStripItem item in _startMenu.Items)
                {
                    if (item is ToolStripMenuItem mi && mi.DropDown is ToolStripDropDownMenu dropDown)
                        PrepareStartSubMenuForDisplay(dropDown);
                }
            }
            finally
            {
                _startMenu.ResumeLayout(true);
            }

            _startMenu.PerformLayout();
            _startMenu.Invalidate();
        }

        private void PrepareStartSubMenuForDisplay(ToolStripDropDownMenu dropDown)
        {
            if (dropDown.IsDisposed)
                return;

            int subPx = GetSmallIconPxFromLayout();

            EnsureToolStripHandle(dropDown);
            dropDown.SuspendLayout();
            try
            {
                dropDown.Font = _startSubMenuFont;
                dropDown.ImageScalingSize = new Size(subPx, subPx);
                ForceSubMenuSizing(dropDown, subPx);
            }
            finally
            {
                dropDown.ResumeLayout(true);
            }

            dropDown.PerformLayout();
            dropDown.Invalidate();
        }

        private void PrepareStartItemContextMenuForDisplay(ContextMenuStrip menu)
        {
            if (menu.IsDisposed)
                return;

            EnsureToolStripHandle(menu);
            menu.SuspendLayout();
            try
            {
                menu.Font = _startSubMenuFont;
                ForceContextMenuSizing(menu);
            }
            finally
            {
                menu.ResumeLayout(true);
            }

            menu.PerformLayout();
            menu.Invalidate();
        }

        private static void EnsureToolStripHandle(ToolStrip toolStrip)
        {
            if (toolStrip.IsDisposed || toolStrip.IsHandleCreated)
                return;

            toolStrip.CreateControl();
            _ = toolStrip.Handle;
        }

        private void ForceRootMenuSizing(ContextMenuStrip menu, int rootIconPx)
        {
            int minHeightPx = GetStartMenuItemMinHeightPx(rootIconPx, Scale(3), _startMenuFont);

            foreach (ToolStripItem item in menu.Items)
                ApplyStartMenuItemSizing(item, minHeightPx, forceImageScalingNone: true, _startMenuFont);
        }

        private void ForceSubMenuSizing(ToolStripDropDownMenu drop, int subIconPx)
        {
            int minHeightPx = GetStartMenuItemMinHeightPx(subIconPx, Scale(2), _startSubMenuFont);

            foreach (ToolStripItem item in drop.Items)
                ApplyStartMenuItemSizing(item, minHeightPx, forceImageScalingNone: true, _startSubMenuFont);
        }

        private void ForceContextMenuSizing(ContextMenuStrip menu)
        {
            int minHeightPx = GetStartMenuItemMinHeightPx(iconPx: 0, verticalPadPx: Scale(4), _startSubMenuFont);

            foreach (ToolStripItem item in menu.Items)
                ApplyStartMenuItemSizing(item, minHeightPx, forceImageScalingNone: false, _startSubMenuFont);
        }

        #endregion
    }
}
