using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClaWeb.Data.Models;
using ClaWeb.Sync.BaseClasses;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using ClaWeb.Auth;
using Dotmim.Sync;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Filter;
using Dotmim.Sync.Sqlite;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Web.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaWeb.Sync.Box
{
    public class PracticeInfo
    {
        public string DoctorEmail;
        public string DoctorId;
        public string PracticeId;
    }
    
    public class SyncService
        : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;
        private readonly SyncProvider _provider;
        private readonly ISyncSender _sync;
        private readonly HubConnection _hubConnection;
        private readonly HubConnection _boxUiHubConnection;
        private readonly RegistryManager _registryManager;
        private readonly SqliteSyncProvider _clientProvider;
        private readonly WebProxyClientProvider _proxyClientProvider;
        private readonly SyncAgent _agent;
        
        private readonly Subject<bool> _changeSubject = new Subject<bool>();

        //private readonly string ServerUrl;
        private string _myJwt;
        private DateTime _jwtLife;
        private string _pracId;
        private string _tpmDeviceId;
        //private const string ConnectionString = "HostName=ClaWebProductionHub.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=JIMURgPXdeiqDwLQg7qFDni39N3i1vrv86NrvRogPaM=";
        private string ConnectionString = "";

        private TaskCompletionSource<object> closedTcs;
        private TaskCompletionSource<object> boxUiClosedTcs;

        public SyncService(SyncProvider provider, IConfiguration configuration, ISyncSender sync, ILogger<SyncService> logger)
        {
            _provider = provider;
            _configuration = configuration;
            ConnectionString = _configuration.GetConnectionString("HUB");

            var db = "box.db";
            var conn = new SQLiteConnection($"Data Source={db};");
            // Open connection to allow encryption
            conn.Open();
            var cmd = conn.CreateCommand();
            var password = _configuration["SqlPassword"];
            // Prevent SQL Injection
            cmd.CommandText = "SELECT quote($password);";
            cmd.Parameters.AddWithValue("$password", password);
            var quotedPassword = (string)cmd.ExecuteScalar();
            // Encrypt database
            cmd.CommandText = "PRAGMA key = " + quotedPassword;
            cmd.Parameters.Clear();
            cmd.ExecuteNonQuery();

            // How do I pass this connection to the SyncAgent?

            _clientProvider = new SqliteSyncProvider("box.db");
            _proxyClientProvider = new WebProxyClientProvider(new Uri(_configuration["ClaWebApiServer"] + "/sync/post"));
            _agent = new SyncAgent(_clientProvider,_proxyClientProvider);

            

            _sync = sync;
            _logger = logger;
            //_sync.ChangeDetect += async (sender, args) => { await _provider.UpdateServer(_hubConnection, _myJwt); };
            _sync.ChangeDetect += (sender, args) =>
            {
                _logger.LogInformation($"[SyncService] Change Registered. Debouncing for 5 seconds...");
                _changeSubject.OnNext(true);
            };

            _changeSubject
                .Throttle(TimeSpan.FromSeconds(5))
                .Subscribe(async s =>
                {
                    try
                    {
                        _logger.LogInformation($"[SyncService] Debounce complete. Attempting to update.");
                        if (_sync.GetConnectedStatus())
                        {
                            await RequestSync();
                        }
                        else
                        {
                            _logger.LogInformation($"[SyncService] Server connection offline. Not updating.");
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"[ChangeDetect] Exception {e.Message}");
                    }
                    
                });
                
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_configuration["ClaWebApiServer"] + "/sync", options =>
                    {
                        options.AccessTokenProvider = async () => _myJwt;
                    })
                .AddJsonProtocol(options =>
                    {
                        options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    })
                .Build();

            _boxUiHubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5001/signalr", options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                })
                .Build();
            
            _registryManager = RegistryManager.CreateFromConnectionString(ConnectionString);
        }
      
        private async Task ConnectToServer(CancellationToken stoppingToken)
        {
            closedTcs = new TaskCompletionSource<object>();
            _hubConnection.Closed += async e => closedTcs.SetResult(null);

            _logger.LogInformation($"[SyncService] Trying to connect to server...");
            
            try
            {
                if (_myJwt == null || _jwtLife < DateTime.Now)
                {
                    await GenerateJwt(stoppingToken);
                }
                var tokenHeader = new AuthenticationHeaderValue("bearer", _myJwt).ToString();
                _proxyClientProvider.AddCustomHeader("Authorization",  tokenHeader);
                _logger.LogInformation($"[SyncService] Adding sync filters for practice id {_pracId}");
                //Add filters to agent
                _agent.Parameters.Add("Practices", "Id", _pracId);
                _agent.Parameters.Add("AspNetUsers", "PracticeId", _pracId);
                _agent.Parameters.Add("Exams", "PracticeId", _pracId);
                _agent.Parameters.Add("Locations", "PracticeId", _pracId);
                _agent.Parameters.Add("Patients", "PracticeId", _pracId);
                _agent.Parameters.Add("PracticeSettings", "PracticeId", _pracId);
                _agent.Parameters.Add("ScanData", "PracticeId", _pracId);
                _agent.Parameters.Add("Scans", "PracticeId", _pracId);
                
                /*
                SqlSyncProvider serverProvider = new SqlSyncProvider("Server=tcp:claweb.database.windows.net,1433;Initial Catalog=ClaWeb.Sql.Production;Persist Security Info=False;User ID=ClaSqlProdUser;Password=9uls3Group;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;");
                var conf = new SyncConfiguration(new string[]{"Locations","Practices"});
                conf.TriggersPrefix = "ds";
                conf.TrackingTablesSuffix = "";
                conf.StoredProceduresPrefix = "ds";
                conf.StoredProceduresSuffix = "";
                conf.Filters.Add(new FilterClause("Practices", "Id"));
                conf.Filters.Add(new FilterClause("AspNetUsers", "PracticeId"));
                conf.Filters.Add(new FilterClause("Exams", "PracticeId"));
                conf.Filters.Add(new FilterClause("Locations", "PracticeId"));
                conf.Filters.Add(new FilterClause("Patients", "PracticeId"));
                conf.Filters.Add(new FilterClause("PracticeSettings", "PracticeId"));
                conf.Filters.Add(new FilterClause("ScanData", "PracticeId"));
                conf.Filters.Add(new FilterClause("Scans", "PracticeId"));
                await serverProvider.DeprovisionAsync(conf, SyncProvision.StoredProcedures | SyncProvision.TrackingTable | SyncProvision.Triggers);
                await serverProvider.ProvisionAsync(conf, SyncProvision.StoredProcedures | SyncProvision.TrackingTable | SyncProvision.Triggers);
                await serverProvider.EndSessionAsync(new SyncContext(new Guid()));
                */
                
                await _hubConnection.StartAsync(stoppingToken);
                _logger.LogInformation($"[SyncService] Connection Started");
                _sync.SetConnectedStatus(true);

                await RequestSync();
                //await _provider.UpdateServer(_hubConnection,_myJwt);
                //await Verify();
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncService] Unable to connect to server - Failed with exception: {e.GetType()} {e.Message}. System is now in offline mode.");
                _sync.SetConnectedStatus(false);
                
                await Task.Delay(10000, stoppingToken);

                await ConnectToServer(stoppingToken);
            }
        }

        private async Task RequestSync()
        {
            _logger.LogInformation($"[SyncService] Requesting sync...");
            try
            {
                var progress = new Progress<ProgressArgs>(s =>
                {
                    _logger.LogInformation($"{s.Context.SyncStage}:\t{s.Message}");
                });

                var result = await _agent.SynchronizeAsync(progress);
                _logger.LogInformation($"[SyncService] Sync Completed at {result.CompleteTime} Downloaded:{result.TotalChangesDownloaded}/Uploaded:{result.TotalChangesUploaded} with {result.TotalSyncErrors} Errors and {result.TotalSyncConflicts} Conflicts");
                if (result.TotalChangesUploaded > 0)
                {
                    _logger.LogInformation($"[SyncService] Upload detected, notifying server");
                    await _hubConnection.InvokeAsync("UploadTrigger", _pracId);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncService] Error during sync: {e.Message}");
            }
            //await _provider.UpdateServer(_hubConnection, _myJwt);
        }
        
        private async Task GenerateJwt(CancellationToken stoppingToken)
        {
            try
            {
                PracticeInfo practiceInfo;
                var filePath = Path.GetFullPath(@"practiceinfo.json");
                if (File.Exists(filePath))
                {
                    var fileText = File.ReadAllText(filePath);
                    practiceInfo = JsonConvert.DeserializeObject<PracticeInfo>(fileText);
                }
                else
                {
                    await ConnectToBoxUi(stoppingToken);
                    _logger.LogInformation("[SyncService] Getting TPM DeviceId...");
                    var deviceInfo = await _boxUiHubConnection.InvokeAsync<DeviceInfoDto>("GetDeviceId");
                        
                    _tpmDeviceId = deviceInfo.DeviceId;
                    
                    // Retrieve requested box
                    var box = await _registryManager.GetTwinAsync(_tpmDeviceId);
                    _logger.LogInformation($"Received box info from azure {box.Tags}");
                    if (!box.Tags.Contains("practiceid"))
                    {
                        _logger.LogInformation($"No Practice ID found in tags {box.Tags}");
                        return;
                    }

                    practiceInfo = new PracticeInfo
                    {
                        PracticeId = box.Tags["practiceid"],
                        DoctorId = box.Tags["doctorid"],
                        DoctorEmail = box.Tags["doctoremail"]
                    };
                    _pracId = practiceInfo.PracticeId;
                    
                    using (var writer = File.CreateText(filePath))
                    {
                        await writer.WriteAsync(JsonConvert.SerializeObject(practiceInfo));
                    }
                }
                _pracId = practiceInfo.PracticeId;
                _myJwt = Jwt.GenerateToken(practiceInfo.DoctorEmail, practiceInfo.DoctorId, practiceInfo.PracticeId, _configuration["JwtKey"],
                    _configuration["JwtExpireDays"], _configuration["JwtIssuer"]);
                _jwtLife = DateTime.Now.AddDays(Convert.ToInt32(_configuration["JwtExpireDays"]));
                _logger.LogInformation($"[SyncService] assigned token: SUCCESS");
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncService] GenerateJwt Exception: {e}");
            }
        }
        
        
        private async Task ConnectToBoxUi(CancellationToken stoppingToken)
        {
            boxUiClosedTcs = new TaskCompletionSource<object>();
            _boxUiHubConnection.Closed += async e => boxUiClosedTcs.SetResult(null);
            
            _logger.LogInformation($"[SyncService] Trying to connect to box ui server...");

            try
            {
                await _boxUiHubConnection.StartAsync();
                _logger.LogInformation($"[SyncService] Box Ui Connection Started");                
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncService] Unable to connect to box ui server - Failed with exception: {e.GetType()}. {e.Message}");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"[SyncService] is starting.");

            stoppingToken.Register(() =>
                _logger.LogInformation($"[SyncService] is stopping"));

            _hubConnection.On<string>("ChangeDetect", async id =>
            {
                try
                {
                    _logger.LogInformation($"[SyncService] Change Reported From Server. Debouncing for 5 seconds...");
                    _changeSubject.OnNext(true);
                }
                catch (Exception e)
                {
                    _logger.LogInformation($"[ChangeDetect] Exception {e.Message}");
                }
            });
            // PATIENTS
            /*
            _hubConnection.On<Patient>(SyncTags.Patient, async pat =>
            {
                _logger.LogInformation($"[SyncService] Adding/Updating patient: {pat.FullName}");
                //Add or edit patient
                await _provider.AddOrEditPatient(pat);
            });
            _hubConnection.On<string>(SyncTags.DeletePatient, async id =>
            {
                _logger.LogInformation($"[SyncService] Deleting patient with Id: {id}");
                //Add or edit patient
                await _provider.DeletePatient(id);
            });
            
            // PRACTICE
            _hubConnection.On<Practice>(SyncTags.Practice, async prac =>
            {
                _logger.LogInformation($"[SyncService] Updating practice with Id: {prac.Id}");
                //Add or edit patient
                await _provider.EditPractice(prac);
            });
            
            // LOCATION
            _hubConnection.On<Location>(SyncTags.Location, async loc =>
            {
                _logger.LogInformation($"[SyncService] Adding/Updating location: {loc.Name}");
                //Add or edit location
                await _provider.AddOrEditLocation(loc);
            });
            _hubConnection.On<string>(SyncTags.DeleteLocation, async id =>
            {
                _logger.LogInformation($"[SyncService] Deleting location with Id: {id}");
                //Delete location
                await _provider.DeleteLocation(id);
            });
            
            // EXAMS
            _hubConnection.On<Exam>(SyncTags.Exam, async exam =>
            {
                _logger.LogInformation($"[SyncService] Adding/Updating exam: {exam.Id}");
                //Add or edit exam
                await _provider.AddOrEditExam(exam);
            });
            _hubConnection.On<string>(SyncTags.DeleteExam, async id =>
            {
                _logger.LogInformation($"[SyncService] Deleting exam with Id: {id}");
                //Delete exam
                await _provider.DeleteExam(id);
            });
            
            // SCAN PROTOCOLS
            _hubConnection.On<ScanProtocolSettings>(SyncTags.ScanProtocol, async sp =>
            {
                _logger.LogInformation($"[SyncService] Adding/Updating scan protocol: {sp.Id}");
                //Add or edit scan protocol
                await _provider.AddOrEditScanProtocol(sp);
            });
            _hubConnection.On<string>(SyncTags.DeleteScanProtocol, async id =>
            {
                _logger.LogInformation($"[SyncService] Deleting scan protocol with Id: {id}");
                //Delete exam
                await _provider.DeleteScanProtocol(id);
            });
            
            // SCANS
            _hubConnection.On<string>(SyncTags.DeleteScan, async id =>
            {
                _logger.LogInformation($"[SyncService] Deleting scan with Id: {id}");
                //delete scan
                await _provider.DeleteScan(id);
            });
            _hubConnection.On<IEnumerable<HRVScanData>>(SyncTags.AddHrvScan, async data =>
            {
                _logger.LogInformation($"[SyncService] Adding hrv scan with Id: {data.First().ScanId}");
                //Add or edit scan
                await _provider.AddScanViaScanData(data);
            });
            _hubConnection.On<IEnumerable<EMGScanData>>(SyncTags.AddEmgScan, async data =>
            {
                _logger.LogInformation($"[SyncService] Adding emg scan with Id: {data.First().ScanId}");
                //Add or edit scan
                await _provider.AddScanViaScanData(data);
            });
            _hubConnection.On<IEnumerable<ThermalScanData>>(SyncTags.AddThermalScan, async data =>
            {
                _logger.LogInformation($"[SyncService] Adding thermal scan with Id: {data.First().ScanId}");
                //Add or edit scan
                await _provider.AddScanViaScanData(data);
            });
            _hubConnection.On<IEnumerable<RawThermalRollingData>>(SyncTags.AddRawThermalRollingData, async data =>
            {
                _logger.LogInformation($"[SyncService] Adding raw thermal rolling data with Id: {data.First().ScanId}");
                //Add or edit scan
                await _provider.AddScanViaScanData(data);
            });
            _hubConnection.On<IEnumerable<ROMScanData>>(SyncTags.AddRomScan, async data =>
            {
                _logger.LogInformation($"[SyncService] Adding rom scan with Id: {data.First().ScanId}");
                //Add or edit scan
                await _provider.AddScanViaScanData(data);
            });
            _hubConnection.On<IEnumerable<AlgoScanData>>(SyncTags.AddAlgoScan, async data =>
            {
                _logger.LogInformation($"[SyncService] Adding algo scan with Id: {data.First().ScanId}");
                //Add or edit scan
                await _provider.AddScanViaScanData(data);
            });
            _hubConnection.On<Scan>(SyncTags.MoveScan, async scan =>
            {
                _logger.LogInformation($"[SyncService] Moving scan with Id: {scan.Id}");
                //Move Scan
                await _provider.MoveScan(scan);
            });
            
            // USERS
            _hubConnection.On<DoctorUser>(SyncTags.User, async doc =>
            {
                _logger.LogInformation($"[SyncService] Adding/Updating doctor: {doc.FullName}");
                //Add or edit patient
                await _provider.AddOrEditUser(doc);
            });
            */
           
            await ConnectToServer(stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {

                    // Do some shit here
                    _logger.LogDebug($"[SyncService] Connection status {closedTcs.Task.Status}");

                    if (closedTcs.Task.IsCompleted)
                    {
                        _logger.LogInformation($"[SyncService] Disconnected. Attempting Reconnect in 10 seconds.");
                        _sync.SetConnectedStatus(false);
                        
                        await Task.Delay(10000, stoppingToken);

                        await ConnectToServer(stoppingToken);
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SyncService] Connection terminated with error: {ex}");
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            // Cleanup stuff here
        }

        /*
        private async Task<bool> VerifyAll()
        {
            try
            {
                // Patients
                _logger.LogInformation($"[SyncService] Verifying Patients...");
                var ptsVerified = await _hubConnection.InvokeAsync<bool>("VerifyData", await _provider.GetHash(_pracId, SyncType.Patients), SyncType.Patients);
            
                // Practice
                _logger.LogInformation($"[SyncService] Verifying Practice...");
                var pracVerified = await _hubConnection.InvokeAsync<bool>("VerifyData", await _provider.GetHash(_pracId, SyncType.Practice), SyncType.Practice);
            
                // Scans
                _logger.LogInformation($"[SyncService] Verifying Scans...");
                var scansVerified = await _hubConnection.InvokeAsync<bool>("VerifyData", await _provider.GetHash(_pracId, SyncType.Scans), SyncType.Scans);
            
                // Users
                _logger.LogInformation($"[SyncService] Verifying Users...");
                var usersVerified = await _hubConnection.InvokeAsync<bool>("VerifyData", await _provider.GetHash(_pracId, SyncType.Users), SyncType.Users);
            
                // Exams
                _logger.LogInformation($"[SyncService] Verifying Exams...");
                var examsVerified = await _hubConnection.InvokeAsync<bool>("VerifyData", await _provider.GetHash(_pracId, SyncType.Exams), SyncType.Exams);
            
                // Practice Settings
                _logger.LogInformation($"[SyncService] Verifying Practice Settings...");
                var settingsVerified = await _hubConnection.InvokeAsync<bool>("VerifyData", await _provider.GetHash(_pracId, SyncType.PracticeSettings), SyncType.PracticeSettings);
            
                _logger.LogInformation($"[SyncService] Verification result: Patients - {ptsVerified} | Practice - {pracVerified} | Scans - {scansVerified} | Users - {usersVerified} | Exams - {examsVerified} | Practice Settings - {settingsVerified}");

                return ptsVerified && pracVerified && scansVerified && usersVerified && examsVerified && settingsVerified;
            }
            catch (Exception e)
            {
                _logger.LogError($"Verify All Exception: {e.Message}");
                return false;
            }

        }

        private async Task Verify()
        {
            var isVerified = await VerifyAll();
            
            _logger.LogInformation($"[SyncService] Server hash result: {isVerified}");

            if (!isVerified)
            {
                _logger.LogInformation("----- CLEARING DATABASE -----");
                await _provider.ClearLocalDb();

                var size = GC.GetTotalMemory(false);
                
                _logger.LogInformation("----- DOWNLOADING PRACTICE -----");
                var prac = await _hubConnection.InvokeAsync<Practice>("DownloadPractice");
                
                _logger.LogInformation($"Received practice: {prac.Name} with id {prac.Id}");
                
                _logger.LogInformation("----- DOWNLOADING Locations -----");
                var locs = await _hubConnection.InvokeAsync<IEnumerable<Location>>("DownloadLocations");
                
                foreach (var loc in locs)
                {
                    _logger.LogInformation($"Received location: {loc.Name} with prac {loc.PracticeId}");
                }
                
                _logger.LogInformation("----- DOWNLOADING PATIENTS -----");
                var pts = await _hubConnection.InvokeAsync<IEnumerable<Patient>>("DownloadPatients");
                
                foreach (var patient in pts)
                {
                    _logger.LogInformation($"Received patient: {patient.FullName} with prac {patient.PracticeId}");
                }
                
                _logger.LogInformation("----- DOWNLOADING SCANS -----");
                var scans = await _hubConnection.InvokeAsync<IEnumerable<Scan>>("DownloadScans");
                
                foreach (var scan in scans)
                {
                    _logger.LogInformation($"Received scan: {scan.ScanType} with id {scan.Id}");
                }
                
                _logger.LogInformation("----- DOWNLOADING SCAN DATA -----");
                var hrv = await _hubConnection.InvokeAsync<IEnumerable<HRVScanData>>("DownloadHrvScanData");
                var emg = await _hubConnection.InvokeAsync<IEnumerable<EMGScanData>>("DownloadEmgScanData");
                var thermal = await _hubConnection.InvokeAsync<IEnumerable<ThermalScanData>>("DownloadThermalScanData");
                var rollingthermal = await _hubConnection.InvokeAsync<IEnumerable<RawThermalRollingData>>("DownloadRawThermalRollingData");
                var rom = await _hubConnection.InvokeAsync<IEnumerable<ROMScanData>>("DownloadRomScanData");
                var algo = await _hubConnection.InvokeAsync<IEnumerable<AlgoScanData>>("DownloadAlgoScanData");
                
                var scandata = new List<ScanData>();
                scandata.AddRange(hrv);
                scandata.AddRange(emg);
                scandata.AddRange(thermal);
                scandata.AddRange(rollingthermal);
                scandata.AddRange(rom);
                scandata.AddRange(algo);
                
                foreach (var sd in scandata)
                {
                    _logger.LogInformation($"Received scan data: {sd.GetType()} with id {sd.Id}");
                }
                
                _logger.LogInformation("----- DOWNLOADING USERS -----");
                var users = await _hubConnection.InvokeAsync<IEnumerable<DoctorUser>>("DownloadUsers");
                
                foreach (var user in users)
                {
                    _logger.LogInformation($"Received user: {user.FullName} with id {user.Id}");
                }

                _logger.LogInformation("----- DOWNLOADING EXAMS -----");
                var exams = await _hubConnection.InvokeAsync<IEnumerable<Exam>>("DownloadExams");
                
                foreach (var exam in exams)
                {
                    _logger.LogInformation($"Received exam: {exam.ReportType} with id {exam.Id}");
                }
                
                _logger.LogInformation("----- DOWNLOADING PRACTICE SETTINGS -----");
                var scanProtocolSettings = await _hubConnection.InvokeAsync<IEnumerable<ScanProtocolSettings>>("DownloadScanProtocolSettings");
                var scanLayoutSettings = await _hubConnection.InvokeAsync<IEnumerable<ScanLayoutSettings>>("DownloadScanLayoutSettings");

                var practiceSettings = new List<PracticeSettings>();
                practiceSettings.AddRange(scanProtocolSettings);
                practiceSettings.AddRange(scanLayoutSettings);
                
                foreach (var practiceSetting in practiceSettings)
                {
                    _logger.LogInformation($"Received practice settings: id {practiceSetting.Id}");
                }
                
                var newSize = GC.GetTotalMemory(false);
                
                _logger.LogInformation("----- DOWNLOAD COMPLETE APPROX SIZE: {0} kb -----", (newSize - size) / 1000);

                await _provider.ClearLocalDb();
                await _provider.RebuildLocalDb(prac, locs, pts, scans, scandata, users, exams, practiceSettings);
                
                // Second check
                var isReverified = await VerifyAll();
            
                _logger.LogInformation($"[SyncService] Server hash result: {isReverified}");
            }
        }
        */
    }
    
    public class DeviceInfoDto
    {
        public string HrvMac { get; set; }
        public string HrvFirmware { get;set;}
        public string RomMac { get; set; }
        public string EmgMac { get; set; }
        public string EmgFirmware { get;set;}
        public string ThermalMac { get; set; }
        public string AlgoMac { get; set; }
        public string DeviceId { get; set; }
        public string IpAddress { get; set; }
        public string SsId { get; set; }
        public string CurrentVersion { get; set; }
        public bool UpdateAvailable { get; set; }
        public string UpdateVersionAvailable { get; set; }
    }

}