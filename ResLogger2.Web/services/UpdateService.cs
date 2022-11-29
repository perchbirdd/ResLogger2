namespace ResLogger2.Web.Services;

public class UpdateService : BackgroundService
{
	public IServiceProvider Services { get; }
	
	private readonly ILogger<UpdateService> _logger;
	private readonly IDbLockService _dbLockService;
	
	public UpdateService(IServiceProvider services, IDbLockService dbLockService, ILogger<UpdateService> logger)
	{
		Services = services;
		_logger = logger;
		_dbLockService = dbLockService;
		
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		return null;
	}
}