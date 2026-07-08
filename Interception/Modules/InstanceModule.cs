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
            TargetBitsPerSecond = Config.GetNamed(Name).GetSettings<long>("TargetBitsPerSecond");
            if (TargetBitsPerSecond == 0) TargetBitsPerSecond = 1000000; // Default 1 Mbps
            
            // Initialize packet release timer (fires every 10ms for smooth packet release)
            packetReleaseTimer = new Timer(ProcessPacketQueue, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
        }

        public override void Toggle()
        {
            IsActivated = !IsActivated;
            if (!IsActivated)
            {
                // Clear the rate limiting queue when deactivated
                if (RateLimitingEnabled)
                {
                    while (packetQueue.TryDequeue(out var _)) { } // Clear all queued packets
                    lock (rateLimitLock)
                    {
                        totalBitsTransferred = 0;
                        lastPacketTime = DateTime.Now;
                    }
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
                                if (Buffer && !p.Flags.HasFlag(TcpFlags.FIN) && !p.Flags.HasFlag(TcpFlags.RST)) await p.SourceProvider.SendPacket(p, true);

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
                // Reset rate limiting counters when activated
                if (RateLimitingEnabled)
                {
                    lock (rateLimitLock)
                    {
                        totalBitsTransferred = 0;
                        lastPacketTime = DateTime.Now;
                    }
                }
            }
        }

        private void ProcessPacketQueue(object state)
        {
            if (!RateLimitingEnabled || isProcessingQueue || !IsActivated)
                return;

            isProcessingQueue = true;
            
            try
            {
                while (packetQueue.TryDequeue(out Packet packet))
                {
                    lock (rateLimitLock)
                    {
                        var now = DateTime.Now;
                        var timeSinceLastPacket = (now - lastPacketTime).TotalSeconds;
                        
                        // Reset counters if more than 1 second has passed (new time window)
                        if (timeSinceLastPacket >= 1.0)
                        {
                            totalBitsTransferred = 0;
                            lastPacketTime = now;
                        }
                        
                        var packetBits = packet.Length * 8; // Convert bytes to bits
                        var projectedTotal = totalBitsTransferred + packetBits;
                        var allowedBitsInCurrentSecond = (long)(TargetBitsPerSecond * Math.Min(1.0, timeSinceLastPacket + 0.01)); // Small buffer
                        
                        if (projectedTotal <= allowedBitsInCurrentSecond)
                        {
                            // Allow this packet and update counters
                            totalBitsTransferred = projectedTotal;
                            
                            // Process the packet by sending it through the original flow
                            Task.Run(async () =>
                            {
                                try
                                {
                                    packet.CreatedAt = DateTime.Now;
                                    packet.Delayed = false;
                                    packet.AckNum = 0; // let storepacket assign highest
                                    packet.SourceProvider.StorePacket(packet);
                                    if (Buffer && !packet.Flags.HasFlag(TcpFlags.FIN) && !packet.Flags.HasFlag(TcpFlags.RST))
                                        await packet.SourceProvider.SendPacket(packet, true);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e, "Rate limiting packet processing");
                                }
                            });
                        }
                        else
                        {
                            // Put the packet back in the queue to try again later
                            packetQueue.Enqueue(packet);
                            break; // Exit loop to avoid processing more packets this cycle
                        }
                    }
                }
            }
            finally
            {
                isProcessingQueue = false;
            }
        }

        public static bool Buffer;
        public static bool RateLimitingEnabled;
        public static long TargetBitsPerSecond;
        
        // Rate limiting tracking variables
        private DateTime lastPacketTime = DateTime.Now;
        private long totalBitsTransferred = 0;
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

            // If rate limiting is enabled, queue the packet for controlled release
            if (RateLimitingEnabled)
            {
                // Add packet to queue for rate-limited processing
                packetQueue.Enqueue(p);
                return false; // Block the original packet, it will be processed through the queue
            }

            // Original blocking behavior when rate limiting is disabled
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
