using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace Halftoner
{
	public class MitchellFilter
	{
		int KernelRadius = 3;
		int[] FilterKernel = null;
		int FilterSum = 1;
		int FilterMult = 1;

		public int Size
		{
			get { return KernelRadius; }
		}

		int FilterValue(double x)
		{
			if( x < 0.0 ) x = -x;

			double fResult;
			if( x >= 2.0 )
				fResult = 0.0;
			else if( x >= 1.0 )
				fResult = (-7.0 / 3.0) * x * x * x + 12.0 * x * x + -20.0 * x + (32.0 / 3.0);
			else	// x < 1
				fResult = 7.0 * x * x * x - 12.0 * x * x + (16.0 / 3.0);

			return (int)(fResult * (1.0 / 6.0) * 1024.0);
		}

		void BuildKernel(int size)
		{
			KernelRadius = size;
			FilterKernel = new int[size + 1];

			FilterSum = 0;
			for( int i = 0; i <= size; i++ ) {
				FilterKernel[i] = FilterValue( (double)i / (double)KernelRadius );
				FilterSum += FilterKernel[i] * 2;
			}
			FilterSum -= FilterKernel[0];
			FilterMult = 2*65536 / FilterSum;
		}

		public unsafe Bitmap ToGrayscale(Bitmap map)
		{
			// Make a grayscale copy
			Rectangle area = new Rectangle( 0, 0, map.Width, map.Height );

			BitmapData sourceData = map.LockBits( area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb );

			Bitmap gray = new Bitmap( map.Width, map.Height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
			BitmapData destData = gray.LockBits( area, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed );

			byte* sData = (byte*)sourceData.Scan0;
			byte* dData = (byte*)destData.Scan0;

			for( int y = 0; y < map.Height; y++ )
			{
				byte* sRow = sData;
				for( int x = 0; x < map.Width; x++, sRow += 4 )
				{
					int bright = ((int)sRow[2] * 306 + (int)sRow[1] * 600+ (int)sRow[0] * 117 + 511) >> 10;
					dData[x] = (byte)bright;
				}

				sData += sourceData.Stride;
				dData += destData.Stride;
			}
			map.UnlockBits( sourceData );
			gray.UnlockBits( destData );

			for( int i = 0; i < 256; i++ ) {
				gray.Palette.Entries[i] = Color.FromArgb( 255, i, i, i );
			}

			// return the result
			return gray;
		}


		public unsafe Bitmap Filter( Bitmap map , int size )
		{
			// assume the source is grayscale

			// Generate the filter kernel
			BuildKernel( size );

			BitmapData sourceBits = map.LockBits( new Rectangle( 0, 0, map.Width, map.Height ), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed );

			Bitmap tempOut = new Bitmap(map.Width, map.Height, PixelFormat.Format8bppIndexed);
			BitmapData destBits = tempOut.LockBits(new Rectangle(0, 0, map.Width, map.Height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

			byte * sData = (byte*)sourceBits.Scan0;
			byte * dData = (byte*)destBits.Scan0;

			byte[] row = new byte[map.Width + size*2];

			// Filter the image in X
			for( int y = 0; y < map.Height; y++ )
			{
				byte* sRow = sData;
				byte* dRow = dData;

				for (int x = 1; x < size; x++) {
					row[size - x] = sRow[0];
					row[map.Width - 1 + x] = sRow[map.Width - 1];
				}

				for (int x = 0; x < map.Width; x++) {
					row[x + size] = sRow[x];
				}

				for( int x = size; x < (map.Width)+size; x++ )
				{
					int result = row[x] * FilterKernel[0];
					for( int k = 1; k <= size; k++ ) {
						result += (row[x - k] + row[x + k]) * FilterKernel[k];
					}
					//result /= FilterSum;
					result = (result * FilterMult + 32767) >> 17;

					result = (result < 0) ? 0 : result;
					result = (result > 255) ? 255 : result;
					dRow[x-size] = (byte)result;
				}

				sData += sourceBits.Stride;
				dData += destBits.Stride;
			}

			map.UnlockBits( sourceBits );
			tempOut.UnlockBits(destBits);


			// For Y Filter pre-load an array of row offsets (strides) to the rows to use for each filter tap.
			// Do a single horizontal row at a time to be more cache friendly, and do fewer multiplies.
			// A single (size * 2 + 1) array holds the stride offsets.  Only need to compute one new one per pass
			// and simply copy the previous list up one entry.  Prime it with 0's to (size), then 0, stride, stride*2, etc..

			int[] offsetList = new int[size * 2 + 1];
			for( int i = -size; i <= 0; i++ ) {
				offsetList[size + i] = 0;
			}

			for( int i = 1; i <=size; i++ ) {
				offsetList[size + i] = i * sourceBits.Stride;
			}


			// Filter the image in Y
			sourceBits = tempOut.LockBits(new Rectangle(0, 0, map.Width, map.Height), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);

			Bitmap output = new Bitmap(map.Width, map.Height, PixelFormat.Format8bppIndexed);
			destBits = output.LockBits(new Rectangle(0, 0, map.Width, map.Height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

			sData = (byte*)sourceBits.Scan0;
			dData = (byte*)destBits.Scan0;

			for( int y=0; y<map.Height; y++ )
			{
				if( y != 0 ) {
					for( int i = 1; i <= size * 2; i++ ) {
						offsetList[i - 1] = offsetList[i];
					}
					int lastY = y + size;
					if( lastY >= map.Height ) lastY = map.Height - 1;
					offsetList[size * 2] = lastY * sourceBits.Stride;
				}

				byte* sCol = sData;
				byte* dCol = dData;

				for (int x = 0; x < map.Width; x++)
				{
					int result = sCol[offsetList[size]] * FilterKernel[0];
					for (int k = 1; k <= size; k++) {
						result += (sCol[offsetList[size-k]] + sCol[offsetList[size+k]]) * FilterKernel[k];
					}
					//result /= FilterSum;
					result = (result * FilterMult + 32767) >> 17;
					//result = (result < 0) ? 0 : result;
					//result = (result > 255) ? 255 : result;
					dCol[x] = (byte)result;
					sCol++;
				}

				dData += destBits.Stride;
			}

			tempOut.UnlockBits(sourceBits);
			output.UnlockBits(destBits);


			// return the result
			return output;
		}


		public unsafe Bitmap Filter2(Bitmap map )
		{
			// assume the source is grayscale

			// Generate the filter kernel
			BuildKernel( 2 );

			BitmapData sourceBits = map.LockBits( new Rectangle( 0, 0, map.Width, map.Height ), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed );

			Bitmap tempOut = new Bitmap( map.Width, map.Height, PixelFormat.Format8bppIndexed );
			BitmapData destBits = tempOut.LockBits( new Rectangle( 0, 0, map.Width, map.Height ), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed );

			byte* sData = (byte*)sourceBits.Scan0;
			byte* dData = (byte*)destBits.Scan0;

			byte[] row = new byte[map.Width + 2 * 2];

			// Filter the image in X
			for( int y = 0; y < map.Height; y++ )
			{
				byte* sRow = sData;
				byte* dRow = dData;

				for( int x = 1; x < 2; x++ ) {
					row[2 - x] = sRow[0];
					row[map.Width - 1 + x] = sRow[map.Width - 1];
				}

				for( int x = 0; x < map.Width; x++ ) {
					row[x + 2] = sRow[x];
				}

				for( int x = 2; x < (map.Width) + 2; x++ )
				{
					int result = row[x] * FilterKernel[0];
					result += (row[x - 1] + row[x + 1]) * FilterKernel[1];
					result += (row[x - 2] + row[x + 2]) * FilterKernel[2];
					result = (result * FilterMult + 32767) >> 17;
					dRow[x - 2] = (byte)result;
				}

				sData += sourceBits.Stride;
				dData += destBits.Stride;
			}

			map.UnlockBits( sourceBits );
			tempOut.UnlockBits( destBits );


			// For Y Filter pre-load an array of row offsets (strides) to the rows to use for each filter tap.
			// Do a single horizontal row at a time to be more cache friendly, and do fewer multiplies.
			// A single (size * 2 + 1) array holds the stride offsets.  Only need to compute one new one per pass
			// and simply copy the previous list up one entry.  Prime it with 0's to (size), then 0, stride, stride*2, etc..

			int[] offsetList = new int[2 * 2 + 1];
			for( int i = -2; i <= 0; i++ ) {
				offsetList[2 + i] = 0;
			}

			for( int i = 1; i <= 2; i++ ) {
				offsetList[2 + i] = i * sourceBits.Stride;
			}


			// Filter the image in Y
			sourceBits = tempOut.LockBits( new Rectangle( 0, 0, map.Width, map.Height ), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed );

			Bitmap output = new Bitmap( map.Width, map.Height, PixelFormat.Format8bppIndexed );
			destBits = output.LockBits( new Rectangle( 0, 0, map.Width, map.Height ), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed );

			sData = (byte*)sourceBits.Scan0;
			dData = (byte*)destBits.Scan0;

			for( int y = 0; y < map.Height; y++ )
			{
				if( y != 0 )
				{
					for( int i = 1; i <= 2 * 2; i++ ) {
						offsetList[i - 1] = offsetList[i];
					}
					int lastY = y + 2;
					if( lastY >= map.Height ) lastY = map.Height - 1;
					offsetList[2 * 2] = lastY * sourceBits.Stride;
				}

				byte* sCol = sData;
				byte* dCol = dData;

				for( int x = 0; x < map.Width; x++ )
				{
					int result = sCol[offsetList[2]] * FilterKernel[0];
					result += (sCol[offsetList[2 - 1]] + sCol[offsetList[2 + 1]]) * FilterKernel[1];
					result += (sCol[offsetList[2 - 2]] + sCol[offsetList[2 + 2]]) * FilterKernel[2];
					result = (result * FilterMult + 32767) >> 17;
					dCol[x] = (byte)result;
					sCol++;
				}

				dData += destBits.Stride;
			}

			tempOut.UnlockBits( sourceBits );
			output.UnlockBits( destBits );


			// return the result
			return output;
		}

	}
}
