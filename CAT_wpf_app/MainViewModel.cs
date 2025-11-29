using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using FRRobot;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Dds.Subscription;
using Microsoft.Win32;

namespace CAT_wpf_app
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // --- Constants ---
        private const int ROBOT_UPDATE_RATE_MS = 10;

        // --- Fields ---
        private Thread _workerThread;
        private volatile bool _shouldRun;
        private readonly object _logLock = new object();
        private TeleopSubscriber _teleopSubscriber; // Promoted to field

        // --- Properties ---


        private string _robotIpAddress = "127.0.0.1";
        public string RobotIpAddress
        {
            get => _robotIpAddress;
            set { _robotIpAddress = value; OnPropertyChanged(); }
        }

        private string _qosFilePath = "QOS.xml";
        public string QosFilePath
        {
            get => _qosFilePath;
            set { _qosFilePath = value; OnPropertyChanged(); }
        }

        private string _licenseFilePath = "rti_license.dat";
        public string LicenseFilePath
        {
            get => _licenseFilePath;
            set { _licenseFilePath = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string _robotStatus = "Disconnected";
        public string RobotStatus
        {
            get => _robotStatus;
            set
            {
                _robotStatus = value;
                OnPropertyChanged();
                UpdateRobotStatusColor();
            }
        }

        private string _robotStatusColor = "#CCCCCC"; // Default Gray
        public string RobotStatusColor
        {
            get => _robotStatusColor;
            set { _robotStatusColor = value; OnPropertyChanged(); }
        }

        private string _ddsStatus = "Not Initialized";
        public string DdsStatus
        {
            get => _ddsStatus;
            set { _ddsStatus = value; OnPropertyChanged(); }
        }

        private int _sampleCount;
        public int SampleCount
        {
            get => _sampleCount;
            set { _sampleCount = value; OnPropertyChanged(); }
        }

        private string _publishRate = "0.0 Hz";
        public string PublishRate
        {
            get => _publishRate;
            set { _publishRate = value; OnPropertyChanged(); }
        }

        private string _systemUptime = "00:00:00";
        public string SystemUptime
        {
            get => _systemUptime;
            set { _systemUptime = value; OnPropertyChanged(); }
        }

        // Robot Data
        private string _j1; public string J1 { get => _j1; set { _j1 = value; OnPropertyChanged(); } }
        private string _j2; public string J2 { get => _j2; set { _j2 = value; OnPropertyChanged(); } }
        private string _j3; public string J3 { get => _j3; set { _j3 = value; OnPropertyChanged(); } }
        private string _j4; public string J4 { get => _j4; set { _j4 = value; OnPropertyChanged(); } }
        private string _j5; public string J5 { get => _j5; set { _j5 = value; OnPropertyChanged(); } }
        private string _j6; public string J6 { get => _j6; set { _j6 = value; OnPropertyChanged(); } }

        private string _x; public string X { get => _x; set { _x = value; OnPropertyChanged(); } }
        private string _y; public string Y { get => _y; set { _y = value; OnPropertyChanged(); } }
        private string _z; public string Z { get => _z; set { _z = value; OnPropertyChanged(); } }
        private string _w; public string W { get => _w; set { _w = value; OnPropertyChanged(); } }
        private string _p; public string P { get => _p; set { _p = value; OnPropertyChanged(); } }
        private string _r; public string R { get => _r; set { _r = value; OnPropertyChanged(); } }

        // Teleop Data
        private string _teleopX = "0.00"; public string TeleopX { get => _teleopX; set { _teleopX = value; OnPropertyChanged(); } }
        private string _teleopY = "0.00"; public string TeleopY { get => _teleopY; set { _teleopY = value; OnPropertyChanged(); } }
        private string _teleopZ = "0.00"; public string TeleopZ { get => _teleopZ; set { _teleopZ = value; OnPropertyChanged(); } }
        private string _teleopW = "0.00"; public string TeleopW { get => _teleopW; set { _teleopW = value; OnPropertyChanged(); } }
        private string _teleopP = "0.00"; public string TeleopP { get => _teleopP; set { _teleopP = value; OnPropertyChanged(); } }
        private string _teleopR = "0.00"; public string TeleopR { get => _teleopR; set { _teleopR = value; OnPropertyChanged(); } }
        private string _teleopSpeed = "0.0"; public string TeleopSpeed { get => _teleopSpeed; set { _teleopSpeed = value; OnPropertyChanged(); } }

        private int _teleopSampleCount = 0;
        public int TeleopSampleCount { get => _teleopSampleCount; set { _teleopSampleCount = value; OnPropertyChanged(); } }

        private string _teleopRate = "0.0 Hz";
        public string TeleopRate { get => _teleopRate; set { _teleopRate = value; OnPropertyChanged(); } }

        // Teleop Configuration
        private int _teleopPositionRegisterId = 1;
        public int TeleopPositionRegisterId
        {
            get => _teleopPositionRegisterId;
            set
            {
                _teleopPositionRegisterId = value;
                OnPropertyChanged();
                if (_teleopSubscriber != null) _teleopSubscriber.PositionRegisterId = value;
            }
        }

        private int _teleopSpeedRegisterId = 1;
        public int TeleopSpeedRegisterId
        {
            get => _teleopSpeedRegisterId;
            set
            {
                _teleopSpeedRegisterId = value;
                OnPropertyChanged();
                if (_teleopSubscriber != null) _teleopSubscriber.SpeedRegisterId = value;
            }
        }

        public class LogEntry
        {
            public string Text { get; set; }
            public string Color { get; set; }
        }

        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

        // --- Commands ---
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand BrowseQosCommand { get; }
        public ICommand BrowseLicenseCommand { get; }
        public ICommand OpenAboutCommand { get; }

        // --- Constructor ---
        public MainViewModel()
        {
            // Enable collection synchronization for cross-thread access
            BindingOperations.EnableCollectionSynchronization(Logs, _logLock);

            StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning && !IsBusy);
            StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning && !IsBusy);
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            BrowseQosCommand = new RelayCommand(_ => BrowseFile("XML Files|*.xml|All Files|*.*", path => QosFilePath = path), _ => !IsRunning);
            BrowseLicenseCommand = new RelayCommand(_ => BrowseFile("License Files|*.dat|All Files|*.*", path => LicenseFilePath = path), _ => !IsRunning);
            OpenAboutCommand = new RelayCommand(_ => OpenAbout());
        }

        // --- Methods ---

        private void OpenAbout()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var aboutWin = new AboutWindow();
                aboutWin.Owner = Application.Current.MainWindow;
                aboutWin.ShowDialog();
            });
        }

        private void BrowseFile(string filter, Action<string> onPathSelected)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = filter;
            if (dlg.ShowDialog() == true)
            {
                onPathSelected(dlg.FileName);
            }
        }

        private void ClearLogs()
        {
            lock (_logLock)
            {
                Logs.Clear();
            }
        }

        private void UpdateRobotStatusColor()
        {
            if (RobotStatus == "Connected") RobotStatusColor = "#4CAF50"; // Green
            else if (RobotStatus == "Error") RobotStatusColor = "#F44336"; // Red
            else RobotStatusColor = "#CCCCCC"; // Gray
        }

        private void Start()
        {
            IsBusy = true;
            Log("Starting system...");
            _shouldRun = true;
            IsRunning = true;
            SampleCount = 0;
            PublishRate = "0.0 Hz";
            SystemUptime = "00:00:00";

            _workerThread = new Thread(WorkerLoop);
            _workerThread.IsBackground = true;
            _workerThread.Name = "RobotWorkerThread";
            // Try STA just in case, though MTA might be default for threads
            _workerThread.SetApartmentState(ApartmentState.STA);
            _workerThread.Start();

            IsBusy = false;
        }


        private void Stop()
        {
            IsBusy = true;
            Log("Stopping system...");
            _shouldRun = false;

            // The thread will clean up and exit
            Task.Run(() =>
            {
                if (_workerThread != null && _workerThread.IsAlive)
                {
                    _workerThread.Join(2000);
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsRunning = false;
                    IsBusy = false;
                    Log("System Stopped.");
                });
            });
        }

        private void WorkerLoop()
        {
            FRCRobot robot = null;
            DomainParticipant participant = null;
            RobotStatePublisher publisher = null;
            // TeleopSubscriber teleopSubscriber = null; // Removed local var
            DateTime startTime = DateTime.Now;
            DateTime lastRateCheck = DateTime.Now;
            int lastSampleCount = 0;
            int lastTeleopSampleCount = 0;

            try
            {
                // 1. Initialize Robot
                string ip = RobotIpAddress; // Capture current IP
                Log($"Connecting to Robot at {ip}...");
                try
                {
                    robot = new FRCRobot();
                    robot.ConnectEx(ip, false, 10, 1);

                    if (robot.IsConnected)
                    {
                        Application.Current.Dispatcher.Invoke(() => RobotStatus = "Connected");
                        Log("Robot Connected.");
                    }
                    else
                    {
                        Log("Robot connection failed. Running in Disconnected mode.");
                        Application.Current.Dispatcher.Invoke(() => RobotStatus = "Disconnected");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Robot connection error: {ex.Message}. Running in Disconnected mode.");
                    Application.Current.Dispatcher.Invoke(() => RobotStatus = "Error");
                }

                // 2. Initialize DDS
                Log("Initializing DDS...");

                // Set License File
                string licensePath = LicenseFilePath;
                if (System.IO.File.Exists(licensePath))
                {
                    Environment.SetEnvironmentVariable("RTI_LICENSE_FILE", licensePath);
                    Log($"License file set: {licensePath}");
                }
                else
                {
                    Log($"Warning: License file not found at {licensePath}. Using default/environment.");
                }

                string qosPath = QosFilePath;
                if (!System.IO.File.Exists(qosPath))
                {
                    Log($"Warning: QOS file {qosPath} not found.");
                }
                else
                {
                    Log($"Using QOS file: {qosPath}");
                }

                QosProvider provider = new QosProvider(qosPath);
                string fullProfileName = "RigQoSLibrary::RigQoSProfile";
                DomainParticipantQos partQos = provider.GetDomainParticipantQos(fullProfileName);
                DataWriterQos writerQos = provider.GetDataWriterQos(fullProfileName);
                DataReaderQos readerQos = provider.GetDataReaderQos(fullProfileName);

                participant = DomainParticipantFactory.Instance.CreateParticipant(0, partQos);
                if (participant == null) throw new Exception("Failed to create Participant.");

                publisher = new RobotStatePublisher(participant, writerQos, msg => Log(msg));
                _teleopSubscriber = new TeleopSubscriber(participant, readerQos, msg => Log(msg));
                Application.Current.Dispatcher.Invoke(() => DdsStatus = "Initialized");
                Log("DDS Initialized.");

                // 3. Loop
                Log("Publish loop started.");
                startTime = DateTime.Now; // Reset start time
                lastRateCheck = DateTime.Now;

                while (_shouldRun)
                {
                    try
                    {
                        // Publish (Only if connected)
                        if (robot != null && robot.IsConnected)
                        {
                            if (publisher.Publish(robot))
                            {
                                Application.Current.Dispatcher.Invoke(() => SampleCount++);
                            }
                        }

                        // Teleop Receive (Always try to receive)
                        if (_teleopSubscriber != null)
                        {
                            _teleopSubscriber.ReceiveAndProcess(robot);
                        }

                        // Update UI Data
                        string j1 = "0.00", j2 = "0.00", j3 = "0.00", j4 = "0.00", j5 = "0.00", j6 = "0.00";
                        string x = "0.00", y = "0.00", z = "0.00", w = "0.00", p = "0.00", r = "0.00";

                        if (robot != null && robot.IsConnected)
                        {
                            FRCCurPosition cur = robot.CurPosition;
                            var grpJoint = cur.Group[1, FRECurPositionConstants.frJointDisplayType];
                            var grpWorld = cur.Group[1, FRECurPositionConstants.frWorldDisplayType];
                            grpJoint.Refresh();
                            grpWorld.Refresh();

                            var joint = grpJoint.Formats[FRETypeCodeConstants.frJoint];
                            var xyz = grpWorld.Formats[FRETypeCodeConstants.frXyzWpr];

                            // Capture values
                            j1 = ((float)joint[1]).ToString("F2");
                            j2 = ((float)joint[2]).ToString("F2");
                            j3 = ((float)joint[3]).ToString("F2");
                            j4 = ((float)joint[4]).ToString("F2");
                            j5 = ((float)joint[5]).ToString("F2");
                            j6 = ((float)joint[6]).ToString("F2");

                            x = ((float)xyz.X).ToString("F2");
                            y = ((float)xyz.Y).ToString("F2");
                            z = ((float)xyz.Z).ToString("F2");
                            w = ((float)xyz.W).ToString("F2");
                            p = ((float)xyz.P).ToString("F2");
                            r = ((float)xyz.R).ToString("F2");
                        }

                        // Update Properties on UI Thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (robot != null && robot.IsConnected)
                            {
                                J1 = j1; J2 = j2; J3 = j3; J4 = j4; J5 = j5; J6 = j6;
                                X = x; Y = y; Z = z; W = w; P = p; R = r;
                            }

                            // Teleop UI Updates
                            if (_teleopSubscriber != null)
                            {
                                TeleopX = _teleopSubscriber.LastX.ToString("F2");
                                TeleopY = _teleopSubscriber.LastY.ToString("F2");
                                TeleopZ = _teleopSubscriber.LastZ.ToString("F2");
                                TeleopW = _teleopSubscriber.LastW.ToString("F2");
                                TeleopP = _teleopSubscriber.LastP.ToString("F2");
                                TeleopR = _teleopSubscriber.LastR.ToString("F2");
                                TeleopSpeed = _teleopSubscriber.LastSpeed.ToString("F1");
                                TeleopSampleCount = _teleopSubscriber.TotalSamplesReceived;
                            }

                            // Update Stats
                            var now = DateTime.Now;
                            SystemUptime = (now - startTime).ToString(@"hh\:mm\:ss");

                            if ((now - lastRateCheck).TotalSeconds >= 1.0)
                            {
                                double rate = (SampleCount - lastSampleCount) / (now - lastRateCheck).TotalSeconds;
                                PublishRate = $"{rate:F1} Hz";
                                lastSampleCount = SampleCount;

                                if (_teleopSubscriber != null)
                                {
                                    double tRate = (TeleopSampleCount - lastTeleopSampleCount) / (now - lastRateCheck).TotalSeconds;
                                    TeleopRate = $"{tRate:F1} Hz";
                                    lastTeleopSampleCount = TeleopSampleCount;
                                }

                                lastRateCheck = now;
                            }
                        });
                    }
                    catch (Exception)
                    {
                        // Log($"Loop Error: {ex.Message}");
                    }

                    Thread.Sleep(ROBOT_UPDATE_RATE_MS);
                }
            }
            catch (Exception ex)
            {
                Log($"Worker Error: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RobotStatus = "Error";
                    DdsStatus = "Error";
                    IsRunning = false;
                });
            }
            finally
            {
                // Cleanup
                Log("Cleaning up...");
                if (participant != null)
                {
                    participant.Dispose();
                    Application.Current.Dispatcher.Invoke(() => DdsStatus = "Disposed");
                }

                // Robot cleanup if needed (COM release)
                if (robot != null)
                {
                    // Marshal.ReleaseComObject(robot); // Optional, GC usually handles it
                    Application.Current.Dispatcher.Invoke(() => RobotStatus = "Disconnected");
                }
            }
        }

        private void Log(string message, string color = "#CCCCCC")
        {
            lock (_logLock)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Logs.Insert(0, new LogEntry { Text = $"[{DateTime.Now:HH:mm:ss}] {message}", Color = color });
                    if (Logs.Count > 100) Logs.RemoveAt(Logs.Count - 1);
                });
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
