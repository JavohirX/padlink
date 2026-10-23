using System;
using System.Drawing;
using System.Windows.Forms;
using QRCoder;

namespace NesReceiver;

/// <summary>
/// Main application window.
///
/// Layout (top to bottom):
///   - PictureBox (QR code, 300x300)
///   - Label: ws:// URL
///   - Label: Port
///   - Label: Player 1 status
///   - Label: Player 2 status
///   - Label: FCEUX status
///
/// A System.Windows.Forms.Timer fires every 2 seconds to re-scan for the FCEUX window.
/// </summary>
public sealed class MainForm : Form
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const int DefaultPort = 8787;
    private const int QrSize = 300;
    private const int FceuxScanIntervalMs = 2000;

    // -------------------------------------------------------------------------
    // Controls
    // -------------------------------------------------------------------------

    private readonly PictureBox _qrPictureBox;
    private readonly Label _urlLabel;
    private readonly Label _portLabel;
    private readonly Label _p1Label;
    private readonly Label _p2Label;
    private readonly Label _fceuxLabel;
    private readonly TextBox _logTextBox;

    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    private readonly WebSocketServer _server;
    private readonly System.Windows.Forms.Timer _fceuxTimer;
    private readonly string _wsUrl;
    private string _lastFceuxStatus = "";

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public MainForm()
    {
        // --- Determine URL ---
        string ip = NetworkHelper.GetLocalIpAddress();
        _wsUrl = $"ws://{ip}:{DefaultPort}";

        // --- Form properties ---
        Text = "Padlink NES";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 520);

        // --- QR code PictureBox ---
        _qrPictureBox = new PictureBox
        {
            Size = new Size(QrSize, QrSize),
            Location = new Point(30, 16),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // --- URL label ---
        _urlLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Width = 340,
            Height = 24,
            Location = new Point(10, _qrPictureBox.Bottom + 10),
            Font = new Font("Consolas", 11f, FontStyle.Bold),
            ForeColor = Color.Cyan,
            BackColor = Color.Transparent,
            Text = _wsUrl,
        };

        // --- Port label ---
        _portLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Width = 340,
            Height = 20,
            Location = new Point(10, _urlLabel.Bottom + 4),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            Text = $"Port: {DefaultPort}",
        };

        // --- Status labels ---
        int statusTop = _portLabel.Bottom + 14;

        _p1Label = CreateStatusLabel("Player 1: Not connected", statusTop);
        _p2Label = CreateStatusLabel("Player 2: Not connected", statusTop + 30);
        _fceuxLabel = CreateStatusLabel("FCEUX: Not detected", statusTop + 60);

        // --- Log console TextBox ---
        _logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(370, 16),
            Size = new Size(340, 488),
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.FixedSingle,
        };

        // Wire up static logs
        KeyboardInput.LogHandler = Log;
        FceusFocuser.LogHandler = Log;

        // --- Add controls ---
        Controls.AddRange(new Control[]
        {
            _qrPictureBox,
            _urlLabel,
            _portLabel,
            _p1Label,
            _p2Label,
            _fceuxLabel,
            _logTextBox,
        });

        // --- Generate QR code ---
        GenerateQrCode(_wsUrl);

        // --- FCEUX timer ---
        _fceuxTimer = new System.Windows.Forms.Timer { Interval = FceuxScanIntervalMs };
        _fceuxTimer.Tick += OnFceuxTimerTick;
        _fceuxTimer.Start();

        // Initial FCEUX scan
        FceusFocuser.RefreshFceux();

        // --- Start WebSocket server ---
        _server = new WebSocketServer(DefaultPort, this);
        _server.Start();

        // Push initial status after everything is set up
        _server.NotifyForm();

        // Handle form close
        FormClosing += OnFormClosing;
    }

    // -------------------------------------------------------------------------
    // Public API (called from WebSocketServer background threads)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Thread-safe logger for displaying events in the UI console log.
    /// </summary>
    public void Log(string message)
    {
        if (IsDisposed) return;

        void Apply()
        {
            _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        if (InvokeRequired)
            Invoke(Apply);
        else
            Apply();
    }

    /// <summary>
    /// Updates the status labels. Thread-safe — uses Invoke when called from a non-UI thread.
    /// </summary>
    public void UpdateStatus(string player1Status, string player2Status, string fceuxStatus)
    {
        if (IsDisposed) return;

        void Apply()
        {
            _p1Label.Text = $"Player 1: {player1Status}";
            _p2Label.Text = $"Player 2: {player2Status}";
            _fceuxLabel.Text = $"FCEUX: {fceuxStatus}";

            // Color-code status
            _p1Label.ForeColor = player1Status == "Connected" ? Color.LightGreen : Color.LightGray;
            _p2Label.ForeColor = player2Status == "Connected" ? Color.LightGreen : Color.LightGray;
            _fceuxLabel.ForeColor = fceuxStatus == "Found" ? Color.LightGreen : Color.OrangeRed;

            // Log status transitions
            if (_lastFceuxStatus != fceuxStatus)
            {
                _lastFceuxStatus = fceuxStatus;
                Log($"FCEUX status: {fceuxStatus}");
            }
        }

        if (InvokeRequired)
            Invoke(Apply);
        else
            Apply();
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void OnFceuxTimerTick(object? sender, EventArgs e)
    {
        FceusFocuser.RefreshFceux();
        // Update just the FCEUX portion of the status
        _server.NotifyForm();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _fceuxTimer.Stop();
        _server.Stop();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private Label CreateStatusLabel(string text, int top)
    {
        return new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 340,
            Height = 26,
            Location = new Point(10, top),
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            Text = text,
        };
    }

    private void GenerateQrCode(string url)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using QRCodeData qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new QRCode(qrData);
            // pixelsPerModule=10 → for a typical 29-module QR that's 290px, fitting nicely in 300px
            Bitmap bmp = qrCode.GetGraphic(pixelsPerModule: 10, darkColor: Color.Black, lightColor: Color.White, drawQuietZones: true);
            _qrPictureBox.Image = bmp;
        }
        catch (Exception ex)
        {
            // If QR generation fails, show the URL as a fallback
            Log($"[QR Error] {ex.Message}");
            _qrPictureBox.BackColor = Color.DarkGray;
        }
    }
}
