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
			int width = Screen.PrimaryScreen.Bounds.Width;
			int height = Screen.PrimaryScreen.Bounds.Height;
			_overlayBitmap = new Bitmap(width, height);
			_overlayGraphics = Graphics.FromImage(_overlayBitmap);
			base.TransparencyKey = Color.Turquoise;
			this.BackColor = Color.Turquoise;
			this.FormBorderStyle = FormBorderStyle.None;
			int initialStyle = Overlay.GetWindowLong(base.Handle, -20);
			Overlay.SetWindowLong(base.Handle, -20, initialStyle | 524288 | 32);
			base.WindowState = FormWindowState.Maximized;
			base.TopMost = true;
			base.Opacity = 0.7;
			FileIniDataParser parser = new FileIniDataParser();
			IniData data = parser.ReadFile(AppDomain.CurrentDomain.BaseDirectory + "settings.ini");
			string sensitivityRaw = data["basic"]["sensitivity"];
			if (float.TryParse(sensitivityRaw, out float parsedSensitivity))
			{
				_sensitivity = Math.Max(0.1f, Math.Min(5.0f, parsedSensitivity));
			}
			this._updateRate = int.Parse(data["basic"]["updateRate"]);
			this._delay = int.Parse(data["basic"]["delay"]);
			this._highlightDurationSeconds = int.Parse(data["sectionHighlights"]["highlightDurationSeconds"]);
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

			lock (_graphicsLock)
			{
				_overlayGraphics.Clear(Color.Transparent);

				Action<int, Rectangle> drawBarIfActive = (channelIndex, rect) =>
				{
					if (channelIndex >= peaks.Count) return;

					float value = peaks[channelIndex];
					if (value < threshold) return;

					float scaled = value * _sensitivity;
					Brush color = scaled < 0.33f ? Brushes.Green :
								  scaled < 0.66f ? Brushes.Yellow : Brushes.Red;

					_overlayGraphics.FillRectangle(color, rect);
				};

				drawBarIfActive(0, new Rectangle(0, 0, topBarWidth, barThickness));                        // Front Left
				drawBarIfActive(1, new Rectangle(topBarWidth * 2, 0, topBarWidth, barThickness));          // Front Right
				drawBarIfActive(2, new Rectangle(topBarWidth, 0, topBarWidth, barThickness));              // Center
				drawBarIfActive(6, new Rectangle(0, 0, barThickness, height));                             // Side Left
				drawBarIfActive(7, new Rectangle(width - barThickness, 0, barThickness, height));          // Side Right
				drawBarIfActive(4, new Rectangle(0, height - barThickness, width / 2, barThickness));      // Rear Left
				drawBarIfActive(5, new Rectangle(width / 2, height - barThickness, width / 2, barThickness)); // Rear Right
			}

			// Update the overlay image
			this.Invoke(new MethodInvoker(() =>
			{
				lock (_graphicsLock)
				{
					this.BackgroundImage?.Dispose();  // Dispose old image to free memory
					this.BackgroundImage = (Bitmap)_overlayBitmap.Clone();  // Clone to avoid cross-thread use
					this.BackgroundImageLayout = ImageLayout.Stretch;
				}
			}));
		}
		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			_overlayGraphics?.Dispose();
			_overlayBitmap?.Dispose();
			base.OnFormClosing(e);
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


		//Delay Time for visuals
		private int _delay = 5;

		// Token: 0x0400000F RID: 15
		private Bitmap _overlayBitmap;
		private Graphics _overlayGraphics;
		private readonly object _graphicsLock = new object();

		// Token: 0x04000010 RID: 16
		public IntPtr ParentHandle;
	}
}
