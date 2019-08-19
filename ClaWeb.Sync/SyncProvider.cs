using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using ClaWeb.Data.Entities;
using ClaWeb.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaWeb.Sync
{
    public class SyncProvider
    {
        private readonly IServiceProvider _provider;
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;

        public SyncProvider(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<SyncProvider> logger)
        {
            _provider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        /* REMVOED
        public async Task<string> GetHash(string pracId, SyncType type)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var hash = "";

                switch (type)
                {
                    case SyncType.Patients:
                        var pts = await context.Patients
                            .Where(p => p.PracticeId == pracId)
                            .OrderBy(p => p.Id)
                            .Select(p => p.ConcurrencyStamp)
                            .ToListAsync();

                        hash = Md5HashGenerator.GenerateKey(pts);
                        break;
                    case SyncType.Practice:
                        var prac = await context.Practices
                            .Include(p => p.Locations)
                                .ThenInclude(l => l.Address)
                            .FirstOrDefaultAsync(p => p.Id == pracId);

                        if (prac == null) break;

                        var locs = prac.Locations
                            .OrderBy(l => l.Id)
                            .Select(l => l.ConcurrencyStamp)
                            .ToList();
                        
                        locs.Add(prac.ConcurrencyStamp);
                        
                        hash = Md5HashGenerator.GenerateKey(locs);
                        break;
                    case SyncType.Scans:
                        var scandata = await context.ScanData
                            .Where(sd => sd.PracticeId == pracId)
                            .OrderBy(sd => sd.Id)
                            .Select(sd => sd.ConcurrencyStamp)
                            .ToListAsync();
                        
                        var scans = await context.Scans
                            .Where(s => s.PracticeId == pracId)
                            .OrderBy(s => s.Id)
                            .Select(s => s.ConcurrencyStamp)
                            .ToListAsync();
                        
                        var data = new List<string>();
                        data.AddRange(scans);
                        data.AddRange(scandata);
                        
                        hash = Md5HashGenerator.GenerateKey(data);
                        break;
                    case SyncType.Users:
                        var users = await context.DoctorUsers
                            .Where(d => d.PracticeId == pracId)
                            .OrderBy(d => d.Id)
                            .Select(d => d.ConcurrencyStamp)
                            .ToListAsync();
                        
                        hash = Md5HashGenerator.GenerateKey(users);
                        //hash = "MYFAKEHASHHERE";
                        break;
                    case SyncType.Exams:
                        var exams = await context.Exams
                            .Where(d => d.PracticeId == pracId)
                            .OrderBy(d => d.Id)
                            .Select(d => d.ConcurrencyStamp)
                            .ToListAsync();
                        
                        hash = Md5HashGenerator.GenerateKey(exams);
                        //hash = "MYFAKEHASHHERE";
                        break; 
                    case SyncType.PracticeSettings:
                        var settings = await context.PracticeSettings
                            .Where(d => d.PracticeId == pracId)
                            .OrderBy(d => d.Id)
                            .Select(d => d.ConcurrencyStamp)
                            .ToListAsync();
                        
                        hash = Md5HashGenerator.GenerateKey(settings);
                        //hash = "MYFAKEHASHHERE";
                        break;         
                }

                return hash;
            }
        }

        public async Task ClearLocalDb()
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                //context.Database.Beg
                //await context.Database.EnsureDeletedAsync();
                await context.Database.MigrateAsync();

                await context.Database.ExecuteSqlCommandAsync("DELETE FROM PracticeSettings");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM Exams");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM Locations;");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM ScanData;");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM Scans;");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM Patients;");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM AspNetUsers;");
                await context.Database.ExecuteSqlCommandAsync("DELETE FROM Practices;");
            }
        }

        public async Task RebuildLocalDb(Practice prac, IEnumerable<Location> locs, IEnumerable<Patient> patients, IEnumerable<Scan> scans, IEnumerable<ScanData> scandata, IEnumerable<DoctorUser> users, IEnumerable<Exam> exams, IEnumerable<PracticeSettings> practiceSettings)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                try
                {
                    context.Practices.Add(prac);
                    context.Locations.AddRange(locs);
                    context.DoctorUsers.AddRange(users);
                    context.Patients.AddRange(patients);
                    context.Scans.AddRange(scans);
                    context.Exams.AddRange(exams);
                    context.PracticeSettings.AddRange(practiceSettings);
                    
                    foreach (var sd in scandata)
                    {
                        context.ScanData.AddRange(sd);
                    }

                    await context.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    _logger.LogError($"SQL ERROR: {e} [Inner Exception]{e.InnerException}");
                }
            }
        }
        
        public async Task<bool> PostRequest(string url, string data, string key)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_configuration["ClaWebApiServer"]);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", key);
                var content = new StringContent(data, Encoding.UTF8, "application/json");
                var result = await client.PostAsync(url, content);
                return result.IsSuccessStatusCode;
            }
        }
        
        public async Task UpdateServer(HubConnection hubConnection, string jwt)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var syncItems = context.SyncItems.Where(s => !s.Uploaded);

                if (syncItems.Any())
                {
                    _logger.LogInformation($"[SyncService] Syncing {syncItems.Count()} Items...");
                    foreach (var item in await syncItems.ToListAsync())
                    {
                        _logger.LogInformation(
                            $"[SyncService] Syncing Item tag - {item.SyncType} with target id - {item.SyncTargetId}");

                        var result = false;

                        switch (item.SyncType)
                        {
                            case SyncTags.Patient:
                            {
                                var pat = await context.Patients.SingleOrDefaultAsync(p => p.Id == item.SyncTargetId);
                                if (pat != null)
                                {
                                    result = await hubConnection.InvokeAsync<bool>("UploadPatient", pat);
                                
                                    _logger.LogInformation(
                                        $"[SyncService] Patient upload result - {result}");

                                }

                                break;
                            }
                            case SyncTags.AddHrvScan:
                            {
                                var scan = await context.Scans
                                    .Include(s => s.ScanData)
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(s => s.Id == item.SyncTargetId);
                                if (scan != null)
                                {
                                    foreach (var data in scan.ScanData)
                                    {
                                        data.Scan = null;
                                    }
                                    var hrvDto = new ScanDto<HRVScanData>
                                    {
                                        Id = scan.Id,
                                        CreatedStamp = scan.CreatedStamp,
                                        DoctorId = scan.DoctorId,
                                        PatientId = scan.PatientId,
                                        ScanData = scan.ScanData.Select(scanData => scanData as HRVScanData).ToList()
                                    };
                                    var encoded = JsonConvert.SerializeObject(hrvDto, Formatting.None);
                                
                                    result = await PostRequest("/sync/hrv", encoded, jwt);
                                    //result = await hubConnection.InvokeAsync<bool>("UploadHrvScanData", hrvDto);
                                

                                    //result = await SyncService.PostRequest("/scans/thermal", thermalDto);
                                
                                    _logger.LogInformation(
                                        $"[SyncService] Hrv upload result - {result}");

                                }

                                break;
                            }
                            case SyncTags.AddEmgScan:
                            {
                                var backupData = JsonConvert.DeserializeObject<ScanDto<EMGScanData>>(item.ObjectBackup);
                                var scan = await context.Scans
                                    .Include(s => s.ScanData)
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(s => s.Id == item.SyncTargetId);
                                if (scan == null)
                                {
                                    _logger.LogInformation(
                                        "[SyncService] EMG upload can't find original... using backup data");
                                    scan = new Scan
                                    {
                                        Id = backupData.Id,
                                        CreatedStamp = backupData.CreatedStamp,
                                        DoctorId = backupData.DoctorId,
                                        PatientId = backupData.PatientId,
                                        BodyType = backupData.BodyType,
                                        ScanPosition = backupData.ScanPosition,
                                        ScanPurpose = backupData.ScanPurpose,
                                        ScanData = new List<ScanData>()
                                    };
                                    foreach (var data in backupData.ScanData)
                                    {
                                        scan.ScanData.Add(new EMGScanData
                                        {
                                            SpinalLocation = data.SpinalLocation,
                                            LeftValue = data.LeftValue,
                                            RightValue = data.RightValue,
                                            ScanId = data.ScanId,
                                            Id = data.Id
                                        });
                                    }
                                }
                                var emgDto = scan.ScanData.Select(scanData => scanData as EMGScanData).ToList();

                                foreach (var data in emgDto)
                                {
                                    if (emgDto.IndexOf(data) != 0)
                                    {
                                        data.Scan = null;
                                    }
                                    else
                                    {
                                        data.Scan = new Scan
                                        {
                                            Id = scan.Id,
                                            ScanType = scan.ScanType,
                                            PracticeId = scan.PracticeId,
                                            ConcurrencyStamp = scan.ConcurrencyStamp,
                                            CreatedStamp = scan.CreatedStamp,
                                            DoctorId = scan.DoctorId,
                                            PatientId = scan.PatientId
                                        };
                                    }
                                }
                                
                                result = await hubConnection.InvokeAsync<bool>("UploadEmgScanData", emgDto);
                            
                                _logger.LogInformation(
                                    $"[SyncService] EMG upload result - {result}");
                                break;
                            }
                            case SyncTags.AddThermalScan:
                            case SyncTags.AddRawThermalRollingData:
                            {
                                try
                                {
                                    var backupData = JsonConvert.DeserializeObject<ScanDto<ThermalScanData>>(item.ObjectBackup);
                                    var scan = await context.Scans
                                        .Include(s => s.ScanData)
                                        .AsNoTracking()
                                        .SingleOrDefaultAsync(s => s.Id == item.SyncTargetId);
                                    if (scan == null)
                                    {
                                        _logger.LogInformation("[SyncService] Thermal upload can't find original... using backup data");
                                        scan = new Scan
                                        {
                                            Id = backupData.Id,
                                            Bias = backupData.Bias,
                                            CreatedStamp = backupData.CreatedStamp,
                                            DoctorId = backupData.DoctorId,
                                            OffsetL = backupData.OffsetL,
                                            OffsetR = backupData.OffsetR,
                                            PatientId = backupData.PatientId,
                                            BodyType = backupData.BodyType,
                                            ScanPosition = backupData.ScanPosition,
                                            ScanPurpose = backupData.ScanPurpose,
                                            ScanData = new List<ScanData>()
                                        };
                                        foreach (var data in backupData.ScanData)
                                        {
                                            scan.ScanData.Add(new ThermalScanData
                                            {
                                                SpinalLocation = data.SpinalLocation,
                                                LeftValue = data.LeftValue,
                                                RightValue = data.RightValue,
                                                ScanId = data.ScanId,
                                                Id = data.Id
                                            });
                                        }

                                        scan.ScanData.Add(new RawThermalRollingData
                                        {
                                            RawData = backupData.ThermalRawData
                                        });
                                    }

                                    var thermalDto = new JObject
                                        {
                                            {"Id", scan.Id},
                                            {"Bias", scan.Bias},
                                            {"CreatedStamp", scan.CreatedStamp},
                                            {"DoctorId", scan.DoctorId},
                                            {"OffsetL", scan.OffsetL},
                                            {"OffsetR", scan.OffsetR},
                                            {"PatientId", scan.PatientId},
                                            {"BodyType",(int)scan.BodyType},
                                            {"ScanPosition",(int)scan.ScanPosition},
                                            {"ScanPurpose",(int)scan.ScanPurpose},
                                        };
                                    var scanData = new JArray(
                                        from data in scan.ScanData.OfType<ThermalScanData>().ToList()
                                        select new JObject(
                                            new JProperty("Id", data.Id),
                                            new JProperty("SpinalLocation", data.SpinalLocation),
                                            new JProperty("LeftValue", data.LeftValue),
                                            new JProperty("RightValue", data.RightValue),
                                            new JProperty("ScanId", scan.Id),
                                            new JProperty("Id", data.Id)
                                        ));
                                    thermalDto.Add("ScanData",scanData);
                                
                                    var rawData = scan.ScanData.OfType<RawThermalRollingData>().FirstOrDefault().RawData;
                                    thermalDto.Add("ThermalRawData", rawData);
                                
                                    result = await hubConnection.InvokeAsync<bool>("UploadThermalScanData", thermalDto);

                                    //result = await SyncService.PostRequest("/scans/thermal", thermalDto);
                                
                                    _logger.LogInformation(
                                        $"[SyncService] Thermal upload result - {result}");
                                    break;                                    
                                }
                                catch (Exception e)
                                {
                                    _logger.LogInformation(
                                        $"[SyncService] Thermal upload EXCEPTION - {e.Message}");
                                    break;
                                }
                                
                            }
                            case SyncTags.AddAlgoScan:
                            {
                                var scan = await context.Scans
                                    .Include(s => s.ScanData)
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(s => s.Id == item.SyncTargetId);
                                if (scan != null)
                                {
                                    var algoDto = scan.ScanData.Select(scanData => scanData as AlgoScanData).ToList();

                                    foreach (var data in algoDto)
                                    {
                                        if (algoDto.IndexOf(data) != 0)
                                        {
                                            data.Scan = null;
                                        }
                                        else
                                        {
                                            data.Scan = new Scan
                                            {
                                                Id = scan.Id,
                                                ScanType = scan.ScanType,
                                                PracticeId = scan.PracticeId,
                                                ConcurrencyStamp = scan.ConcurrencyStamp,
                                                CreatedStamp = scan.CreatedStamp,
                                                DoctorId = scan.DoctorId,
                                                PatientId = scan.PatientId
                                            };
                                        }
                                    }
                                
                                    result = await hubConnection.InvokeAsync<bool>("UploadAlgoScanData", algoDto);
                                
                                    _logger.LogInformation(
                                        $"[SyncService] Algo upload result - {result}");
                                }

                                break;
                            }
                            case SyncTags.AddRomScan:
                            {
                                var scan = await context.Scans
                                    .Include(s => s.ScanData)
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(s => s.Id == item.SyncTargetId);
                                if (scan != null)
                                {
                                    var romDto = scan.ScanData.Select(scanData => scanData as ROMScanData).ToList();

                                    foreach (var data in romDto)
                                    {
                                        if (romDto.IndexOf(data) != 0)
                                        {
                                            data.Scan = null;
                                        }
                                        else
                                        {
                                            data.Scan = new Scan
                                            {
                                                Id = scan.Id,
                                                ScanType = scan.ScanType,
                                                PracticeId = scan.PracticeId,
                                                ConcurrencyStamp = scan.ConcurrencyStamp,
                                                CreatedStamp = scan.CreatedStamp,
                                                DoctorId = scan.DoctorId,
                                                PatientId = scan.PatientId
                                            };
                                        }
                                    }
                                
                                    result = await hubConnection.InvokeAsync<bool>("UploadRomScanData", romDto);
                                
                                    _logger.LogInformation(
                                        $"[SyncService] ROM upload result - {result}");
                                }

                                break;
                            }
                            case SyncTags.Exam:
                            {
                                var exam = await context.Exams
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(e => e.Id == item.SyncTargetId);
                                if (exam != null)
                                {
                                    result = await hubConnection.InvokeAsync<bool>("UploadExam", exam);
                                }

                                _logger.LogInformation(
                                    $"[SyncService] Exam upload result - {result}");
                                break;
                            }
                            case SyncTags.ScanProtocol:
                            {
                                var setting = await context.PracticeSettings
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(e => e.Id == item.SyncTargetId);
                                if (setting != null)
                                {
                                    result = await hubConnection.InvokeAsync<bool>("UploadScanProtocol", setting);
                                }

                                _logger.LogInformation(
                                    $"[SyncService] Exam upload result - {result}");
                                break;
                            }
                        }

                        item.Uploaded = true;
                        item.Result = result;
                        item.TimeStamp = DateTime.Now;

                        await context.SaveChangesAsync();
                    }
                }
            }

        }

        public async Task<IEnumerable<Patient>> GetPatients(string pracId)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await context.Patients
                    .Include(p => p.Address)
                    .Where(p => p.PracticeId == pracId)
                    .ToListAsync();
            }
        }
        
        public async Task<Practice> GetPractice(string pracId)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await context.Practices
                    .FirstOrDefaultAsync(p => p.Id == pracId);
            }
        }

        public async Task<IEnumerable<Location>> GetLocations(string pracId)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await context.Locations
                    .Include(l => l.Address)
                    .Where(l => l.PracticeId == pracId)
                    .ToListAsync();
            }
        }
        
        public async Task<IEnumerable<Scan>> GetScans(string pracId)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await context.Scans
                    .Where(s => s.PracticeId == pracId)
                    .ToListAsync();
            }
        }
        
        public async Task<IEnumerable<TKey>> GetScanData<TKey>(string pracId) where TKey : ScanData
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var data = context.ScanData
                    .ToList();
                
                return data
                    .OfType<TKey>()
                    .Where(sd => sd.PracticeId == pracId)
                    .ToList();
            }
        }

        public async Task<IEnumerable<DoctorUser>> GetUsers(string pracId)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<UserManager<DoctorUser>>();

                var usrs = context.Users
                    .ToList();
                
                return usrs
                    .Where(d => d.PracticeId == pracId)
                    .ToList();
            }
        }

        public async Task<IEnumerable<Exam>> GetExams(string pracId)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return await context.Exams
                    .Where(e => e.PracticeId == pracId)
                    .ToListAsync();
            }
        }

        public async Task<IEnumerable<TKey>> GetPracticeSettings<TKey>(string pracId) where TKey : PracticeSettings
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var data = context.PracticeSettings
                    .ToList();

                return data.OfType<TKey>()
                    .Where(ps => ps.PracticeId == pracId)
                    .ToList();
            }
        }
        
        public async Task AddOrEditExam(Exam exam)
        {
            // Remove object references, leave only id's
            exam.Doctor = null;
            exam.Patient = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Exams
                    .FirstOrDefaultAsync(e => e.Id == exam.Id);

                if (exist == null)
                {
                    await context.Exams.AddAsync(exam);
                    await context.SaveChangesAsync();
                }
                else
                {
                    exist.CoreScore = exam.CoreScore;
                    exist.CoreScoreVersion = exam.CoreScoreVersion;
                    exist.PatientId = exam.PatientId;
                    exist.DoctorId = exam.DoctorId;
                    exist.ReportType = exam.ReportType;
                    exist.EmgScanId = exam.EmgScanId;
                    exist.HrvScanId = exam.HrvScanId;
                    exist.RomScanId = exam.RomScanId;
                    exist.AlgoScanId = exam.AlgoScanId;
                    exist.ThermalScanId = exam.ThermalScanId;
                    exist.ConcurrencyStamp = exam.ConcurrencyStamp;
                    exist.HrvScore = exam.HrvScore;
                    exist.HrvScoreVersion = exam.HrvScoreVersion;
                    exist.EmgScore = exam.EmgScore;
                    exist.EmgScoreVersion = exam.EmgScoreVersion;
                    exist.ThermalScore = exam.ThermalScore;
                    exist.ThermalScoreVersion = exam.ThermalScoreVersion;
                    exist.AlgoScore = exam.AlgoScore;
                    exist.AlgoScoreVersion = exam.AlgoScoreVersion;
                    exist.RomScore = exam.RomScore;
                    exist.RomScoreVersion = exam.RomScoreVersion;

                    context.Exams.Update(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task DeleteExam(string id)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Exams
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (exist != null)
                {
                    context.Exams.Remove(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task AddOrEditPatient(Patient pat)
        {
            // Remove object references, leave only id's
            pat.Practice = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Patients
                    .FirstOrDefaultAsync(p => p.Id == pat.Id);

                if (exist == null)
                {
                    await context.Patients.AddAsync(pat);
                    await context.SaveChangesAsync();
                }
                else
                {
                    // If the existing patient is newer than the modified
                    if (exist.ActivityStamp > pat.ActivityStamp)
                    {
                        _logger.Log(LogLevel.Critical,"AddOrEditPatient Existing Patient is newer... ignoring");
                        return;
                    }

                    exist.Address = pat.Address;
                    exist.Email = pat.Email;
                    exist.Gender = pat.Gender;
                    exist.Goal = pat.Goal;
                    exist.Height = pat.Height;
                    exist.Notes = pat.Notes;
                    exist.Weight = pat.Weight;
                    exist.ActivityStamp = pat.ActivityStamp;
                    exist.AuthorizedEmails = pat.AuthorizedEmails;
                    exist.BirthDay = pat.BirthDay;
                    exist.ConcurrencyStamp = pat.ConcurrencyStamp;
                    exist.FirstName = pat.FirstName;
                    exist.LastName = pat.LastName;
                    exist.PhoneNumber = pat.PhoneNumber;
                    
                    context.Patients.Update(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task DeletePatient(string id)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Patients
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (exist != null)
                {
                    context.Patients.Remove(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task EditPractice(Practice prac)
        {
            // Remove object references, leave only id's
            prac.Locations = null;
            prac.Doctors = null;
            prac.Patients = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Practices
                    .FirstOrDefaultAsync(p => p.Id == prac.Id);

                if (exist == null)
                {
                    //TODO: This should NEVER happen. Need more error handling here
                    await context.Practices.AddAsync(prac);
                    await context.SaveChangesAsync();
                }
                else
                {
                    exist.Name = prac.Name;
                    exist.Logo = prac.Logo;
                    exist.Techniques = prac.Techniques;
                    exist.Metric = prac.Metric;
                    exist.DisableThermalShift = prac.DisableThermalShift;
                    exist.UseDefaultScanBg = prac.UseDefaultScanBg;
                    exist.ConcurrencyStamp = prac.ConcurrencyStamp;
                    
                    context.Practices.Update(exist);
                    await context.SaveChangesAsync();
                    
                    //await context.Practices.AddAsync(prac);
                    //await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task AddOrEditLocation(Location loc)
        {
            // Remove object references, leave only id's
            loc.Practice = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Locations
                    .FirstOrDefaultAsync(l => l.Id == loc.Id);

                if (exist == null)
                {
                    await context.Locations.AddAsync(loc);
                    await context.SaveChangesAsync();
                }
                else
                {
                    exist.Name = loc.Name;
                    exist.Email = loc.Email;
                    exist.PhoneNumber = loc.PhoneNumber;
                    exist.ConcurrencyStamp = loc.ConcurrencyStamp;

                    exist.Address = loc.Address;
                    
                    context.Locations.Update(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task DeleteLocation(string id)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Locations
                    .FirstOrDefaultAsync(l => l.Id == id);

                if (exist != null)
                {
                    context.Locations.Remove(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task DeleteScan(string id)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Scans
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (exist != null)
                {
                    context.Scans.Remove(exist);
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task AddRawRollingData(RawThermalRollingData data)
        {
            var scan = data.Scan;

            scan.Doctor = null;
            scan.Patient = null;

            data.Scan = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                await context.ScanData.AddAsync(data);
                await context.SaveChangesAsync();
            }
        }

        public async Task MoveScan(Scan scan)
        {
            // Remove object references, leave only id's
            scan.Doctor = null;
            scan.Patient = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.Scans
                    .FirstOrDefaultAsync(p => p.Id == scan.Id);

                if (exist == null)
                {
                    //TODO: This should NEVER happen. Need more error handling here
                    return;
                }
                
                exist.PatientId = scan.PatientId;
                exist.ConcurrencyStamp = scan.ConcurrencyStamp;
                
                context.Scans.Update(exist);
                await context.SaveChangesAsync();
                
                //await context.Practices.AddAsync(prac);
                //await context.SaveChangesAsync();
            }
        }
        
        public async Task AddScan<TKey>(ScanDto<TKey> model) where TKey : ScanData
        {
            var scan = new Scan
            {
                Id = model.Id,
                ScanType = ScanTypes.Thermal,
                CreatedStamp = model.CreatedStamp,
                DoctorId = model.DoctorId,
                PatientId = model.PatientId,
                Bias = model.Bias,
                OffsetL = model.OffsetL,
                OffsetR = model.OffsetR,
                PracticeId = model.PracticeId
            };

            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                await context.Scans.AddAsync(scan);

                await context.ScanData.AddRangeAsync(model.ScanData);
                if (model.ThermalRawData != null)
                {
                    await context.ScanData.AddAsync(new RawThermalRollingData
                    {
                        ScanId = scan.Id,
                        RawData = model.ThermalRawData,
                        PracticeId = model.PracticeId
                    });
                }
                await context.SaveChangesAsync();
            }
        }
        public async Task AddScanViaScanData<TKey>(IEnumerable<TKey> data) where TKey : ScanData
        {
            var scan = data.First().Scan;

            scan.Doctor = null;
            scan.Patient = null;

            foreach (var scanData in data)
            {
                scanData.Scan = null;
            }
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                await context.Scans.AddAsync(scan);
                await context.ScanData.AddRangeAsync(data);
                await context.SaveChangesAsync();
            }
        }
        
        public async Task AddOrEditScanProtocol(ScanProtocolSettings settings)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var data = await context.PracticeSettings.FirstOrDefaultAsync(sp => sp.Id == sp.Id);

                try
                {
                    var exist = data as ScanProtocolSettings;
                    if (data == null)
                    {
                        await context.PracticeSettings.AddAsync(data);
                        await context.SaveChangesAsync();
                    }
                    else
                    {                    
                        exist.Options = settings.Options;
                        exist.ConcurrencyStamp = settings.ConcurrencyStamp;
    
                        context.PracticeSettings.Update(exist);
                        await context.SaveChangesAsync();
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError("AddOrEditScanProtocol type conversion failure");
                }
            }
        }
        
        public async Task DeleteScanProtocol(string id)
        {
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var exist = await context.PracticeSettings
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (exist != null)
                {
                    context.PracticeSettings.Remove(exist);
                    await context.SaveChangesAsync();
                }
            }
        }
        
        public async Task AddOrEditUser(DoctorUser doc)
        {
            // Remove object references, leave only id's
            doc.Practice = null;
            doc.Admin = null;
            
            using (var scope = _provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<DoctorUser>>();

                var exist = await userManager.GetClaimsAsync(doc);

                if (exist == null)
                {
                    await userManager.CreateAsync(doc);
                }
                await userManager.UpdateAsync(doc);
            }
        }
        */
       
    }

    public enum SyncType
    {
        Practice,
        Patients,
        Scans,
        Users,
        Exams,
        PracticeSettings
    }    
    
    #region DATA TRANSFER OBJECTS
 
    public class MoveDto
    {
        [Required]
        public string NewPatientId { get; set; }
        [Required]
        public string ScanId { get; set; }
    }
         
    #endregion
}