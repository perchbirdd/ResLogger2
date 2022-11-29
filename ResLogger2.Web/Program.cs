using LettuceEncrypt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Quartz;
using ResLogger2.Common.ServerDatabase;
using ResLogger2.Web.Jobs;
using ResLogger2.Web.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddRazorPages();
builder.Services.AddSwaggerGen();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDbContextPool<ServerHashDatabase>(
	opt => opt
		.UseNpgsql(Environment.GetEnvironmentVariable("RL2_CONNSTRING")));
builder.Services.AddSingleton<IDbLockService, DbLockService>();
builder.Services.AddScoped<IPathDbService, PathDbService>();
builder.Services.AddScoped<IThaliakService, ThaliakService>();

builder.Services.AddQuartz(q =>
{
	q.UseMicrosoftDependencyInjectionJobFactory();
	
	q.ScheduleJob<UpdateJob>(trigger => trigger
		.WithIdentity("UpdateJob")
		.WithCronSchedule("0 30 0/4 * * ?")
		.StartNow());
	
	q.ScheduleJob<ExportJob>(trigger => trigger
		.WithIdentity("ExportJob")
		.WithCronSchedule("0 0 0/12 * * ?")
		.StartNow());
});
builder.Services.AddQuartzServer(q => q.WaitForJobsToComplete = true);

if (builder.Environment.IsDevelopment())
{
	// Do non prod things
}
else
{
	var certDirectory = builder.Configuration["CertDirectory"];
	builder.Services
		.AddLettuceEncrypt()
		.PersistDataToDirectory(new DirectoryInfo(certDirectory), null);
}

var app = builder.Build();

// app.UseMiddleware<GzipRequestMiddleware>();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}
else
{
	app.UseExceptionHandler("/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
	app.UseHttpsRedirection();
}

var exportDir = app.Configuration["ExportDirectory"];
if (!Path.IsPathFullyQualified(exportDir))
	exportDir = Path.GetFullPath(exportDir);
if (!Directory.Exists(exportDir))
	Directory.CreateDirectory(exportDir);

app.UseFileServer(new FileServerOptions
{
	FileProvider = new PhysicalFileProvider(exportDir),
	RequestPath = "/download",
	EnableDirectoryBrowsing = false,
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();
app.Run();