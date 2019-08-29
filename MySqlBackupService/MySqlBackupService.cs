using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace MySqlBackupService {

    public enum ServiceState {
        SERVICE_STOPPED = 0x00000001,
        SERVICE_START_PENDING = 0x00000002,
        SERVICE_STOP_PENDING = 0x00000003,
        SERVICE_RUNNING = 0x00000004,
        SERVICE_CONTINUE_PENDING = 0x00000005,
        SERVICE_PAUSE_PENDING = 0x00000006,
        SERVICE_PAUSED = 0x00000007,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatus {
        public int dwServiceType;
        public ServiceState dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    };


    public partial class MySqlBackupService : ServiceBase {
        private int eventId = 0;
        private DateTime backupTime;
        private Timer pollTimer;
        private string file = @"G:\QuickTestBackups\QuicktestBackup-";
        private static int maxBackup=15;
        private static string source = "MySqlBackupSource";
        private static string log = "MySqlBackupLog";
        private static string constring = "server=172.20.4.20;user=aelmendorf;pwd=Drizzle123!;database=epi;";

        public MySqlBackupService() {
            InitializeComponent();
        }

        protected override void OnStart(string[] args) {
            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
            this.eventLog1.WriteEntry("Starting Service", EventLogEntryType.Information, eventId++);
            this.eventId = 1;
            this.pollTimer = new Timer();
            this.pollTimer.Interval = 60000;//10min
            this.pollTimer.AutoReset = true;
            this.pollTimer.Elapsed += new ElapsedEventHandler(this.Polling);
            this.eventLog1 = new EventLog();
            if (!EventLog.SourceExists(source)) {
                EventLog.CreateEventSource(source, log);
            }
            eventLog1.Source = source;
            eventLog1.Log = log;

            this.backupTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day,23,0,0); //9:00:00 AM
            if (DateTime.Now >= this.backupTime) {
                this.backupTime = this.backupTime.AddDays(1);
            }
            this.BackupDatabase();
            this.pollTimer.Start();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        protected override void OnStop() {
            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            this.eventLog1.WriteEntry("Stopped");
            this.pollTimer.Stop();

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        public void Polling(object sender, ElapsedEventArgs args) {
            if (DateTime.Now >= this.backupTime) {
                this.pollTimer.Stop();
                this.CheckAndDeleteBackups();
                this.BackupDatabase();
                this.backupTime = this.backupTime.AddDays(1);
                this.pollTimer.Start();
            }
        }

        public void BackupDatabase() {
            this.eventLog1.WriteEntry("Starting Backup", EventLogEntryType.Information, eventId++);
            try {
                var backupFile = file + DateTime.Now.Month + "_" + DateTime.Now.Day + "_" + DateTime.Now.Year+".sql";
                using (MySqlConnection conn = new MySqlConnection(constring)) {
                    using (MySqlCommand cmd = new MySqlCommand()) {
                        using (MySqlBackup mb = new MySqlBackup(cmd)) {
                            cmd.Connection = conn;
                            cmd.CommandTimeout = 0;
                            conn.Open();
                            mb.ExportToFile(backupFile);
                            conn.Close();
                        }
                    }
                }
                this.eventLog1.WriteEntry("Backup Succeeded", EventLogEntryType.Information, eventId++);
            } catch {
                this.eventLog1.WriteEntry("Backup Failed", EventLogEntryType.Error, eventId++);
            }
        }

        public void CheckAndDeleteBackups() {
            this.eventLog1.WriteEntry("Checking Backups For Deletion", EventLogEntryType.Information, eventId++);
            Directory.GetFiles(@"G:\QuickTestBackups\")
               .Select(f => new FileInfo(f))
               .Where(f => (DateTime.Now - f.CreationTime).TotalDays > maxBackup)
               .ToList()
               .ForEach(f => {
                   try {
                       this.eventLog1.WriteEntry("Oldest Backup Deleted", EventLogEntryType.Information, eventId++);
                   } catch {
                       this.eventLog1.WriteEntry("Error Deleting Backup", EventLogEntryType.Error, eventId++);
                   }
               });
            this.eventLog1.WriteEntry("Backup Checks Finished", EventLogEntryType.Information, eventId++);
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(System.IntPtr handle, ref ServiceStatus serviceStatus);
    }
}
