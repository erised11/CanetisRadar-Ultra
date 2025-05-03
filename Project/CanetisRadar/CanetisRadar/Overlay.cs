using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using IniParser;
using IniParser.Model;
using NAudio.CoreAudioApi;

namespace CanetisRadar
{
	// Token: 0x02000003 RID: 3
	public partial class Overlay : Form
	{
		// Token: 0x06000008 RID: 8
		[DllImport("user32.dll")]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		// Token: 0x06000009 RID: 9
		[DllImport("user32.dll")]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		// Token: 0x0600000A RID: 10 RVA: 0x0000277A File Offset: 0x0000097A

		private DateTime[] lastHighlightedTimestamps = new DateTime[12];

		public Overlay()
		{
			this.InitializeComponent();
		}

		// Token: 0x0600000B RID: 11 RVA: 0x000027BC File Offset: 0x000009BC
		private void Overlay_Load(object sender, EventArgs e)
		{
			base.TransparencyKey = Color.Turquoise;
			this.BackColor = Color.Turquoise;
			this.FormBorderStyle = FormBorderStyle.None;
			int initialStyle = Overlay.GetWindowLong(base.Handle, -20);
			Overlay.SetWindowLong(base.Handle, -20, initialStyle | 524288 | 32);
			base.WindowState = FormWindowState.Maximized;
			_radar = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
			base.TopMost = true;
			base.Opacity = 0.7;
			FileIniDataParser parser = new FileIniDataParser();
			IniData data = parser.ReadFile(AppDomain.CurrentDomain.BaseDirectory + "settings.ini");
			string sensitivityRaw = data["basic"]["sensitivity"];
			if (float.TryParse(sensitivityRaw, out float parsedSensitivity))
			{
				_sensitivity = Math.Max(0.1f, Math.Min(5.0f, parsedSensitivity));
			}
			this._multiplier = int.Parse(data["basic"]["multiplier"]);
			this._updateRate = int.Parse(data["basic"]["updateRate"]);
			this._delay = int.Parse(data["basic"]["delay"]);
			this._highlightDurationSeconds = int.Parse(data["sectionHighlights"]["highlightDurationSeconds"]);
			this._highlightSoundThreshold = int.Parse(data["sectionHighlights"]["highlightSoundThreshold"]);
			Thread t = new Thread(new ThreadStart(this.Loop));
			t.Start();
		}

		// Token: 0x0600000C RID: 12 RVA: 0x0000289C File Offset: 0x00000A9C
		private void Loop()
		{
			this._enumerator = new MMDeviceEnumerator();
			this._device = this._enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

			if (this._device.AudioMeterInformation.PeakValues.Count < 8)
			{
				MessageBox.Show("You are not using 7.1 audio device! Please look again at setup guide.", "No 7.1 audio detected!", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				Environment.Exit(-1);
			}

			while (true)
			{
				this.CreateEdgeOverlay(); // Draw bars based on current audio
				Thread.Sleep(this._updateRate); // From config
			}
		}


		private void CreateEdgeOverlay()
		{
			float threshold = 0.02f; // Minimum level to draw

			var peaks = this._device.AudioMeterInformation.PeakValues;

			int width = Screen.PrimaryScreen.Bounds.Width;
			int height = Screen.PrimaryScreen.Bounds.Height;
			int barThickness = 10;
			int topBarWidth = width / 3;

			// Create fresh overlay every frame (this clears previous content)
			Bitmap fullOverlay = new Bitmap(width, height);
			using (Graphics fullGrp = Graphics.FromImage(fullOverlay))
			{
				fullGrp.Clear(Color.Transparent); // Force clearing old bars

				// Channel index reference:
				// 0: Front Left, 1: Front Right, 2: Center, 3: LFE, 4: Rear Left, 5: Rear Right, 6: Side Left, 7: Side Right

				Action<int, Rectangle> drawBarIfActive = (channelIndex, rect) =>
				{
					if (channelIndex >= peaks.Count) return;

					float value = peaks[channelIndex];
					if (value < threshold) return; // Skip drawing if too quiet

					float scaled = value * _sensitivity; // Use actual audio meter value (0.0 to 1.0)
					Brush color = scaled < 0.33f ? Brushes.Green :
								  scaled < 0.66f ? Brushes.Yellow : Brushes.Red;

					fullGrp.FillRectangle(color, rect);
				};

				// FRONT LEFT – top left
				drawBarIfActive(0, new Rectangle(0, 0, topBarWidth, barThickness));

				// FRONT RIGHT – top right
				drawBarIfActive(1, new Rectangle(topBarWidth * 2, 0, topBarWidth, barThickness));

				// CENTER – top center
				drawBarIfActive(2, new Rectangle(topBarWidth, 0, topBarWidth, barThickness));

				// SIDE LEFT – full height
				drawBarIfActive(6, new Rectangle(0, 0, barThickness, height));

				// SIDE RIGHT – full height
				drawBarIfActive(7, new Rectangle(width - barThickness, 0, barThickness, height));

				// REAR LEFT – bottom left
				drawBarIfActive(4, new Rectangle(0, height - barThickness, width / 2, barThickness));

				// REAR RIGHT – bottom right
				drawBarIfActive(5, new Rectangle(width / 2, height - barThickness, width / 2, barThickness));
			}

			// Set the rendered image to the UI
			this.Invoke(new MethodInvoker(() =>
			{
				this.BackgroundImage = fullOverlay;
				this.BackgroundImageLayout = ImageLayout.Stretch;
			}));
		}

		private Point GetIntersectionPoint(Point p1, Point p2)
		{
			float t;
			if (p2.X - p1.X != 0)
			{
				t = Math.Min(Math.Max((-150 - p1.X) / (float)(p2.X - p1.X), (300 - p1.X) / (float)(p2.X - p1.X)), 1);
			}
			else
			{
				t = Math.Min(Math.Max((-150 - p1.Y) / (float)(p2.Y - p1.Y), (300 - p1.Y) / (float)(p2.Y - p1.Y)), 1);
			}

			return new Point((int)(p1.X + t * (p2.X - p1.X)), (int)(p1.Y + t * (p2.Y - p1.Y)));
		}

		// Method to check if a point is inside a triangle
		private bool IsPointInTriangle(Point pt, Point v1, Point v2, Point v3)
		{
			float d1, d2, d3;
			bool has_neg, has_pos;

			d1 = Sign(pt, v1, v2);
			d2 = Sign(pt, v2, v3);
			d3 = Sign(pt, v3, v1);

			has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
			has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

			return !(has_neg && has_pos);
		}

		private float Sign(Point p1, Point p2, Point p3)
		{
			return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
		}
		// Token: 0x0400000B RID: 11
		private MMDeviceEnumerator _enumerator;

		// Token: 0x0400000C RID: 12
		private MMDevice _device;

		// Token: 0x0400000D RID: 13
		private int _multiplier = 500;

		// Token: 0x0400000E RID: 14
		private int _updateRate = 50;

		private float _sensitivity = 0.5f;  // Default value

		// Highlighting Duration in Seconds
		private int _highlightDurationSeconds = 3;

		// Highlighting SoundThreshold
		private int _highlightSoundThreshold = 50;

		//Delay Time for visuals
		private int _delay = 5;

		// Token: 0x0400000F RID: 15
		private Bitmap _radar;

		// Token: 0x04000010 RID: 16
		public IntPtr ParentHandle;
	}
}
