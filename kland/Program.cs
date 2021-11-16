using Amazon.S3;
using kland;
using kland.Controllers;
using kland.Db;
using kland.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var services = builder.Services;
var configuration = builder.Configuration;

void AddConfigBinding<T>(IServiceCollection services, IConfiguration config) where T : class
{
    var name = typeof(T).Name;
    services.Configure<T>(config.GetSection(name));
    services.AddTransient<T>(p => (p.GetService<IOptionsMonitor<T>>() ?? throw new InvalidOperationException($"Mega config failure on {name}!")).CurrentValue);
}

services.AddDefaultAWSOptions(configuration.GetAWSOptions());
services.AddAWSService<IAmazonS3>();
services.AddDbContext<KlandDbContext>(opts =>
{
    opts.UseSqlite(builder.Configuration.GetConnectionString("kland"));
});

//Why are these singletons? They store no state, so why not!
services.AddSingleton<IPageRenderer, MustacheRenderer>();
services.AddSingleton<IUploadStore, S3UploadStore>();

//I want the ACTUAL configs in the service
AddConfigBinding<KlandImageHostControllerConfig>(services, configuration);
AddConfigBinding<S3UploadStoreConfig>(services, configuration);
AddConfigBinding<KlandControllerConfig>(services, configuration);
AddConfigBinding<RenderConfig>(services, configuration);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
