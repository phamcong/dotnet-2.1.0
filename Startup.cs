// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityServer4;
using IdentityServer4.EntityFramework;
using IdentityServer4.EntityFramework.Storage;
using Arch.IS4Host.Data;
using Arch.IS4Host.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Reflection;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Arch.IS4Host
{
    public class Startup
    {
        public IWebHostEnvironment Environment { get; }
        public IConfiguration Configuration { get; }

        public Startup(IWebHostEnvironment environment, IConfiguration configuration)
        {
            Environment = environment;
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Store connectionString in a var
            var connectionString = Configuration.GetConnectionString("DefaultConnection");

            // Sotre assembly for migrations
            var migrationAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;

            services.AddControllersWithViews();

            // Replace DbContext database from SqLite to PostgreSQL
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            var builder = services.AddIdentityServer(options =>
            {
                // see https://identityserver4.readthedocs.io/en/latest/topics/resources.html
                options.EmitStaticAudienceClaim = true;
            })
            // Use our Postgres Database for storing cofniguration data
            .AddConfigurationStore(configDb =>
            {
                configDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                sql => sql.MigrationsAssembly(migrationAssembly));
            })
            // Use our Postgres Database for stroring operational data
            .AddOperationalStore(operationalDb =>
            {
                operationalDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                sql => sql.MigrationsAssembly(migrationAssembly));
            })
            .AddAspNetIdentity<ApplicationUser>();

            // not recommended for production - you need to store your key material somewhere secure
            builder.AddDeveloperSigningCredential();

            services.AddAuthentication()
                .AddGoogle(options =>
                {
                    options.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;

                    // register your IdentityServer with Google at https://console.developers.google.com
                    // enable the Google+ API
                    // set the redirect URI to https://localhost:5001/signin-google
                    options.ClientId = "copy client ID from Google here";
                    options.ClientSecret = "copy client secret from Google here";
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            InitializeDatabase(app);

            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }

            app.UseStaticFiles();

            app.UseRouting();
            app.UseIdentityServer();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
            });
        }

        private void InitializeDatabase(IApplicationBuilder app)
        {

            // Using service scope
            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                // Create PersistedGrant Database (we're using a single db here)
                // If it doesn't exist, add run outstanding migrations
                var persistetGrantDbContext = serviceScope.ServiceProvider
                .GetRequiredService<PersistedGrantDbContext>();
                persistetGrantDbContext.Database.Migrate();


                // Create IS4 Configuration Database (we're using a single db here)
                // if it doesn't exist, and run outstanding migrations
                var configDbContext = serviceScope.ServiceProvider
                .GetRequiredService<ConfigurationDbContext>();
                configDbContext.Database.Migrate();


                // Generating the records corresponding to the Clients, IdentityResources,
                // and API Resources that are defined in our Config class
                if (!configDbContext.Clients.Any())
                {
                    foreach (var client in Config.Clients)
                    {
                        configDbContext.Clients.Add(client);
                    }
                    configDbContext.SaveChanges();
                }

                if (!configDbContext.IdentityResources.Any())
                {
                    foreach (var resource in Config.IdentityResources)
                    {
                        configDbContext.IdentityResources.Add(resource);
                    }
                    configDbContext.SaveChanges();
                }

                if (!configDbContext.ApiScopes.Any())
                {
                    foreach (var resource in Config.ApiScopes)
                    {
                        configDbContext.ApiScopes.Add(resource);
                    }
                    configDbContext.SaveChanges();
                }
            }
        }
    }
}