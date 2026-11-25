using MultiSEngine.Protocol.Framing;
using Terraria;
using TrProtocol.Models;
using TrProtocol.NetPackets;

namespace MultiSEngine.Protocol.Terraria;

/// <summary>
/// Single source of truth for the virtual lobby world geometry and pre-encoded static packets.
/// </summary>
public static class LobbyProtocol
{
    public const short WorldWidth = 8400;
    public const short WorldHeight = 2400;
    public const short SpawnX = WorldWidth / 2;
    public const short SpawnY = WorldHeight / 2;
    public const int SpawnSectionStartX = SpawnX - 50;
    public const int SpawnSectionStartY = SpawnY - 50;
    public const short SpawnSectionSize = 100;
    public const int MaxTerrariaPlayerSlots = 255; // Valid player slots are 0..254 (255 is server slot)

    private static readonly Lazy<byte[]> CachedSpawnSquareFrame = new(BuildSpawnSquareFrame);

    public static ReadOnlyMemory<byte> SpawnSquarePacket => CachedSpawnSquareFrame.Value;

    public static WorldData CreateLobbyWorldData(string worldName)
    {
        var eventInfo1 = new BitsByte();
        eventInfo1[6] = true; // Enable SSC bit in virtual lobby so proxy controls state cleanly

        return new WorldData
        {
            MaxTileX = WorldWidth,
            MaxTileY = WorldHeight,
            SpawnX = SpawnX,
            SpawnY = SpawnY,
            WorldSurface = 0,
            RockLayer = 0,
            WorldName = worldName,
            WorldUniqueID = Guid.Empty,
            EventInfo1 = eventInfo1,
            GameMode = 0,
            ExtraSpawnPoints = [],
        };
    }

    /// <summary>
    /// Writes PlayerActive(false) for every player slot 0..254 except the player's own active slot.
    /// Fixes the v1 buffer overrun (255 packets into 254-packet rent) and prevents hiding the player's own remote slot.
    /// </summary>
    public static void WriteDeactivateOtherPlayers(PooledBufferWriter writer, byte excludeSlot)
    {
        var packet = new PlayerActive { Active = false };
        for (var slot = 0; slot < MaxTerrariaPlayerSlots; slot++)
        {
            if (slot == excludeSlot)
                continue;

            packet.PlayerSlot = (byte)slot;
            writer.WritePacket(packet, fromServer: true);
        }
    }

    public static byte[] CreateTileSectionFrame(int x, int y, short width, short height, ushort tileType = 541)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var flags1 = new BitsByte();
        flags1[1] = true;
        flags1[5] = true;

        var tile = new ComplexTileData
        {
            TileType = tileType,
            Liquid = 0,
            WallColor = 0,
            WallType = 0,
            TileColor = 0,
            Flags1 = flags1.value,
            Flags2 = 0,
            Flags3 = 0,
        };

        var count = checked(width * height);
        var tiles = new ComplexTileData[count];
        Array.Fill(tiles, tile);

        var section = new TileSection
        {
            Data = new SectionData
            {
                StartX = x,
                StartY = y,
                Width = width,
                Height = height,
                Tiles = tiles,
                ChestCount = 0,
                Chests = [],
                SignCount = 0,
                Signs = [],
                TileEntityCount = 0,
                TileEntities = [],
            },
        };

        return PacketFrameCodec.SerializeToArray(section, fromServer: true);
    }

    private static byte[] BuildSpawnSquareFrame()
        => CreateTileSectionFrame(SpawnSectionStartX, SpawnSectionStartY, SpawnSectionSize, SpawnSectionSize);
}
