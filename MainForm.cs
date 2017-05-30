using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace Halftoner
{
	public partial class MainForm : Form
	{
		Bitmap fileImage = null;
		Bitmap previewImage = null;
		Bitmap scaledImage = null;
		Bitmap grayImage = null;
		Bitmap filterImage = null;

		//Bitmap histogramImage = null;
		int[] sourceLevels = new int[256];	// source image computed levels
		int[] adjustedLevels = new int[256];
		float brightness = 0.0f;
		float contrast = 1.0f;
		bool negateImage = false;
		bool adjustmentsChanged = true;


		MitchellFilter Filter = new MitchellFilter();

		HTImage points = new HTImage();
		float[] dotLookup = null;

		CultureInfo us = new CultureInfo( "en-US" );

		enum Style
		{
			Dots,
			Lines,
			Squares,
			Circles,
			Random,
		};

		enum FileMode
		{
			DXF,
			PNG,
		};

		Style style = Style.Dots;
		FileMode fileMode = FileMode.DXF;

		double workWidth = 1.0;
		double workHeight = 1.0;
		double border = 0.25;
		double spacing = 0.125;
		double minSize = 0.0;
		double maxSize = 0.25;
		double angle = 0.0;
		double wavelength = 0.0;
		double amplitude = 0.0;
		double centerOffsX = 0.0;
		double centerOffsY = 0.0;
		bool offsetOdd = false;
		bool invert = false;
		bool gammaCorrect = false;
		bool imperial = true;
		bool FixedSizes = false;
		int dotCount = 1000;

		const double PI = 3.1415926535897931;
		const double TwoPI = PI * 2.0;
		const double HalfPI = PI * 0.5;
		const double InvPI = 1.0 / PI;

		bool InternalChange = false;


		public MainForm()
		{
			InitializeComponent();
			pbPreview.Visible = false;
			lblDirections.Visible = true;

			#if !DEBUG
			lblRandomDots.Visible = false;
			udRandomDots.Visible = false;
			rbRandom.Visible = false;

			btnTest.Visible = false;
			btnTest.Enabled = false;
			#endif

			LoadSettings();

			if(style == Style.Squares || style == Style.Circles)
			{
				udCenterOffsX.Enabled = true;
				udCenterOffsY.Enabled = true;
			}
			else 
			{
				udCenterOffsX.Enabled = false;
				udCenterOffsY.Enabled = false;
			}
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			SaveSettings();
		}

		private void MainForm_DragOver(object sender, DragEventArgs e)
		{
			if( e.Data.GetDataPresent( "FileNameW" ) ) {
				e.Effect = DragDropEffects.Copy;
			}
			else {
				e.Effect = DragDropEffects.None;
			}
		}

		private void MainForm_DragDrop(object sender, DragEventArgs e)
		{
			string[] names = e.Data.GetData( "FileNameW" ) as string[];
			LoadImage( names[0] );
			ImageChanged();
		}

		private void btnLoadImage_Click(object sender, EventArgs e)
		{
			OpenFileDialog dlg = new OpenFileDialog();

			dlg.CheckFileExists = true;
			dlg.Filter = "Image files|*.jpg;*.jpeg;*.gif;*.png;*.bmp";
			dlg.FilterIndex = 1;
			dlg.AddExtension = true;
			dlg.DefaultExt = "jpg";

			if (dlg.ShowDialog() != DialogResult.OK) return;

			LoadImage(dlg.FileName);
			ImageChanged();
		}


		void LoadImage(string Filename)
		{
			try {
				fileImage = new Bitmap( Filename );
				grayImage = Filter.ToGrayscale(fileImage);
			}

			catch( Exception ) {
			}
		}


		void ImageChanged()
		{
			if( fileImage == null ) return;

			if (fileImage != null && fileImage.Width > 0)
			{
				InternalChange = true;
				double aspect = (double)fileImage.Height / (double)fileImage.Width;
				udHeight.Value = udWidth.Value * (decimal)aspect;
				InternalChange = false;
			}

			if( pbPreview.Visible == false )
			{
				pbPreview.Visible = true;
				lblDirections.Visible = false;
			}

			// Resize the preview image
			filterImage = null;		// Force a rebuild of the filter image
			RedrawPreview(true, false, true );

			ComputeSourceLevels();
			ComputeAdjustedLevels();

			// Set button to show original image
			rbOriginal.Checked = true;

			// Update preview image
			pbPreview.Image = fileImage;
			pbPreview_SizeChanged( this, new EventArgs() );
		}



		private void pbPreview_SizeChanged(object sender, EventArgs e)
		{
			if (fileImage == null) return;

			Size size = pbPreview.Size;
			if( rbOriginal.Checked )
			{
				if( size.Width < 1 || size.Height < 1 ) return;

				double xScale = (double)size.Width / (double)fileImage.Width;
				double yScale = (double)size.Height / (double)fileImage.Height;

				double Scale = xScale < yScale ? xScale : yScale;

				size.Width = (int)((double)fileImage.Width * Scale);
				size.Height = (int)((double)fileImage.Height * Scale);

				previewImage = new System.Drawing.Bitmap( fileImage, size );
				pbPreview.Image = previewImage;
			}
			else
			{
				RedrawPreview( true , false , false );
			}
		}


		void RedrawPreview( bool SizeChanged , bool SettingsChanged , bool FilterChanged )
		{
			if (fileImage == null) return;
			if(rbOriginal.Checked) return;

			bool ImageChanged = false;
			if( SettingsChanged || adjustmentsChanged ) {
				UpdateSettings();

				if( FilterChanged || filterImage == null )
				{
					/*
					double imageSamples = workWidth / spacing;
					double filterSize = ((double)previewImage.Width / imageSamples) * 0.5;
					if( filterSize < 1.0 ) filterSize = 1.0;

					if (filterImage == null || (int)filterSize != Filter.Size) {
						filterImage = Filter.Filter(grayImage, (int)filterSize);
					*/

					int xSamples = (int)Math.Ceiling( 2.5 * workWidth / spacing );
					int ySamples = (int)Math.Ceiling( 2.5 * workHeight / spacing );

					scaledImage = new Bitmap( fileImage, xSamples, ySamples );
					grayImage = Filter.ToGrayscale( scaledImage );
					filterImage = Filter.Filter2( grayImage );
					adjustmentsChanged = true;
				}

				ComputePoints();
				ImageChanged = true;
			}

			if( SizeChanged || previewImage == null ) {

				// Compute an aspect-correct image size from the work area
				if( pbPreview.Size.Width == 0 || pbPreview.Size.Height == 0 ) return;

				Size size = pbPreview.Size;

				double xScale = (double)size.Width / workWidth;
				double yScale = (double)size.Height / workHeight;

				double Scale = xScale < yScale ? xScale : yScale;

				size.Width = (int)(workWidth * Scale);
				size.Height = (int)(workHeight * Scale);

				if( previewImage == null || size.Width != previewImage.Width || size.Height != previewImage.Height )
				{
					ImageChanged = true;
					if( previewImage != null ) {
						previewImage.Dispose();
					}

					previewImage = new Bitmap( size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb );
				}
			}

			if( ImageChanged ) {
				DrawPreviewCircles();
			}
			pbPreview.Image = previewImage;
			lblStatus.Text = String.Format("{0} rows, {1} lines, {2} pts", points.NumRows, points.NumLines, points.NumPoints);
		}


		void DrawPreviewCircles()
		{
			// Compute an appropriate scale
			Size size = previewImage.Size;
			Color canvasCol = invert ? Color.White : Color.Black;
			Brush inkCol = invert ? Brushes.Black : Brushes.White;

			double xScale = (float)size.Width / workWidth;
			double yScale = (float)size.Height / workHeight;

			double Scale = xScale < yScale ? xScale : yScale;

			Graphics g = Graphics.FromImage( previewImage );
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
			g.Clear( canvasCol );

			foreach (HTRow row in points.Rows)
			{
				foreach (HTLine line in row.Lines)
				{
					for (int i = 0; i < line.Points.Count; i++)
					{
						HTPoint pt = line.Points[i];
						float w = (float)(pt.w * Scale);
						float x = (float)(pt.x * Scale);
						float y = (float)(pt.y * Scale);

						g.FillEllipse(inkCol, x - w * 0.5f, y - w * 0.5f, w, w);
					}
				}
			}
			g.Flush();
			g.Dispose();
		}



		public unsafe void ComputeSourceLevels()
		{
			Rectangle area = new Rectangle( 0, 0, grayImage.Width, grayImage.Height );
			BitmapData sourceData = grayImage.LockBits( area, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed );

			for( int i=0; i<256; i++ ) {
				sourceLevels[i] = 0;
			}

			byte* sData = (byte*)sourceData.Scan0;

			for(int y = 0; y < grayImage.Height; y++)
			{
				byte* sRow = sData;
				for(int x = 0; x < grayImage.Width; x++)
				{
					sourceLevels[ sRow[x] ]++;
				}

				sData += sourceData.Stride;
			}
			grayImage.UnlockBits( sourceData );
		}



		public void ComputeAdjustedLevels()
		{
			for(int i = 0; i < 256; i++) {
				adjustedLevels[i] = 0;
			}

			for(int i = 0; i < 256; i++)
			{
				// Convert i to float
				float f = (float)i / 255.0f;

				// linearize?

				// adjust contrast (scale around 0.5) and offset for brightness
				f = (f - 0.5f + brightness) * contrast + 0.5f;

				// clamp
				f = (f < 0.0f) ? 0.0f : f;
				f = (f > 1.0f) ? 1.0f : f;

				// convert to int
				int v = (int)(f * 255.0f + 0.5f);

				adjustedLevels[v] += sourceLevels[i];
			}

			// TODO: Generate the histogram levels image
		}


		/*
		public unsafe void ComputeAdjustedImage( Bitmap source )
		{
			if(adjustedImage != null) adjustedImage.Dispose();

			// Make a grayscale copy
			Rectangle area = new Rectangle( 0, 0, source.Width, source.Height );
			BitmapData sourceData = source.LockBits( area, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed );

			adjustedImage = new Bitmap( source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
			for(int i = 0; i < 256; i++) {
				adjustedImage.Palette.Entries[i] = Color.FromArgb( i, i, i );
			}

			BitmapData destData = adjustedImage.LockBits( area, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed );

			byte* sData = (byte*)sourceData.Scan0;
			byte* dData = (byte*)destData.Scan0;

			for(int y = 0; y < source.Height; y++)
			{
				byte* sRow = sData;
				for(int x = 0; x < source.Width; x++ )
				{
					float bright = (sRow[x] * (1.0f/255.0f) - 0.5f) * contrast + 0.5f + brightness;
					if(bright < 0.0f) bright = 0.0f;
					if(bright > 1.0f) bright = 1.0f;

					dData[x] = (byte)(bright * 255.0f + 0.5f);
				}

				sData += sourceData.Stride;
				dData += destData.Stride;
			}
			source.UnlockBits( sourceData );
			adjustedImage.UnlockBits( destData );
		}
		*/


		private void rbOriginal_CheckedChanged( object sender, EventArgs e )
		{
			if( rbOriginal.Checked ) pbPreview_SizeChanged(sender, e);
		}

		private void rbPreview_CheckedChanged(object sender, EventArgs e)
		{
			if( rbPreview.Checked ) RedrawPreview(true, true, true );
		}



		void UpdateSettings()
		{
			if (rbHalftone.Checked) style = Style.Dots;
			else if (rbLines.Checked) style = Style.Lines;
			else if (rbSquares.Checked) style = Style.Squares;
			else if(rbCircles.Checked) style = Style.Circles;
			else if(rbRandom.Checked) style = Style.Random;

			workWidth = (double)udWidth.Value;
			workHeight = (double)udHeight.Value;
			border = (double)udBorder.Value;
			spacing = (double)udSpacing.Value;
			minSize = (double)udMinSize.Value;
			maxSize = (double)udMaxSize.Value;
			angle = (double)udAngle.Value * (PI / 180);
			dotCount = (int)udRandomDots.Value;
			wavelength = (double)udWavelength.Value;
			amplitude = (double)udAmplitude.Value;
			centerOffsX = (double)udCenterOffsX.Value;
			centerOffsY = -(double)udCenterOffsY.Value;

			offsetOdd = cbOffsetOdd.Checked && style == Style.Dots;
			invert = cbInvert.Checked;
			gammaCorrect = cbGammaCorrect.Checked;
			FixedSizes = cbFixedSize.Checked;
		}


		void ComputeDotLookup()
		{
			double RSquared = (maxSize * 0.5) * (maxSize * 0.5);
			double MaxArea = PI * RSquared;

			// Compute all 256 possible dot-to-circle conversions and store in a lookup
			dotLookup = new float[256];
			for (int i = 0; i < 256; i++)
			{
				double bright = (double)i * (1.0 / 255.0);
				if (gammaCorrect) {
					bright = Math.Pow(bright, 1.0 / 1.5);
				}

				bright = (bright - 0.5 + brightness) * contrast + 0.5;

				if (invert ^ negateImage) bright = 1.0 - bright;

				if (style == Style.Dots || style == Style.Random )
				{
					double dotArea = bright * MaxArea;
					double dotRadiusSqr = dotArea * InvPI;
					double dotRadius = Math.Sqrt( dotRadiusSqr );

					if(FixedSizes) {
						if(dotRadius < 2.0) dotRadius = 0.0;
						else if(dotRadius < 3.5) dotRadius = 3.0;
						else if(dotRadius < 4.5) dotRadius = 4.0;
						else dotRadius = 5.0;
					}
					dotLookup[i] = (float)(dotRadius * 2.0);
				}
				else
					dotLookup[i] = (float)(bright * maxSize);
			}
		}


		PointD Rotate( PointD pt, double ang )
		{
			double sn = Math.Sin( ang );
			double cs = Math.Cos( ang );

			PointD r = new PointD();
			r.X = cs * pt.X + sn * pt.Y;
			r.Y = -sn * pt.X + cs * pt.Y;

			return r;
		}

		bool IsLineStyle()
		{
			return style == Style.Lines || style == Style.Circles || style == Style.Squares;
		}

		unsafe void ComputePoints()
		{
			ComputePoints( false );
		}


		unsafe void ComputePoints( bool IsHighQuality )
		{
			points.Clear();

			double lineSpacing = spacing;
			double pointSpacing = spacing;
			if(IsLineStyle()) {
				pointSpacing *= 0.25;
				if(IsHighQuality) {
					pointSpacing *= 0.25;
				}
			}

			// Compute the size of a circle enclosing the work area
			// Compute a bounding rect to enclose that circle
			// The corners of that rect are the maximum area to work within, rotated about the work center

			// Transform the upper left corner of that rect as the start point
			// by rotating it about the center of the work piece and arbirary angles will work

			if(style == Style.Dots && offsetOdd) {
				pointSpacing *= 1.235;	// adjust horizontal spacing of rows to match adjusted offset between rows
			}

			double MaxRadius = Math.Sqrt( workHeight * workHeight + workWidth * workWidth ) * 0.5 + amplitude;
			PointD TL = new PointD(-MaxRadius, -MaxRadius);

			PointD Start = Rotate( TL, angle );
			PointD LineStepX = Rotate( new PointD( lineSpacing, 0 ), angle );
			PointD LineStepY = Rotate( new PointD( 0, lineSpacing ), angle );
			PointD LineTangentX = Rotate( new PointD( 0, 1 ), angle );
			PointD LineTangentY = Rotate( new PointD( 1, 0 ), angle );
			PointD StepX = Rotate( new PointD( pointSpacing, 0 ), angle );
			PointD StepY = Rotate( new PointD( 0, pointSpacing ), angle );

			Start.X = workWidth * 0.5 + Start.X;
			Start.Y = workHeight * 0.5 + Start.Y;


			// Values to scale from workspace into image space (inches to pixels)
			double scaleX = workWidth / (double)filterImage.Width;
			double scaleY = workHeight / (double)filterImage.Height;
			double invScaleX = 1.0 / scaleX;
			double invScaleY = 1.0 / scaleY;

			double waveStep = TwoPI / (wavelength / pointSpacing);
			bool OddLine = false;

			RectangleF workRect = new RectangleF( (float)border, (float)border, (float)(workWidth - border * 2.0), (float)(workHeight - border * 2.0) );
			
			ComputeDotLookup();
			MaxRadius += Math.Max( Math.Abs(centerOffsX), Math.Abs(centerOffsY) ) * 1.42;


			BitmapData sourceBits = filterImage.LockBits( new Rectangle( 0, 0, filterImage.Width, filterImage.Height ), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed );
			byte* sData = (byte*)sourceBits.Scan0;

			if(style == Style.Circles)
			{
				Start.X = workWidth * 0.5;
				Start.Y = workHeight * 0.5;

				// Draw circles from center, expanding in radius
				for(double rad = 0.0; rad < MaxRadius; rad += lineSpacing)
				{
					double x = Start.X + centerOffsX;
					double y = Start.Y + centerOffsY;
					bool GotRowIntersection = false;
					int lastStep = -2;

					int TotalSteps = (int)((rad * 2.0 * Math.PI) / pointSpacing + 0.5);

					double stepAngle = (TotalSteps * pointSpacing) / rad / TotalSteps;

					int step = 0;
					for(double circleAngle = 0.0; circleAngle < 2.0 * Math.PI; circleAngle += stepAngle, step++)
					{
						double xpt = x + Math.Sin( circleAngle ) * rad;
						double ypt = y + Math.Cos( circleAngle ) * rad;


						// If the current point is within the workspace (including the border)
						if(workRect.Contains( (float)xpt, (float)ypt ))
						{
							// See if the point + size will fit, if so, add it to the points list
							int imgX = (int)(xpt * invScaleX + 0.5);
							int imgY = (int)(ypt * invScaleY + 0.5);
							if(imgX < 0) imgX = 0;
							if(imgY < 0) imgY = 0;
							if(imgX >= filterImage.Width) imgX = filterImage.Width - 1;
							if(imgY >= filterImage.Height) imgY = filterImage.Height - 1;

							// Sample the image at this point and compute a brightness value
							byte pix = sData[imgX + imgY * sourceBits.Stride];
							float dotSize = dotLookup[pix];

							if(dotSize > minSize)
							{
								if(!GotRowIntersection)
								{
									points.NewRow();
									GotRowIntersection = true;
								}

								if(lastStep != step - 1)
								{
									points.NewLine();
								}

								points.AddPoint( new HTPoint( (float)xpt, (float)ypt, dotSize ) );
								lastStep = step;	// Record the last added point index
							}
						}
					}
				}
			}
			else if (style == Style.Squares)
			{
				Start.X = workWidth * 0.5;
				Start.Y = workHeight * 0.5;
				int RadSteps = 0;

				for(double rad = 0.0; rad < MaxRadius; rad += lineSpacing)
				{
					double x = Start.X + centerOffsX;
					double y = Start.Y + centerOffsY;
					bool GotRowIntersection = false;

					int HorizSteps = RadSteps * 4;
					int VertSteps = (RadSteps * 4) - 1;
					int TotalSteps = HorizSteps * 2 + VertSteps * 2;
					PointD CurrentTangent = LineTangentX;
					double waveAngle = (double)(-VertSteps / 2) * waveStep;

					int lastStep = -2;
					int phase = 0;
					for (int step = 0; step < TotalSteps; step++)
					{
						if (step < HorizSteps) {
						}
						else if (step < (HorizSteps + VertSteps)) {
							if (phase == 0) {
								waveAngle = (double)(-VertSteps / 2 + 1) * waveStep;
								lastStep = -2;
							}
							phase = 1;
						}
						else if (step < (HorizSteps * 2 + VertSteps))
						{
							if (phase == 1) {
								waveAngle = (double)(-HorizSteps / 2) * waveStep;
								lastStep = -2;
							}
							phase = 2;
						}
						else {
							if (phase == 2) {
								waveAngle = (double)(-VertSteps / 2 + 1) * waveStep;
								lastStep = -2;
							}
							phase = 3;
						}

						double waveMult = Math.Sin( waveAngle ) * amplitude;
						double xpt = x + CurrentTangent.X * waveMult;
						double ypt = y + CurrentTangent.Y * waveMult;

						// If the current point is within the workspace (including the border)
						if( workRect.Contains( (float)xpt, (float)ypt ) )
						{
							// See if the point + size will fit, if so, add it to the points list
							int imgX = (int)(xpt * invScaleX + 0.5);
							int imgY = (int)(ypt * invScaleY + 0.5);
							if( imgX < 0 ) imgX = 0;
							if( imgY < 0 ) imgY = 0;
							if(imgX >= filterImage.Width) imgX = filterImage.Width - 1;
							if(imgY >= filterImage.Height) imgY = filterImage.Height - 1;

							// Sample the image at this point and compute a brightness value
							byte pix = sData[imgX + imgY * sourceBits.Stride];
							float dotSize = dotLookup[pix];

							if( dotSize > minSize )
							{
								if (!GotRowIntersection) {
									points.NewRow();
									GotRowIntersection = true;
								}

								if(lastStep != step - 1) {
									points.NewLine();
								}

								points.AddPoint( new HTPoint( (float)xpt, (float)ypt, dotSize ) );
								lastStep = step;	// Record the last added point index
							}
						}

						if (step < HorizSteps) {
							x += StepX.X;
							y += StepX.Y;
							CurrentTangent = LineTangentX;
						}
						else if( step < (HorizSteps + VertSteps) )
						{
							x += StepY.X;
							y += StepY.Y;
							CurrentTangent = LineTangentY;
						}
						else if( step < (HorizSteps*2 + VertSteps) )
						{
							x -= StepX.X;
							y -= StepX.Y;
							CurrentTangent = LineTangentX;
						}
						else
						{
							x -= StepY.X;
							y -= StepY.Y;
							CurrentTangent = LineTangentY;
						}
						waveAngle += waveStep;
					}


					RadSteps+=2;
					Start.X -= LineStepX.X + LineStepY.X;
					Start.Y -= LineStepX.Y + LineStepY.Y;
				}
			}
			else if( style == Style.Dots || style == Style.Lines )
			{
				for(double fy = -MaxRadius; fy < MaxRadius; fy += lineSpacing)
				{
					double x = Start.X;
					double y = Start.Y;
					double waveAngle = 0.0;
					bool GotXIntersection = false;
					if (offsetOdd & OddLine) {
						x += StepX.X * 0.5;
						y += StepX.Y * 0.5;
						waveAngle += waveStep * 0.5;
					}

					// Used to detect a break in the line (used for line mode to move the tool)
					int lastStep = -2;
					int step = 0;

					for(double fx = -MaxRadius; fx < MaxRadius; fx += pointSpacing, step++)
					{
						double waveMult = Math.Sin( waveAngle ) * amplitude;
						double xpt = x + LineTangentX.X * waveMult;
						double ypt = y + LineTangentX.Y * waveMult;

						// If the current point is within the workspace (including the border)
						if( workRect.Contains( (float)xpt, (float)ypt ) )
						{
							// See if the point + size will fit, if so, add it to the points list
							int imgX = (int)(xpt * invScaleX + 0.5);
							int imgY = (int)(ypt * invScaleY + 0.5);
							if( imgX < 0 ) imgX = 0;
							if( imgY < 0 ) imgY = 0;
							if(imgX >= filterImage.Width) imgX = filterImage.Width - 1;
							if(imgY >= filterImage.Height) imgY = filterImage.Height - 1;

							// Sample the image at this point and compute a brightness value
							byte pix = sData[imgX + imgY * sourceBits.Stride];
							float dotSize = dotLookup[pix];

							if( dotSize > minSize )
							{
								if (!GotXIntersection) {
									points.NewRow();
									GotXIntersection = true;
								}

								if(lastStep != step - 1) {
									points.NewLine();
								}

								points.AddPoint( new HTPoint( (float)xpt, (float)ypt, dotSize ) );
								lastStep = step;	// Record the last added point index
							}
						}

						x += StepX.X;
						y += StepX.Y;
						waveAngle += waveStep;
					}

					// Move the starting point by the lineStep
					Start.X += LineStepY.X;
					Start.Y += LineStepY.Y;
					OddLine = !OddLine;
				}
			}
			else if(style == Style.Random)
			{
				Random r = new Random( 0 );

				int generatedDots = 0;
				while(generatedDots < dotCount)
				{
					// Compute a random dot location
					float x = workRect.Left + (float)(r.NextDouble() * workRect.Width);
					float y = workRect.Top + (float)(r.NextDouble() * workRect.Height);

					// Compute that location on the image itself
					int imgX = (int)(x * invScaleX + 0.5);
					int imgY = (int)(y * invScaleY + 0.5);
					if(imgX < 0) imgX = 0;
					if(imgY < 0) imgY = 0;
					if(imgX >= filterImage.Width) imgX = filterImage.Width - 1;
					if(imgY >= filterImage.Height) imgY = filterImage.Height - 1;

					// Sample the image at this point
					byte pix = sData[imgX + imgY * sourceBits.Stride];

					// add dot to list
					points.AddPoint( new HTPoint( (float)x, (float)y, dotLookup[pix] ) );
					generatedDots++;
				}
			}


			filterImage.UnlockBits( sourceBits );
		}


		void OptimizePoints()
		{
			if(style != Style.Lines && style != Style.Squares && style != Style.Circles ) return;
			float xyThresh = 0.0025f;
			float wThresh = 0.0005f;

			if(imperial == false) {	//metric units? (make tolerances larger to account for smaller units)
				xyThresh *= 25.0f;
				wThresh *= 25.0f;
			}

			foreach(HTRow row in points.Rows)
			{
				foreach(HTLine line in row.Lines)
				{
					int i = 1;
					while( i < line.Points.Count-1 )
					{
						// check to see if this point is a lerp between its neighbors, if so, delete it

						HTPoint p0 = line.Points[i-1];
						HTPoint p1 = line.Points[i];
						HTPoint p2 = line.Points[i+1];

						HTPoint delt1 = p2 - p0;	// Delta between neighbor points
						HTPoint delt2 = p1 - p0;	// Delta between this point and left point

						float len1 = delt1.Length;	// Distance between neighbors
						float len2 = delt2.Length;	// Distance between this point and left point

						float ratio = len2 / len1;	// Ratio of those distances

						// If p1 is linearly between p0 and p2, a lerp along the path from p0 to p2 based on the ratio of distances
						// should produce p1.

						HTPoint interp = p0 + delt1 * ratio;	// Compute a lerp between p0 to p2 based on the ratio of distances
						HTPoint delt = interp - p1;

						if(Math.Abs( delt.x ) < xyThresh && Math.Abs( delt.y ) < xyThresh && Math.Abs( delt.w ) < wThresh ) {
							line.Points.RemoveAt( i );
						}
						else {
							i++;
						}
					}
				}
			}
		}


		private void udWidth_ValueChanged(object sender, EventArgs e)
		{
			if(fileImage != null && fileImage.Width > 0 && InternalChange == false )
			{
				if(cbLockAspect.Checked)
				{
					InternalChange = true;
					float aspect = (float)fileImage.Height / (float)fileImage.Width;
					udHeight.Value = udWidth.Value * (decimal)aspect;
					InternalChange = false;
					RedrawPreview( false, true, true );
				}
				else {
					RedrawPreview( true, true, true );
				}
			}
		}

		private void udHeight_ValueChanged(object sender, EventArgs e)
		{
			if (fileImage != null && fileImage.Height > 0 && InternalChange == false )
			{
				if(cbLockAspect.Checked)
				{
					InternalChange = true;
					float aspect = (float)fileImage.Width / (float)fileImage.Height;
					udWidth.Value = udHeight.Value * (decimal)aspect;
					InternalChange = false;
					RedrawPreview( false, true, true );
				}
				else {
					RedrawPreview( true, true, true );
				}
			}
		}


		private void cbLockAspect_CheckedChanged( object sender, EventArgs e )
		{
			if(cbLockAspect.Checked == true)
			{
				if(fileImage != null && fileImage.Width > 0 && InternalChange == false)
				{
					InternalChange = true;
					float aspect = (float)fileImage.Height / (float)fileImage.Width;
					udHeight.Value = udWidth.Value * (decimal)aspect;
					InternalChange = false;
					RedrawPreview( true, true, true );
				}
			}
		}


		private void Controls_ValueChanged(object sender, EventArgs e)
		{
			if(InternalChange) return;

			cbOffsetOdd.Enabled = (rbHalftone.Checked == true);
			cbTwoPassCuts.Enabled = (rbHalftone.Checked == false);

			if(rbCircles.Checked == true || rbSquares.Checked == true)
			{
				udCenterOffsX.Enabled = true;
				udCenterOffsY.Enabled = true;
			}
			else {
				udCenterOffsX.Enabled = false;
				udCenterOffsY.Enabled = false;
			}

			if( rbPreview.Checked == false ) {
				rbPreview.Checked = true;	// Triggers a full redraw of the preview
			}
			else
				RedrawPreview(false, true, false);
		}

		private void udSpacing_ValueChanged(object sender, EventArgs e)
		{
			if (InternalChange) return;
			RedrawPreview(false, true, true);
		}


		private void btnWriteDXF_Click(object sender, EventArgs e)
		{
			WriteDXF();
		}

		private void btnWriteGCode_Click(object sender, EventArgs e)
		{
			OptimizePoints();
			WriteGCode();
			ComputePoints();
		}


		void WriteDXF()
		{
			WriteDXF( null );
		}


		void WriteLine( StreamWriter file, double d )
		{
			file.WriteLine( d.ToString( us ) );
		}

		void WriteLine( StreamWriter file, int d )
		{
			file.WriteLine( d.ToString( us ) );
		}

		void Write( StreamWriter file, double d )
		{
			file.Write( d.ToString( us ) );
		}

		void Write( StreamWriter file, int d )
		{
			file.Write( d.ToString( us ) );
		}



		void WriteDXF( string filename )
		{
			// Choose DXF file
			SaveFileDialog dlg = new SaveFileDialog();
			dlg.Filter = "DXF files|*.dxf|PNG files|*.png|All files (*.*)|*.*";
			dlg.FilterIndex = 1;
			dlg.AddExtension = true;
			dlg.DefaultExt = "dxf";
			dlg.OverwritePrompt = true;

			dlg.FileName = filename;

			if( filename != null || dlg.ShowDialog() == DialogResult.OK )
			{
				// Output points as circles to DXF or PNG file

				fileMode = dlg.FileName.ToLower().EndsWith( ".png" ) ? FileMode.PNG : FileMode.DXF;

				if(fileMode == FileMode.PNG)
				{
					// Recompute in high-quality mode (extra points)
					ComputePoints( true );

					// Figure out the correct size to make the "preview" image for 600dpi

					int pixWidth, pixHeight;
					if(imperial)
					{
						pixWidth = (int)(workWidth * 600.0);
						pixHeight = (int)(workHeight * 600.0);
					}
					else
					{
						pixWidth = (int)(workWidth * 600.0 / 25.4);
						pixHeight = (int)(workHeight * 600.0 / 25.4);
					}

					if(previewImage != null) {
						previewImage.Dispose();
					}

					previewImage = new Bitmap( pixWidth, pixHeight, System.Drawing.Imaging.PixelFormat.Format32bppRgb );
					previewImage.SetResolution( 600.0f, 600.0f );

					// redraw it
					DrawPreviewCircles();

					// save it
					previewImage.Save( dlg.FileName, ImageFormat.Png );

					// discard it
					previewImage.Dispose();
					previewImage = null;

					// invalidate the preview window
					RedrawPreview( false, true, false );
					return;
				}

				// Create the file
				StreamWriter file = new StreamWriter(dlg.FileName, false, Encoding.ASCII);

				// Write the header
				file.WriteLine( 0 );
				file.WriteLine( "SECTION" );
				file.WriteLine( 2 );
				file.WriteLine( "ENTITIES" );

				if (style == Style.Dots)
				{
					foreach (HTRow row in points.Rows)
					{
						foreach (HTLine line in row.Lines)
						{
							// Write the points (circles)
							foreach (HTPoint pt in line.Points) {
								WriteCircle( file, pt );
							}
						}
					}
				}
				else if ( IsLineStyle() )
				{
					foreach (HTRow row in points.Rows)
					{
						foreach (HTLine line in row.Lines)
						{
							// Compute the outline for this line using "connected circles

							if(line.Points.Count == 1) {
								WriteCircle( file, line.Points[0] );
							}
							else
							{
								PointD ptTop, ptBot;
								PointD prevTop = new PointD(0,0), prevBot = new PointD(0,0);

								List<string> returnLines = new List<string>();	// This list stores lines for the way back


								for( int p=0; p<line.Points.Count; p++ )
								{
									// Generate the body...
									// ------------------------

									HTPoint pt = line.Points[p];

									double lineAngleToPrev = 0;
									double radAngleToPrev = 0;
									double lineAngleToNext = 0;
									double radAngleToNext = 0;

									// Compute the angle between this point and the previous
									if(p > 0)
										ComputeAngleBetween(line.Points[p - 1], line.Points[p], out lineAngleToPrev, out radAngleToPrev);

									// Compute the angle between this point and the next
									if(p < line.Points.Count - 1)
										ComputeAngleBetween(line.Points[p], line.Points[p + 1], out lineAngleToNext, out radAngleToNext);


									if(p == 0)
									{
										// Generate the start end cap
											// "Half circle" arc oriented at the angle to the next point
										double startAng = lineAngleToNext + HalfPI + radAngleToNext;
										double endAng = lineAngleToNext + PI + HalfPI - radAngleToNext;

										ptTop = ArcPos( pt, startAng );
										ptBot = ArcPos( pt, endAng );

										WriteArc( file, pt, startAng, endAng );
									}
									else if(p == line.Points.Count - 1)
									{
										// Generate the finish end cap
											// "Half circle" arc oriented at the angle from the last point

										double startAng = lineAngleToPrev + HalfPI + radAngleToPrev;
										double endAng = lineAngleToPrev + PI + HalfPI - radAngleToPrev;

										ptTop = ArcPos( pt, startAng );
										ptBot = ArcPos( pt, endAng );

										WriteLine( file, prevTop, ptTop );
										WriteLine( file, prevBot, ptBot );

										WriteArc( file, pt, endAng, startAng );
									}
									else
									{
										// For both top & bottom
											// If angleDiff > 0 compute the entry point, exit point, and arc
											// If angleDiff < 0, compute intersection between incoming & outgoing lines

										double topFromA = HalfPI + lineAngleToPrev + radAngleToPrev;
										double botFromA = PI + HalfPI + lineAngleToPrev - radAngleToPrev;

										double topToA = lineAngleToNext + HalfPI + radAngleToNext;
										double botToA = lineAngleToNext + PI + HalfPI - radAngleToNext;

										ptTop = ArcPos( pt, topFromA );
										ptBot = ArcPos( pt, botFromA );

										PointD ptTopExit = ArcPos( pt, topToA );
										PointD ptBotExit = ArcPos( pt, botToA );


										if(topToA > topFromA) {
											// Need to intersect the top entry/exit lines
											PointD inter, ptNextTop = ArcPos( line.Points[p + 1], topToA );

											if(DoLinesIntersect( prevTop, ptTop, ptTopExit, ptNextTop, out inter )) {
												ptTop = inter;
												ptTopExit = new PointD( inter.X, inter.Y );
											}
										}
										else if( (topFromA - topToA) >= 0.00005f )
										{
											// Write the arc to fill the gap between top entry & exit lines
											WriteArc( file, pt, topToA, topFromA );
										}


										if(botFromA > botToA) {
											// Need to intersect the bottom entry/exit lines
											PointD inter, ptNextBot = ArcPos( line.Points[p + 1], botToA );

											if(DoLinesIntersect( prevBot, ptBot, ptBotExit, ptNextBot, out inter )) {
												ptBot = inter;
												ptBotExit = new PointD( inter.X, inter.Y );
											}
										}
										else if((botToA - botFromA) >= 0.00005f)
										{
											// Write the arc to fill the gap between top entry & exit lines
											WriteArc( file, pt, botFromA, botToA );
										}


										// Generate the top line from the previous exit point to the current entry point
										WriteLine( file, prevTop, ptTop );

										// Generate the bottom line from the previous exit point to the current entry point
										WriteLine( file, prevBot, ptBot );

										// Test circle
										//WriteCircle( file, pt );


										ptTop = ptTopExit;
										ptBot = ptBotExit;
									}

									prevTop = ptTop;
									prevBot = ptBot;
								}
							}
						}
					}
				}

				// Write the footer
				file.WriteLine( 0 );
				file.WriteLine( "ENDSEC" );
				file.WriteLine( 0 );
				file.WriteLine( "EOF" );

				// Close the file
				file.Flush();
				file.Close();
			}
		}

		void ComputeAngleBetween(HTPoint a, HTPoint b, out double lineAngle, out double radAngle )
		{
			double dx = b.x - a.x;
			double dy = -(b.y - a.y);
			double Dist = Math.Sqrt(dx * dx + dy * dy);

			lineAngle = Math.Atan2(dy, dx);
			if(lineAngle < 0.0) lineAngle += TwoPI;
			double ra = a.w * 0.5;
			double rb = b.w * 0.5;
			radAngle = Math.Atan((rb - ra) / Dist);
		}

		PointD ArcPos( HTPoint pt, double angle )
		{
			PointD result = new PointD();
			result.X = pt.x + Math.Cos( angle ) * pt.w * 0.5;
			result.Y = pt.y - Math.Sin( angle ) * pt.w * 0.5;
			return result;
		}


		/// <summary>
		/// This is based off an explanation and expanded math presented by Paul Bourke:
		/// 
		/// It takes two lines as inputs and returns true if they intersect, false if they 
		/// don't.
		/// If they do, ptIntersection returns the point where the two lines intersect.  
		/// </summary>
		/// <param name="L1">The first line</param>
		/// <param name="L2">The second line</param>
		/// <param name="ptIntersection">The point where both lines intersect (if they do).</param>
		/// <returns></returns>
		/// <remarks>See http://local.wasp.uwa.edu.au/~pbourke/geometry/lineline2d/</remarks>
		bool DoLinesIntersect( PointD L11, PointD L12, PointD L21, PointD L22, out PointD ptIntersection )
		{
			ptIntersection = new PointD( 0, 0 );

			// Denominator for ua and ub are the same, so store this calculation
			double d =
			   (L22.Y - L21.Y) * (L12.X - L11.X)
			   -
			   (L22.X - L21.X) * (L12.Y - L11.Y);

			//n_a and n_b are calculated as seperate values for readability
			double n_a =
			   (L22.X - L21.X) * (L11.Y - L21.Y)
			   -
			   (L22.Y - L21.Y) * (L11.X - L21.X);

			double n_b =
			   (L12.X - L11.X) * (L11.Y - L21.Y)
			   -
			   (L12.Y - L11.Y) * (L11.X - L21.X);

			// Make sure there is not a division by zero - this also indicates that
			// the lines are parallel.  
			// If n_a and n_b were both equal to zero the lines would be on top of each 
			// other (coincidental).  This check is not done because it is not 
			// necessary for this implementation (the parallel check accounts for this).
			if(d == 0)
				return false;

			// Calculate the intermediate fractional point that the lines potentially intersect.
			double ua = n_a / d;
			double ub = n_b / d;

			// The fractional point will be between 0 and 1 inclusive if the lines
			// intersect.  If the fractional calculation is larger than 1 or smaller
			// than 0 the lines would need to be longer to intersect.
			if(ua >= 0d && ua <= 1d && ub >= 0d && ub <= 1d)
			{
				ptIntersection.X = L11.X + (ua * (L12.X - L11.X));
				ptIntersection.Y = L11.Y + (ua * (L12.Y - L11.Y));
				return true;
			}
			return false;
		}



		void WriteCircle( StreamWriter file, HTPoint pt )
		{
			WriteLine( file, 0 );
			file.WriteLine( "CIRCLE" );

			WriteLine( file, 8 );			// Group code for layer name
			WriteLine( file, 0 );			// Layer number

			WriteLine( file, 10 );			// Center point of circle (x,y,x = 10,20,30)
			WriteLine( file, pt.x );			// X in OCS coordinates
			WriteLine( file, 20 );
			WriteLine( file, workHeight - pt.y );	// Y in OCS coordinates
			WriteLine( file, 30 );
			WriteLine( file, 0.0 );			// Z in OCS coordinates

			WriteLine( file, 40 );			// radius of circle
			WriteLine( file, pt.w * 0.5f );
		}


		void WriteArc( StreamWriter file, HTPoint pt, double startAngle, double endAngle )
		{
			WriteLine( file, 0 );
			file.WriteLine( "ARC" );

			WriteLine( file, 8 );			// Group code for layer name
			WriteLine( file, 0 );			// Layer number

			WriteLine( file, 10 );			// Center point of circle (x,y,x = 10,20,30)
			WriteLine( file, pt.x );			// X in OCS coordinates
			WriteLine( file, 20 );
			WriteLine( file, workHeight - pt.y );	// Y in OCS coordinates
			WriteLine( file, 30 );
			WriteLine( file, 0.0 );			// Z in OCS coordinates

			WriteLine( file, 40 );			// radius of circle
			WriteLine( file, pt.w * 0.5f );

			WriteLine( file, 50 );			// Start angle
			WriteLine( file, startAngle * (180.0f / PI) );

			WriteLine( file, 51 );			// End angle
			WriteLine( file, endAngle * (180.0f / PI) );
		}


		void WriteLine( StreamWriter file, PointD from, PointD to )
		{
			WriteLine( file, 0 );
			file.WriteLine( "LINE" );

			WriteLine( file, 8 );			// Group code for layer name
			WriteLine( file, 0 );			// Layer number

			WriteLine( file, 10 );			// Center point of circle (x,y,x = 10,20,30)
			WriteLine( file, from.X );			// X in OCS coordinates
			WriteLine( file, 20 );
			WriteLine( file, workHeight - from.Y );	// Y in OCS coordinates
			WriteLine( file, 30 );
			WriteLine( file, 0.0 );			// Z in OCS coordinates

			WriteLine( file, 11 );			// Center point of circle (x,y,x = 10,20,30)
			WriteLine( file, to.X );			// X in OCS coordinates
			WriteLine( file, 21 );
			WriteLine( file, workHeight - to.Y );	// Y in OCS coordinates
			WriteLine( file, 31 );
			WriteLine( file, 0.0 );			// Z in OCS coordinates
		}


		void WriteGCode()
		{
			double safeZ = (double)udSafeZ.Value;
			double pointRetract = (double)udPointRetract.Value;
			double toolAngle = (double)udToolAngle.Value * (PI / 180.0) * 0.5;
			double tanAngle = Math.Tan( toolAngle );
			double feedRate = (double)udFeedRate.Value;
			double spindleSpeed = (double)udSpindleSpeed.Value;
			double engraveDepth = (double)udEngraveDepth.Value;

			double originX = (double)udOriginX.Value;
			double originY = (double)udOriginY.Value;
			double zeroZ = (double)udZOffset.Value;
			bool twoPassCuts = cbTwoPassCuts.Checked;
			bool addLineNumbers = cbIncludeLineNumbers.Checked;
			bool GRBLCode = cbGrblCompat.Checked;
			if(GRBLCode) addLineNumbers = false;

			// If the user doesn't want a specific per-point retract value, just use the Safe Z
			if (cbPointRetract.Checked == false) {
				pointRetract = safeZ;
			}


			// Choose TXT file
			SaveFileDialog dlg = new SaveFileDialog();
			dlg.Filter = "Text files|*.txt|All files (*.*)|*.*";
			dlg.FilterIndex = 1;
			dlg.AddExtension = true;
			dlg.DefaultExt = "txt";
			dlg.OverwritePrompt = true;

			if (dlg.ShowDialog() == DialogResult.OK)
			{
				// Output points as GCode to TXT file

				// Create the file
				StreamWriter file = new StreamWriter(dlg.FileName, false, Encoding.ASCII);

				// Write the header, including comments, machine settings, etc

				int LineNum = 10;

				// Rapid Move(00), Inches(20)/Metric(21), XY Plane(17), Absolute mode(90), Radius Comp off(40), Tool length comp off(49), Cancel canned cycle(80)
				int UseMetric = rbInches.Checked ? 0 : 1;
				if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );

				if(GRBLCode) {
					file.WriteLine( "G00 G2{0} G17 G90 G40", UseMetric );
				}
				else {
					file.WriteLine( "G00 G2{0} G17 G90 G40 G49 G80", UseMetric );

					// Select Tool #1, auto-change
					if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
					file.WriteLine( "T1 M06" );
				}


				// Goto SafeZ rapid
				if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
				file.WriteLine( "G00 Z{0}", safeZ.ToString( us ) );

				if(GRBLCode == false) {
					// Spindle start
					if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
					file.WriteLine( "S{0} M03", spindleSpeed.ToString( us ) );
				}

				string XYFmt = UseMetric == 1 ? "0.00" : "0.0000";
				string ZFmt = UseMetric == 1 ? "0.000" : "0.0000";

				bool OddRow = false;

				if (style == Style.Dots)
				{
					// Write the points
					foreach (HTRow row in points.Rows)
					{
						int lineStart, lineEnd;
						int lineStep;
						if (OddRow == false) {
							lineStart = 0;
							lineEnd = row.Lines.Count;
							lineStep = 1;
						}
						else {
							lineStart = row.Lines.Count - 1;
							lineEnd = -1;
							lineStep = -1;
						}

						for( int lineIndex=lineStart; lineIndex != lineEnd; lineIndex += lineStep )
						{
							HTLine line = row.Lines[lineIndex];

							int pointStart, pointEnd;
							int pointStep;
							if (OddRow == false) {
								pointStart = 0;
								pointEnd = line.Points.Count;
								pointStep = 1;
							}
							else {
								pointStart = line.Points.Count - 1;
								pointEnd = -1;
								pointStep = -1;
							}

							for (int pointIndex = pointStart; pointIndex != pointEnd; pointIndex += pointStep)
							{
								HTPoint pt = line.Points[pointIndex];

								// Rapid GotoXY  (+ originX, originY)
								double X = pt.x + originX;
								double Y = (workHeight - pt.y) + originY;

								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G00 X{0} Y{1}", X.ToString( XYFmt, us ), Y.ToString( XYFmt, us ) );

								double Depth = (pt.w * 0.5) / tanAngle + engraveDepth;

								// Rapid Goto Z0.0
								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G00 Z{0}", zeroZ.ToString( ZFmt, us ) );

								// Feed Goto -Depth
								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G01 Z{0} F{1}", (zeroZ - Depth).ToString( ZFmt, us ), feedRate.ToString( us ) );

								// Rapid Goto Point Retract depth
								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G00 Z{0}", pointRetract.ToString( us ) );
							}
						}

						OddRow = !OddRow;
					}
				}
				else if (IsLineStyle())
				{
					int RowPassShift = twoPassCuts ? 1 : 0;

					// Write the lines
					for(int iRow = 0; iRow < points.Rows.Count << RowPassShift; iRow++)
					{
						HTRow row = points.Rows[ iRow >> RowPassShift ];

						int lineStart, lineEnd;
						int lineStep;
						if (OddRow == false) {
							lineStart = 0;
							lineEnd = row.Lines.Count;
							lineStep = 1;
						}
						else {
							lineStart = row.Lines.Count - 1;
							lineEnd = -1;
							lineStep = -1;
						}

						for( int lineIndex=lineStart; lineIndex != lineEnd; lineIndex += lineStep )
						{
							HTLine line = row.Lines[lineIndex];
							if( line.Points.Count != 0 )
							{
								int pointStart, pointEnd;
								int pointStep;
								if (OddRow == false) {
									pointStart = 0;
									pointEnd = line.Points.Count;
									pointStep = 1;
								}
								else {
									pointStart = line.Points.Count - 1;
									pointEnd = -1;
									pointStep = -1;
								}

								HTPoint firstPt = line.Points[pointStart];

								double X = firstPt.x + originX;
								double Y = (workHeight - firstPt.y) + originY;

								// Rapid GotoXY line start (+ originX, originY)
								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G00 X{0} Y{1}", X.ToString( XYFmt, us ), Y.ToString( XYFmt, us ) );

								// Rapid Goto Z0.0
								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G00 Z{0}", zeroZ.ToString( ZFmt, us ) );

								double Depth = (firstPt.w * 0.5) / tanAngle + engraveDepth;

								if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
								file.WriteLine( "G1 X{0} Y{1} Z{2} F{3}", 
									X.ToString( XYFmt, us ), Y.ToString( XYFmt, us ), (zeroZ - Depth).ToString( ZFmt, us ), feedRate.ToString( us ) );

								for( int pointIndex=pointStart; pointIndex != pointEnd; pointIndex += pointStep )
								{
									HTPoint pt = line.Points[pointIndex];

									Depth = (pt.w * 0.5) / tanAngle + engraveDepth;

									X = pt.x + originX;
									Y = (workHeight - pt.y) + originY;

									// Feed Goto X,Y, -Depth
									if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
									file.WriteLine( "G1 X{0} Y{1} Z{2}",
										X.ToString(XYFmt,us), Y.ToString(XYFmt,us), (zeroZ-Depth).ToString(ZFmt,us));
								}

								if(twoPassCuts == false || OddRow || (lineIndex + lineStep != lineEnd))
								{
									// Rapid Goto Point Retract depth
									if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
									file.WriteLine( "G00 Z{0}", pointRetract.ToString( us ) );
								}
							}
						}
						OddRow = !OddRow;
					}
				}

				if(GRBLCode == false) {
					// Spindle stop
					if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
					file.WriteLine( "M05" );
				}

				// Rapid Goto Safe Z depth
				if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
				file.WriteLine( "G00 Z{0}", safeZ.ToString( us ) );

				// Write the program stop
				if(addLineNumbers) file.Write( "N{0}0 ", LineNum++ );
				file.WriteLine( "M30" );

				// Close the file
				file.Flush();
				file.Close();
			}
		}

		private void cbPointRetract_CheckedChanged(object sender, EventArgs e)
		{
			udPointRetract.Enabled = cbPointRetract.Checked;
		}


		private void rbInches_CheckedChanged(object sender, EventArgs e)
		{
			ConfigureUnits(rbInches.Checked == true);
		}

		void ConfigureUpDown(NumericUpDown udCtrl, Decimal min, Decimal max, int places, Decimal step)
		{
			Decimal prevValue = udCtrl.Value;

			udCtrl.BeginInit();
			udCtrl.Minimum = min;
			udCtrl.Maximum = max;
			udCtrl.DecimalPlaces = places;
			udCtrl.Increment = step;

			Decimal newVal = imperial ? (prevValue / (decimal)25.4) : (prevValue * (decimal)25.4);
			if (newVal < min) newVal = min;
			if (newVal > max) newVal = max;
			udCtrl.Value = newVal;

			udCtrl.EndInit();
		}

		void ConfigureUnits(bool UseImperial)
		{
			InternalChange = true;

			imperial = UseImperial;
			if( imperial )
			{
				ConfigureUpDown(udWidth, 4, 250, 3, 1);
				ConfigureUpDown(udHeight, 4, 250, 3, 1);
				ConfigureUpDown(udCenterOffsX, -500, 500, 3, 1);
				ConfigureUpDown(udCenterOffsY, -500, 500, 3, 1);

				ConfigureUpDown(udBorder, 0, 250, 3, (decimal)0.125);
				ConfigureUpDown(udSpacing, (decimal)0.001, 10, 4, (decimal)0.01);
				ConfigureUpDown(udMinSize, 0, 10, 4, (decimal)0.01);
				ConfigureUpDown(udMaxSize, (decimal)0.001, 10, 4, (decimal)0.01);

				ConfigureUpDown(udWavelength, (decimal)0.1, 20, 3, (decimal)0.125);
				ConfigureUpDown(udAmplitude, -20, 20, 3, (decimal)0.01);

				ConfigureUpDown(udSafeZ, 0, 10, 4, (decimal)0.0625);
				ConfigureUpDown(udPointRetract, 0, 10, 4, (decimal)0.0625);

				lblFeedRate.Text = "Feed (Inch/min)";
				ConfigureUpDown(udFeedRate, (decimal)0.1, 1000, 1, 10);

				ConfigureUpDown( udEngraveDepth, 0, 1, 4, (decimal)0.005 );

				ConfigureUpDown( udZOffset, 0, 5, 4, (decimal)0.0625 );

				ConfigureUpDown(udOriginX, -250, 250, 3, 1);
				ConfigureUpDown(udOriginY, -250, 250, 3, 1);
			}
			else
			{
				ConfigureUpDown(udWidth, 5, 6000, 0, 10);
				ConfigureUpDown(udHeight, 5, 6000, 0, 10);
				ConfigureUpDown(udCenterOffsX, -12000, 12000, 0, 10);
				ConfigureUpDown(udCenterOffsY, -12000, 12000, 0, 10);
				ConfigureUpDown(udBorder, 0, 6000, 3, 5);
				ConfigureUpDown(udSpacing, (decimal)0.05, 250, 2, (decimal)0.25);
				ConfigureUpDown(udMinSize, 0, 250, 2, (decimal)0.25);
				ConfigureUpDown(udMaxSize, (decimal)0.05, 250, 2, (decimal)0.25);

				ConfigureUpDown(udWavelength, 1, 600, 1, (decimal)2.5);
				ConfigureUpDown(udAmplitude, -600, 600, 1, (decimal)0.25);

				ConfigureUpDown(udSafeZ, 0, 250, 1, 2);
				ConfigureUpDown(udPointRetract, 0, 250, 1, 2);

				lblFeedRate.Text = "Feed (mm/min)";
				ConfigureUpDown(udFeedRate, (decimal)1.0, 25000, 1, 250);

				ConfigureUpDown( udEngraveDepth, 0, 25, 2, (decimal)0.1 );

				ConfigureUpDown(udZOffset,  0, 150, 1, 2);
				ConfigureUpDown(udOriginX, -6000, 6000, 0, 20);
				ConfigureUpDown(udOriginY, -6000, 6000, 0, 20);
			}

			InternalChange = false;
			RedrawPreview(false, true, false);
		}


		string GetConfigName()
		{
			string exePath = Application.ExecutablePath;
			string cfgName = Path.GetDirectoryName(exePath);
			cfgName += "\\Halftoner.cfg";
			return cfgName;
		}


		void WriteSetting(XmlWriter cfg, string Name, string Value)
		{
			cfg.WriteStartElement(Name);
			cfg.WriteAttributeString("Value", Value);
			cfg.WriteEndElement();
		}

		void SaveSettings()
		{
			try {
				string cfgName = GetConfigName();
				XmlWriterSettings settings = new XmlWriterSettings();
				settings.NewLineChars = "\n";
				settings.Indent = true;

				XmlWriter cfg = XmlWriter.Create(cfgName, settings);

				cfg.WriteStartDocument();
				cfg.WriteStartElement("HalftoneConfig");

				// Save Inch/mm setting
				WriteSetting( cfg, "Units", rbInches.Checked ? "inch" : "mm" );

				// Save everything else
				WriteSetting(cfg, "Width", udWidth.Value.ToString());
				WriteSetting(cfg, "Height", udHeight.Value.ToString());
				WriteSetting(cfg, "Border", udBorder.Value.ToString());
				WriteSetting(cfg, "Spacing", udSpacing.Value.ToString());
				WriteSetting(cfg, "MinSize", udMinSize.Value.ToString());
				WriteSetting(cfg, "MaxSize", udMaxSize.Value.ToString());

				WriteSetting(cfg, "Angle", udAngle.Value.ToString());
				WriteSetting(cfg, "Wavelength", udWavelength.Value.ToString());
				WriteSetting(cfg, "Amplitude", udAmplitude.Value.ToString());
				WriteSetting(cfg, "CenterOffsX", udCenterOffsX.Value.ToString());
				WriteSetting(cfg, "CenterOffsY", udCenterOffsY.Value.ToString());

				WriteSetting(cfg, "DarkBoost", cbGammaCorrect.Checked.ToString());
				WriteSetting(cfg, "OffsetOdd", cbOffsetOdd.Checked.ToString());
				WriteSetting(cfg, "Invert", cbInvert.Checked.ToString());

				WriteSetting(cfg, "Style", style.ToString() );
				WriteSetting( cfg, "RandomDots", udRandomDots.Value.ToString() );

				WriteSetting(cfg, "SafeZ", udSafeZ.Value.ToString());
				WriteSetting(cfg, "UsePointRetract", cbPointRetract.Checked.ToString());
				WriteSetting(cfg, "PointRetract", udPointRetract.Value.ToString());

				WriteSetting(cfg, "FeedRate", udFeedRate.Value.ToString());
				WriteSetting(cfg, "ToolAngle", udToolAngle.Value.ToString());
				WriteSetting(cfg, "SpindleRPM", udSpindleSpeed.Value.ToString());

				WriteSetting( cfg, "EngraveDepth", udEngraveDepth.Value.ToString() );
				WriteSetting( cfg, "TwoPass", cbTwoPassCuts.Checked.ToString() );
				WriteSetting( cfg, "LineNumbers", cbIncludeLineNumbers.Checked.ToString() );
				WriteSetting( cfg, "GrblCompat", cbGrblCompat.Checked.ToString() );

				WriteSetting(cfg, "ZOffset", udZOffset.Value.ToString());
				WriteSetting(cfg, "OriginX", udOriginX.Value.ToString());
				WriteSetting(cfg, "OriginY", udOriginY.Value.ToString());

				WriteSetting( cfg, "LockAspect", cbLockAspect.Checked.ToString() );

				cfg.WriteEndElement();
				cfg.Flush();
				cfg.Close();
			}

			catch (Exception)
			{
			}
		}

		string GetStringSetting(XmlElement cfg, string Name)
		{
			XmlElement val = cfg[Name];
			if(val == null) return null;
			return val.GetAttribute("Value");
		}

		void GetBoolSetting(CheckBox cbControl, XmlElement cfg, string Name)
		{
			string str = GetStringSetting(cfg, Name);
			if(str != null) {
				cbControl.Checked = bool.Parse( str );
			}
		}

		void GetDecimalSetting(NumericUpDown udControl, XmlElement cfg, string Name)
		{
			string str = GetStringSetting(cfg, Name);
			if( str != null ) {
				udControl.Value = decimal.Parse(str);
			}
		}

		void LoadSettings()
		{
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(GetConfigName());

				XmlElement cfg = doc["HalftoneConfig"];
				if (cfg == null) return;

				// Set Inch/mm first
				string strUnit = GetStringSetting(cfg, "Units");
				if (strUnit == "inch") rbInches.Checked = true;
				if (strUnit == "mm") rbMillimeters.Checked = true;

				InternalChange = true;
				// Set everything else

				GetDecimalSetting( udWidth, cfg, "Width" );
				GetDecimalSetting( udHeight, cfg, "Height" );

				GetDecimalSetting( udBorder, cfg, "Border" );
				GetDecimalSetting( udSpacing, cfg, "Spacing" );
				GetDecimalSetting( udMinSize, cfg, "MinSize" );
				GetDecimalSetting( udMaxSize, cfg, "MaxSize" );

				GetDecimalSetting( udAngle, cfg, "Angle" );
				GetDecimalSetting( udWavelength, cfg, "Wavelength" );
				GetDecimalSetting( udAmplitude, cfg, "Amplitude" );
				GetDecimalSetting( udCenterOffsX, cfg, "CenterOffsX" );
				GetDecimalSetting( udCenterOffsY, cfg, "CenterOffsY" );

				GetBoolSetting( cbGammaCorrect, cfg, "DarkBoost" );
				GetBoolSetting( cbOffsetOdd, cfg, "OffsetOdd" );
				GetBoolSetting( cbInvert, cfg, "Invert" );

				GetDecimalSetting( udRandomDots, cfg, "RandomDots" );

				string styleString = GetStringSetting(cfg, "Style" );
				switch(styleString)
				{
					case "Dots": rbHalftone.Checked = true;
						style = Style.Dots;
						break;

					case "Lines": rbLines.Checked = true;
						style = Style.Lines;
						break;

					case "Squares":	rbSquares.Checked = true;
						style = Style.Squares;
						break;

					case "Circles": rbCircles.Checked = true;
						style = Style.Circles;
						break;
				}

				GetDecimalSetting( udSafeZ, cfg, "SafeZ" );
				GetBoolSetting( cbPointRetract, cfg, "UsePointRetract" );
				GetDecimalSetting( udPointRetract, cfg, "PointRetract" );

				GetDecimalSetting( udFeedRate, cfg, "FeedRate" );
				GetDecimalSetting( udToolAngle, cfg, "ToolAngle" );
				GetDecimalSetting( udSpindleSpeed, cfg, "SpindleRPM" );

				GetDecimalSetting( udEngraveDepth, cfg, "EngraveDepth" );
				GetBoolSetting( cbTwoPassCuts, cfg, "TwoPass" );
				GetBoolSetting( cbIncludeLineNumbers, cfg, "LineNumbers" );
				GetBoolSetting( cbGrblCompat, cfg, "GrblCompat" );

				GetDecimalSetting( udZOffset, cfg, "ZOffset" );
				GetDecimalSetting( udOriginX, cfg, "OriginX" );
				GetDecimalSetting( udOriginY, cfg, "OriginY" );

				GetBoolSetting( cbLockAspect, cfg, "LockAspect" );

				InternalChange = false;
			}

			catch (Exception)
			{
			}

			finally
			{
				InternalChange = false;
			}
		}


		private void btnTest_Click( object sender, EventArgs e )
		{
			WriteDXF( @"C:\Users\Jason\Desktop\TestDXF.dxf" );
		}

		private void Controls_AdjustmentsChanged( object sender, EventArgs e )
		{
			if(InternalChange) return;

			lblBright.Text = (tbBright.Value * 2).ToString();
			lblContrast.Text = (tbContrast.Value * 2).ToString();

			brightness = (float)tbBright.Value / 50.0f;
			contrast = (float)tbContrast.Value / 50.0f;

			contrast = (1.01f*(contrast + 1.0f)) / (1.01f - contrast);
			adjustmentsChanged = true;

			ComputeAdjustedLevels();

			if( rbPreview.Checked == false ) {
				rbPreview.Checked = true;	// Triggers a full redraw of the preview
			}
			else
				RedrawPreview(false, true, false);
		}


		private void btnAutoLevels_Click( object sender, EventArgs e )
		{
			// Figure out from the existing levels what a good contrast / bright setting would be
			float minLevel = 1.0f, maxLevel = 0.0f;

			for(int i = 0; i < 256; i++)
			{
				float f = (float)i / 255.0f;
				if(sourceLevels[i] > 0)
				{
					if(minLevel > f) minLevel = f;
					if(maxLevel < f) maxLevel = f;
				}
			}

			float mid = (minLevel + maxLevel) * 0.5f;
			float bright = 0.5f - mid;

			float range = maxLevel - minLevel;
			float cont = 1.0f / range;

			cont = (101.0f * (cont - 1.0f)) / (100.0f * cont + 101.0f);
			//cont = (1.01f * cont - 1.01f) / (cont + 1.01f);



			InternalChange = true;
			tbBright.Value = (int)(bright * 50.0f);
			tbContrast.Value = (int)(cont * 50.0f);
			InternalChange = false;

			Controls_AdjustmentsChanged( null, null );
		}

		private void cbNegateImage_CheckedChanged( object sender, EventArgs e )
		{
			negateImage = cbNegateImage.Checked;
			Controls_AdjustmentsChanged( null, null );
		}
	}
}
