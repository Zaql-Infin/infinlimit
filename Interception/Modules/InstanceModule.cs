using InfinLimit.Models;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace InfinLimit.Interception.Modules
{
    public class InstanceModule : PacketModuleBase
    {
        PacketProviderBase provider;
        public InstanceModule() : base("З0K", true, InterceptionManager.GetProvider("30000"))
        {
            Icon = System.Windows.Application.Current.FindResource("Traveller") as Geometry;
            Description = @"Blocks inbound 30k updates with optional rate limiting";
            provider = PacketProviders.First();
            Buffer = Config.GetNamed(Name).GetSettings<bool>("Buffer");
            
            // Initialize rate limiting settings
            RateLimitingEnabled = Config.GetNamed(Name).GetSettings<bool>("RateLimitingEnabled");
            TargetBytesPerSecond = Config.GetNamed(Name).GetSettings<long>("TargetBytesPerSecond");
            if (TargetBytesPerSecond == 0) TargetBytesPerSecond = 100;
            
            // Initialize packet release timer (fires every 10ms for smooth packet release)
            packetReleaseTimer = new Timer(ProcessPacketQueue, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
        }

        public override void Toggle()
        {
            IsActivated = !IsActivated;
            if (!IsActivated)
            {
                while (packetQueue.TryDequeue(out var _)) { }
                lock (rateLimitLock)
                {
                    totalBitsTransferred = 0;
                    lastPacketTime = DateTime.Now;
                }
                
                Task.Run(async () =>
                {
                    foreach (var addr in TcpReordering.Cache.Keys.Where(x => x.Contains(":300")).ToArray())
                    {
                        try
                        {
                            var send = TcpReordering.Cache[addr].Location[FlagType.Remote].Blocked.ToArray();
                            TcpReordering.Cache[addr].Location[FlagType.Remote].Blocked.Clear();

                            if (!send.Any()) continue;

                            foreach (var p in send)
                            {
                                p.CreatedAt = DateTime.Now;
                                p.Delayed = false;
                                p.AckNum = 0; // let storepacket assign highest
                                p.SourceProvider.StorePacket(p);
                                if ((Buffer || ForceBufferOn) && !p.Flags.HasFlag(TcpFlags.FIN) && !p.Flags.HasFlag(TcpFlags.RST)) await p.SourceProvider.SendPacket(p, true);

                                Logger.Debug($"{Name}: Seq dist {TcpReordering.Cache[addr].Location[FlagType.Remote].HighSeq - p.SeqNum}");
                            }

                            Logger.Debug($"{Name}: Sent {send.Length} on {addr}");

                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "at 30k");
                        }
                    }
                });
            }
            else
            {
                lock (rateLimitLock)
                {
                    totalBitsTransferred = 0;
                    lastPacketTime = DateTime.Now;
                }
            }
        }

        private void ProcessPacketQueue(object state)
        {
            if (!RateLimitingEnabled || ForceRateLimitOff || isProcessingQueue || !IsActivated)
                return;

            isProcessingQueue = true;
            try
            {
                var now = DateTime.Now;
                lock (rateLimitLock)
                {
                    if ((now - lastPacketTime).TotalSeconds >= 1.0)
                    {
                        totalBitsTransferred = 0;
                        lastPacketTime = now;
                    }

                    if (TargetBytesPerSecond <= 0) TargetBytesPerSecond = 100;

                    Packet peek;
                    while (packetQueue.TryPeek(out peek)
                        && (totalBitsTransferred + peek.Length <= TargetBytesPerSecond || totalBitsTransferred <= 0))
                    {
                        if (!packetQueue.TryDequeue(out Packet packet)) break;
                        totalBitsTransferred += packet.Length;
                        double delayFraction = (double)packet.Length / TargetBytesPerSecond;
                        try
                        {
                            packet.CreatedAt = DateTime.Now;
                            packet.Delayed = true;
                            packet.AckNum = 0;
                            packet.SourceProvider.StorePacket(packet);
                            packet.SourceProvider.DelayPacket(packet, TimeSpan.FromSeconds(delayFraction));
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Rate limiting packet processing");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "ProcessPacketQueue");
            }
            finally
            {
                isProcessingQueue = false;
            }
        }

        public static bool Buffer;
        public static bool ForceBufferOn;
        public static bool RateLimitingEnabled;
        public static bool ForceRateLimitOff;
        public static long TargetBytesPerSecond;
        
        // Rate limiting tracking variables
        private DateTime lastPacketTime = DateTime.Now;
        private long totalBitsTransferred = 0; // tracks bytes per second window
        private readonly object rateLimitLock = new object();
        
        // Packet queue for smooth rate limiting
        private readonly ConcurrentQueue<Packet> packetQueue = new ConcurrentQueue<Packet>();
        private readonly Timer packetReleaseTimer;
        private volatile bool isProcessingQueue = false;

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;

            if (!IsActivated) return true;

            if (p.Outbound || p.Length == 0) return true;

            // 73-byte packets are keepalives — always pass through
            if (p.Length == 73) return true;

            // If rate limiting is enabled, queue the packet for controlled release
            if (RateLimitingEnabled && !ForceRateLimitOff)
            {
                packetQueue.Enqueue(p);
                return false;
            }

            return false;
        }
        
        public override void StopListening()
        {
            // Clean up rate limiting resources when stopping
            packetReleaseTimer?.Dispose();
            
            // Clear any remaining packets in the queue
            while (packetQueue.TryDequeue(out var _)) { }
            
            base.StopListening();
        }
    }
}
