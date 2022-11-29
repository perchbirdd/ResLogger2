using System.Net;
using System.Net.Http;

namespace ResLogger2.Plugin;

public class Api
{
	private const string Endpoint = "https://rl2.perchbird.dev";
	// private const string Endpoint = "https://rl2-stg.perchbird.dev";
	// private const string Endpoint = "http://localhost:5103";
	public const string UploadEndpoint = $"{Endpoint}/api/upload";
	public const string StatsEndpoint = $"{Endpoint}/api/stats";

	public static HttpClient Client { get; }

	static Api()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
		Client = new HttpClient();
	}

	public static void Dispose()
	{
		Client.Dispose();
	}
}