using System;
using System.Drawing;
using System.Windows.Forms;
using QRCoder;

namespace BlastemReceiver;

/// <summary>
/// Main application window for Padlink Genesis.
///
/// Layout (top to bottom):
///   - PictureBox (QR code, 300x300)
///   - Label: ws:// URL
///   - Label: Port
///   - Label: Player 1 status
///   - Label: Player 2 status
///   - Label: BlastEm status
///
/// A Timer fires every 2 seconds to re-scan for the BlastEm window.
/// </summary>
public sealed class MainForm : Form
{
    private const int DefaultPort = 8788;
    private const int QrSize = 300;
    private const int ScanIntervalMs = 2000;

    private readonly PictureBox _qrPictureBox;
    private readonly Label _urlLabel;
    private readonly Label _portLabel;
    private readonly Label _p1Label;
    private readonly Label _p2Label;
    private readonly Label _blastemLabel;
    private readonly TextBox _logTextBox;

    private readonly WebSocketServer _server;
    private readonly System.Windows.Forms.Timer _scanTimer;
    private readonly string _wsUrl;
    private string _lastBlastemStatus = "";

    public MainForm()
    {
        string ip = NetworkHelper.GetLocalIpAddress();
        _wsUrl = $"ws://{ip}:{DefaultPort}";

        Text = "Padlink Genesis";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 520);

        _qrPictureBox = new PictureBox
        {
            Size = new Size(QrSize, QrSize),
            Location = new Point(30, 16),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };

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

        int statusTop = _portLabel.Bottom + 14;

        _p1Label      = CreateStatusLabel("Player 1: Not connected", statusTop);
        _p2Label      = CreateStatusLabel("Player 2: Not connected", statusTop + 30);
        _blastemLabel = CreateStatusLabel("BlastEm: Not detected",   statusTop + 60);

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

        KeyboardInput.LogHandler = Log;
        BlastemFocuser.LogHandler = Log;

        Controls.AddRange(new Control[]
        {
            _qrPictureBox,
            _urlLabel,
            _portLabel,
            _p1Label,
            _p2Label,
            _blastemLabel,
            _logTextBox,
        });

        GenerateQrCode(_wsUrl);

        _scanTimer = new System.Windows.Forms.Timer { Interval = ScanIntervalMs };
        _scanTimer.Tick += OnScanTimerTick;
        _scanTimer.Start();

        BlastemFocuser.RefreshBlastem();

        _server = new WebSocketServer(DefaultPort, this);
        _server.Start();
        _server.NotifyForm();

        FormClosing += OnFormClosing;
    }

    public void Log(string message)
    {
        if (IsDisposed) return;

        void Apply()
        {
            _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        if (InvokeRequired) Invoke(Apply);
        else Apply();
    }

    public void UpdateStatus(string player1Status, string player2Status, string blastemStatus)
    {
        if (IsDisposed) return;

        void Apply()
        {
            _p1Label.Text      = $"Player 1: {player1Status}";
            _p2Label.Text      = $"Player 2: {player2Status}";
            _blastemLabel.Text = $"BlastEm: {blastemStatus}";

            _p1Label.ForeColor      = player1Status == "Connected" ? Color.LightGreen : Color.LightGray;
            _p2Label.ForeColor      = player2Status == "Connected" ? Color.LightGreen : Color.LightGray;
            _blastemLabel.ForeColor = blastemStatus  == "Found"    ? Color.LightGreen : Color.OrangeRed;

            if (_lastBlastemStatus != blastemStatus)
            {
                _lastBlastemStatus = blastemStatus;
                Log($"BlastEm status: {blastemStatus}");
            }
        }

        if (InvokeRequired) Invoke(Apply);
        else Apply();
    }

    private void OnScanTimerTick(object? sender, EventArgs e)
    {
        BlastemFocuser.RefreshBlastem();
        _server.NotifyForm();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _scanTimer.Stop();
        _server.Stop();
    }

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
            Bitmap bmp = qrCode.GetGraphic(pixelsPerModule: 10, darkColor: Color.Black, lightColor: Color.White, drawQuietZones: true);
            _qrPictureBox.Image = bmp;
        }
        catch (Exception ex)
        {
            Log($"[QR Error] {ex.Message}");
            _qrPictureBox.BackColor = Color.DarkGray;
        }
    }
}
