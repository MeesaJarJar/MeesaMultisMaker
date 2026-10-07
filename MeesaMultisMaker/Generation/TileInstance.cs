using System;

namespace MeesaMultisMaker.Generation
{
    /// <summary>
    /// Represents a tile instance with its ID, position, and metadata
    /// </summary>
    public class TileInstance
    {
        public ushort TileId { get; set; }
public Point3D Position { get; set; }
   public int Flags { get; set; }

        public TileInstance(ushort tileId, Point3D position, int flags = 1)
        {
    TileId = tileId;
            Position = position;
            Flags = flags;
        }

        public TileInstance Clone()
 {
       return new TileInstance(TileId, Position, Flags);
        }

        public override string ToString()
        {
     return $"Tile 0x{TileId:X4} at {Position}";
        }
    }
}
