using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using ClaWeb.Data.Entities;
using ClaWeb.Data.Models;
using ClaWeb.Sync.Cloud;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaWeb.Sync
{
    public class CloudSyncSender : ISyncSender
    {
        private readonly IHubContext<SyncHub> _sync;
        private readonly ILogger _logger;
        public event EventHandler ChangeDetect; 

        public CloudSyncSender(IHubContext<SyncHub> sync, ILogger<CloudSyncSender> logger)
        {
            _sync = sync;
            _logger = logger;
        }

        public async Task RegisterChange(string pracId, string tag, object data)
        {
            await _sync.Clients.Group(pracId).SendAsync("ChangeDetect", pracId);
            
            _logger.LogInformation($"[CloudSyncSender]: Registered Change - {tag}:{data}");
        }

        public bool GetConnectedStatus()
        {
            //Always return false on the server
            //Returning true indicates the box is online, and will reject and API calls
            return false;
        }
        
        public void SetConnectedStatus(bool status)
        {
            //This should never be called
        }
        
        public bool IsCloud()
        {
            return true;
        }
    }

    public class BoxSyncSender : ISyncSender
    {
        private bool _connectedStatus;
        
        //private readonly Collection<SyncItem> _syncItems = new Collection<SyncItem>();
        private readonly IServiceProvider _provider;
        private readonly ILogger _logger;
        public event EventHandler ChangeDetect; 

        public BoxSyncSender(IServiceProvider provider, ILogger<BoxSyncSender> logger)
        {
            _provider = provider;
            _logger = logger;
        }
        
        public async Task RegisterChange(string pracId, string tag, object data)
        {
            /* REMOVED
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                // Make sure it is a valid offline change
                if (!SyncTags.IsAllowedOffline(tag))
                {
                    _logger.LogWarning($"[BoxSyncSender]: Attempted to register blocked offline tag {tag}");
                    return;
                }

                switch (tag)
                {
                    case SyncTags.Patient:
                    {
                        if (!(data is Patient pat))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted patient");
                            return;
                        }

                        // Check for dupes
                        if (context.SyncItems.Any(i => i.SyncTargetId == pat.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated Patient already scheduled for sync {pat.Id}");
                            return;
                        }
                        var item = new SyncItem
                        {
                            SyncTargetId = pat.Id,
                            SyncType = tag,
                            ObjectBackup = JsonConvert.SerializeObject(pat)
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();

                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{pat.Id}");
                        break;
                    }
                    case SyncTags.Exam:
                    {
                        if (!(data is Exam exam))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted exam");
                            return;
                        }
                    
                        // Check for dupes
                        if (context.SyncItems.Any(i => i.SyncTargetId == exam.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated Exam already scheduled for sync {exam.Id}");
                            return;
                        }

                        // Remove unnessesary info
                        exam.Doctor = null;
                        exam.Patient = null;
                        
                        var item = new SyncItem
                        {
                            SyncTargetId = exam.Id,
                            SyncType = tag,
                            ObjectBackup = JsonConvert.SerializeObject(exam)
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();
                    
                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{exam.Id}");
                        break;
                    }
                    case SyncTags.ScanProtocol:
                    {
                        if (!(data is ScanProtocolSettings settings))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted scan protocol");
                            return;
                        }
                    
                        // Check for dupes
                        if (context.SyncItems.Any(i => i.SyncTargetId == settings.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated Protocol Settings already scheduled for sync {settings.Id}");
                            return;
                        }

                        var item = new SyncItem
                        {
                            SyncTargetId = settings.Id,
                            SyncType = tag,
                            ObjectBackup = ""
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();
                    
                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{settings.Id}");
                        break;
                    }
                    case SyncTags.AddEmgScan:
                    {
                        if (!(data is IEnumerable<EMGScanData> scanData))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted EMG scan ");
                            return;
                        }
                        var scan = scanData.First().Scan;
                        var scanDto = new JObject
                        {
                            {"Id", scan.Id},
                            {"CreatedStamp", scan.CreatedStamp},
                            {"DoctorId", scan.DoctorId},
                            {"PatientId", scan.PatientId},
                            {"BodyType",(int)scan.BodyType},
                            {"ScanPosition",(int)scan.ScanPosition},
                            {"ScanPurpose",(int)scan.ScanPurpose},
                        };
                        var scanDataDto = new JArray( from sdata in scanData
                            select new JObject(
                                new JProperty("SpinalLocation", sdata.SpinalLocation),
                                new JProperty("LeftValue", sdata.LeftValue),
                                new JProperty("RightValue", sdata.RightValue),
                                new JProperty("ScanId",scan.Id)
                            ));
                        scanDto.Add("ScanData", scanDataDto);

                        if (context.SyncItems.Any(i => i.SyncTargetId == scan.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated EMG Scan already scheduled for sync {scan.Id}");
                            return;
                        }
                    

                        var item = new SyncItem
                        {
                            SyncTargetId = scan.Id,
                            SyncType = tag,
                            ObjectBackup = scanDto.ToString(Formatting.None)
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();

                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{scan.Id}");
                        break;
                    }
                    case SyncTags.AddAlgoScan:
                    {
                        if (!(data is IEnumerable<AlgoScanData> scanData))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted Algo scan ");
                            return;
                        }

                        var scan = scanData.First().Scan;
                        var scanDto = new JObject
                        {
                            {"Id", scan.Id},
                            {"CreatedStamp", scan.CreatedStamp},
                            {"DoctorId", scan.DoctorId},
                            {"PatientId", scan.PatientId},
                            {"BodyType",(int)scan.BodyType},
                            {"ScanPosition",(int)scan.ScanPosition},
                            {"ScanPurpose",(int)scan.ScanPurpose},
                        };
                        var scanDataDto = new JArray( from sdata in scanData
                            select new JObject(
                                new JProperty("SpinalLocation", sdata.SpinalLocation),
                                new JProperty("LeftValue", sdata.LeftValue),
                                new JProperty("RightValue", sdata.RightValue),
                                new JProperty("BaselineFlag", sdata.BaselineFlag),
                                new JProperty("CenterValue", sdata.CenterValue),
                                new JProperty("ScanId",scan.Id)
                            ));
                        scanDto.Add("ScanData", scanDataDto);
                        if (context.SyncItems.Any(i => i.SyncTargetId == scan.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated Algo Scan already scheduled for sync {scan.Id}");
                            return;
                        }
                    
                        var item = new SyncItem
                        {
                            SyncTargetId = scan.Id,
                            SyncType = tag,
                            ObjectBackup = scanDto.ToString(Formatting.None)
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();

                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{scan.Id}");
                        break;
                    }
                    case SyncTags.AddHrvScan:
                    {
                        if (!(data is IEnumerable<HRVScanData> scanData))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted HRV scan ");
                            return;
                        }

                        var scan = scanData.First().Scan;
                    
                        if (context.SyncItems.Any(i => i.SyncTargetId == scan.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated HRV Scan already scheduled for sync {scan.Id}");
                            return;
                        }

                        var hrvDto = new JObject
                        {
                            {"Id", scan.Id},
                            {"CreatedStamp", scan.CreatedStamp},
                            {"DoctorId", scan.DoctorId},
                            {"PatientId", scan.PatientId},
                            {"PracticeId",scan.PracticeId}
                        };
                        var scanDataDto = new JArray( from sdata in scanData
                            select new JObject(
                                new JProperty("RestingDuration", sdata.RestingDuration),
                                new JProperty("DCDuration", sdata.DCDuration),
                                new JProperty("MeanIBI", sdata.MeanIBI),
                                new JProperty("StdDevIBI", sdata.StdDevIBI),
                                new JProperty("RMSIBI", sdata.RMSIBI),
                                new JProperty("TotalPowerIBI", sdata.TotalPowerIBI),
                                new JProperty("LFIBI", sdata.LFIBI),
                                new JProperty("HFIBI", sdata.HFIBI),
                                new JProperty("AutonomicActivity", sdata.AutonomicActivity),
                                new JProperty("AutonomicBalance", sdata.AutonomicBalance),
                                new JProperty("RawPpg", sdata.RawPpg),
                                new JProperty("RawScr", sdata.RawScr),
                                new JProperty("RawTemp", sdata.RawTemp),
                                new JProperty("PracticeId",scan.PracticeId),
                                new JProperty("ScanId",scan.Id)
                            ));
                        hrvDto.Add("ScanData",scanDataDto);
                    
                        var item = new SyncItem
                        {
                            SyncTargetId = scan.Id,
                            SyncType = tag,
                            ObjectBackup = hrvDto.ToString(Formatting.None)
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();

                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{scan.Id}");
                        break;
                    }
                    case SyncTags.AddRomScan:
                    {
                        if (!(data is IEnumerable<ROMScanData> scanData))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted ROM scan ");
                            return;
                        }

                        var scan = scanData.First().Scan;
                        var scanDto = new JObject
                        {
                            {"Id", scan.Id},
                            {"CreatedStamp", scan.CreatedStamp},
                            {"DoctorId", scan.DoctorId},
                            {"PatientId", scan.PatientId},
                            {"BodyType",(int)scan.BodyType},
                            {"ScanPosition",(int)scan.ScanPosition},
                            {"ScanPurpose",(int)scan.ScanPurpose},
                        };
                        var scanDataDto = new JArray( from sdata in scanData
                            select new JObject(
                                new JProperty("DataValue", sdata.DataValue),
                                new JProperty("InvalidFlag", sdata.InvalidFlag),
                                new JProperty("ResultType", (int)sdata.ResultType),
                                new JProperty("TestType", (int)sdata.TestType),
                                new JProperty("ScanId",scan.Id)
                            ));
                        scanDto.Add("ScanData", scanDataDto);
                        if (context.SyncItems.Any(i => i.SyncTargetId == scan.Id && i.Uploaded == false))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Ignoring Duplicated ROM Scan already scheduled for sync {scan.Id}");
                            return;
                        }

                        var item = new SyncItem
                        {
                            SyncTargetId = scan.Id,
                            SyncType = tag,
                            ObjectBackup = scanDto.ToString(Formatting.None)
                        };

                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();

                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{scan.Id}");
                        break;
                    }
                    case SyncTags.AddThermalScan:
                    {
                        try
                        {
                            if (!(data is ScanDto<ThermalScanData> scanDto))
                            {
                                _logger.LogWarning($"[BoxSyncSender]: Attempted to register corrupted Thermal scan ");
                                return;
                            }

                            if (context.SyncItems.Any(i => i.SyncTargetId == scanDto.Id && i.Uploaded == false))
                            {
                                _logger.LogWarning(
                                    $"[BoxSyncSender]: Ignoring Duplicated Thermal Scan already scheduled for sync {scanDto.Id}");
                                return;
                            }

                            var thermalDto = new JObject
                            {
                                {"Id", scanDto.Id},
                                {"Bias", scanDto.Bias},
                                {"CreatedStamp", scanDto.CreatedStamp},
                                {"DoctorId", scanDto.DoctorId},
                                {"OffsetL", scanDto.OffsetL},
                                {"OffsetR", scanDto.OffsetR},
                                {"PatientId", scanDto.PatientId},
                                {"BodyType",(int)scanDto.BodyType},
                                {"ScanPosition",(int)scanDto.ScanPosition},
                                {"ScanPurpose",(int)scanDto.ScanPurpose},
                            };
                            var scanDataDto = new JArray(
                                from sdata in scanDto.ScanData.OfType<ThermalScanData>().ToList()
                                select new JObject(
                                    new JProperty("SpinalLocation", sdata.SpinalLocation),
                                    new JProperty("LeftValue", sdata.LeftValue),
                                    new JProperty("RightValue", sdata.RightValue),
                                    new JProperty("ScanId",scanDto.Id)
                                ));
                            thermalDto.Add("ScanData", scanDataDto);

                            var rawData = scanDto.ThermalRawData;
                            thermalDto.Add("ThermalRawData", rawData);
                            var item = new SyncItem
                            {
                                SyncTargetId = scanDto.Id,
                                SyncType = tag,
                                ObjectBackup = thermalDto.ToString(Formatting.None)
                            };

                            await context.SyncItems.AddAsync(item);
                            await context.SaveChangesAsync();

                            _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{scanDto.Id}");
                            break;
                        }
                        catch (Exception e)
                        {
                            _logger.LogCritical($"[BoxSyncSender Thermal Exception]: {e.Message}");
                            break;
                        }
                        
                    }
                    case SyncTags.MoveScan:
                    {
                        if (!(data is MoveDto moveInfo))
                        {
                            _logger.LogWarning($"[BoxSyncSender]: Attempted to move corrupted scan");
                            return;
                        }

                        var item = new SyncItem
                        {
                            SyncTargetId = moveInfo.ScanId,
                            SyncType = tag,
                            ObjectBackup = JsonConvert.SerializeObject(moveInfo)
                        };
                        await context.SyncItems.AddAsync(item);
                        await context.SaveChangesAsync();

                        _logger.LogInformation($"[BoxSyncSender]: Registered Change - {tag}:{moveInfo.ScanId}");
                        break;
                    }
                }
            }
            */
            ChangeDetect?.Invoke(this,null);

        }

        public bool GetConnectedStatus()
        {
            return _connectedStatus;
        }
        
        public void SetConnectedStatus(bool status)
        {
            _connectedStatus = status;
        }

        public bool IsCloud()
        {
            return false;
        }
    }

    public interface ISyncSender
    {
        bool GetConnectedStatus();
        void SetConnectedStatus(bool status);
        bool IsCloud();
        Task RegisterChange(string pracId, string tag, object data);
        event EventHandler ChangeDetect; 

    }
    
    public static class SyncTags
    {
        public const string User = "User";
        public const string Patient = "Patient";
        public const string DeletePatient = "DeletePatient";
        public const string Practice = "Practice";
        public const string Location = "Location";
        public const string DeleteLocation = "DeleteLocation";
        public const string DeleteScan = "DeleteScan";
        public const string DeleteExam = "DeleteExam";
        public const string DeleteScanProtocol = "DeleteScanProtocol";
        public const string AddHrvScan = "AddHrvScan";
        public const string AddEmgScan = "AddEmgScan";
        public const string AddRomScan = "AddRomScan";
        public const string AddThermalScan = "AddThermalScan";
        public const string AddRawThermalRollingData = "AddRawThermalRollingData";
        public const string AddAlgoScan = "AddAlgoScan";
        public const string MoveScan = "MoveScan";
        public const string Exam = "Exam";
        public const string ScanProtocol = "ScanProtocol";

        public static bool IsAllowedOffline(string tag)
        {
            return tag == Patient || 
                   tag == AddHrvScan || 
                   tag == AddEmgScan || 
                   tag == AddRomScan || 
                   tag == AddThermalScan ||
                   tag == AddRawThermalRollingData ||
                   tag == Exam ||
                   tag == AddAlgoScan ||
                   tag == ScanProtocol;
        }                
    }  
}