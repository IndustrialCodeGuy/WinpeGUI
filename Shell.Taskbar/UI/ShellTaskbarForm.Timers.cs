namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // =====================================================================
        //  TIMERS (CLOCK + TASK REFRESH)
        // =====================================================================
        //
        // Debug entry point:
        // - StartTimers()
        //
        // Paired files (only if something breaks):
        // - ShellTaskbarForm.BuildTaskbar.cs: _timeLabel/_dateLabel are created there.
        // - ShellTaskbarForm.Taskbar.cs: RefreshTaskButtons() is called by _refreshTimer.
        //
        // Notes:
        // - Kept isolated on purpose so it stays “out of the way”.
        //
        // =====================================================================

        #region Timers (fields)

        private readonly System.Windows.Forms.Timer _refreshTimer = new();
        private readonly System.Windows.Forms.Timer _clockTimer = new();
        private bool _timersWired;
        private bool _timersStarted;

        #endregion

        #region Timers (methods)

        private void StartTimers()
        {
            if (!_timersWired)
            {
                _clockTimer.Interval = 1000;
                _clockTimer.Tick += (s, e) => UpdateClockText(refreshMetrics: true);

                _refreshTimer.Interval = 400;
                _refreshTimer.Tick += (s, e) => RefreshTaskButtons();

                _timersWired = true;
            }

            if (_timersStarted) return;
            _timersStarted = true;

            _clockTimer.Start();
            _refreshTimer.Start();
        }

        #endregion
    }
}
