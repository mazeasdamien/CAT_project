using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OpenDDSharp;
using OpenDDSharp.DDS;
using OpenDDSharp.OpenDDS.DCPS;
using RobotDDS;

namespace FanucDDS_wpf_Damien_Mazeas
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private DomainParticipant? _participant;
        private Topic? _topic;
        private Subscriber? _subscriber;
        private TeleopDataDataReader? _reader;
        private DispatcherTimer _timer;

        // Data properties
        private double _j1;
        public double J1 { get => _j1; set { _j1 = value; OnPropertyChanged(); } }

        private double _j2;
        public double J2 { get => _j2; set { _j2 = value; OnPropertyChanged(); } }

        private double _j3;
        public double J3 { get => _j3; set { _j3 = value; OnPropertyChanged(); } }

        private double _j4;
        public double J4 { get => _j4; set { _j4 = value; OnPropertyChanged(); } }

        private double _j5;
        public double J5 { get => _j5; set { _j5 = value; OnPropertyChanged(); } }

        private double _j6;
        public double J6 { get => _j6; set { _j6 = value; OnPropertyChanged(); } }

        private int _samples;
        public int Samples { get => _samples; set { _samples = value; OnPropertyChanged(); } }

        private string _statusMessage = "Disconnected";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; set { _isConnected = value; OnPropertyChanged(); } }

        public MainViewModel()
        {
            // Auto-start DDS on load
            InitializeDDS();

            // Setup timer for polling data
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(20); // 50Hz
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void InitializeDDS()
        {
            try
            {
                StatusMessage = "Initializing DDS...";

                // 0. Initialize ACE
                try
                {
                    Ace.Init();
                }
                catch (Exception)
                {
                    // Ignore if already initialized
                }

                // 1. Get Factory with Config File
                string configPath = "rtps.ini"; // Ensure this file is copied to output directory
                if (!System.IO.File.Exists(configPath))
                {
                    // Try absolute path if running from IDE
                    configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rtps.ini");
                }

                DomainParticipantFactory? dpf = null;
                if (System.IO.File.Exists(configPath))
                {
                    try
                    {
                        dpf = ParticipantService.Instance.GetDomainParticipantFactory("-DCPSConfigFile", configPath);
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error loading config: {ex.Message}";
                        return;
                    }
                }
                else
                {
                    // Fallback to default
                    dpf = ParticipantService.Instance.GetDomainParticipantFactory();
                    StatusMessage = "Warning: rtps.ini not found, using default config.";
                }

                if (dpf == null)
                {
                    StatusMessage = "Error: Failed to get DomainParticipantFactory";
                    return;
                }

                // 2. Create Participant
                // Use default QoS (null) and no listener (null)
                try
                {
                    _participant = dpf.CreateParticipant(0, null, null, 0);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Exception creating Participant: {ex.Message}";
                    return;
                }

                if (_participant == null)
                {
                    StatusMessage = $"Error: Failed to create DomainParticipant. Config: {(System.IO.File.Exists(configPath) ? "Found" : "Not Found")}";
                    return;
                }

                // 3. Register Type
                var typeSupport = new TeleopDataTypeSupport();
                if (typeSupport.RegisterType(_participant, "TeleopData") != ReturnCode.Ok)
                {
                    StatusMessage = "Error: Failed to register type";
                    return;
                }

                // 4. Create Topic
                // Use default QoS (null) and no listener (null)
                _topic = _participant.CreateTopic("Teleop_Topic", "TeleopData", null, null, 0);
                if (_topic == null)
                {
                    StatusMessage = "Error: Failed to create Topic";
                    return;
                }

                // 5. Create Subscriber
                // Use default QoS (null) and no listener (null)
                _subscriber = _participant.CreateSubscriber(null, null, 0);
                if (_subscriber == null)
                {
                    StatusMessage = "Error: Failed to create Subscriber";
                    return;
                }

                // 6. Create DataReader with matching QoS
                DataReaderQos readerQos = new DataReaderQos();
                _subscriber.GetDefaultDataReaderQos(readerQos);

                // Match Publisher QoS: Reliable + TransientLocal
                readerQos.Reliability.Kind = ReliabilityQosPolicyKind.ReliableReliabilityQos;
                readerQos.Durability.Kind = DurabilityQosPolicyKind.TransientLocalDurabilityQos;
                readerQos.History.Kind = HistoryQosPolicyKind.KeepLastHistoryQos;
                readerQos.History.Depth = 1;

                // Use no listener (null) and mask 0
                var baseReader = _subscriber.CreateDataReader(_topic, readerQos, null, 0);
                if (baseReader == null)
                {
                    StatusMessage = "Error: Failed to create DataReader";
                    return;
                }

                _reader = new TeleopDataDataReader(baseReader);

                IsConnected = true;
                StatusMessage = "Connected and Listening...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Exception: {ex.Message}";
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_reader == null) return;

            var dataList = new List<TeleopData>();
            var infoList = new List<SampleInfo>();

            try
            {
                var ret = _reader.Take(dataList, infoList, 10, SampleStateMask.AnySampleState, ViewStateMask.AnyViewState, InstanceStateMask.AnyInstanceState);

                if (ret == ReturnCode.Ok && dataList.Count > 0)
                {
                    // Process the latest valid sample
                    for (int i = dataList.Count - 1; i >= 0; i--)
                    {
                        if (infoList[i].ValidData)
                        {
                            var data = dataList[i];
                            J1 = data.J1;
                            J2 = data.J2;
                            J3 = data.J3;
                            J4 = data.J4;
                            J5 = data.J5;
                            J6 = data.J6;
                            Samples = data.Samples;
                            break; // Only take the latest
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle or log error
                System.Diagnostics.Debug.WriteLine($"Error reading data: {ex.Message}");
            }
        }

        public void Cleanup()
        {
            _timer?.Stop();
            if (_participant != null)
            {
                _participant.DeleteContainedEntities();
                ParticipantService.Instance.GetDomainParticipantFactory().DeleteParticipant(_participant);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}