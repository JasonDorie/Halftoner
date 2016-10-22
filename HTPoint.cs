using System;
using System.Collections.Generic;
using System.Text;

namespace Halftoner
{
	public class HTPoint
	{
		public float x;		// X Coord
		public float y;		// Y Coord
		public float w;		// Width

		public HTPoint()
		{
			x = y = w = 0;
		}

		public HTPoint( float _x, float _y, float _w )
		{
			x = _x;
			y = _y;
			w = _w;
		}

		public float Length
		{
			get { return (float)Math.Sqrt( x * x + y * y + w * w ); }
		}

		public static HTPoint operator +( HTPoint a, HTPoint b )
		{
			return new HTPoint( a.x + b.x, a.y + b.y, a.w + b.w );
		}

		public static HTPoint operator -( HTPoint a, HTPoint b )
		{
			return new HTPoint( a.x - b.x, a.y - b.y, a.w - b.w );
		}

		public static HTPoint operator *( HTPoint a, float b )
		{
			return new HTPoint( a.x * b, a.y * b, a.w * b );
		}
	}
}
