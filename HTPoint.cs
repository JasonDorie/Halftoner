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
	}
}
