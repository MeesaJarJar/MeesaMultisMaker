using System;

namespace MeesaMultisMaker.Generation
{
    public struct Point3D : IEquatable<Point3D>
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public Point3D(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
    }

        public override bool Equals(object obj)
      {
     return obj is Point3D d && Equals(d);
  }

        public bool Equals(Point3D other)
     {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

public override int GetHashCode()
        {
        unchecked
            {
  int hash = 17;
       hash = hash * 23 + X.GetHashCode();
      hash = hash * 23 + Y.GetHashCode();
   hash = hash * 23 + Z.GetHashCode();
                return hash;
      }
        }

        public static bool operator ==(Point3D left, Point3D right)
        {
            return left.Equals(right);
        }

     public static bool operator !=(Point3D left, Point3D right)
        {
            return !(left == right);
        }

        public static Point3D operator +(Point3D a, Point3D b)
        {
     return new Point3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Point3D operator -(Point3D a, Point3D b)
     {
      return new Point3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public override string ToString()
        {
 return $"({X}, {Y}, {Z})";
        }
    }
}
