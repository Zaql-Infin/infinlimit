using InfinLimit.Models;
using InfinLimit.Utility;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace InfinLimit.Interception.Modules
{
    public class PvpModule : PacketModuleBase
    {
        PacketProviderBase players;
        public PvpModule() : base("PVP", true, InterceptionManager.GetProvider("Players"))
        {
            players = PacketProviders.First();
            Icon = System.Windows.Application.Current.FindResource("MeleeIcon") as Geometry;
            Description =
@"Block players updates [27K]
Can cause error code if used for too long";

            KeyListener.KeysPressed += OutboundHandler;

            AutoResync = Config.GetNamed(Name).GetSettings<bool>("AutoResync");
            Buffer = Config.GetNamed(Name).GetSettings<bool>("Buffer");
            OutboundKeybind.AddRange(Config.GetNamed(Name).GetSettings<List<Keycode>>("OutboundKeybind"));
        }

        public override void StartListening()
        {
            base.StartListening();
            if (Inbound || Outbound)
            {
                StartTime = DateTime.Now;
                IsActivated = true;
            }
        }
        public override void StopListening()
        {
            IsActivated = false;
            base.StopListening();
        }

        public override void Toggle()
        {
            ToggleSwitch(ref Inbound);
        }

        public static bool Inbound = false;
        public static bool Outbound = false;
        public static bool AutoResync;
        public static bool Buffer;
        public void ToggleSwitch(ref bool target, bool? enable = null)
        {
            var currentIn = Inbound;
            var currentOut = Outbound;

            var old = target;
            target = enable ?? !target;
            if (old != target)
            {
                if (target && !IsActivated)
                {
                    IsActivated = true;
                    StartTime = DateTime.Now;
                    breatheConnections.Clear();
                }
                else if (!Outbound && !Inbound && IsActivated)
                {
                    if (Buffer)
                    {
                        try
                        {
                            foreach (var addr in players.Connections.Where(x => x.Value.Any() && DateTime.Now - x.Value.Last().CreatedAt < TimeSpan.FromSeconds(5))
                                                                    .Select(x => x.Key).Distinct().ToArray())
                            {
                                players.ClearDelayQueue(addr, true, 10);
                            }
                        }
                        catch { }
                    }
                    players.ClearDelayQueue();

                    IsActivated = false;
                    StartTime = DateTime.Now;
                }
                 

                var save = target;
                MainWindow.Instance.Dispatcher.BeginInvoke(() =>
                {
                    (save ? EnableSound : DisableSound).Play();
                    MainWindow.Instance.PvpInCB.SetState(Inbound);
                    MainWindow.Instance.PvpOutCB.SetState(Outbound);
                });
            }
        }


        public static List<Keycode> OutboundKeybind = new List<Keycode>();
        private void OutboundHandler(LinkedList<Keycode> keycodes)
        {
            if (!KeybindChecks()) return;

            if (!OutboundKeybind.Any() || keycodes.Count < OutboundKeybind.Count) return;

            if (OutboundKeybind.All(x => keycodes.Contains(x)))
            {
                ToggleSwitch(ref Outbound);
            }
        }


        Dictionary<string, DateTime> breatheConnections = new();
        void DisableBreathe(string key)
        {
            var time = breatheConnections[key];
            foreach (var k in breatheConnections.Keys.ToArray())
            {
                var duration = (time - breatheConnections[k]).Duration();
                if (duration < TimeSpan.FromSeconds(0.005))
                {
                    Logger.Debug($"{Name}: {k} Finish");
                    breatheConnections.Remove(k);
                }
            }
        }

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;

            if (!IsActivated) return true;

            var addr = p.RemoteAddress;
            var con = players.Connections[addr];

            if (con.IsReadonlyPlayerConnection()) return true;

            // TODO: Move breathing to provider level

            bool breathe = breatheConnections.TryGetValue(addr, out var time) && AutoResync;
            if (breathe && DateTime.Now - time > TimeSpan.FromSeconds(5))
            {
                breathe = false;
                DisableBreathe(addr);
            }

            if (breathe)
            {
                if (p.Outbound && p.Length > 1175 && p.Length < 1195) // 1185
                {
                    DisableBreathe(addr);
                    if (!Outbound)
                    {
                        if (Buffer) players.DelayPacket(p, TimeSpan.FromMinutes(30));
                        return false;
                    }
                }

                if (p.Inbound && p.Length > 1100 && p.Length < 1300)
                {
                    DisableBreathe(addr);
                    if (!Inbound)
                    {
                        if (Buffer) players.DelayPacket(p, TimeSpan.FromMinutes(30));
                        return false;
                    }
                }

                return true;
            }



            var result = false;
            if (p.Outbound)
            {
                if (!breathe && p.Length == 1300)
                {
                    players.ClearDelayQueue(p.RemoteAddress);
                    breatheConnections[addr] = DateTime.Now;
                    Logger.Debug($"{Name}: {addr} Start");
                    result = true;
                }

                result = !Outbound;
            }
            else
            {
                result = !Inbound;
            }

            if (!result && Buffer)
                players.DelayPacket(p, TimeSpan.FromMinutes(30));

            //if (!result && Buffer && p.Inbound)
            //{
            //    var con1 = con.Where(x => DateTime.Now - x.CreatedAt < TimeSpan.FromSeconds(2)).ToArray();
            //    var InTickrate = con1.Where(x => x.Inbound && !p.Delayed).Count() / 2d; // 35
            //    var OutTickrate = con1.Where(x => x.Outbound && !p.Delayed).Count() / 2d; // 35

            //    // 1000 / current tickrate = delay
            //    // 1000 / target tickrate = target delay
            //    // delay = target delay - delay

            //    var TargetTickrate = 15;
            //    if (TargetTickrate < InTickrate)
            //    {
            //        var InDelay = 1000 / InTickrate;
            //        var TargetDelay = 1000 / TargetTickrate;

            //        players.DelayPacket(p, TimeSpan.FromMilliseconds(Math.Max(TargetDelay - InDelay, 0)), true, true);

            //        Logger.Debug($"{Name}: DL:{InTickrate} UL:{OutTickrate}");
            //    }
            //    else return true;
            //}   

            return result;
        }
    }
}