using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ResLogger2.Common;
using ResLogger2.Web.Services;

namespace ResLogger2.Web.Controllers;

[Route("api")]
[ApiController]
public class PathController : Controller
{
	private readonly IPathDbService _dbService;
	private readonly ILogger _logger;
	
	public PathController(IPathDbService dbService, ILogger<PathController> logger)
	{
		_dbService = dbService;
		_logger = logger;
	}

	[HttpPost]
	[Route("upload")]
	public async Task Upload()
	{
		var content = new StreamReader(Request.Body).ReadToEndAsync().Result;
		var data = Utils.GetUploadDataObjectFromString(content);

		if (data == null || data.Entries == null || data.Entries.Count > 2000)
			return;

		var result = await _dbService.ProcessDataAsync(data);
		Response.StatusCode = result ? 202 : 503;
	}
	
	[HttpGet]
	[Route("stats")]
	public async Task<IActionResult> Stats()
	{
		var stopwatch = Stopwatch.StartNew();
		var data = await _dbService.GetStatsAsync();
		_logger.LogInformation("Stats request took {time}ms", stopwatch.ElapsedMilliseconds);
		return Ok(data);
	}
}