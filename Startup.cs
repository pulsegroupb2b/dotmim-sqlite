using System;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClaWeb.Api.LetsEncrypt;
using ClaWeb.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using ClaWeb.Data.Entities;
using ClaWeb.Data.Models;

using ClaWeb.Sync;
using ClaWeb.Sync.Box;
using ClaWeb.Sync.Cloud;
using Dotmim.Sync.Filter;
using Dotmim.Sync.SqlServer;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;

namespace ClaWeb.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostingEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        private readonly IConfiguration _configuration;
        private readonly IHostingEnvironment _environment;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors();
            
            // ==== Add Sync Provider =====
            services.AddSingleton<SyncProvider>();
            
            if (_environment.EnvironmentName == "Box")
            {
                // ===== Add Sync Service ========
                services.AddSingleton<IHostedService, SyncService>();
                
                // ===== Add Sync Sender =====
                services.AddSingleton<ISyncSender, BoxSyncSender>();
            }
            else
            {
                // ===== Add SignalR =======
                services.AddSignalR().AddJsonProtocol(options =>
                    {
                        options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                        //options.PayloadSerializerSettings.PreserveReferencesHandling = PreserveReferencesHandling.None;
                    });
                // ===== Add Sync Sender =====
                services.AddSingleton<ISyncSender, CloudSyncSender>();
                
                // ===== Add Event Processor =====
                //services.AddHostedService<EventHubProcessor>();
            }
            
            // ===== Add our DbContext ========


            if (_environment.EnvironmentName == "Box")
            {
                var db = "box.db";
                var conn = new SqliteConnection($"Data Source={db};");
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
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(conn));
            }
            else
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    var builder = new SqlConnectionStringBuilder(_configuration.GetConnectionString("DB"))
                    {
                        Password = _configuration["SqlPassword"]
                    };
                    options.UseSqlServer(
                        builder.ConnectionString
                        , b => b.MigrationsAssembly("ClaWeb.Api")
                    );
                });   
            }
            
            // ===== Add Identity ========
            services.AddIdentity<DoctorUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            // ===== Add Jwt Authentication ========
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear(); // => remove default claims
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

                })
                .AddJwtBearer(cfg =>
                {
                    cfg.RequireHttpsMetadata = false;
                    cfg.SaveToken = true;
                    cfg.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = _configuration["JwtIssuer"],
                        ValidAudience = _configuration["JwtIssuer"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JwtKey"])),
                        ClockSkew = TimeSpan.Zero // remove delay of token when expire
                    };
                });
            
            // ===== Add Authorization =====
            services.AddSingleton<IAuthorizationHandler, ValidDoctorHandler>();
            services.AddAuthorization(options =>
            {
                options.AddPolicy("ValidDoctorOnly",
                    policy => policy
                        .RequireClaim(ClaimTypes.NameIdentifier)
                        .RequireClaim(ClaClaimTypes.Practice)
                        .AddRequirements(new ValidDoctorRequirement()));
            });
            
            // ===== Lets Encrypt ======
            var letsEncryptInitialKey = _configuration["LetsEncrypt:Key"];

            // ===== Add MVC ========
            if (_environment.EnvironmentName == "Box")
            {
                services.AddMvc()
                    .AddJsonOptions(options =>
                    {
                        // Prevent issues from nested object loops in ef
                        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;

                        // Auto serialize enums into strings and vice versa for API
                        options.SerializerSettings.Converters.Add(new StringEnumConverter {CamelCaseText = false});

                        // Disable camel case in property names when serializing
                        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                    });
            }
            else
            {
                services.AddMvc()
                    .AddJsonOptions(options =>
                    {
                        // Prevent issues from nested object loops in ef
                        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    
                        // Auto serialize enums into strings and vice versa for API
                        options.SerializerSettings.Converters.Add(new StringEnumConverter{ CamelCaseText = false });
                    
                        // Disable camel case in property names when serializing
                        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                    })
                    .AddRazorPagesOptions(options =>
                    {
                        options.Conventions.AddPageRoute("/LetsEncrypt", $".well-known/acme-challenge/{letsEncryptInitialKey.Split('.')[0]}");
                    });
            
                services.AddSingleton<ILetsEncryptKey>(l => new LetsEncryptKey(letsEncryptInitialKey));

                services.AddMemoryCache();

                var syncBuilder = new SqlConnectionStringBuilder(_configuration.GetConnectionString("DB"))
                {
                    Password = _configuration["SqlPassword"]
                };

                services.AddSyncServer<SqlSyncProvider>(syncBuilder.ConnectionString, c =>
                {
                    // Table Order is VERY Important for syncing!!!
                    var tables = new string[] {
                        "Practices",
                        "AspNetUsers", 
                        "Locations",
                        "Patients",
                        "PracticeSettings",
                        "Scans",
                        "ScanData",
                        "Exams"
                    };
                    c.Add(tables);
                    c.ScopeInfoTableName = "tscopeinfo";
                    c.SerializationFormat = Dotmim.Sync.Enumerations.SerializationFormat.Binary;
                    c.StoredProceduresPrefix = "ds";
                    c.StoredProceduresSuffix = "";
                    c.TrackingTablesPrefix = "ds";
                    c.TrackingTablesSuffix = "";
                    c.Filters.Add(new FilterClause("Practices", "Id"));
                    c.Filters.Add(new FilterClause("AspNetUsers", "PracticeId"));
                    c.Filters.Add(new FilterClause("Locations", "PracticeId"));
                    c.Filters.Add(new FilterClause("Patients", "PracticeId"));
                    c.Filters.Add(new FilterClause("PracticeSettings", "PracticeId"));
                    c.Filters.Add(new FilterClause("Scans", "PracticeId"));
                    c.Filters.Add(new FilterClause("ScanData", "PracticeId"));
                    c.Filters.Add(new FilterClause("Exams", "PracticeId"));
                });

                var hangFireBuilder = new SqlConnectionStringBuilder(_configuration.GetConnectionString("HANGFIRE"))
                {
                    Password = _configuration["SqlPassword"]
                };

                services.AddHangfire(x =>
                    x.UseSqlServerStorage(
                        hangFireBuilder.ConnectionString));
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app,
            ApplicationDbContext dbContext
        )
        {
            if (_environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // ===== Use Authentication ======
            app.UseAuthentication();

            if (_environment.EnvironmentName != "Box")
            {
                app.UseSignalR(options => options.MapHub<SyncHub>("/sync"));
            }
            
            app.UseCors(options =>
            {
                options.AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowAnyOrigin();
            });

            app.UseStaticFiles();
            app.UseMvcWithDefaultRoute();
            
            // ===== Create tables ======
            //dbContext.Database.EnsureDeleted();
            //dbContext.Database.EnsureCreated();
            if (_environment.EnvironmentName == "Box")
            {
                dbContext.Database.EnsureCreated();
                dbContext.Database.Migrate();
            }
            else
            {
                //dbContext.Database.Migrate();
                app.UseHangfireServer();
                app.UseHangfireDashboard();
            }

        }
    }
}
