using InfinLimit.Interception.Modules;
using InfinLimit.Utility;
using InfinLimit.Windows;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InfinLimit.Interception
{
    // Toggle macro for the 3074 DL motes farming timing.
    // First trigger: saves Buffer + AutoResync, disables both, enables 3074 DL inbound.
    // Second trigger (or when DL inbound turns off by any means): restores saved settings.
    public static class MotesModule
    {
        private static bool _active = false;
        private static bool _savedBuffer;
        private static bool _savedAutoResync;
        private static CancellationTokenSource _cts;

        public static bool IsActive => _active;

        public static void Hook()
        {
            KeyListener.KeysPressed += OnKeysPressed;
        }

        private static void OnKeysPressed(LinkedList<Keycode> keycodes)
        {
            var bind = Config.Instance.Settings.ZaqlMotes_Keybind;
            if (bind.Count == 0) return;
            if (Config.Instance.Settings.AltTabSupressKeybinds && !OverlayWindow.CheckGameFocus()) return;
            if (bind.Count != keycodes.Count) return;
            foreach (var k in bind)
                if (!keycodes.Contains(k)) return;

            Task.Run(Run);
        }

        public static async Task Run()
        {
            var pve = InterceptionManager.GetModule("PVE") as PveModule;
            if (pve == null || !pve.IsEnabled)
            {
                _cts?.Cancel();
                _active = false;
                return;
            }

            try
            {
                if (!_active)
                {
                    _savedBuffer    = PveModule.Buffer;
                    _savedAutoResync = PveModule.AutoResync;
                    _active = true;
                    _cts?.Cancel();
                    _cts = new CancellationTokenSource();
                    StartMonitor(_cts.Token);
                    await MainWindow.Instance.Dispatcher.InvokeAsync(() =>
                    {
                        PveModule.Buffer    = false;
                        PveModule.AutoResync = false;
                        pve.ToggleSwitch(ref PveModule.Inbound, true);
                    });
                }
                else
                {
                    _cts?.Cancel();
                    _active = false;
                    await MainWindow.Instance.Dispatcher.InvokeAsync(() =>
                    {
                        pve.ToggleSwitch(ref PveModule.Inbound, false);
                        PveModule.Buffer    = _savedBuffer;
                        PveModule.AutoResync = _savedAutoResync;
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MotesModule");
                _cts?.Cancel();
                _active = false;
            }
        }

        private static void StartMonitor(CancellationToken token)
        {
            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(100, token);
                        if (_active && !PveModule.Inbound)
                        {
                            _active = false;
                            await MainWindow.Instance.Dispatcher.InvokeAsync(() =>
                            {
                                PveModule.Buffer    = _savedBuffer;
                                PveModule.AutoResync = _savedAutoResync;
                            });
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
    }
}
