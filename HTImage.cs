using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Halftoner
{
	public class HTLine
	{
		public List<HTPoint> Points = new List<HTPoint>();
		public int NumPoints {
			get { return Points.Count(); }
		}
	}


	public class HTRow
	{
		HTLine curLine = null;

		public List<HTLine> Lines = new List<HTLine>();
		public int NumPoints {
			get {
				int count = 0;
				foreach( HTLine line in Lines )
					count += line.NumPoints;
				return count;
			}
		}

		public void NewLine()
		{
			curLine = new HTLine();
			Lines.Add( curLine );
		}

		public void AddPoint(HTPoint pt)
		{
			if (curLine == null) NewLine();
			curLine.Points.Add(pt);
		}
	}


	public class HTImage
	{
		HTRow curRow = null;

		public List<HTRow> Rows = new List<HTRow>();

		public int NumRows
		{
			get { return Rows.Count; }
		}

		public int NumLines
		{
			get {
				int count = 0;
				foreach( HTRow row in Rows ) {
					count += row.Lines.Count;
				}
				return count;
			}
		}

		public int NumPoints {
			get {
				int count = 0;
				foreach (HTRow row in Rows)
					count += row.NumPoints;
				return count;
			}
		}

		public void Clear()
		{
			Rows.Clear();
			curRow = null;
		}

		public void NewRow()
		{
			curRow = new HTRow();
			Rows.Add( curRow );
		}

		public void NewLine()
		{
			if (curRow == null) NewRow();
			curRow.NewLine();
		}

		public void AddPoint(HTPoint pt)
		{
			if (curRow == null) NewRow();
			curRow.AddPoint(pt);
		}
	}
}
