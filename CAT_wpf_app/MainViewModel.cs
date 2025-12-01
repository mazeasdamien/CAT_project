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
using System.Collections.Concurrent;

namespace CAT_wpf_app
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // --- Constants ---
        private const int ROBOT_UPDATE_RATE_MS = 33;

        // --- Fields ---
        private Thread _workerThread;
        private volatile bool _shouldRun;
        private readonly object _logLock = new object();
        private TeleopSubscriber _teleopSubscriber; // Promoted to field
        private ConcurrentQueue<Action<FRCRobot>> _robotCommandQueue = new ConcurrentQueue<Action<FRCRobot>>();

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
        private string _teleopJ1 = "0.00"; public string TeleopJ1 { get => _teleopJ1; set { _teleopJ1 = value; OnPropertyChanged(); } }
        private string _teleopJ2 = "0.00"; public string TeleopJ2 { get => _teleopJ2; set { _teleopJ2 = value; OnPropertyChanged(); } }
        private string _teleopJ3 = "0.00"; public string TeleopJ3 { get => _teleopJ3; set { _teleopJ3 = value; OnPropertyChanged(); } }
        private string _teleopJ4 = "0.00"; public string TeleopJ4 { get => _teleopJ4; set { _teleopJ4 = value; OnPropertyChanged(); } }
        private string _teleopJ5 = "0.00"; public string TeleopJ5 { get => _teleopJ5; set { _teleopJ5 = value; OnPropertyChanged(); } }
        private string _teleopJ6 = "0.00"; public string TeleopJ6 { get => _teleopJ6; set { _teleopJ6 = value; OnPropertyChanged(); } }

        // Removed TeleopSpeed as it's not in the struct anymore, but keeping property to avoid UI binding errors if not updated yet
        private string _teleopSpeed = "0.0"; public string TeleopSpeed { get => _teleopSpeed; set { _teleopSpeed = value; OnPropertyChanged(); } }

        private int _teleopSampleCount = 0;
        public int TeleopSampleCount { get => _teleopSampleCount; set { _teleopSampleCount = value; OnPropertyChanged(); } }

        private string _teleopRate = "0.0 Hz";
        public string TeleopRate { get => _teleopRate; set { _teleopRate = value; OnPropertyChanged(); } }

        private string _robotWriteRate = "0.0 Hz";
        public string RobotWriteRate { get => _robotWriteRate; set { _robotWriteRate = value; OnPropertyChanged(); } }

        private string _teleopReachability = "Unknown";
        public string TeleopReachability { get => _teleopReachability; set { _teleopReachability = value; OnPropertyChanged(); } }

        private string _teleopReachabilityColor = "#CCCCCC";
        public string TeleopReachabilityColor { get => _teleopReachabilityColor; set { _teleopReachabilityColor = value; OnPropertyChanged(); } }

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



        public class LogEntry : INotifyPropertyChanged
        {
            private string _text;
            public string Text
            {
                get => _text;
                set { _text = value; OnPropertyChanged(); }
            }

            private string _color;
            public string Color
            {
                get => _color;
                set { _color = value; OnPropertyChanged(); }
            }

            public string Topic { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public class AlarmEntry
        {
            public string Time { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
            public string Severity { get; set; }
            public string Color { get; set; }
        }

        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();
        public ObservableCollection<AlarmEntry> AlarmLogs { get; } = new ObservableCollection<AlarmEntry>();

        // --- Commands ---
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ClearAlarmLogsCommand { get; }
        public ICommand BrowseQosCommand { get; }
        public ICommand BrowseLicenseCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand StartTeleopProgramCommand { get; }
        public ICommand AbortCommand { get; }
        public ICommand ResetCommand { get; }

        // --- Constructor ---
        public MainViewModel()
        {
            // Enable collection synchronization for cross-thread access
            BindingOperations.EnableCollectionSynchronization(Logs, _logLock);
            BindingOperations.EnableCollectionSynchronization(AlarmLogs, _logLock);

            StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning && !IsBusy);
            StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning && !IsBusy);
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            ClearAlarmLogsCommand = new RelayCommand(_ => ClearAlarmLogs());
            BrowseQosCommand = new RelayCommand(_ => BrowseFile("XML Files|*.xml|All Files|*.*", path => QosFilePath = path), _ => !IsRunning);
            BrowseLicenseCommand = new RelayCommand(_ => BrowseFile("License Files|*.dat|All Files|*.*", path => LicenseFilePath = path), _ => !IsRunning);
            OpenAboutCommand = new RelayCommand(_ => OpenAbout());
            StartTeleopProgramCommand = new RelayCommand(_ => StartTeleopProgram(), _ => IsRunning && RobotStatus == "Connected");
            AbortCommand = new RelayCommand(_ => AbortTasks(), _ => IsRunning && RobotStatus == "Connected");
            ResetCommand = new RelayCommand(_ => ResetAlarms(), _ => IsRunning && RobotStatus == "Connected");
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

        private void ClearAlarmLogs()
        {
            lock (_logLock)
            {
                AlarmLogs.Clear();
            }
        }

        private void AbortTasks()
        {
            Log("Queuing Abort Command...", "#E0E0E0");
            _robotCommandQueue.Enqueue(robot =>
            {
                try
                {
                    Log("Executing: Abort All Tasks...", "#E0E0E0");
                    robot.Tasks.AbortAll();
                    Log("Tasks Aborted.", "#FF9800");
                    LogAlarm("Tasks Aborted by User", "#FFFF00");
                }
                catch (Exception ex)
                {
                    Log($"Abort Failed: {ex.Message}", "#F44336");
                    LogAlarm($"Abort Failed: {ex.Message}", "#F44336");
                }
            });
        }

        private void ResetAlarms()
        {
            Log("Queuing Reset Command...", "#E0E0E0");
            _robotCommandQueue.Enqueue(robot =>
            {
                try
                {
                    Log("Executing: Reset Alarms...", "#E0E0E0");
                    robot.Alarms.Reset();
                    Log("Alarms Reset.", "#4CAF50");
                    LogAlarm("Alarms Reset by User", "#4CAF50");
                }
                catch (Exception ex)
                {
                    Log($"Reset Failed: {ex.Message}", "#F44336");
                    LogAlarm($"Reset Failed: {ex.Message}", "#F44336");
                }
            });
        }

        private void StartTeleopProgram()
        {
            Log("Queuing Teleop Start Sequence...", "#E0E0E0");
            _robotCommandQueue.Enqueue(robot =>
            {
                try
                {
                    // Optional: Auto-Abort and Reset before starting
                    Thread.Sleep(500);
                    robot.Tasks.AbortAll();
                    Thread.Sleep(500);
                    robot.Alarms.Reset();
                    Thread.Sleep(500);

                    string programName = "TELEOP";
                    Log($"Executing: Select Program '{programName}'...", "#E0E0E0");
                    robot.Programs.Selected = programName;

                    Log("Executing: Run Program...", "#E0E0E0");
                    FRCTPProgram prog = (FRCTPProgram)robot.Programs[robot.Programs.Selected, Type.Missing, Type.Missing];

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            prog.Run();
                        }
                        catch (System.Runtime.InteropServices.COMException comEx)
                        {
                            if (comEx.Message.Contains("UOP is the master device"))
                            {
                                throw new Exception("Robot rejected command: UOP is Master. Check Remote/Local Setup (Set to 'Software' or 'Cell').");
                            }
                            throw;
                        }
                    });

                    Log("Teleop Program Started Successfully.", "#4CAF50");
                }
                catch (Exception ex)
                {
                    Log($"Failed to start Teleop Program: {ex.Message}", "#F44336");
                    LogAlarm($"Start Teleop Failed: {ex.Message}", "#F44336");
                }
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
            int lastRobotWriteCount = 0;
            DateTime _lastDebugUpdate = DateTime.Now;

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

                publisher = new RobotStatePublisher(participant, writerQos, (msg, color, topic) => Log(msg, color, topic));
                _teleopSubscriber = new TeleopSubscriber(participant, readerQos, (msg, color, topic) => Log(msg, color, topic));
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
                        // Process Command Queue
                        while (_robotCommandQueue.TryDequeue(out var action))
                        {
                            if (robot != null && robot.IsConnected)
                            {
                                action(robot);
                            }
                            else
                            {
                                Log("Cannot execute command: Robot not connected.", "#F44336");
                            }
                        }

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
                            if (robot != null && robot.IsConnected)
                            {
                                J1 = j1; J2 = j2; J3 = j3; J4 = j4; J5 = j5; J6 = j6;
                                X = x; Y = y; Z = z; W = w; P = p; R = r;
                            }

                            // Teleop UI Updates
                            if (_teleopSubscriber != null)
                            {
                                TeleopJ1 = _teleopSubscriber.LastJ1.ToString("F2");
                                TeleopJ2 = _teleopSubscriber.LastJ2.ToString("F2");
                                TeleopJ3 = _teleopSubscriber.LastJ3.ToString("F2");
                                TeleopJ4 = _teleopSubscriber.LastJ4.ToString("F2");
                                TeleopJ5 = _teleopSubscriber.LastJ5.ToString("F2");
                                TeleopJ6 = _teleopSubscriber.LastJ6.ToString("F2");

                                TeleopSampleCount = _teleopSubscriber.TotalSamplesReceived;

                                if (_teleopSubscriber.IsReachable)
                                {
                                    TeleopReachability = "Reachable";
                                    TeleopReachabilityColor = "#4CAF50"; // Green
                                }
                                else
                                {
                                    TeleopReachability = "Unreachable";
                                    TeleopReachabilityColor = "#F44336"; // Red
                                }
                            }

                            // --- Alarm Monitoring ---
                            try
                            {
                                // Check if there are any alarms
                                if (robot.Alarms.Count > 0)
                                {
                                    FRCAlarm alarm = robot.Alarms[1]; // Get most recent (1-based index)
                                    string code = alarm.ErrorNumber;
                                    string msg = alarm.ErrorMessage;
                                    string severity = "ALARM";

                                    // Basic severity inference
                                    if (code.Contains("WARN")) severity = "WARN";
                                    else if (code.Contains("STOP")) severity = "STOP";

                                    // Check if we already logged this recently to avoid spam
                                    // We check the top entry of AlarmLogs
                                    if (AlarmLogs.Count == 0 || AlarmLogs[0].Message != msg)
                                    {
                                        AddAlarmEntry(msg, code, severity, "#F44336");
                                    }
                                }
                            }
                            catch { }

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

                                    double wRate = (_teleopSubscriber.TotalRobotWrites - lastRobotWriteCount) / (now - lastRateCheck).TotalSeconds;
                                    RobotWriteRate = $"{wRate:F1} Hz";
                                    lastRobotWriteCount = _teleopSubscriber.TotalRobotWrites;
                                }

                                lastRateCheck = now;
                            }
                        }
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

        private void Log(string message, string color = "#CCCCCC", string topic = null)
        {
            lock (_logLock)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(topic))
                    {
                        // Find existing log with same topic
                        // We search the entire list or just the top few? 
                        // Searching the whole list (max 100) is fast enough.
                        System.Linq.Enumerable.FirstOrDefault(Logs, l => l.Topic == topic);
                        LogEntry existingLog = null;
                        foreach (var log in Logs)
                        {
                            if (log.Topic == topic)
                            {
                                existingLog = log;
                                break;
                            }
                        }

                        if (existingLog != null)
                        {
                            existingLog.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
                            existingLog.Color = color;
                            // Move to top to indicate activity?
                            // Logs.Move(Logs.IndexOf(existingLog), 0); 
                            // User asked to "update the line instead of adding it in the feed".
                            // Keeping it in place seems to fit "update the line" better than moving it.
                            return;
                        }
                    }

                    Logs.Insert(0, new LogEntry { Text = $"[{DateTime.Now:HH:mm:ss}] {message}", Color = color, Topic = topic });
                    if (Logs.Count > 100) Logs.RemoveAt(Logs.Count - 1);
                });
            }
        }

        private void LogAlarm(string message, string color = "#F44336")
        {
            // Backward compatibility wrapper
            AddAlarmEntry(message, "SYS", "INFO", color);
        }

        private void AddAlarmEntry(string message, string code, string severity, string color)
        {
            lock (_logLock)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AlarmLogs.Insert(0, new AlarmEntry
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Code = code,
                        Message = message,
                        Severity = severity,
                        Color = color
                    });
                    if (AlarmLogs.Count > 100) AlarmLogs.RemoveAt(AlarmLogs.Count - 1);
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
