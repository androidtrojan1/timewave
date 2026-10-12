using System;
using System.IO;
using System.Text;

namespace TimeWave.Shared
{
    public enum MessageType : byte
    {
        // client -> engine
        Hello = 1,
        IssueCommand = 2,
        JumpTo = 3,
        SetRate = 4,
        JumpToPresent = 5,

        // engine -> client
        Welcome = 100,
        WorldFrame = 101,
        TimelineStats = 102,
        Notice = 103,
    }

    public abstract class Message
    {
        public abstract MessageType Type { get; }
        public abstract void Write(BinaryWriter w);
    }

    public sealed class Hello : Message
    {
        public int Version = Protocol.Version;
        public string Name = "player";
        /// <summary>Player slot to take, or -1 for the first free one.</summary>
        public int RequestedPlayer = -1;

        public override MessageType Type => MessageType.Hello;
        public override void Write(BinaryWriter w) { w.Write(Version); w.Write(Name ?? ""); w.Write(RequestedPlayer); }
        public static Hello Read(BinaryReader r) => new Hello { Version = r.ReadInt32(), Name = r.ReadString(), RequestedPlayer = r.ReadInt32() };
    }

    /// <summary>An order issued at the tick the player is currently looking at (past, present or future).</summary>
    public sealed class IssueCommand : Message
    {
        public OrderType Order;
        public int[] UnitIds = Array.Empty<int>();
        public int TargetX, TargetY, TargetId;

        public override MessageType Type => MessageType.IssueCommand;
        public override void Write(BinaryWriter w)
        {
            w.Write((byte)Order);
            Protocol.WriteInts(w, UnitIds);
            w.Write(TargetX); w.Write(TargetY); w.Write(TargetId);
        }
        public static IssueCommand Read(BinaryReader r) => new IssueCommand
        {
            Order = (OrderType)r.ReadByte(),
            UnitIds = Protocol.ReadInts(r),
            TargetX = r.ReadInt32(), TargetY = r.ReadInt32(), TargetId = r.ReadInt32(),
        };
    }

    public sealed class JumpTo : Message
    {
        public int Tick;
        public override MessageType Type => MessageType.JumpTo;
        public override void Write(BinaryWriter w) => w.Write(Tick);
        public static JumpTo Read(BinaryReader r) => new JumpTo { Tick = r.ReadInt32() };
    }

    public sealed class SetRate : Message
    {
        public TimeRate Rate;
        public override MessageType Type => MessageType.SetRate;
        public override void Write(BinaryWriter w) => w.Write((byte)Rate);
        public static SetRate Read(BinaryReader r) => new SetRate { Rate = (TimeRate)r.ReadByte() };
    }

    public sealed class JumpToPresent : Message
    {
        public override MessageType Type => MessageType.JumpToPresent;
        public override void Write(BinaryWriter w) { }
    }

    public sealed class Welcome : Message
    {
        public int PlayerId;
        public int PlayerCount;
        public int MapWidth, MapHeight;
        public int TicksPerSecond;
        public int WindowTicks;
        public int PresentOffsetTicks;
        public float ChronoMax;
        public string[] PlayerNames = Array.Empty<string>();

        public override MessageType Type => MessageType.Welcome;
        public override void Write(BinaryWriter w)
        {
            w.Write(PlayerId); w.Write(PlayerCount); w.Write(MapWidth); w.Write(MapHeight);
            w.Write(TicksPerSecond); w.Write(WindowTicks); w.Write(PresentOffsetTicks); w.Write(ChronoMax);
            w.Write(PlayerNames.Length);
            foreach (var n in PlayerNames) w.Write(n ?? "");
        }
        public static Welcome Read(BinaryReader r)
        {
            var m = new Welcome
            {
                PlayerId = r.ReadInt32(), PlayerCount = r.ReadInt32(), MapWidth = r.ReadInt32(), MapHeight = r.ReadInt32(),
                TicksPerSecond = r.ReadInt32(), WindowTicks = r.ReadInt32(), PresentOffsetTicks = r.ReadInt32(), ChronoMax = r.ReadSingle(),
            };
            m.PlayerNames = new string[Protocol.CheckCount(r.ReadInt32())];
            for (int i = 0; i < m.PlayerNames.Length; i++) m.PlayerNames[i] = r.ReadString();
            return m;
        }
    }

    /// <summary>The world as seen from the receiving player's position on the timeline, plus where everything else is.</summary>
    public sealed class WorldFrame : Message
    {
        public int PlayerTick;
        public TimeRate Rate;
        public int PresentTick;
        public int WindowStart, WindowEnd;
        public float Chrono, ChronoMax;
        public WaveInfo[] Waves = Array.Empty<WaveInfo>();
        public PlayerTimeInfo[] Players = Array.Empty<PlayerTimeInfo>();
        public UnitSnapshot[] Units = Array.Empty<UnitSnapshot>();

        public override MessageType Type => MessageType.WorldFrame;
        public override void Write(BinaryWriter w)
        {
            w.Write(PlayerTick); w.Write((byte)Rate); w.Write(PresentTick);
            w.Write(WindowStart); w.Write(WindowEnd); w.Write(Chrono); w.Write(ChronoMax);
            w.Write(Waves.Length);
            foreach (var v in Waves) { w.Write(v.Tick); w.Write(v.Rate); w.Write((byte)v.Kind); w.Write(v.Owner); }
            w.Write(Players.Length);
            foreach (var p in Players) { w.Write(p.Player); w.Write(p.Tick); w.Write((byte)p.Rate); w.Write(p.AtPresent); w.Write(p.Human); }
            w.Write(Units.Length);
            foreach (var u in Units)
            {
                w.Write(u.Id); w.Write(u.Name); w.Write(u.Owner); w.Write((byte)u.Type);
                w.Write(u.X); w.Write(u.Y); w.Write(u.Hp); w.Write(u.MaxHp);
                w.Write((byte)u.Order); w.Write(u.TargetX); w.Write(u.TargetY); w.Write(u.TargetId); w.Write((byte)u.Flags);
            }
        }
        public static WorldFrame Read(BinaryReader r)
        {
            var m = new WorldFrame
            {
                PlayerTick = r.ReadInt32(), Rate = (TimeRate)r.ReadByte(), PresentTick = r.ReadInt32(),
                WindowStart = r.ReadInt32(), WindowEnd = r.ReadInt32(), Chrono = r.ReadSingle(), ChronoMax = r.ReadSingle(),
            };
            m.Waves = new WaveInfo[Protocol.CheckCount(r.ReadInt32())];
            for (int i = 0; i < m.Waves.Length; i++)
                m.Waves[i] = new WaveInfo { Tick = r.ReadInt32(), Rate = r.ReadSingle(), Kind = (WaveKind)r.ReadByte(), Owner = r.ReadSByte() };
            m.Players = new PlayerTimeInfo[Protocol.CheckCount(r.ReadInt32())];
            for (int i = 0; i < m.Players.Length; i++)
                m.Players[i] = new PlayerTimeInfo { Player = r.ReadByte(), Tick = r.ReadInt32(), Rate = (TimeRate)r.ReadByte(), AtPresent = r.ReadBoolean(), Human = r.ReadBoolean() };
            m.Units = new UnitSnapshot[Protocol.CheckCount(r.ReadInt32())];
            for (int i = 0; i < m.Units.Length; i++)
            {
                m.Units[i] = new UnitSnapshot
                {
                    Id = r.ReadInt32(), Name = r.ReadInt32(), Owner = r.ReadByte(), Type = (UnitType)r.ReadByte(),
                    X = r.ReadInt32(), Y = r.ReadInt32(), Hp = r.ReadInt16(), MaxHp = r.ReadInt16(),
                    Order = (OrderType)r.ReadByte(), TargetX = r.ReadInt32(), TargetY = r.ReadInt32(), TargetId = r.ReadInt32(),
                    Flags = (UnitFlags)r.ReadByte(),
                };
            }
            return m;
        }
    }

    /// <summary>
    /// Aggregated activity over the whole time window, for the timeline panel.
    /// Arrays are indexed [bucket * PlayerCount + player].
    /// </summary>
    public sealed class TimelineStats : Message
    {
        public int FirstTick;
        public int BucketTicks;
        public int BucketCount;
        public int PlayerCount;
        public ushort[] UnitCount = Array.Empty<ushort>();
        public ushort[] DamageDealt = Array.Empty<ushort>();
        public ushort[] DamageTaken = Array.Empty<ushort>();
        public byte[] Created = Array.Empty<byte>();
        /// <summary>Orders that exist in the timeline at that time (issued from present, past or future).</summary>
        public byte[] Commands = Array.Empty<byte>();

        public override MessageType Type => MessageType.TimelineStats;
        public override void Write(BinaryWriter w)
        {
            w.Write(FirstTick); w.Write(BucketTicks); w.Write(BucketCount); w.Write(PlayerCount);
            int n = BucketCount * PlayerCount;
            for (int i = 0; i < n; i++) w.Write(UnitCount[i]);
            for (int i = 0; i < n; i++) w.Write(DamageDealt[i]);
            for (int i = 0; i < n; i++) w.Write(DamageTaken[i]);
            w.Write(Created, 0, n);
            w.Write(Commands, 0, n);
        }
        public static TimelineStats Read(BinaryReader r)
        {
            var m = new TimelineStats { FirstTick = r.ReadInt32(), BucketTicks = r.ReadInt32(), BucketCount = r.ReadInt32(), PlayerCount = r.ReadInt32() };
            int n = Protocol.CheckCount(m.BucketCount * m.PlayerCount);
            m.UnitCount = new ushort[n]; m.DamageDealt = new ushort[n]; m.DamageTaken = new ushort[n];
            for (int i = 0; i < n; i++) m.UnitCount[i] = r.ReadUInt16();
            for (int i = 0; i < n; i++) m.DamageDealt[i] = r.ReadUInt16();
            for (int i = 0; i < n; i++) m.DamageTaken[i] = r.ReadUInt16();
            m.Created = r.ReadBytes(n);
            m.Commands = r.ReadBytes(n);
            return m;
        }
    }

    public sealed class Notice : Message
    {
        public string Text = "";
        public override MessageType Type => MessageType.Notice;
        public override void Write(BinaryWriter w) => w.Write(Text ?? "");
        public static Notice Read(BinaryReader r) => new Notice { Text = r.ReadString() };
    }

    /// <summary>Wire format: [int32 length][byte type][payload], little-endian, length covers type + payload.</summary>
    public static class Protocol
    {
        public const int Version = 1;
        public const int DefaultPort = 7615;
        const int MaxFrameBytes = 16 * 1024 * 1024;
        const int MaxCount = 1 << 20;

        public static byte[] Encode(Message message)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(0);
                w.Write((byte)message.Type);
                message.Write(w);
                w.Flush();
                var bytes = ms.ToArray();
                int len = bytes.Length - 4;
                bytes[0] = (byte)len; bytes[1] = (byte)(len >> 8); bytes[2] = (byte)(len >> 16); bytes[3] = (byte)(len >> 24);
                return bytes;
            }
        }

        /// <summary>Blocks until a whole frame is read. Returns null when the stream ends cleanly.</summary>
        public static Message ReadFrame(Stream stream)
        {
            var header = new byte[4];
            if (!ReadExactly(stream, header, 4)) return null;
            int len = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (len < 1 || len > MaxFrameBytes) throw new InvalidDataException("bad frame length " + len);
            var body = new byte[len];
            if (!ReadExactly(stream, body, len)) throw new EndOfStreamException("truncated frame");
            return Decode(body);
        }

        public static Message Decode(byte[] body)
        {
            using (var ms = new MemoryStream(body, 1, body.Length - 1))
            using (var r = new BinaryReader(ms, Encoding.UTF8))
            {
                switch ((MessageType)body[0])
                {
                    case MessageType.Hello: return Hello.Read(r);
                    case MessageType.IssueCommand: return IssueCommand.Read(r);
                    case MessageType.JumpTo: return JumpTo.Read(r);
                    case MessageType.SetRate: return SetRate.Read(r);
                    case MessageType.JumpToPresent: return new JumpToPresent();
                    case MessageType.Welcome: return Welcome.Read(r);
                    case MessageType.WorldFrame: return WorldFrame.Read(r);
                    case MessageType.TimelineStats: return TimelineStats.Read(r);
                    case MessageType.Notice: return Notice.Read(r);
                    default: throw new InvalidDataException("unknown message type " + body[0]);
                }
            }
        }

        static bool ReadExactly(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0)
                {
                    if (read == 0) return false;
                    throw new EndOfStreamException();
                }
                read += n;
            }
            return true;
        }

        internal static void WriteInts(BinaryWriter w, int[] values)
        {
            values = values ?? Array.Empty<int>();
            w.Write(values.Length);
            foreach (var v in values) w.Write(v);
        }

        internal static int[] ReadInts(BinaryReader r)
        {
            var a = new int[CheckCount(r.ReadInt32())];
            for (int i = 0; i < a.Length; i++) a[i] = r.ReadInt32();
            return a;
        }

        internal static int CheckCount(int n)
        {
            if (n < 0 || n > MaxCount) throw new InvalidDataException("bad element count " + n);
            return n;
        }
    }
}
