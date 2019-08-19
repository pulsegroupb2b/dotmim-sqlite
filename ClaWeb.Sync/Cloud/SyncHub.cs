using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ClaWeb.Data.Entities;
using ClaWeb.Data.Models;
using ClaWeb.Sync.Box;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaWeb.Sync.Cloud
{
    [Authorize]
    public class SyncHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly SyncProvider _provider;
        private readonly ILogger _logger;

        public SyncHub(ApplicationDbContext context, SyncProvider provider, ILogger<SyncHub> logger)
        {
            _context = context;
            _provider = provider;
            _logger = logger;
        }
        /* REMOVED
        #region VERIFICATION
        
        public async Task<bool> VerifyData(string hash, SyncType type)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return true;
            }

            var servHash = await _provider.GetHash(pracId.Value, type);
            
            _logger.LogInformation($"[SyncHub] Verify {Enum.GetName(typeof(SyncType), type)} request from practice {pracId.Value} with client hash: {hash} and server hash: {servHash}");

            return servHash == hash;
        }
        
        #endregion
        
        #region DOWNLOADS
        
        public async Task<Practice> DownloadPractice()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            return await _provider.GetPractice(pracId.Value);
        }
        
        public async Task<IEnumerable<Location>> DownloadLocations()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            return await _provider.GetLocations(pracId.Value);
        }

        public async Task<IEnumerable<Patient>> DownloadPatients()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            return await _provider.GetPatients(pracId.Value);
        }
        
        public async Task<IEnumerable<Scan>> DownloadScans()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            return await _provider.GetScans(pracId.Value);
        }
        
        public async Task<IEnumerable<HRVScanData>> DownloadHrvScanData()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var data = await _provider.GetScanData<HRVScanData>(pracId.Value);

            return data;
        }
        
        public async Task<IEnumerable<EMGScanData>> DownloadEmgScanData()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var data = await _provider.GetScanData<EMGScanData>(pracId.Value);

            return data;
        }
        
        public async Task<IEnumerable<ThermalScanData>> DownloadThermalScanData()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var data = await _provider.GetScanData<ThermalScanData>(pracId.Value);

            return data;
        }
        
        public async Task<IEnumerable<RawThermalRollingData>> DownloadRawThermalRollingData()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var data = await _provider.GetScanData<RawThermalRollingData>(pracId.Value);

            return data;
        }
        
        public async Task<IEnumerable<ROMScanData>> DownloadRomScanData()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var data = await _provider.GetScanData<ROMScanData>(pracId.Value);

            return data;
        }
        
        public async Task<IEnumerable<AlgoScanData>> DownloadAlgoScanData()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var data = await _provider.GetScanData<AlgoScanData>(pracId.Value);

            return data;
        }
        
        public async Task<IEnumerable<DoctorUser>> DownloadUsers()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var docs = await _provider.GetUsers(pracId.Value);

            return docs;
        }

        public async Task<IEnumerable<Exam>> DownloadExams()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");

            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var exams = await _provider.GetExams(pracId.Value);

            return exams;
        }

        public async Task<IEnumerable<ScanProtocolSettings>> DownloadScanProtocolSettings()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");

            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var settings = await _provider.GetPracticeSettings<ScanProtocolSettings>(pracId.Value);

            return settings;
        }
        public async Task<IEnumerable<ScanLayoutSettings>> DownloadScanLayoutSettings()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");

            if (pracId == null)
            {
                // We return true to make the problem box stop its execution and prevent further calls
                return null;
            }

            var settings = await _provider.GetPracticeSettings<ScanLayoutSettings>(pracId.Value);

            return settings;
        }
        #endregion
        
        #region UPLOADS

        public async Task<bool> UploadPatient(Patient pat)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // This could either be a new or modified patient
            _logger.LogInformation($"[SyncHub] Adding/Updating patient: {pat.FullName}");
            try
            {
                await _provider.AddOrEditPatient(pat);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add/Update patient: {e}");
                return false;
            }

            return true;
        }
      
        public async Task<bool> UploadEmgScanData(IEnumerable<EMGScanData> scanData)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // This is always a new scan
            _logger.LogInformation($"[SyncHub] Adding scan: {scanData.First().ScanId}");
            try
            {
                await _provider.AddScanViaScanData(scanData);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add scan: {e}");
                return false;
            }

            return true;
        }
        
        public async Task<bool> UploadThermalScanData(JObject scanData)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // Fix base54 issue
            scanData["ThermalRawData"] = System.Convert.ToBase64String(Encoding.ASCII.GetBytes(scanData["ThermalRawData"].ToString()));

            var thermalDto = JsonConvert.DeserializeObject<ScanDto<ThermalScanData>>(scanData.ToString());
            thermalDto.PracticeId = pracId.Value;
            foreach (var data in thermalDto.ScanData)
            {
                data.PracticeId = pracId.Value;
            }
            // This is always a new scan
            _logger.LogInformation($"[SyncHub] Adding scan: {thermalDto.Id}");
            try
            {
                await _provider.AddScan(thermalDto);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add scan: {e}");
                return false;
            }

            return true;
        }
        
        public async Task<bool> UploadAlgoScanData(IEnumerable<AlgoScanData> scanData)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // This is always a new scan
            _logger.LogInformation($"[SyncHub] Adding scan: {scanData.First().ScanId}");
            try
            {
                await _provider.AddScanViaScanData(scanData);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add scan: {e}");
                return false;
            }

            return true;
        }
        
        public async Task<bool> UploadRomScanData(IEnumerable<ROMScanData> scanData)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // This is always a new scan
            _logger.LogInformation($"[SyncHub] Adding scan: {scanData.First().ScanId}");
            try
            {
                await _provider.AddScanViaScanData(scanData);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add scan: {e}");
                return false;
            }

            return true;
        }
        
        public async Task<bool> UploadExam(Exam exam)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // This could either be a new or modified exam
            _logger.LogInformation($"[SyncHub] Adding/Updating exam: {exam.Id}");
            try
            {
                await _provider.AddOrEditExam(exam);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add/Update exam: {e}");
                return false;
            }

            return true;
        }

        public async Task<bool> UploadScanProtocol(ScanProtocolSettings scanProtocolSettings)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId == null)
            {
                return false;
            }
            
            // This is always a new scan
            _logger.LogInformation($"[SyncHub] Adding scan protocol: {scanProtocolSettings.Id}");
            try
            {
                await _provider.AddOrEditScanProtocol(scanProtocolSettings);
            }
            catch (Exception e)
            {
                _logger.LogError($"[SyncHub] Unable to Add scan protocol: {e}");
                return false;
            }

            return true;
        }
        #endregion
        */
        public async void UploadTrigger(string pracId)
        {
            await Clients.Group(pracId).SendAsync("ChangeDetect", pracId);
            _logger.LogInformation($"[SyncHub]: Registered UploadTrigger - {pracId}");
        }
        
        #region PORTAL

        public async void RegisterChange(string pracId, string tag, object data)
        {
            await Clients.Group(pracId).SendAsync(tag, data);
            
            _logger.LogInformation($"[SyncHub]: Registered Portal Change - {tag}:{data}");
        }

        #endregion
        
        public override Task OnConnectedAsync()
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId != null)
            {
                if (pracId.Value == "PORTAL")
                {
                    _logger.LogInformation($"[SyncHub] Portal Connected");
                }
                else
                {
                    Groups.AddToGroupAsync(Context.ConnectionId, pracId.Value);
                    _logger.LogInformation($"[SyncHub] Box connected from practice {pracId.Value}");
                }
            }

            return base.OnConnectedAsync();
        }
        
        public override Task OnDisconnectedAsync(Exception exception)
        {
            var pracId = Context.User.Claims.SingleOrDefault(c => c.Type == "PracticeId");
            
            if (pracId != null)
            {
                if (pracId.Value == "PORTAL")
                {
                    _logger.LogInformation($"[SyncHub] Portal Disconnected");
                }
                else
                {
                    Groups.RemoveFromGroupAsync(Context.ConnectionId, pracId.Value);
                    _logger.LogInformation($"[SyncHub] Box disconnected from practice {pracId.Value}");
                }
            }
            
            return base.OnDisconnectedAsync(exception);
        }
        

    }
}